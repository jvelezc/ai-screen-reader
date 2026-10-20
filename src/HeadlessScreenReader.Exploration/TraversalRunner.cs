using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Exploration;

/// <summary>One exclusive, bounded authored route. No search, retries, focus repair or replay.</summary>
public sealed class TraversalRunner(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<TraversalReceipt> RunAsync(
        IScreenReaderBrowser browser, TraversalPlan plan, TraversalLimits? limits = null,
        ObservationWaitOptions? waitOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.Name);
        ArgumentNullException.ThrowIfNull(plan.Precondition);
        ArgumentNullException.ThrowIfNull(plan.Destination);
        if (plan.Instructions.IsDefaultOrEmpty || plan.Instructions.Length > 500)
            throw new ArgumentOutOfRangeException(nameof(plan), "An authored route requires 1..500 instructions.");
        if (plan.Instructions.Any(i => i is null)) throw new ArgumentException("Route instructions cannot be null.", nameof(plan));
        limits ??= new();
        if (limits.MaxCommands is < 1 or > 500 || limits.MaxConsecutiveUnchanged is < 1 or > 500
            || limits.Duration <= TimeSpan.Zero || limits.Duration > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(limits));

        var started = _time.GetTimestamp();
        var startedAtUtc = _time.GetUtcNow();
        double Elapsed() => _time.GetElapsedTime(started).TotalMilliseconds;
        using var deadline = new CancellationTokenSource(limits.Duration, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var capture = new ObservedBrowser(browser);
        using var reader = new ScreenReaderSession(capture, maxCommands: limits.MaxCommands + 1,
            waitOptions: waitOptions, timeProvider: _time);
        var observations = new List<TraversalObservation>();
        var attempts = new List<TraversalAttempt>();
        var execution = TraversalExecution.Interrupted;
        var reachability = TraversalReachability.Unknown;
        var barrier = TraversalBarrier.NotAssessed;
        var policy = TraversalPolicy.Allowed;
        var stop = TraversalStopReason.RouteCompleted;
        string? error = null;
        var unchanged = 0;

        TraversalObservation Observe(ReaderStep step)
        {
            // Preserve reader semantics, but never retain a URL query/fragment or userinfo in this schema.
            var snapshot = capture.Last! with { DocumentUrl = null };
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                new { Version = "same-document-reader-state-v1", Snapshot = snapshot, step.Mode, step.CursorNodeId })));
            var observation = new TraversalObservation($"o{observations.Count + 1}", snapshot,
                step.Mode, step.CursorNodeId, fingerprint, Elapsed());
            observations.Add(observation);
            return observation;
        }

        try
        {
            var baseline = await reader.ExecuteAsync(ReaderCommand.ReadCurrent, cancellationToken: linked.Token).ConfigureAwait(false);
            if (baseline.Outcome == ReaderOutcome.BrowserError)
            {
                stop = TraversalStopReason.BrowserError;
                execution = TraversalExecution.SystemError;
                error = baseline.ErrorCode;
            }
            else
            {
                var source = Observe(baseline);
                if (!plan.Precondition.IsSatisfiedBy(source.Snapshot))
                    stop = TraversalStopReason.PreconditionNotObserved;
                else
                {
                    barrier = TraversalBarrier.NoneObserved;
                    for (var index = 0; index < plan.Instructions.Length; index++)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        if (index >= limits.MaxCommands) { stop = TraversalStopReason.CommandLimit; break; }
                        var instruction = plan.Instructions[index];
                        var beforeCapture = capture.Version;
                        var beforeEvent = reader.Timeline.LastOrDefault()?.Sequence ?? 0;
                        var attemptStarted = Elapsed();
                        ReaderStep? step = null;
                        TraversalObservation? result = null;
                        var cancelled = false;
                        try
                        {
                            step = await reader.ExecuteAsync(instruction.Command, instruction.Key, linked.Token,
                                expectation: index == plan.Instructions.Length - 1 ? plan.Destination : null,
                                expectedDocumentId: source.Snapshot.DocumentId).ConfigureAwait(false);
                            if (capture.Version > beforeCapture && step.Outcome != ReaderOutcome.BrowserError
                                && step.Verification != VerificationStatus.Interrupted)
                                result = Observe(step);
                        }
                        catch (OperationCanceledException) { cancelled = true; }
                        attempts.Add(new($"a{attempts.Count + 1}", source.Id, result?.Id, instruction, step,
                            [.. reader.Timeline.Where(e => e.Sequence > beforeEvent)], reader.TimelineTruncated,
                            attemptStarted, Elapsed()));
                        if (cancelled || linked.IsCancellationRequested || step?.Verification == VerificationStatus.Interrupted)
                        {
                            execution = cancellationToken.IsCancellationRequested ? TraversalExecution.Cancelled : TraversalExecution.Interrupted;
                            stop = cancellationToken.IsCancellationRequested ? TraversalStopReason.Cancelled : TraversalStopReason.DurationElapsed;
                            break;
                        }
                        if (step?.Outcome == ReaderOutcome.BrowserError)
                        {
                            execution = TraversalExecution.SystemError;
                            stop = TraversalStopReason.BrowserError;
                            error = step?.ErrorCode;
                            break;
                        }
                        if (step?.Outcome == ReaderOutcome.Denied || step?.ErrorCode == "DOCUMENT_CHANGED"
                            || attempts[^1].Events.Any(e => e.Kind == ReaderEventKind.InputDenied))
                        {
                            policy = TraversalPolicy.Denied;
                            stop = TraversalStopReason.PolicyDenied;
                            error = step?.ErrorCode;
                            break;
                        }
                        if (step?.Outcome == ReaderOutcome.Blocked)
                        {
                            barrier = TraversalBarrier.Observed;
                            reachability = TraversalReachability.NotReached;
                            execution = TraversalExecution.Completed;
                            stop = TraversalStopReason.ObservedBarrier;
                            break;
                        }
                        if (step?.Outcome is ReaderOutcome.Unsupported or ReaderOutcome.FocusRequired or ReaderOutcome.BudgetExceeded
                            || result is null)
                        { stop = TraversalStopReason.ReaderLimitation; break; }
                        if (index == plan.Instructions.Length - 1)
                        {
                            execution = TraversalExecution.Completed;
                            reachability = step?.Verification == VerificationStatus.Satisfied
                                ? TraversalReachability.Reached : TraversalReachability.Unknown;
                            stop = TraversalStopReason.RouteCompleted;
                            break;
                        }
                        unchanged = result.Fingerprint == source.Fingerprint ? unchanged + 1 : 0;
                        source = result;
                        if (unchanged >= limits.MaxConsecutiveUnchanged)
                        { stop = TraversalStopReason.NoObservedProgress; break; }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            execution = cancellationToken.IsCancellationRequested ? TraversalExecution.Cancelled : TraversalExecution.Interrupted;
            stop = cancellationToken.IsCancellationRequested ? TraversalStopReason.Cancelled : TraversalStopReason.DurationElapsed;
        }
        return new("hsr-traversal-v1", ScreenReaderSession.Profile, plan, limits, execution, reachability,
            barrier, policy, stop, [.. observations], [.. attempts], reader.Timeline, reader.TimelineTruncated,
            plan.Instructions.Length - attempts.Count, error)
        { StartedAtUtc = startedAtUtc, DurationMilliseconds = Elapsed() };
    }

    private sealed class ObservedBrowser(IScreenReaderBrowser browser) : IScreenReaderBrowser
    {
        public AccessibilitySnapshot? Last { get; private set; }
        public int Version { get; private set; }
        public async Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = await browser.CaptureAsync(cancellationToken).ConfigureAwait(false);
            Last = snapshot;
            Version++;
            return snapshot;
        }
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) => browser.PressKeyAsync(key, cancellationToken);
        public Task PressListboxKeyAsync(ListboxKeyInput input, CancellationToken cancellationToken = default) =>
            browser.PressListboxKeyAsync(input, cancellationToken);
    }
}
