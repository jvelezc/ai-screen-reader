using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Exploration;

/// <summary>One explicit fresh synthetic checkout attempt. Never a public replay or passive-viewer action.</summary>
public sealed class ControlledReplay(TimeProvider? timeProvider = null)
{
    public const string PolicyId = "trusted-shop-checkout-replay-v1";
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);
    public static TraversalPlan Plan => new("Successful checkout preview",
        new("heading", "Synthetic shop"), new("heading", "Checkout preview", MustBeFocused: true),
        [new(ReaderCommand.Tab), new(ReaderCommand.Activate)]);

    public async Task<ReplayReceipt> RunAsync(IScreenReaderBrowser browser, TraversalReceipt historical,
        ReplayEnvironment recorded, ReplayEnvironment current, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(historical);
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(current);
        var requestId = Guid.NewGuid().ToString("N");
        ReplaySourceContract? source = null, fresh = null;
        AccessibilitySnapshot? preflight = null, rejected = null;
        ReplayReceipt Receipt(ReplayDisposition disposition, string code, TraversalReceipt? result = null) =>
            new(requestId, historical.RunId, disposition, code, recorded, current, source, fresh, result)
            { PreflightObservation = preflight, RejectedObservation = rejected };
        if (cancellationToken.IsCancellationRequested)
            return Receipt(ReplayDisposition.CancelledBeforeAttempt, "REPLAY_CANCELLED");
        var rejection = Eligibility(historical, recorded, current);
        if (rejection is not null) return Receipt(ReplayDisposition.Rejected, rejection);
        try { source = ReplaySource.Create(historical.Observations[0].Snapshot); }
        catch (ArgumentException) { return Receipt(ReplayDisposition.Rejected, "REPLAY_HISTORICAL_SOURCE_UNSUPPORTED"); }
        try
        {
            var snapshot = await browser.CaptureAsync(cancellationToken).WaitAsync(OperationTimeout, _time, cancellationToken).ConfigureAwait(false);
            preflight = snapshot with { DocumentUrl = null };
            try { fresh = ReplaySource.Create(snapshot); }
            catch (ArgumentException) { return Receipt(ReplayDisposition.Rejected, "REPLAY_FRESH_SOURCE_UNSUPPORTED"); }
            if (fresh != source) return Receipt(ReplayDisposition.Rejected, "REPLAY_SOURCE_MISMATCH");
            var guarded = new SourceGuard(browser, source, _time);
            var result = await new TraversalRunner(_time).RunAsync(guarded, Plan,
                new() { MaxCommands = 2, MaxConsecutiveUnchanged = 3, Duration = TimeSpan.FromSeconds(15) },
                new() { Timeout = TimeSpan.FromMilliseconds(400), MaxPolls = 10 },
                cancellationToken).ConfigureAwait(false);
            rejected = guarded.RejectedObservation;
            return guarded.Rejection is { } changed
                ? Receipt(ReplayDisposition.Rejected, changed, result)
                : Receipt(ReplayDisposition.AttemptRecorded, result.ErrorCode ?? "REPLAY_ATTEMPT_RECORDED", result);
        }
        catch (OperationCanceledException) { return Receipt(ReplayDisposition.CancelledBeforeAttempt, "REPLAY_CANCELLED"); }
        catch (TimeoutException) { return Receipt(ReplayDisposition.EngineErrorBeforeAttempt, "REPLAY_SOURCE_CAPTURE_TIMEOUT"); }
        catch (BrowserOperationException exception) { return Receipt(ReplayDisposition.EngineErrorBeforeAttempt, exception.Code); }
    }

    private static string? Eligibility(TraversalReceipt historical, ReplayEnvironment recorded, ReplayEnvironment current)
    {
        if (historical.Profile != ScreenReaderSession.Profile || recorded.Profile != historical.Profile || current.Profile != historical.Profile)
            return "REPLAY_PROFILE_INCOMPATIBLE";
        if (recorded.PolicyId != PolicyId || current.PolicyId != PolicyId) return "REPLAY_POLICY_INELIGIBLE";
        if (recorded.FixtureId != "Traversal" || current.FixtureId != "Traversal"
            || recorded.FixtureSha256 is not { Length: 64 } || !recorded.FixtureSha256.All(char.IsAsciiHexDigit)
            || string.IsNullOrWhiteSpace(recorded.BrowserVersion) || string.IsNullOrWhiteSpace(recorded.DriverVersion)
            || recorded.FramePolicy != "8:3" || recorded.Headed || recorded != current)
            return "REPLAY_ENVIRONMENT_INCOMPATIBLE";
        if (historical.Schema != "hsr-traversal-v1" || !Guid.TryParseExact(historical.RunId, "N", out _)
            || historical.HasRecording || historical.Execution != TraversalExecution.Completed
            || historical.Reachability != TraversalReachability.Reached || historical.Barrier != TraversalBarrier.NoneObserved
            || historical.Policy != TraversalPolicy.Allowed || historical.StopReason != TraversalStopReason.RouteCompleted
            || historical.UnexecutedInstructions != 0 || historical.ErrorCode is not null)
            return "REPLAY_HISTORICAL_RUN_INELIGIBLE";
        if (historical.Plan.Precondition != Plan.Precondition || historical.Plan.Destination != Plan.Destination
            || !historical.Plan.Instructions.SequenceEqual(Plan.Instructions) || historical.Attempts.Length != 2
            || historical.Observations.Length != 3 || historical.Observations[0].Mode != ReaderMode.Browse
            || historical.Observations[0].CursorNodeId is not null)
            return "REPLAY_ROUTE_UNSUPPORTED";
        if (historical.Timeline.IsDefault || historical.TimelineTruncated || historical.Attempts.Any(a => a.TimelineTruncated
            || a.Step is null || a.Step.InputMayHaveOccurred || a.Step.ErrorCode is not null))
            return "REPLAY_INPUT_EVIDENCE_UNCERTAIN";
        if (!historical.Timeline.Where(e => e.Kind == ReaderEventKind.InputAttempted).Select(e => e.Detail).SequenceEqual(["Tab", "Enter"])
            || !historical.Timeline.Where(e => e.Kind == ReaderEventKind.InputCompleted).Select(e => e.Detail).SequenceEqual(["Tab", "Enter"])
            || historical.Timeline.Any(e => e.Kind == ReaderEventKind.InputDenied)
            || historical.Timeline.Select(e => e.Sequence).Distinct().Count() != historical.Timeline.Length)
            return "REPLAY_INPUT_EVIDENCE_UNCERTAIN";
        try
        {
            var graph = TraversalProjection.Build(historical);
            if (graph.Edges.Length != 2 || graph.Edges.Any(e => !e.InputEvidenceComplete || e.InputMayHaveOccurred)
                || !graph.Edges[0].KeyboardInputs.Select(k => k.Key).SequenceEqual(["Tab"])
                || !graph.Edges[1].KeyboardInputs.Select(k => k.Key).SequenceEqual(["Enter"])
                || historical.Attempts.Where((a, i) => a.Instruction != Plan.Instructions[i]
                    || a.Step!.Command != a.Instruction.Command || a.SourceObservationId != historical.Observations[i].Id
                    || a.ResultObservationId != historical.Observations[i + 1].Id).Any()
                || historical.Attempts[^1].Step!.Verification != VerificationStatus.Satisfied
                || !Plan.Precondition.IsSatisfiedBy(historical.Observations[0].Snapshot)
                || !Plan.Destination.IsSatisfiedBy(historical.Observations[^1].Snapshot))
                return "REPLAY_INPUT_EVIDENCE_UNCERTAIN";
        }
        catch (ArgumentException) { return "REPLAY_INPUT_EVIDENCE_UNCERTAIN"; }
        return null;
    }

    private sealed class SourceGuard(IScreenReaderBrowser browser, ReplaySourceContract expected, TimeProvider time) : IScreenReaderBrowser
    {
        private bool _inputAttempted;
        public string? Rejection { get; private set; }
        public AccessibilitySnapshot? RejectedObservation { get; private set; }
        private void Check(AccessibilitySnapshot snapshot)
        {
            if (_inputAttempted) return;
            try
            {
                if (ReplaySource.Create(snapshot) == expected) return;
                Rejection = "REPLAY_SOURCE_CHANGED_BEFORE_INPUT";
            }
            catch (ArgumentException) { Rejection = "REPLAY_SOURCE_UNCONFIRMED_BEFORE_INPUT"; }
            RejectedObservation = snapshot with { DocumentUrl = null };
            throw new BrowserOperationException(Rejection);
        }
        public async Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var snapshot = await browser.CaptureAsync(cancellationToken).WaitAsync(OperationTimeout, time, cancellationToken).ConfigureAwait(false);
                Check(snapshot);
                return snapshot;
            }
            catch (TimeoutException) { throw new BrowserOperationException("REPLAY_OBSERVATION_TIMEOUT"); }
        }
        public async Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            if (!_inputAttempted)
            {
                try { _ = await CaptureAsync(cancellationToken).ConfigureAwait(false); }
                catch (BrowserOperationException) when (Rejection is not null)
                { throw new BrowserInputDeniedException(Rejection); }
            }
            _inputAttempted = true;
            try { await browser.PressKeyAsync(key, cancellationToken).WaitAsync(OperationTimeout, time, cancellationToken).ConfigureAwait(false); }
            catch (TimeoutException) { throw new BrowserOperationException("REPLAY_INPUT_UNCONFIRMED"); }
        }
        public Task PressListboxKeyAsync(ListboxKeyInput input, CancellationToken cancellationToken = default) =>
            Task.FromException(new BrowserInputDeniedException("REPLAY_SCOPED_INPUT_UNAVAILABLE"));
    }
}
