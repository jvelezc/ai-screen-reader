using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class TraversalRunnerTests
{
    private static readonly ObservationWaitOptions Wait = new() { MaxPolls = 1 };
    internal static TraversalPlan Plan(params TraversalInstruction[] instructions) =>
        new("Authored route", new("button", "Open"), new("heading", "Destination"), [.. instructions]);

    [Fact]
    public async Task VerifiedRoute_RetainsImmutableSourceResultAndOrderedInputEvidence()
    {
        var browser = new ControlledBrowser();
        browser.AfterKey = () => browser.Snapshot = Snapshot(new AccessibleNode("destination", "heading", "Destination", IsFocused: true));
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.Activate)));
        Assert.Equal(TraversalReachability.Reached, receipt.Reachability);
        Assert.Equal(TraversalExecution.Completed, receipt.Execution);
        Assert.Equal(TraversalBarrier.NoneObserved, receipt.Barrier);
        Assert.Equal(TraversalPolicy.Allowed, receipt.Policy);
        Assert.Equal(TraversalStopReason.RouteCompleted, receipt.StopReason);
        Assert.Equal(0, receipt.UnexecutedInstructions);
        Assert.Equal(1, receipt.AttemptedKeystrokes);
        Assert.Equal(1, receipt.CompletedKeystrokes);
        Assert.Equal(new[] { "Enter" }, browser.Keys);
        var attempt = Assert.Single(receipt.Attempts);
        Assert.Equal("o1", attempt.SourceObservationId);
        Assert.Equal("o2", attempt.ResultObservationId);
        Assert.Equal(VerificationStatus.Satisfied, attempt.Step!.Verification);
        Assert.Equal(new[] { ReaderEventKind.InputAttempted, ReaderEventKind.InputCompleted, ReaderEventKind.GoalVerified },
            attempt.Events.Where(e => e.Kind is ReaderEventKind.InputAttempted or ReaderEventKind.InputCompleted or ReaderEventKind.GoalVerified).Select(e => e.Kind));
        Assert.All(attempt.Events, e => Assert.Equal(attempt.Step.Sequence, e.CommandSequence));
        Assert.Equal(Enumerable.Range(1, receipt.Timeline.Length).Select(i => (long)i), receipt.Timeline.Select(e => e.Sequence));
        Assert.Contains(receipt.Observations[0].Snapshot.Nodes, n => n.Name == "Open");
        Assert.DoesNotContain(receipt.Observations[0].Snapshot.Nodes, n => n.Name == "Destination");
        Assert.Contains(receipt.Observations[1].Snapshot.Nodes, n => n.Name == "Destination");
        browser.Snapshot = Snapshot(new AccessibleNode("later", "heading", "Later mutation"));
        Assert.DoesNotContain(receipt.Observations.SelectMany(o => o.Snapshot.Nodes), n => n.Name == "Later mutation");
        Assert.False(receipt.HasRecording);
        Assert.False(receipt.TimelineTruncated);
        Assert.NotEqual(default, receipt.StartedAtUtc);
        Assert.True(receipt.DurationMilliseconds >= 0);
    }

    [Fact]
    public async Task WrongDestination_ProjectsObservedDialogAndNeverCreatesIntendedGoalNode()
    {
        var browser = new ControlledBrowser();
        browser.AfterKey = () => browser.Snapshot = Snapshot(new AccessibleNode("validation", "dialog", "Address required", IsFocused: true));
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.Activate)));
        Assert.Equal(TraversalExecution.Completed, receipt.Execution);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(TraversalBarrier.NoneObserved, receipt.Barrier);
        Assert.Equal(VerificationStatus.TimedOut, receipt.Attempts[0].Step!.Verification);
        var graph = TraversalProjection.Build(receipt);
        Assert.Equal(2, graph.Nodes.Length);
        Assert.Equal("o2", Assert.Single(graph.Edges).ResultObservationId);
        Assert.Equal("Address required", receipt.Observations[^1].Snapshot.Nodes[0].Name);
        Assert.DoesNotContain(receipt.Observations.SelectMany(o => o.Snapshot.Nodes), n => n.Name == "Destination");
        Assert.DoesNotContain(receipt.Timeline, e => e.Kind == ReaderEventKind.GoalVerified);
    }

    [Fact]
    public async Task ObservedDisabledControl_IsRouteBarrierWithoutInput()
    {
        var browser = new ControlledBrowser { Snapshot = Snapshot(new AccessibleNode("open", "button", "Open", IsFocused: true, IsDisabled: true)) };
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.Activate), new TraversalInstruction(ReaderCommand.Tab)));
        Assert.Equal(TraversalExecution.Completed, receipt.Execution);
        Assert.Equal(TraversalReachability.NotReached, receipt.Reachability);
        Assert.Equal(TraversalBarrier.Observed, receipt.Barrier);
        Assert.Equal(TraversalStopReason.ObservedBarrier, receipt.StopReason);
        Assert.Equal("OBSERVED_DISABLED_CONTROL", receipt.Attempts[0].Step!.BarrierCode);
        Assert.Equal(1, receipt.UnexecutedInstructions);
        Assert.Empty(browser.Keys);
        Assert.Equal(0, receipt.AttemptedKeystrokes);
        Assert.Equal(0, receipt.CompletedKeystrokes);
    }

    [Fact]
    public async Task MissingPrecondition_PreservesBaselineAndDoesNotRunRoute()
    {
        var browser = new ControlledBrowser { Snapshot = Snapshot(new AccessibleNode("other", "button", "Other")) };
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.Tab)));
        Assert.Equal(TraversalStopReason.PreconditionNotObserved, receipt.StopReason);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(TraversalBarrier.NotAssessed, receipt.Barrier);
        Assert.Single(receipt.Observations);
        Assert.Empty(receipt.Attempts);
        Assert.Empty(browser.Keys);
        Assert.Equal(1, receipt.UnexecutedInstructions);
    }

    [Fact]
    public async Task CommandBudget_ExecutesLastAllowedInstructionAndStopsBeforeNext()
    {
        var browser = new ControlledBrowser();
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.Tab), new TraversalInstruction(ReaderCommand.Tab), new TraversalInstruction(ReaderCommand.Activate)),
            new() { MaxCommands = 2, MaxConsecutiveUnchanged = 10 });
        Assert.Equal(2, receipt.Attempts.Length);
        Assert.Equal(new[] { "Tab", "Tab" }, browser.Keys);
        Assert.Equal(TraversalStopReason.CommandLimit, receipt.StopReason);
        Assert.Equal(TraversalExecution.Interrupted, receipt.Execution);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(1, receipt.UnexecutedInstructions);
    }

    [Fact]
    public async Task GoalOnExactCommandBudget_IsVerified()
    {
        var browser = new ControlledBrowser();
        browser.AfterKey = () => browser.Snapshot = Snapshot(new AccessibleNode("dest", "heading", "Destination"));
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.Activate)), new() { MaxCommands = 1 });
        Assert.Equal(TraversalReachability.Reached, receipt.Reachability);
        Assert.Equal(TraversalStopReason.RouteCompleted, receipt.StopReason);
        Assert.Single(browser.Keys);
    }

    [Fact]
    public async Task NoProgressBudget_StopsAtBoundaryWithoutCallingUnchangedStateBarrier()
    {
        var browser = new ControlledBrowser();
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.ReadCurrent), new TraversalInstruction(ReaderCommand.ReadCurrent), new TraversalInstruction(ReaderCommand.Activate)),
            new() { MaxConsecutiveUnchanged = 2 });
        Assert.Equal(2, receipt.Attempts.Length);
        Assert.Equal(TraversalStopReason.NoObservedProgress, receipt.StopReason);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(TraversalBarrier.NoneObserved, receipt.Barrier);
        Assert.Empty(browser.Keys);
        Assert.Equal(3, receipt.Observations.Length);
        Assert.Single(receipt.Observations.Select(o => o.Fingerprint).Distinct());
    }

    [Fact]
    public async Task FocusRequired_StopsWithoutRepairOrContinuation()
    {
        var browser = new ControlledBrowser { Snapshot = Snapshot(new AccessibleNode("open", "button", "Open")) };
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.NextButton), new TraversalInstruction(ReaderCommand.Activate), new TraversalInstruction(ReaderCommand.Tab)));
        Assert.Equal(ReaderOutcome.FocusRequired, receipt.Attempts[^1].Step!.Outcome);
        Assert.Equal(TraversalStopReason.ReaderLimitation, receipt.StopReason);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Empty(browser.Keys);
        Assert.Equal(1, receipt.UnexecutedInstructions);
    }

    [Fact]
    public async Task InputPolicyDenial_IsSeparateFromWebsiteBarrier()
    {
        var browser = new ControlledBrowser();
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.PressKey, "password"), new TraversalInstruction(ReaderCommand.Tab)));
        Assert.Equal(TraversalPolicy.Denied, receipt.Policy);
        Assert.Equal(TraversalStopReason.PolicyDenied, receipt.StopReason);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(TraversalBarrier.NoneObserved, receipt.Barrier);
        Assert.Empty(browser.Keys);
        Assert.Null(receipt.Attempts[0].ResultObservationId);
        Assert.Empty(TraversalProjection.Build(receipt).Edges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowserFailure_PreservesInputUncertaintyWithoutReplayOrResultEdge(bool failDuringKey)
    {
        var browser = new ControlledBrowser();
        if (failDuringKey) browser.AfterKey = () => throw new BrowserOperationException("INPUT_FAILURE");
        else browser.AfterKey = () => browser.FailCapture = true;
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.Activate), new TraversalInstruction(ReaderCommand.Tab)));
        Assert.Equal(TraversalExecution.SystemError, receipt.Execution);
        Assert.Equal(TraversalStopReason.BrowserError, receipt.StopReason);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(TraversalBarrier.NoneObserved, receipt.Barrier);
        Assert.Equal(failDuringKey ? "INPUT_FAILURE" : "CAPTURE_FAILURE", receipt.ErrorCode);
        var attempt = Assert.Single(receipt.Attempts);
        Assert.True(attempt.Step!.InputMayHaveOccurred);
        Assert.Null(attempt.ResultObservationId);
        Assert.Equal(1, receipt.AttemptedKeystrokes);
        Assert.Equal(failDuringKey ? 0 : 1, receipt.CompletedKeystrokes);
        Assert.Equal(new[] { "Enter" }, browser.Keys);
        Assert.Empty(TraversalProjection.Build(receipt).Edges);
    }

    [Fact]
    public async Task BaselineBrowserFailure_ReturnsReceiptWithNoInventedObservation()
    {
        var receipt = await Run(new ControlledBrowser { FailCapture = true }, Plan(new TraversalInstruction(ReaderCommand.Tab)));
        Assert.Equal(TraversalExecution.SystemError, receipt.Execution);
        Assert.Equal("CAPTURE_FAILURE", receipt.ErrorCode);
        Assert.Empty(receipt.Observations);
        Assert.Empty(receipt.Attempts);
        Assert.Equal(1, receipt.UnexecutedInstructions);
        Assert.Equal(TraversalBarrier.NotAssessed, receipt.Barrier);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeOrDuringInput_RetainsReceiptAndNeverRunsNextInstruction(bool afterInput)
    {
        using var cancel = new CancellationTokenSource();
        var browser = new ControlledBrowser();
        if (afterInput) browser.AfterKey = cancel.Cancel;
        else cancel.Cancel();
        var receipt = await new TraversalRunner().RunAsync(browser, Plan(new TraversalInstruction(ReaderCommand.Activate), new TraversalInstruction(ReaderCommand.Tab)),
            waitOptions: Wait, cancellationToken: cancel.Token);
        Assert.Equal(TraversalExecution.Cancelled, receipt.Execution);
        Assert.Equal(TraversalStopReason.Cancelled, receipt.StopReason);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(afterInput ? 1 : 0, receipt.Attempts.Length);
        Assert.Equal(afterInput ? 1 : 0, browser.Keys.Count);
        Assert.Empty(TraversalProjection.Build(receipt).Edges);
        if (afterInput)
        {
            Assert.True(receipt.Attempts[0].Step!.InputMayHaveOccurred);
            Assert.Equal(VerificationStatus.Interrupted, receipt.Attempts[0].Step!.Verification);
            Assert.Equal(1, receipt.CompletedKeystrokes);
        }
    }

    [Fact]
    public async Task DurationBudget_StopsAtExactDeadlineBeforeDispatch()
    {
        var time = new FakeTimeProvider();
        var browser = new ControlledBrowser();
        browser.AfterCapture = () => time.Advance(TimeSpan.FromSeconds(1));
        var receipt = await new TraversalRunner(time).RunAsync(browser, Plan(new TraversalInstruction(ReaderCommand.Tab)),
            new() { Duration = TimeSpan.FromSeconds(1) }, Wait);
        Assert.Equal(TraversalStopReason.DurationElapsed, receipt.StopReason);
        Assert.Equal(TraversalExecution.Interrupted, receipt.Execution);
        Assert.Equal(1000, receipt.DurationMilliseconds);
        Assert.Empty(browser.Keys);
        Assert.Empty(receipt.Attempts);
    }

    [Fact]
    public async Task DurationJustBeforeDeadline_AllowsObservedGoal()
    {
        var time = new FakeTimeProvider();
        var browser = new ControlledBrowser();
        browser.AfterCapture = () => { if (browser.Captures == 1) time.Advance(TimeSpan.FromMilliseconds(999)); };
        browser.AfterKey = () => browser.Snapshot = Snapshot(new AccessibleNode("dest", "heading", "Destination"));
        var receipt = await new TraversalRunner(time).RunAsync(browser, Plan(new TraversalInstruction(ReaderCommand.Activate)),
            new() { Duration = TimeSpan.FromSeconds(1) }, Wait);
        Assert.Equal(TraversalReachability.Reached, receipt.Reachability);
        Assert.Equal(999, receipt.DurationMilliseconds);
        Assert.Single(browser.Keys);
    }

    [Fact]
    public async Task DurationDuringInput_PreservesUncertaintyAndStopsBeforeNextCommand()
    {
        var time = new FakeTimeProvider();
        var browser = new ControlledBrowser { AfterKey = () => time.Advance(TimeSpan.FromSeconds(1)) };
        var receipt = await new TraversalRunner(time).RunAsync(browser,
            Plan(new TraversalInstruction(ReaderCommand.Activate), new TraversalInstruction(ReaderCommand.Tab)),
            new() { Duration = TimeSpan.FromSeconds(1) }, Wait);
        Assert.Equal(TraversalStopReason.DurationElapsed, receipt.StopReason);
        Assert.Equal(TraversalExecution.Interrupted, receipt.Execution);
        Assert.True(Assert.Single(receipt.Attempts).Step!.InputMayHaveOccurred);
        Assert.Null(receipt.Attempts[0].ResultObservationId);
        Assert.Single(browser.Keys);
        Assert.Equal(1, receipt.CompletedKeystrokes);
        Assert.Empty(TraversalProjection.Build(receipt).Edges);
    }

    [Fact]
    public async Task StateChange_ResetsConsecutiveNoProgressCount()
    {
        var browser = new ControlledBrowser();
        browser.AfterKey = () => browser.Snapshot = browser.Keys.Count == 1
            ? Snapshot(new AccessibleNode("open", "button", "Open", IsFocused: true, Description: "Changed"))
            : Snapshot(new AccessibleNode("dest", "heading", "Destination"));
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.ReadCurrent),
            new TraversalInstruction(ReaderCommand.Tab), new TraversalInstruction(ReaderCommand.ReadCurrent),
            new TraversalInstruction(ReaderCommand.Activate)), new() { MaxConsecutiveUnchanged = 2 });
        Assert.Equal(TraversalReachability.Reached, receipt.Reachability);
        Assert.Equal(4, receipt.Attempts.Length);
        Assert.Equal(new[] { "Tab", "Enter" }, browser.Keys);
        Assert.NotEqual(receipt.Observations[1].Fingerprint, receipt.Observations[2].Fingerprint);
    }

    [Fact]
    public async Task MaximumRouteAndBounds_AcceptFiveHundredInstructions()
    {
        var browser = new ControlledBrowser();
        browser.AfterKey = () => browser.Snapshot = Snapshot(new AccessibleNode("dest", "heading", "Destination"));
        var instructions = Enumerable.Repeat(new TraversalInstruction(ReaderCommand.ReadCurrent), 499)
            .Append(new(ReaderCommand.Activate)).ToArray();
        var receipt = await Run(browser, Plan(instructions),
            new() { MaxCommands = 500, MaxConsecutiveUnchanged = 500, Duration = TimeSpan.FromMinutes(10) });
        Assert.Equal(TraversalReachability.Reached, receipt.Reachability);
        Assert.Equal(500, receipt.Attempts.Length);
        Assert.Equal(501, receipt.Observations.Length);
        Assert.Equal(0, receipt.UnexecutedInstructions);
        Assert.False(receipt.TimelineTruncated);
        Assert.Single(browser.Keys);
    }

    [Fact]
    public async Task AdapterInputDenial_StopsAndKeepsPolicySeparateFromReachability()
    {
        var browser = new ControlledBrowser { AfterKey = () => throw new BrowserInputDeniedException("ADAPTER_DENIED") };
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.Activate), new TraversalInstruction(ReaderCommand.Tab)));
        Assert.Equal(TraversalPolicy.Denied, receipt.Policy);
        Assert.Equal(TraversalStopReason.PolicyDenied, receipt.StopReason);
        Assert.Equal("ADAPTER_DENIED", receipt.ErrorCode);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
        Assert.Equal(TraversalBarrier.NoneObserved, receipt.Barrier);
        Assert.Equal(0, receipt.CompletedKeystrokes);
        Assert.Single(receipt.Attempts);
        Assert.Contains(receipt.Attempts[0].Events, e => e.Kind == ReaderEventKind.InputDenied);
    }

    [Fact]
    public async Task NullInstruction_IsRejectedBeforeCapture()
    {
        var browser = new ControlledBrowser();
        await Assert.ThrowsAsync<ArgumentException>(() => Run(browser, Plan([null!])));
        Assert.Equal(0, browser.Captures);
    }

    [Fact]
    public async Task ChangedDocument_IsObservedButStaleInputIsDenied()
    {
        var browser = new ControlledBrowser();
        browser.AfterCapture = () => browser.Snapshot = browser.Snapshot with { DocumentId = "replacement" };
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.Activate)));
        Assert.Equal("DOCUMENT_CHANGED", receipt.ErrorCode);
        Assert.Equal(TraversalPolicy.Denied, receipt.Policy);
        Assert.Equal("replacement", receipt.Observations[^1].Snapshot.DocumentId);
        Assert.Empty(browser.Keys);
        Assert.Equal(TraversalReachability.Unknown, receipt.Reachability);
    }

    [Fact]
    public async Task SnapshotUrlIsOmitted_AndSameUrlNeverMergesDistinctObservedStates()
    {
        var browser = new ControlledBrowser();
        browser.Snapshot = browser.Snapshot with { DocumentUrl = "https://example.com/?secret=canary#token" };
        browser.AfterKey = () => browser.Snapshot = Snapshot(new AccessibleNode("dest", "heading", "Destination"))
            with
        { DocumentUrl = "https://example.com/?secret=canary#token" };
        var receipt = await Run(browser, Plan(new TraversalInstruction(ReaderCommand.Activate)));
        Assert.All(receipt.Observations, o => Assert.Null(o.Snapshot.DocumentUrl));
        Assert.NotEqual(receipt.Observations[0].Fingerprint, receipt.Observations[1].Fingerprint);
        Assert.Equal(2, TraversalProjection.Build(receipt).Nodes.Length);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(501, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 501, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 601)]
    public async Task InvalidBounds_AreRejectedBeforeBrowserCapture(int commands, int unchanged, int seconds)
    {
        var browser = new ControlledBrowser();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Run(browser, Plan(new TraversalInstruction(ReaderCommand.Tab)),
            new() { MaxCommands = commands, MaxConsecutiveUnchanged = unchanged, Duration = TimeSpan.FromSeconds(seconds) }));
        Assert.Equal(0, browser.Captures);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task InvalidRouteLength_IsRejectedBeforeCapture(int length)
    {
        var browser = new ControlledBrowser();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Run(browser,
            Plan(Enumerable.Repeat(new TraversalInstruction(ReaderCommand.Tab), length).ToArray())));
        Assert.Equal(0, browser.Captures);
    }

    internal static Task<TraversalReceipt> Run(ControlledBrowser browser, TraversalPlan plan, TraversalLimits? limits = null) =>
        new TraversalRunner().RunAsync(browser, plan, limits, Wait);

    internal static AccessibilitySnapshot Snapshot(params AccessibleNode[] nodes) => new("doc", [.. nodes], []);

    internal sealed class ControlledBrowser : IScreenReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = TraversalRunnerTests.Snapshot(new AccessibleNode("open", "button", "Open", IsFocused: true));
        public List<string> Keys { get; } = [];
        public bool FailCapture { get; set; }
        public Action? AfterKey { get; set; }
        public Action? AfterCapture { get; set; }
        public int Captures { get; private set; }
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Captures++;
            if (FailCapture) throw new BrowserOperationException("CAPTURE_FAILURE");
            var value = Snapshot;
            AfterCapture?.Invoke();
            return Task.FromResult(value);
        }
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Keys.Add(key);
            AfterKey?.Invoke();
            return Task.CompletedTask;
        }
    }
}
