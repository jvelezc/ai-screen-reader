using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>Exposed semantics only. Editable form values and diagnostic labels are deliberately absent.</summary>
public sealed record AccessibleNode(
    string Id,
    string Role,
    string Name,
    bool IsFocused = false,
    bool IsDisabled = false,
    int? HeadingLevel = null,
    string? Checked = null,
    bool? Expanded = null,
    bool? Selected = null,
    bool IsRequired = false,
    string? Invalid = null,
    string? Description = null,
    string? ActiveDescendantId = null,
    bool IsModal = false,
    string? ParentId = null,
    LiveRegionObservation? LiveRegion = null,
    RequiredObservation? Required = null,
    LogicalSetPosition? LogicalSet = null,
    ProtectedObservation? Protection = null,
    ExposedText? Text = null,
    string? FrameId = null,
    string? DocumentId = null,
    RangeObservation? Range = null,
    HierarchyLevelObservation? HierarchyLevel = null);

public enum SemanticSource { ChromiumAccessibility, NativeHtmlProperty, DeclaredAria }

/// <summary>Null means unobserved; false is an explicit source observation.</summary>
public sealed record RequiredObservation(bool Value, SemanticSource Source);

public sealed record ProtectedObservation(bool Value, SemanticSource Source);

/// <summary>Author-declared logical position, independent of currently exposed peers.</summary>
public sealed record LogicalSetPosition(int? Position, int? Size, bool IsValid, SemanticSource Source);

/// <summary>Exposed live-root text, after the same editable-content exclusion as the snapshot.</summary>
public sealed record LiveRegionObservation(string Politeness, bool IsAtomic, bool IsBusy, string Text);

public sealed record AccessibleContainer(string NodeId, string Role, string Name);

/// <summary>Position among currently exposed peers, never an inferred logical set size.</summary>
public sealed record ExposedPeerPosition(string ContainerId, int Position, int Count);

/// <summary>Outer-to-inner exposed containers surrounding the announced node.</summary>
public sealed record ReaderContext(
    string NodeId, ImmutableArray<AccessibleContainer> Ancestors,
    bool AncestryComplete, ExposedPeerPosition? ExposedPeers,
    LogicalSetPosition? LogicalSet = null,
    HierarchyLevelObservation? HierarchyLevel = null);

/// <summary>IDs are scoped to a live document, never treated as cross-session identities.</summary>
public sealed record AccessibilitySnapshot(
    string DocumentId,
    ImmutableArray<AccessibleNode> Nodes,
    ImmutableArray<string> Limitations)
{
    public ImmutableArray<FrameObservation> Frames { get; init; } = [];

    public AccessibleNode? FocusedNode =>
        Nodes.FirstOrDefault(node => node.IsFocused && node.Role != "RootWebArea")
        ?? Nodes.FirstOrDefault(node => node.IsFocused);

    public AccessibleNode? EffectiveFocusedNode =>
        Nodes.FirstOrDefault(node => node.Id == FocusedNode?.ActiveDescendantId) ?? FocusedNode;
}

/// <summary>Observation and keyboard operations; no locator clicks or injected focus.</summary>
public interface IScreenReaderBrowser
{
    Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default);
    Task PressKeyAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class BrowserOperationException(string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public string Code { get; } = code;
}
