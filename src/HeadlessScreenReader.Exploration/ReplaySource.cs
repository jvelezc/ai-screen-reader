using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

/// <summary>Single-document equivalence preserves ordered semantics and ownership, not live identities.</summary>
public static class ReplaySource
{
    public static ReplaySourceContract Create(AccessibilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.DocumentId) || snapshot.Nodes.IsDefaultOrEmpty || snapshot.Nodes.Length > 5000
            || snapshot.Frames.IsDefault || snapshot.Frames.Length != 1
            || snapshot.Limitations.IsDefault || snapshot.CaptureEpoch != 1)
            throw new ArgumentException("REPLAY_SOURCE_UNSUPPORTED", nameof(snapshot));
        var frame = snapshot.Frames[0];
        var nodes = snapshot.Nodes;
        if (frame.Status != FrameObservationStatus.Observed || string.IsNullOrWhiteSpace(frame.FrameId)
            || frame.ParentFrameId is not null || frame.OwnerNodeId is not null
            || frame.DocumentId != snapshot.DocumentId || frame.RootNodeId != nodes[0].Id
            || nodes[0].Role != "RootWebArea" || nodes[0].ParentId is not null
            || nodes.Any(n => string.IsNullOrEmpty(n.Id) || n.DocumentId != snapshot.DocumentId || n.FrameId != frame.FrameId)
            || nodes.Select(n => n.Id).Distinct(StringComparer.Ordinal).Count() != nodes.Length)
            throw new ArgumentException("REPLAY_SOURCE_UNSUPPORTED", nameof(snapshot));
        var ids = nodes.Select((node, index) => (node.Id, Index: index))
            .ToDictionary(n => n.Id, n => n.Index, StringComparer.Ordinal);
        for (var i = 1; i < nodes.Length; i++)
            if (nodes[i].ParentId is not { } parent || !ids.TryGetValue(parent, out var parentIndex) || parentIndex >= i)
                throw new ArgumentException("REPLAY_SOURCE_UNSUPPORTED", nameof(snapshot));
        if (nodes.Any(n => n.ActiveDescendantId is { } active && !ids.ContainsKey(active)))
            throw new ArgumentException("REPLAY_SOURCE_UNSUPPORTED", nameof(snapshot));
        string? Normalize(string? id) => id is null ? null : "n" + ids[id];
        var normalized = nodes.Select(node => node with
        {
            Id = Normalize(node.Id)!,
            ParentId = Normalize(node.ParentId),
            ActiveDescendantId = Normalize(node.ActiveDescendantId),
            DocumentId = "document",
            FrameId = "main"
        });
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = "same-fixture-reader-start-v1",
            Mode = ReaderMode.Browse,
            Cursor = (string?)null,
            snapshot.CaptureEpoch,
            Nodes = normalized,
            FrameStatus = frame.Status,
            frame.ReasonCode,
            snapshot.Limitations
        });
        if (bytes.Length > 4 * 1024 * 1024)
            throw new ArgumentException("REPLAY_SOURCE_LIMIT", nameof(snapshot));
        return new("same-fixture-reader-start-v1", Convert.ToHexStringLower(SHA256.HashData(bytes)), nodes.Length);
    }
}
