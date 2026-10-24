using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>Page completed command/state evidence; reads do not capture or navigate.</summary>
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed record PortalReaderTraceRequest(long AfterSequence, int Limit);

/// <summary>Transient, explicitly bounded trace; neither a durable receipt nor a verified objective.</summary>
internal sealed record PortalReaderTraceResponse(Guid SessionId, string ReaderProfile, string Schema, string Retention,
    ImmutableArray<ReaderNavigationTraceEntry> Entries, long NextAfterSequence, bool HasMore,
    bool TraceTruncated, string? TruncationReason, int RetainedCommands, long LatestCommandSequence,
    int DispatchedInputs, bool GoalVerified);

internal sealed class PortalReaderTrace
{
    internal const int MaximumCommands = 64;
    internal const int MaximumBytes = 8 * 1024 * 1024;
    internal const int MaximumEntryBytes = PortalReaderSessions.MaximumResponseBytes - 2048;
    internal const int MaximumPage = 20;
    private readonly List<ReaderNavigationTraceEntry> _entries = [];
    private int _bytes;
    private long _latest;
    private string? _reason;

    public void Append(Guid session, int captureSequence, AccessibilitySnapshot snapshot, EvidenceInterval window,
        string? sourceCursor, ReaderStep step, string expectedDocument)
    {
        _latest = step.Sequence;
        if (_reason is not null) return;
        if (_entries.Count >= MaximumCommands) { _reason = "CommandLimit"; return; }
        var prefix = $"{session:N}:command:{step.Sequence}";
        var capture = $"{session:N}:capture:{captureSequence}";
        ReaderTraceState State(string suffix, string? cursor) => new(prefix + suffix, capture, snapshot.DocumentId,
            snapshot.CaptureEpoch, ReaderMode.Browse, cursor, snapshot.FocusedNode?.Id, snapshot.FocusedNode?.ActiveDescendantId);
        var source = State(":source", sourceCursor);
        var result = State(":result", step.CursorNodeId);
        var edge = new ReaderTraceEdge(prefix + ":edge", source.Id, result.Id, step.Command, expectedDocument,
            step.Outcome, step.Verification, step.BrowserKeystrokes, step.InputMayHaveOccurred);
        var entry = new ReaderNavigationTraceEntry(step.Sequence, captureSequence, capture, snapshot, window,
            _entries.LastOrDefault()?.ResultState.Id, source, result, edge, step);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry, PortalJson.Options).Length;
        if (bytes > MaximumEntryBytes) { _reason = "EntryTooLarge"; return; }
        if (_bytes + bytes > MaximumBytes) { _reason = "ByteLimit"; return; }
        _entries.Add(entry); _bytes += bytes;
    }

    public PortalReaderTraceResponse Page(Guid session, PortalReaderTraceRequest request)
    {
        if (request.AfterSequence < 0 || request.Limit is < 1 or > MaximumPage)
            throw new PortalFault(400, "CONTROL_READER_PAGE_INVALID");
        var available = _entries.Where(item => item.Sequence > request.AfterSequence).ToArray();
        PortalReaderTraceResponse Response(int count)
        {
            var entries = available.Take(count).ToImmutableArray();
            return new(session, ScreenReaderSession.Profile, "hsr-reader-navigation-trace-v1", "Transient", entries,
                entries.LastOrDefault()?.Sequence ?? request.AfterSequence, count < available.Length,
                _reason is not null, _reason, _entries.Count, _latest, 0, false);
        }
        var maximum = Math.Min(request.Limit, available.Length);
        var complete = Response(maximum);
        if (Fits(complete)) return complete;
        var lower = 1; var upper = maximum - 1;
        PortalReaderTraceResponse? fitting = null;
        while (lower <= upper)
        {
            var count = lower + (upper - lower) / 2;
            var candidate = Response(count);
            if (Fits(candidate)) { fitting = candidate; lower = count + 1; }
            else upper = count - 1;
        }
        return fitting ?? throw new PortalFault(503, "CONTROL_READER_TRACE_TOO_LARGE");
    }

    private static bool Fits(PortalReaderTraceResponse response) =>
        JsonSerializer.SerializeToUtf8Bytes(response, PortalJson.Options).Length <= PortalReaderSessions.MaximumResponseBytes;
    public void Clear() { _entries.Clear(); _bytes = 0; _latest = 0; _reason = null; }
}

internal sealed partial class PortalReaderSessions
{
    public async Task<PortalReaderTraceResponse> TraceAsync(PortalIdentity actor, Guid organization, Guid session,
        PortalReaderTraceRequest request, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var entry = await FindAsync(actor, organization, session, token).ConfigureAwait(false);
            return entry.Trace.Page(entry.Id, request);
        }
        finally { _gate.Release(); }
    }
}
