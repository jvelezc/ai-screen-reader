using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Cli.HostedControl;

internal sealed record PortalReaderCommandRequest(string Command, string ExpectedDocumentId);
internal sealed record PortalReaderCommandResponse(Guid SessionId, string ReaderProfile, string DocumentId,
    bool RequiresObservation, ReaderStep Step, int DispatchedInputs, bool GoalVerified);
[JsonNumberHandling(JsonNumberHandling.Strict)]
internal sealed record PortalReaderTimelineRequest(long AfterSequence, int Limit);
internal sealed record PortalReaderTimelineResponse(Guid SessionId, string ReaderProfile, ImmutableArray<ReaderEvent> Events,
    long NextAfterSequence, bool HasMore, bool TimelineTruncated);

/// <summary>Only pure browse operations. New Domain commands are denied until explicitly reviewed.</summary>
internal static class PortalReaderCommands
{
    internal static readonly ImmutableDictionary<string, ReaderCommand> Allowed = ReadOnlyReaderCommands.Allowed;
}

internal sealed partial class PortalReaderSessions
{
    internal const int MaximumTimelineEvents = 256;

    public async Task<PortalReaderCommandResponse> CommandAsync(PortalIdentity actor, Guid organization, Guid session,
        PortalReaderCommandRequest request, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var entry = await FindAsync(actor, organization, session, token).ConfigureAwait(false);
            if (request.Command is null || !PortalReaderCommands.Allowed.TryGetValue(request.Command, out var command)
                || string.IsNullOrWhiteSpace(request.ExpectedDocumentId) || request.ExpectedDocumentId.Length > 2048)
                throw new PortalFault(400, "CONTROL_READER_COMMAND_INVALID");
            if (entry.PublishedDocumentId is null) throw new PortalFault(409, "CONTROL_READER_OBSERVATION_REQUIRED");
            if (entry.PublishedDocumentId != request.ExpectedDocumentId) throw new PortalFault(409, "CONTROL_READER_DOCUMENT_CHANGED");
            if (entry.Observations >= MaximumObservations) throw new PortalFault(409, "CONTROL_READER_BUDGET_EXHAUSTED");
            try
            {
                entry.CommandSnapshot = null;
                entry.CommandCaptureWindow = null;
                entry.Reader ??= new ScreenReaderSession(new ReadOnlyReaderBrowser(async () =>
                {
                    if (entry.Observations >= MaximumObservations) throw new PortalFault(409, "CONTROL_READER_BUDGET_EXHAUSTED");
                    entry.Observations++;
                    var start = _time.GetElapsedTime(entry.OpenedAt).TotalMilliseconds;
                    var snapshot = await entry.Browser.CaptureAsync().ConfigureAwait(false);
                    var end = _time.GetElapsedTime(entry.OpenedAt).TotalMilliseconds;
                    var observed = new PortalReaderSnapshotResponse(entry.Id, ScreenReaderSession.Profile, snapshot, 0, false);
                    if (snapshot.Nodes.Length > MaximumNodes || TooLarge(observed))
                        throw new PortalFault(503, "CONTROL_READER_OBSERVATION_TOO_LARGE");
                    entry.CapturedDocumentId = snapshot.DocumentId;
                    entry.CommandSnapshot = snapshot;
                    entry.CommandCaptureWindow = new(start, end);
                    return snapshot;
                }), timeProvider: _time, maxTimelineEvents: MaximumTimelineEvents);
                // Hold ownership until the actual capture/reader operation joins, even if the caller disconnects.
                var step = await entry.Reader.ExecuteAsync(command, expectedDocumentId: request.ExpectedDocumentId,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                EnsureOpen();
                if (Expired(entry)) throw new PortalFault(404, "CONTROL_READER_NOT_FOUND");
                if (step.Outcome == ReaderOutcome.BrowserError || step.Mode != ReaderMode.Browse || step.BrowserKeystrokes != 0 || step.InputMayHaveOccurred
                    || step.Verification != VerificationStatus.NotRequested)
                    throw new PortalFault(503, "CONTROL_READER_COMMAND_UNCONFIRMED");
                var changed = entry.CapturedDocumentId != request.ExpectedDocumentId;
                if (changed) entry.PublishedDocumentId = null;
                var response = new PortalReaderCommandResponse(entry.Id, ScreenReaderSession.Profile,
                    entry.CapturedDocumentId!, changed, step, 0, false);
                if (TooLarge(response)) throw new PortalFault(503, "CONTROL_READER_COMMAND_TOO_LARGE");
                var snapshot = entry.CommandSnapshot ?? throw new PortalFault(503, "CONTROL_READER_COMMAND_UNCONFIRMED");
                var sourceCursor = entry.ReaderDocumentId == snapshot.DocumentId && entry.ReaderCaptureEpoch == snapshot.CaptureEpoch
                    && snapshot.Nodes.Any(node => node.Id == entry.ReaderCursorNodeId) ? entry.ReaderCursorNodeId : null;
                entry.Trace.Append(entry.Id, entry.Observations, snapshot, entry.CommandCaptureWindow!, sourceCursor, step, request.ExpectedDocumentId);
                entry.ReaderDocumentId = snapshot.DocumentId;
                entry.ReaderCaptureEpoch = snapshot.CaptureEpoch;
                entry.ReaderCursorNodeId = step.CursorNodeId;
                return response;
            }
            catch
            {
                if (_sessions.ContainsKey(entry.Id)) await CloseCoreAsync(entry).ConfigureAwait(false);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<PortalReaderTimelineResponse> TimelineAsync(PortalIdentity actor, Guid organization, Guid session,
        PortalReaderTimelineRequest request, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var entry = await FindAsync(actor, organization, session, token).ConfigureAwait(false);
            if (request.AfterSequence < 0 || request.Limit is < 1 or > 100)
                throw new PortalFault(400, "CONTROL_READER_PAGE_INVALID");
            var history = entry.Reader?.Timeline ?? [];
            var available = history.Where(item => item.Sequence > request.AfterSequence).ToArray();
            PortalReaderTimelineResponse Page(int count)
            {
                var events = available.Take(count).ToImmutableArray();
                return new(entry.Id, ScreenReaderSession.Profile, events,
                    events.LastOrDefault()?.Sequence ?? request.AfterSequence, count < available.Length,
                    entry.Reader?.TimelineTruncated ?? false);
            }
            var maximum = Math.Min(request.Limit, available.Length);
            var complete = Page(maximum);
            if (!TooLarge(complete)) return complete;
            // Bound serialization work as well as output size; never skip an oversized first event.
            var lower = 1; var upper = maximum - 1;
            PortalReaderTimelineResponse? fitting = null;
            while (lower <= upper)
            {
                var count = lower + (upper - lower) / 2;
                var candidate = Page(count);
                if (TooLarge(candidate)) upper = count - 1;
                else { fitting = candidate; lower = count + 1; }
            }
            return fitting ?? throw new PortalFault(503, "CONTROL_READER_TIMELINE_TOO_LARGE");
        }
        finally { _gate.Release(); }
    }

    private static bool TooLarge<T>(T response) => JsonSerializer.SerializeToUtf8Bytes(response, PortalJson.Options).Length > MaximumResponseBytes;

    private sealed class ReadOnlyReaderBrowser(Func<Task<AccessibilitySnapshot>> capture) : IScreenReaderBrowser
    {
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => capture();
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromException(new BrowserInputDeniedException("HOSTED_READER_INPUT_DENIED"));
        public Task PressListboxKeyAsync(ListboxKeyInput input, CancellationToken cancellationToken = default) =>
            Task.FromException(new BrowserInputDeniedException("HOSTED_READER_INPUT_DENIED"));
    }
}
