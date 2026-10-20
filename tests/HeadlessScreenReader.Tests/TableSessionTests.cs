using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class TableSessionTests
{
    [Theory]
    [InlineData(3, 2, 2, 2, true)]
    [InlineData(3, 2, 1, 1, true)]
    [InlineData(3, 2, 3, 2, true)]
    [InlineData(0, 0, null, null, true)]
    [InlineData(-1, 2, null, null, false)]
    [InlineData(3, -1, null, null, false)]
    [InlineData(3, 2, 0, 1, false)]
    [InlineData(3, 2, 4, 1, false)]
    [InlineData(3, 2, 2, 3, false)]
    [InlineData(3, 2, 2, 0, false)]
    [InlineData(3, 2, null, 1, false)]
    public void TableGeometry_ValidatesNativePositions(int rows, int columns, int? row, int? column, bool expected)
    {
        Assert.Equal(expected, new TableObservation(rows, columns, row, column, null, true, null,
            SemanticSource.NativeHtmlProperty).IsConsistent);
    }

    [Theory]
    [InlineData(ReaderCommand.NextCell, "r3c1", ReaderOutcome.CursorMoved)]
    [InlineData(ReaderCommand.PreviousCell, "r2c1", ReaderOutcome.CursorMoved)]
    [InlineData(ReaderCommand.NextRow, "r3c2", ReaderOutcome.CursorMoved)]
    [InlineData(ReaderCommand.PreviousRow, "r1c2", ReaderOutcome.CursorMoved)]
    [InlineData(ReaderCommand.NextColumn, "r2c2", ReaderOutcome.NoMatch)]
    [InlineData(ReaderCommand.PreviousColumn, "r2c1", ReaderOutcome.CursorMoved)]
    public async Task TableMovement_PreservesCoordinatesAndFocus(ReaderCommand command, string expected, ReaderOutcome outcome)
    {
        var browser = new TableBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        await SelectPrice(reader);
        var step = await reader.ExecuteAsync(command);
        Assert.Equal(expected, step.CursorNodeId);
        Assert.Equal(outcome, step.Outcome);
        Assert.Equal("root", step.FocusedNodeId);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.Empty(browser.Keys);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task TableHeaders_AreSourceTaggedAndReadCurrentRetainsContext()
    {
        using var reader = new ScreenReaderSession(new TableBrowser(Snapshot()));
        var step = await SelectPrice(reader);
        var context = Assert.IsType<ReaderTableContext>(step.Table);
        Assert.Equal(new("r2c1", "Apples", SemanticSource.NativeHtmlProperty), Assert.Single(context.RowHeaders));
        Assert.Equal(new("r1c2", "Price", SemanticSource.NativeHtmlProperty), Assert.Single(context.ColumnHeaders));
        Assert.Equal("r2", context.RowNodeId);
        Assert.Equal("table", context.TableNodeId);
        Assert.Equal("$3, cell, native table 3 rows, 2 columns, row 2, column 2, row header Apples, column header Price", step.Announcement);
        var read = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(step.Announcement, read.Announcement);
        Assert.Equal(context.Geometry, read.Table!.Geometry);
        Assert.Equal(context.RowHeaders.ToArray(), read.Table.RowHeaders.ToArray());
        Assert.Equal(context.ColumnHeaders.ToArray(), read.Table.ColumnHeaders.ToArray());
        Assert.Equal(2, reader.Timeline.Last(e => e.Kind == ReaderEventKind.Announcement).Table?.RowIndex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TableHeaders_MissingUnnamedForeignAndUnscopedRemainUnknown(int scenario)
    {
        var snapshot = Snapshot();
        var browser = new TableBrowser(snapshot);
        using var reader = new ScreenReaderSession(browser);
        await SelectPrice(reader);
        snapshot = snapshot with
        {
            Nodes = snapshot.Nodes.Select(n => n.Id is "r1c2" or "r2c1" ? scenario switch
            {
                0 => n with { Role = "cell" },
                1 => n with { Name = "" },
                2 => n with { DocumentId = "foreign" },
                _ => n with { Table = n.Table! with { HeaderScope = "" } }
            } : n).ToImmutableArray()
        };
        browser.Snapshot = snapshot;
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Empty(step.Table!.RowHeaders);
        Assert.Empty(step.Table.ColumnHeaders);
        Assert.Contains("row header unknown, column header unknown", step.Announcement, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, ReaderOutcome.Unknown)]
    [InlineData(1, ReaderOutcome.Unknown)]
    [InlineData(2, ReaderOutcome.Unsupported)]
    [InlineData(3, ReaderOutcome.Unsupported)]
    [InlineData(4, ReaderOutcome.Unsupported)]
    [InlineData(5, ReaderOutcome.Unsupported)]
    [InlineData(6, ReaderOutcome.Unsupported)]
    public async Task TableOwnership_MalformedOrForeignCannotManufactureCellNavigation(int scenario, ReaderOutcome expected)
    {
        var browser = new TableBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        await SelectPrice(reader);
        browser.Snapshot = browser.Snapshot with
        {
            Nodes = browser.Snapshot.Nodes.Select(n => n.Id != "r2c2" ? n : scenario switch
            {
                0 => n with { ParentId = "missing" },
                1 => n with { ParentId = n.Id },
                2 => n with { DocumentId = "other-document" },
                3 => n with { DocumentId = null },
                4 => n with { Table = n.Table! with { RowIndex = 3 } },
                5 => n with { Table = n.Table! with { RowCount = 20 } },
                _ => n with { Table = n.Table! with { Source = SemanticSource.DeclaredAria } }
            }).ToImmutableArray()
        };
        var step = await reader.ExecuteAsync(ReaderCommand.NextCell);
        Assert.Equal(expected, step.Outcome);
        Assert.Equal("r2c2", step.CursorNodeId);
        Assert.Empty(browser.Keys);
        Assert.Null(step.BarrierCode);
    }

    [Fact]
    public async Task TableBoundaries_DoNotWrapOrEnterAnotherTable()
    {
        var snapshot = Snapshot();
        var another = Node("other", "table", "Other", "root", Geometry());
        var browser = new TableBrowser(snapshot with { Nodes = snapshot.Nodes.Add(another) });
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextTable);
        var last = await reader.ExecuteAsync(ReaderCommand.PreviousCell);
        Assert.Equal("r3c2", last.CursorNodeId);
        var end = await reader.ExecuteAsync(ReaderCommand.NextCell);
        Assert.Equal(ReaderOutcome.NoMatch, end.Outcome);
        Assert.Equal(last.CursorNodeId, end.CursorNodeId);
        await reader.ExecuteAsync(ReaderCommand.NextTable);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextTable)).Outcome);
        Assert.Equal("table", (await reader.ExecuteAsync(ReaderCommand.PreviousTable)).CursorNodeId);
        var first = await reader.ExecuteAsync(ReaderCommand.NextCell);
        Assert.Equal("r1c1", first.CursorNodeId);
        var start = await reader.ExecuteAsync(ReaderCommand.PreviousCell);
        Assert.Equal(ReaderOutcome.NoMatch, start.Outcome);
        Assert.Equal(first.CursorNodeId, start.CursorNodeId);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.PreviousTable)).Outcome);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(ReaderCommand.NextTable)]
    [InlineData(ReaderCommand.PreviousTable)]
    [InlineData(ReaderCommand.NextCell)]
    [InlineData(ReaderCommand.PreviousCell)]
    [InlineData(ReaderCommand.NextRow)]
    [InlineData(ReaderCommand.PreviousRow)]
    [InlineData(ReaderCommand.NextColumn)]
    [InlineData(ReaderCommand.PreviousColumn)]
    public async Task TableCommands_FocusModeNeverDispatchesInput(ReaderCommand command)
    {
        var browser = new TableBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await reader.ExecuteAsync(command);
        Assert.Equal(ReaderOutcome.Unsupported, step.Outcome);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TableRefresh_StaleCellAndNewDocumentResetCursor(bool newDocument)
    {
        var browser = new TableBrowser(Snapshot());
        using var reader = new ScreenReaderSession(browser);
        await SelectPrice(reader);
        browser.Snapshot = newDocument ? new("replacement", [Node("new-root", "RootWebArea", "New", null)], [])
            : browser.Snapshot with { Nodes = browser.Snapshot.Nodes.Where(n => n.Id != "r2c2").ToImmutableArray() };
        var step = await reader.ExecuteAsync(ReaderCommand.NextCell);
        Assert.Equal(ReaderOutcome.Unknown, step.Outcome);
        Assert.Null(step.CursorNodeId);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task TableCoordinates_ConflictingCellsRemainUnknown()
    {
        var snapshot = Snapshot();
        var duplicate = snapshot.Nodes.Single(n => n.Id == "r2c2") with { Id = "duplicate" };
        var browser = new TableBrowser(snapshot with { Nodes = snapshot.Nodes.Add(duplicate) });
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextTable);
        var step = await reader.ExecuteAsync(ReaderCommand.NextCell);
        Assert.Equal(ReaderOutcome.Unknown, step.Outcome);
        Assert.Equal("table", step.CursorNodeId);
        Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task TableEmptyAndUnsupportedGeometry_StayExplicit()
    {
        var browser = new TableBrowser(new("doc", [Node("empty", "table", "Empty", null, Geometry(0, 0))], []));
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.NextTable);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextCell)).Outcome);
        browser.Snapshot = browser.Snapshot with
        {
            Nodes = [browser.Snapshot.Nodes[0] with
        { Table = Geometry(0, 0) with { IsSupported = false, ReasonCode = "TABLE_SPAN_UNSUPPORTED" } }]
        };
        var step = await reader.ExecuteAsync(ReaderCommand.NextCell);
        Assert.Equal(ReaderOutcome.Unsupported, step.Outcome);
        Assert.Contains("TABLE_SPAN_UNSUPPORTED", step.Announcement, StringComparison.Ordinal);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(ReaderCommand.NextRow)]
    [InlineData(ReaderCommand.PreviousRow)]
    [InlineData(ReaderCommand.NextColumn)]
    [InlineData(ReaderCommand.PreviousColumn)]
    public async Task TableDirections_RequireCellBeforeCoordinateMovement(ReaderCommand command)
    {
        using var reader = new ScreenReaderSession(new TableBrowser(Snapshot()));
        await reader.ExecuteAsync(ReaderCommand.NextTable);
        var step = await reader.ExecuteAsync(command);
        Assert.Equal(ReaderOutcome.Unsupported, step.Outcome);
        Assert.Equal("table", step.CursorNodeId);
        Assert.Equal(0, step.BrowserKeystrokes);
    }

    [Fact]
    public async Task TableRowEntry_UsesOnlyCellsInCurrentRow()
    {
        using var reader = new ScreenReaderSession(new TableBrowser(Snapshot()));
        await SelectPrice(reader);
        await reader.ExecuteAsync(ReaderCommand.MovePrevious);
        var row = await reader.ExecuteAsync(ReaderCommand.MovePrevious);
        Assert.Equal("r2", row.CursorNodeId);
        Assert.Equal("r2c1", (await reader.ExecuteAsync(ReaderCommand.NextCell)).CursorNodeId);
        await reader.ExecuteAsync(ReaderCommand.MovePrevious);
        Assert.Equal("r2c2", (await reader.ExecuteAsync(ReaderCommand.PreviousCell)).CursorNodeId);
    }

    [Fact]
    public async Task TableDiscovery_EmptySnapshotAndMissingNativeGeometryRemainExplicit()
    {
        var browser = new TableBrowser(new("doc", [], []));
        using var reader = new ScreenReaderSession(browser);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextTable)).Outcome);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.PreviousTable)).Outcome);
        browser.Snapshot = new("doc", [Node("table", "table", "Unobserved", null)], []);
        await reader.ExecuteAsync(ReaderCommand.NextTable);
        var unknown = await reader.ExecuteAsync(ReaderCommand.NextCell);
        Assert.Equal(ReaderOutcome.Unknown, unknown.Outcome);
        Assert.Null(unknown.Table);
        Assert.Empty(browser.Keys);
    }

    private static async Task<ReaderStep> SelectPrice(ScreenReaderSession reader)
    {
        await reader.ExecuteAsync(ReaderCommand.NextTable);
        await reader.ExecuteAsync(ReaderCommand.NextCell);
        await reader.ExecuteAsync(ReaderCommand.NextColumn);
        return await reader.ExecuteAsync(ReaderCommand.NextRow);
    }

    private static TableObservation Geometry(int rows = 3, int columns = 2, int? row = null, int? column = null, string? scope = null) =>
        new(rows, columns, row, column, scope, true, null, SemanticSource.NativeHtmlProperty);

    private static AccessibleNode Node(string id, string role, string name, string? parent, TableObservation? table = null) =>
        new(id, role, name, ParentId: parent, DocumentId: "doc", Table: table, IsFocused: id == "root");

    private static AccessibilitySnapshot Snapshot() => new("doc", [
        Node("root", "RootWebArea", "Root", null), Node("table", "table", "Products", "root", Geometry()),
        Node("r1", "row", "", "table", Geometry(row: 1)),
        Node("r1c1", "columnheader", "Product", "r1", Geometry(row: 1, column: 1, scope: "col")),
        Node("r1c2", "columnheader", "Price", "r1", Geometry(row: 1, column: 2, scope: "col")),
        Node("r2", "row", "", "table", Geometry(row: 2)),
        Node("r2c1", "rowheader", "Apples", "r2", Geometry(row: 2, column: 1, scope: "row")),
        Node("r2c2", "cell", "$3", "r2", Geometry(row: 2, column: 2)),
        Node("r3", "row", "", "table", Geometry(row: 3)),
        Node("r3c1", "rowheader", "Pears", "r3", Geometry(row: 3, column: 1, scope: "row")),
        Node("r3c2", "cell", "$4", "r3", Geometry(row: 3, column: 2))], []);

    private sealed class TableBrowser(AccessibilitySnapshot snapshot) : IScreenReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = snapshot;
        public List<string> Keys { get; } = [];
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) { Keys.Add(key); return Task.CompletedTask; }
    }
}
