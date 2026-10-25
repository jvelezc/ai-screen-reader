using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Mcp;

public sealed partial class HostedReaderClient
{
    public Task<ToolReply> TimelineAsync(string sessionId, long afterSequence = 0, int limit = 100, CancellationToken token = default)
    {
        if (afterSequence < 0 || limit is < 1 or > 100) return Task.FromResult(ToolReply.Error("PAGE_BOUNDS_INVALID"));
        return InSessionAsync(sessionId, async () =>
        {
            using var document = await _transport.SendAsync(HttpMethod.Post, Resource + "/timeline", new { afterSequence, limit }).ConfigureAwait(false);
            var result = Parse<HostedTimeline>(document); Profile(result.ReaderProfile);
            Require(result.SessionId == _session && !result.Events.IsDefault && result.Events.Length <= limit);
            var previous = afterSequence;
            foreach (var item in result.Events)
            {
                Require(item.Sequence > previous && double.IsFinite(item.ElapsedMilliseconds) && item.ElapsedMilliseconds >= 0);
                Require(item.Kind is not (ReaderEventKind.InputAttempted or ReaderEventKind.InputCompleted or ReaderEventKind.GoalVerified));
                previous = item.Sequence;
            }
            Require(result.NextAfterSequence == previous && (!result.HasMore || result.Events.Length > 0));
            ToolReply Page(int count) => new("timeline", SessionId, ScreenReaderSession.Profile, result with
            {
                Events = result.Events.Take(count).ToImmutableArray(),
                NextAfterSequence = count == 0 ? afterSequence : result.Events[count - 1].Sequence,
                HasMore = count < result.Events.Length || result.HasMore
            });
            return FitPage(Page, result.Events.Length, result.Events.Length == 0, "HOSTED_TIMELINE_ENTRY_TOO_LARGE");
        }, token);
    }

    public Task<ToolReply> TraceAsync(string sessionId, long afterSequence = 0, int limit = 20, CancellationToken token = default)
    {
        if (afterSequence < 0 || limit is < 1 or > 20) return Task.FromResult(ToolReply.Error("PAGE_BOUNDS_INVALID"));
        return InSessionAsync(sessionId, async () =>
        {
            using var document = await _transport.SendAsync(HttpMethod.Post, Resource + "/trace", new { afterSequence, limit }).ConfigureAwait(false);
            var result = Parse<HostedTrace>(document); Profile(result.ReaderProfile);
            Require(result.SessionId == _session && result.Schema == "hsr-reader-navigation-trace-v1" && result.Retention == "Transient"
                && result.DispatchedInputs == 0 && !result.GoalVerified && !result.Entries.IsDefault && result.Entries.Length <= limit
                && result.RetainedCommands is >= 0 and <= 64 && result.RetainedCommands >= result.Entries.Length
                && result.LatestCommandSequence >= 0);
            Require(result.TraceTruncated ? result.TruncationReason is "CommandLimit" or "ByteLimit" or "EntryTooLarge" : result.TruncationReason is null);
            var previous = afterSequence; var index = 0;
            foreach (var entry in result.Entries)
            {
                Require(entry.Sequence > previous && entry.Sequence <= result.LatestCommandSequence);
                ValidateTrace(entry, document.RootElement.GetProperty("entries")[index++]);
                previous = entry.Sequence;
            }
            Require(result.NextAfterSequence == previous && (!result.HasMore || result.Entries.Length > 0));
            ToolReply Page(int count) => new("trace", SessionId, ScreenReaderSession.Profile, result with
            {
                Entries = result.Entries.Take(count).ToImmutableArray(),
                NextAfterSequence = count == 0 ? afterSequence : result.Entries[count - 1].Sequence,
                HasMore = count < result.Entries.Length || result.HasMore
            });
            return FitPage(Page, result.Entries.Length, result.Entries.Length == 0, "HOSTED_TRACE_ENTRY_TOO_LARGE");
        }, token);
    }

    private static void ValidateTrace(ReaderNavigationTraceEntry entry, JsonElement raw)
    {
        Require(entry.CaptureSequence > 0 && !string.IsNullOrWhiteSpace(entry.CaptureId)
            && entry.Snapshot.Nodes.Length <= 2048 && !string.IsNullOrWhiteSpace(entry.Snapshot.DocumentId)
            && entry.Step.Sequence == entry.Sequence && ReadOnlyStep(entry.Step));
        var source = entry.SourceState; var result = entry.ResultState; var edge = entry.Edge;
        Require(source.Id != result.Id && !string.IsNullOrWhiteSpace(source.Id) && !string.IsNullOrWhiteSpace(result.Id)
            && source.CaptureId == entry.CaptureId && result.CaptureId == entry.CaptureId
            && source.DocumentId == entry.Snapshot.DocumentId && result.DocumentId == source.DocumentId
            && source.CaptureEpoch == entry.Snapshot.CaptureEpoch && result.CaptureEpoch == source.CaptureEpoch
            && source.Mode == ReaderMode.Browse && result.Mode == ReaderMode.Browse
            && result.CursorNodeId == entry.Step.CursorNodeId
            && source.FocusedNodeId == entry.Snapshot.FocusedNode?.Id && result.FocusedNodeId == source.FocusedNodeId
            && source.ActiveDescendantNodeId == entry.Snapshot.FocusedNode?.ActiveDescendantId && result.ActiveDescendantNodeId == source.ActiveDescendantNodeId);
        Require(!string.IsNullOrWhiteSpace(edge.Id) && edge.SourceStateId == source.Id && edge.ResultStateId == result.Id
            && edge.Command == entry.Step.Command && edge.Outcome == entry.Step.Outcome
            && edge.Verification == entry.Step.Verification && edge.BrowserKeystrokes == 0 && !edge.InputMayHaveOccurred
            && raw.GetProperty("edge").GetProperty("mechanism").GetString() == "ReaderCursor");
        RequireExplicitStep(raw.GetProperty("step"));
        Require(double.IsFinite(entry.CaptureWindow.StartMilliseconds) && double.IsFinite(entry.CaptureWindow.EndMilliseconds)
            && entry.CaptureWindow.StartMilliseconds >= 0 && entry.CaptureWindow.EndMilliseconds >= entry.CaptureWindow.StartMilliseconds);
    }

    private static void RequireExplicitStep(JsonElement raw) => Require(raw.TryGetProperty("browserKeystrokes", out _)
        && raw.TryGetProperty("inputMayHaveOccurred", out _) && raw.TryGetProperty("verification", out _) && raw.TryGetProperty("mode", out _));
}
