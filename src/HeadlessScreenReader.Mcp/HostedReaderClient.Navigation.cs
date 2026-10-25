using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Mcp;

public sealed partial class HostedReaderClient
{
    public Task<ToolReply> SnapshotAsync(string sessionId, int offset = 0, int limit = 80,
        string? observationId = null, CancellationToken token = default)
    {
        if (offset < 0 || limit is < 1 or > 100) return Task.FromResult(ToolReply.Error("PAGE_BOUNDS_INVALID"));
        return InSessionAsync(sessionId, async () =>
        {
            if (_sessionUnconfirmed) return ToolReply.Error("HOSTED_SESSION_UNCONFIRMED", SessionId);
            if (offset == 0 && observationId is null)
            {
                InvalidateSnapshot(); _publishedDocument = null;
                using var document = await _transport.SendAsync(HttpMethod.Post, Resource + "/observations", new { }).ConfigureAwait(false);
                var captured = Parse<HostedSnapshot>(document); Profile(captured.ReaderProfile);
                Require(captured.SessionId == _session && captured.DispatchedInputs == 0 && !captured.GoalVerified
                    && captured.Snapshot.Nodes.Length <= 2048 && !string.IsNullOrWhiteSpace(captured.Snapshot.DocumentId));
                _snapshot = captured.Snapshot; _observationId = Guid.NewGuid().ToString("N");
            }
            else
            {
                if (_snapshot is null || observationId != _observationId) return ToolReply.Error("OBSERVATION_CHANGED", SessionId);
                // Even immutable cached pages need current upstream authority, lifetime and quarantine checks.
                using var document = await _transport.SendAsync(HttpMethod.Get, Resource).ConfigureAwait(false);
                var current = Parse<HostedSession>(document); Profile(current.ReaderProfile);
                Require(current.SessionId == _session && current.OrganizationId == _options.Organization
                    && current.State == "Open" && current.ReadOnly && current.Fixture == "Foundation");
            }
            var snapshot = _snapshot!;
            if (offset > snapshot.Nodes.Length) return ToolReply.Error("PAGE_BOUNDS_INVALID", SessionId);
            ToolReply Page(int count) => new("observed", SessionId, ScreenReaderSession.Profile, new
            {
                snapshot.DocumentId,
                snapshot.DocumentUrl,
                ObservationId = _observationId,
                Offset = offset,
                TotalNodes = snapshot.Nodes.Length,
                ReturnedNodes = count,
                HasMore = offset + count < snapshot.Nodes.Length,
                Nodes = snapshot.Nodes.Skip(offset).Take(count).ToArray(),
                FocusedNodeId = snapshot.FocusedNode?.Id,
                ActiveDescendantNodeId = snapshot.FocusedNode?.ActiveDescendantId,
                snapshot.Frames,
                snapshot.Limitations
            });
            var maximum = Math.Min(limit, snapshot.Nodes.Length - offset);
            var page = FitPage(Page, maximum, maximum == 0, "HOSTED_SNAPSHOT_ENTRY_TOO_LARGE");
            if (page.Status != "error") _publishedDocument = snapshot.DocumentId;
            return page;
        }, token);
    }

    public Task<ToolReply> ExecuteAsync(string sessionId, string documentId, string command, string? key = null,
        ObservationExpectation? goal = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(documentId) || documentId.Length > 2048) return Task.FromResult(ToolReply.Error("DOCUMENT_REQUIRED"));
        if (command is null || !ReadOnlyReaderCommands.Allowed.TryGetValue(command, out var requested) || key is not null || goal is not null)
            return Task.FromResult(ToolReply.Error("HOSTED_READ_ONLY_COMMAND_DENIED"));
        return InSessionAsync(sessionId, async () =>
        {
            if (_sessionUnconfirmed) return ToolReply.Error("HOSTED_SESSION_UNCONFIRMED", SessionId);
            if (_publishedDocument is null) return ToolReply.Error("CONTROL_READER_OBSERVATION_REQUIRED", SessionId);
            if (_publishedDocument != documentId) return ToolReply.Error("CONTROL_READER_DOCUMENT_CHANGED", SessionId);
            InvalidateSnapshot();
            using var document = await _transport.SendAsync(HttpMethod.Post, Resource + "/commands", new { command, expectedDocumentId = documentId }).ConfigureAwait(false);
            var result = Parse<HostedReaderCommand>(document); Profile(result.ReaderProfile);
            Require(result.SessionId == _session && result.Step.Command == requested && result.Step.Sequence > 0
                && !string.IsNullOrWhiteSpace(result.DocumentId));
            var raw = document.RootElement.GetProperty("step");
            RequireExplicitStep(raw);
            if (result.DispatchedInputs != 0 || result.GoalVerified || !ReadOnlyStep(result.Step))
                throw new HostedReaderFault("HOSTED_READ_ONLY_VIOLATION", result);
            Require(result.RequiresObservation == (result.DocumentId != documentId));
            if (result.RequiresObservation) _publishedDocument = null;
            return new("step", SessionId, ScreenReaderSession.Profile, result);
        }, token);
    }

    private static bool ReadOnlyStep(ReaderStep step) => step.Mode == ReaderMode.Browse && step.BrowserKeystrokes == 0
        && !step.InputMayHaveOccurred && step.Verification == VerificationStatus.NotRequested
        && ReadOnlyReaderCommands.Allowed.ContainsKey(step.Command.ToString());

    private ToolReply FitPage(Func<int, ToolReply> page, int maximum, bool allowEmpty, string error)
    {
        var complete = page(maximum);
        if (Fits(complete)) return complete;
        var lower = allowEmpty ? 0 : 1; var upper = maximum - 1; ToolReply? fitting = null;
        while (lower <= upper)
        {
            var count = lower + (upper - lower) / 2; var candidate = page(count);
            if (Fits(candidate)) { fitting = candidate; lower = count + 1; } else upper = count - 1;
        }
        return fitting ?? ToolReply.Error(error, SessionId);
    }
}
