using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>One-based native positions and dimensions, including hidden rows/cells; not exposed peer counts.</summary>
public sealed record TableObservation(
    int RowCount, int ColumnCount, int? RowIndex, int? ColumnIndex,
    string? HeaderScope, bool IsSupported, string? ReasonCode,
    SemanticSource Source)
{
    public bool IsConsistent => RowCount >= 0 && ColumnCount >= 0
        && (RowIndex is null || RowIndex > 0 && RowIndex <= RowCount)
        && (ColumnIndex is null || ColumnIndex > 0 && ColumnIndex <= ColumnCount)
        && (ColumnIndex is null || RowIndex is not null);
}

public sealed record TableHeaderObservation(string NodeId, string Name, SemanticSource Source);

/// <summary>Context resolved from native geometry and exposed same-document ownership. Empty headers remain unknown.</summary>
public sealed record ReaderTableContext(
    string TableNodeId, string? RowNodeId, TableObservation Geometry,
    ImmutableArray<TableHeaderObservation> RowHeaders,
    ImmutableArray<TableHeaderObservation> ColumnHeaders);
