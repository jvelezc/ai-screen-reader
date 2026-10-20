using System.Collections.Immutable;
using System.Globalization;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.ScreenReader;

internal static class ReaderTableNavigator
{
    public static bool IsCellCommand(ReaderCommand command) => command is ReaderCommand.NextCell
        or ReaderCommand.PreviousCell or ReaderCommand.NextRow or ReaderCommand.PreviousRow
        or ReaderCommand.NextColumn or ReaderCommand.PreviousColumn;

    public static bool IsTable(AccessibleNode node) => node.Role is "table" or "grid" or "treegrid";
    private static bool IsCell(AccessibleNode node) => node.Role is "cell" or "rowheader" or "columnheader" or "gridcell";

    private static List<AccessibleNode>? Path(AccessibilitySnapshot snapshot, AccessibleNode node)
    {
        var byId = snapshot.Nodes.ToDictionary(item => item.Id);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<AccessibleNode>();
        while (true)
        {
            if (!seen.Add(node.Id)) return null;
            result.Add(node);
            if (node.Role == "RootWebArea") return result;
            if (node.ParentId is null) return result;
            if (!byId.TryGetValue(node.ParentId, out node!)) return null;
        }
    }

    private static bool SameDocument(AccessibleNode node, AccessibleNode table) =>
        node.DocumentId is not null && node.DocumentId == table.DocumentId && node.FrameId == table.FrameId;

    public static ReaderTableContext? Build(AccessibilitySnapshot? snapshot, AccessibleNode? current)
    {
        if (snapshot is null || current is null) return null;
        var path = Path(snapshot, current);
        if (path is null) return null;
        var table = path.FirstOrDefault(IsTable);
        if (table?.Table is not { } tableGeometry) return null;
        var cell = path.TakeWhile(node => node.Id != table.Id).FirstOrDefault(IsCell);
        var row = path.TakeWhile(node => node.Id != table.Id).FirstOrDefault(node => node.Role == "row");
        var geometry = cell?.Table ?? row?.Table ?? tableGeometry;
        var valid = path.TakeWhile(node => node.Role != "RootWebArea").All(node => SameDocument(node, table))
            && table.Role == "table" && tableGeometry.RowIndex is null && tableGeometry.ColumnIndex is null
            && tableGeometry.IsSupported && tableGeometry.IsConsistent && geometry.IsConsistent
            && geometry.IsSupported && geometry.Source == SemanticSource.NativeHtmlProperty
            && tableGeometry.Source == SemanticSource.NativeHtmlProperty
            && geometry.RowCount == tableGeometry.RowCount && geometry.ColumnCount == tableGeometry.ColumnCount
            && (row is null || geometry.RowIndex is not null)
            && (cell is null || row?.Table is { IsSupported: true, IsConsistent: true } rowGeometry
                && rowGeometry.RowIndex == geometry.RowIndex && rowGeometry.RowCount == geometry.RowCount
                && rowGeometry.ColumnCount == geometry.ColumnCount && rowGeometry.Source == SemanticSource.NativeHtmlProperty
                && geometry.RowIndex is not null && geometry.ColumnIndex is not null);
        if (!valid)
            return new(table.Id, row?.Id, geometry with
            { IsSupported = false, ReasonCode = tableGeometry.ReasonCode ?? geometry.ReasonCode ?? "TABLE_OWNERSHIP_OR_GEOMETRY_UNKNOWN" }, [], []);
        var rows = ImmutableArray.CreateBuilder<TableHeaderObservation>();
        var columns = ImmutableArray.CreateBuilder<TableHeaderObservation>();
        if (cell is not null)
        {
            foreach (var header in snapshot.Nodes.Where(node => node.Role is "rowheader" or "columnheader"))
            {
                if (header.Id == cell.Id || string.IsNullOrWhiteSpace(header.Name) || header.Table is not { IsSupported: true, IsConsistent: true } h
                    || h.RowIndex is null || h.ColumnIndex is null
                    || !SameDocument(header, table) || h.Source != SemanticSource.NativeHtmlProperty
                    || h.RowCount != geometry.RowCount || h.ColumnCount != geometry.ColumnCount) continue;
                var hp = Path(snapshot, header);
                var hr = hp?.TakeWhile(node => node.Id != table.Id).FirstOrDefault(node => node.Role == "row");
                if (hp?.FirstOrDefault(IsTable)?.Id != table.Id || hr?.Table is not { IsSupported: true, IsConsistent: true } hg
                    || hg.Source != SemanticSource.NativeHtmlProperty || hg.RowIndex != h.RowIndex
                    || hg.RowCount != h.RowCount || hg.ColumnCount != h.ColumnCount
                    || hp.TakeWhile(node => node.Role != "RootWebArea").Any(node => !SameDocument(node, table))) continue;
                var observed = new TableHeaderObservation(header.Id, header.Name, h.Source);
                if (header.Role == "rowheader" && h.HeaderScope == "row" && h.RowIndex == geometry.RowIndex) rows.Add(observed);
                if (header.Role == "columnheader" && h.HeaderScope == "col" && h.ColumnIndex == geometry.ColumnIndex) columns.Add(observed);
            }
        }
        return new(table.Id, row?.Id, geometry, rows.ToImmutable(), columns.ToImmutable());
    }

    public static (ReaderOutcome Outcome, AccessibleNode? Target, string? Message) Move(
        AccessibilitySnapshot snapshot, AccessibleNode? current, ReaderCommand command)
    {
        var context = Build(snapshot, current);
        if (context is null) return (ReaderOutcome.Unknown, null, "Table ownership or native geometry is not observed.");
        if (!context.Geometry.IsSupported)
            return (ReaderOutcome.Unsupported, null, $"Table navigation unsupported: {context.Geometry.ReasonCode}.");
        var cells = snapshot.Nodes.Where(IsCell).Where(node =>
        {
            var c = Build(snapshot, node);
            return c?.TableNodeId == context.TableNodeId && c.Geometry.IsSupported && c.Geometry.ColumnIndex is not null;
        }).OrderBy(node => node.Table!.RowIndex).ThenBy(node => node.Table!.ColumnIndex).ToArray();
        if (cells.GroupBy(node => (node.Table!.RowIndex, node.Table.ColumnIndex)).Any(group => group.Count() > 1))
            return (ReaderOutcome.Unknown, null, "Table cell coordinates conflict.");
        var geometry = context.Geometry;
        AccessibleNode? target;
        if (command is ReaderCommand.NextCell or ReaderCommand.PreviousCell)
        {
            var backwards = command == ReaderCommand.PreviousCell;
            if (geometry.ColumnIndex is null && geometry.RowIndex is not null)
                cells = cells.Where(node => node.Table!.RowIndex == geometry.RowIndex).ToArray();
            var index = Array.FindIndex(cells, node => node.Id == current?.Id
                || node.Table!.RowIndex == geometry.RowIndex && node.Table.ColumnIndex == geometry.ColumnIndex);
            if (geometry.ColumnIndex is not null && index < 0)
                return (ReaderOutcome.Unknown, null, "Current table cell is not exposed.");
            var next = index < 0 ? backwards ? cells.Length - 1 : 0 : index + (backwards ? -1 : 1);
            target = next >= 0 && next < cells.Length ? cells[next] : null;
        }
        else
        {
            if (geometry.ColumnIndex is null || geometry.RowIndex is null)
                return (ReaderOutcome.Unsupported, null, "Select a table cell before row or column navigation.");
            var row = geometry.RowIndex + (command == ReaderCommand.NextRow ? 1 : command == ReaderCommand.PreviousRow ? -1 : 0);
            var column = geometry.ColumnIndex + (command == ReaderCommand.NextColumn ? 1 : command == ReaderCommand.PreviousColumn ? -1 : 0);
            target = cells.FirstOrDefault(node => node.Table!.RowIndex == row && node.Table.ColumnIndex == column);
        }
        return target is null ? (ReaderOutcome.NoMatch, null, "No exposed cell at the requested table boundary or position.")
            : (ReaderOutcome.CursorMoved, target, null);
    }

    public static string Describe(ReaderTableContext? context)
    {
        if (context is null) return "";
        var g = context.Geometry;
        if (!g.IsSupported) return $", table navigation unsupported: {g.ReasonCode}";
        var dimensions = FormattableString.Invariant($", native table {g.RowCount} rows, {g.ColumnCount} columns");
        if (g.ColumnIndex is null) return dimensions;
        static string Headers(ImmutableArray<TableHeaderObservation> headers) => headers.IsEmpty ? "unknown" : string.Join("; ", headers.Select(h => h.Name));
        return dimensions + string.Create(CultureInfo.InvariantCulture, $", row {g.RowIndex}, column {g.ColumnIndex}")
            + $", row header {Headers(context.RowHeaders)}, column header {Headers(context.ColumnHeaders)}";
    }
}
