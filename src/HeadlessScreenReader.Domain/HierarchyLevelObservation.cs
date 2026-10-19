namespace HeadlessScreenReader.Domain;

/// <summary>An exposed hierarchical level, not a parent relationship inferred from flat order.</summary>
public sealed record HierarchyLevelObservation(int Value, SemanticSource Source)
{
    public bool IsValid => Value > 0;
}
