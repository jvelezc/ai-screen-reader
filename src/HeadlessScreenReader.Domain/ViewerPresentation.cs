using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

public enum PrivateViewPhase { Loading, Viewing, Paused, Excluded, Unavailable, Ended }
public enum PrivateViewAction { Pause, Resume, Refresh, End }
public sealed record PrivateViewIntent(PrivateViewAction Action, int Revision);
/// <summary>Display-only facts. Carries no connection credentials, target URL or input authority.</summary>
public sealed record PrivateViewContent(long Epoch, long ObservationOrdinal, long PixelBaselineOrdinal,
    ImmutableArray<string> Lines, bool Truncated, ImmutableArray<byte> Png, bool PriorSimulatedAssistance)
{
    public static PrivateViewContent Create(AccessibilitySnapshot snapshot, long ordinal, long baseline,
        ImmutableArray<byte> png, bool assisted)
    {
        if (snapshot.CaptureEpoch < 1 || ordinal < 0 || baseline < 0 || baseline > ordinal || snapshot.Nodes.IsDefault
            || !ViewerPixelFrame.HasBoundedPngEnvelope(png)) throw new BrowserOperationException("VIEW_CLIENT_CONTENT_INVALID");
        var truncated = snapshot.Nodes.Length > 128;
        string Clip(string value, int limit) { if (value.Length <= limit) return value; truncated = true; return value[..limit] + "…"; }
        var lines = snapshot.Nodes.Take(128).Select(n => $"{Clip(n.Role, 80)}: {Clip(n.Name, 512)}{(n.IsFocused ? " [focused]" : "")}{(n.IsDisabled ? " [disabled]" : "")}").ToImmutableArray();
        return new(snapshot.CaptureEpoch, ordinal, baseline, lines, truncated, png, assisted);
    }
}
