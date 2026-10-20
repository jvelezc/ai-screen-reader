using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using HeadlessScreenReader.ScreenReader;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class ControlledReplayTests
{
    internal static readonly ReplayEnvironment Environment = new(ScreenReaderSession.Profile, "browser-1", "driver-1",
        "Traversal", new string('a', 64), ControlledReplay.PolicyId, false, "8:3");

    [Fact]
    public void EquivalentFreshIdentities_NormalizeWithoutDroppingReaderOrderSemanticsOrOwnership()
    {
        var first = ReplaySource.Create(new ReplayBrowser("old").Snapshot());
        var second = ReplaySource.Create(new ReplayBrowser("fresh").Snapshot());
        Assert.Equal(first, second);
        Assert.Equal("same-fixture-reader-start-v1", first.Version);
        Assert.Equal(64, first.Sha256.Length);
        Assert.Equal(3, first.Nodes);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("disabled")]
    [InlineData("focus")]
    [InlineData("role")]
    [InlineData("required")]
    [InlineData("active")]
    [InlineData("text")]
    public void ChangedReaderFacts_ChangeSourceContractDespiteSameUrl(string change)
    {
        var snapshot = new ReplayBrowser("source").Snapshot();
        var node = snapshot.Nodes[2];
        var changed = change switch
        {
            "name" => node with { Name = "Different action" },
            "disabled" => node with { IsDisabled = true },
            "focus" => node with { IsFocused = true },
            "role" => node with { Role = "link" },
            "required" => node with { Required = new(true, SemanticSource.DeclaredAria) },
            "active" => node with { ActiveDescendantId = snapshot.Nodes[1].Id },
            _ => node with { Text = new("changed text", SemanticSource.ChromiumAccessibility) }
        };
        var after = snapshot with { Nodes = snapshot.Nodes.SetItem(2, changed) };
        Assert.Equal(snapshot.DocumentUrl, after.DocumentUrl);
        Assert.NotEqual(ReplaySource.Create(snapshot), ReplaySource.Create(after));
    }

    [Theory]
    [InlineData("frame")]
    [InlineData("epoch")]
    [InlineData("duplicate")]
    [InlineData("parent")]
    [InlineData("active")]
    [InlineData("foreign")]
    public void UnsupportedOrAmbiguousSource_CannotProduceAnEquivalenceProof(string invalid)
    {
        var snapshot = new ReplayBrowser("source").Snapshot();
        snapshot = invalid switch
        {
            "frame" => snapshot with { Frames = [] },
            "epoch" => snapshot with { CaptureEpoch = 2 },
            "duplicate" => snapshot with { Nodes = snapshot.Nodes.Add(snapshot.Nodes[2]) },
            "parent" => snapshot with { Nodes = snapshot.Nodes.SetItem(2, snapshot.Nodes[2] with { ParentId = snapshot.Nodes[2].Id }) },
            "active" => snapshot with { Nodes = snapshot.Nodes.SetItem(2, snapshot.Nodes[2] with { ActiveDescendantId = "missing" }) },
            _ => snapshot with { Nodes = snapshot.Nodes.SetItem(2, snapshot.Nodes[2] with { DocumentId = "foreign" }) }
        };
        Assert.Throws<ArgumentException>(() => ReplaySource.Create(snapshot));
    }

    [Fact]
    public async Task EligibleFreshRun_CreatesIndependentReceiptAndLeavesHistoricalEvidenceUntouched()
    {
        var historical = await Historical();
        var before = JsonSerializer.Serialize(historical);
        var browser = new ReplayBrowser("new");
        var replay = await new ControlledReplay().RunAsync(browser, historical, Environment, Environment);
        Assert.Equal(ReplayDisposition.AttemptRecorded, replay.Disposition);
        Assert.Equal(historical.RunId, replay.HistoricalRunId);
        Assert.NotEqual(historical.RunId, replay.Result!.RunId);
        Assert.Equal(TraversalReachability.Reached, replay.Result.Reachability);
        Assert.Equal(VerificationStatus.Satisfied, replay.Result.Attempts[^1].Step!.Verification);
        Assert.Equal(new[] { "Tab", "Enter" }, browser.Keys);
        Assert.Equal(replay.HistoricalSource, replay.FreshSource);
        Assert.NotNull(replay.PreflightObservation);
        Assert.Null(replay.PreflightObservation.DocumentUrl);
        Assert.NotEqual(historical.Observations[0].Snapshot.DocumentId, replay.Result.Observations[0].Snapshot.DocumentId);
        Assert.Equal(before, JsonSerializer.Serialize(historical));
    }

    [Theory]
    [InlineData("profile", "REPLAY_PROFILE_INCOMPATIBLE")]
    [InlineData("policy", "REPLAY_POLICY_INELIGIBLE")]
    [InlineData("browser", "REPLAY_ENVIRONMENT_INCOMPATIBLE")]
    [InlineData("driver", "REPLAY_ENVIRONMENT_INCOMPATIBLE")]
    [InlineData("fixture", "REPLAY_ENVIRONMENT_INCOMPATIBLE")]
    [InlineData("hash", "REPLAY_ENVIRONMENT_INCOMPATIBLE")]
    [InlineData("headed", "REPLAY_ENVIRONMENT_INCOMPATIBLE")]
    [InlineData("frames", "REPLAY_ENVIRONMENT_INCOMPATIBLE")]
    public async Task IncompatibleEnvironmentOrPolicy_RejectsBeforeCaptureOrInput(string change, string code)
    {
        var historical = await Historical();
        var current = change switch
        {
            "profile" => Environment with { Profile = "other-profile" },
            "policy" => Environment with { PolicyId = "other-policy" },
            "browser" => Environment with { BrowserVersion = "other-browser" },
            "driver" => Environment with { DriverVersion = "other-driver" },
            "fixture" => Environment with { FixtureId = "Public" },
            "hash" => Environment with { FixtureSha256 = new string('b', 64) },
            "headed" => Environment with { Headed = true },
            _ => Environment with { FramePolicy = "1:0" }
        };
        var browser = new ReplayBrowser("new");
        var replay = await new ControlledReplay().RunAsync(browser, historical, Environment, current);
        Assert.Equal(ReplayDisposition.Rejected, replay.Disposition);
        Assert.Equal(code, replay.Code);
        Assert.Null(replay.Result);
        Assert.Equal(0, browser.Captures);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("unknown")]
    [InlineData("policy")]
    [InlineData("barrier")]
    [InlineData("recording")]
    [InlineData("truncated")]
    [InlineData("uncertain")]
    [InlineData("missing-input")]
    [InlineData("missing-timeline")]
    [InlineData("route")]
    [InlineData("missing-source")]
    [InlineData("mode")]
    public async Task IneligibleOrUncertainHistoricalEvidence_RejectsBeforeCaptureOrInput(string change)
    {
        var historical = await Historical();
        historical = change switch
        {
            "cancelled" => historical with { Execution = TraversalExecution.Cancelled },
            "unknown" => historical with { Reachability = TraversalReachability.Unknown },
            "policy" => historical with { Policy = TraversalPolicy.Denied },
            "barrier" => historical with { Barrier = TraversalBarrier.Observed },
            "recording" => historical with { HasRecording = true },
            "truncated" => historical with { TimelineTruncated = true },
            "uncertain" => historical with { Attempts = historical.Attempts.SetItem(0, historical.Attempts[0] with { Step = historical.Attempts[0].Step! with { InputMayHaveOccurred = true } }) },
            "missing-input" => historical with { Attempts = historical.Attempts.SetItem(0, historical.Attempts[0] with { Events = [] }) },
            "missing-timeline" => historical with { Timeline = [] },
            "route" => historical with { Plan = historical.Plan with { Instructions = [new(ReaderCommand.PressKey, "Enter")] } },
            "missing-source" => historical with { Observations = [] },
            _ => historical with { Observations = historical.Observations.SetItem(0, historical.Observations[0] with { Mode = ReaderMode.Focus }) }
        };
        var browser = new ReplayBrowser("new");
        var replay = await new ControlledReplay().RunAsync(browser, historical, Environment, Environment);
        Assert.Equal(ReplayDisposition.Rejected, replay.Disposition);
        Assert.Null(replay.Result);
        Assert.Equal(0, browser.Captures);
        Assert.Empty(browser.Keys);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ChangedSourceAtPreflightOrBeforeFirstNativeDispatch_RejectsWithNoNativeKeys(int changeAt)
    {
        var historical = await Historical();
        var browser = new ReplayBrowser("new") { ChangeAtCapture = changeAt };
        var replay = await new ControlledReplay().RunAsync(browser, historical, Environment, Environment);
        Assert.Equal(ReplayDisposition.Rejected, replay.Disposition);
        Assert.StartsWith("REPLAY_SOURCE_", replay.Code);
        Assert.Empty(browser.Keys);
        if (changeAt == 1) Assert.Null(replay.Result);
        else
        {
            Assert.Equal(0, replay.Result!.CompletedKeystrokes);
            Assert.NotEqual(TraversalBarrier.Observed, replay.Result.Barrier);
            Assert.DoesNotContain(replay.Result.Attempts, a => a.Step?.InputMayHaveOccurred == true);
            Assert.Contains(replay.RejectedObservation!.Nodes, n => n.Name == "Changed action");
            Assert.Null(replay.RejectedObservation.DocumentUrl);
        }
        Assert.NotNull(replay.PreflightObservation);
    }

    [Fact]
    public async Task CurrentPolicyDenial_RemainsDeniedWithoutAnAccessibilityBarrier()
    {
        var historical = await Historical();
        var browser = new ReplayBrowser("new") { DenyInput = true };
        var replay = await new ControlledReplay().RunAsync(browser, historical, Environment, Environment);
        Assert.Equal(ReplayDisposition.AttemptRecorded, replay.Disposition);
        Assert.Equal(TraversalPolicy.Denied, replay.Result!.Policy);
        Assert.Equal(TraversalBarrier.NoneObserved, replay.Result.Barrier);
        Assert.Equal(TraversalReachability.Unknown, replay.Result.Reachability);
        Assert.Empty(browser.Keys);
        Assert.Equal(1, replay.Result.AttemptedKeystrokes);
        Assert.Equal(0, replay.Result.CompletedKeystrokes);
    }

    [Fact]
    public async Task PostInputBrowserFailure_PreservesPartialKeysAndUncertaintyInNewReceipt()
    {
        var historical = await Historical();
        var browser = new ReplayBrowser("new") { FailOnEnter = true };
        var replay = await new ControlledReplay().RunAsync(browser, historical, Environment, Environment);
        Assert.Equal(TraversalExecution.SystemError, replay.Result!.Execution);
        Assert.Equal(TraversalReachability.Unknown, replay.Result.Reachability);
        Assert.Equal(TraversalBarrier.NoneObserved, replay.Result.Barrier);
        Assert.True(replay.Result.Attempts[^1].Step!.InputMayHaveOccurred);
        Assert.Equal(1, replay.Result.CompletedKeystrokes);
        Assert.Equal(2, replay.Result.AttemptedKeystrokes);
        Assert.NotEqual(historical.RunId, replay.Result.RunId);
    }

    [Fact]
    public async Task CancellationAfterInput_RetainsCancelledUncertainAttemptInsteadOfHistoricalSuccess()
    {
        var historical = await Historical();
        using var cancellation = new CancellationTokenSource();
        var browser = new ReplayBrowser("new") { AfterKey = cancellation.Cancel };
        var replay = await new ControlledReplay().RunAsync(browser, historical, Environment, Environment, cancellation.Token);
        Assert.Equal(ReplayDisposition.AttemptRecorded, replay.Disposition);
        Assert.Equal(TraversalExecution.Cancelled, replay.Result!.Execution);
        Assert.Equal(TraversalReachability.Unknown, replay.Result.Reachability);
        Assert.True(replay.Result.Attempts[0].Step!.InputMayHaveOccurred);
        Assert.Equal(new[] { "Tab" }, browser.Keys);
        Assert.Equal(0, replay.Result.CompletedKeystrokes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StalledCaptureOrInput_HasBoundedErrorAndNeverInheritsHistoricalGoal(bool input)
    {
        var historical = await Historical();
        var time = new FakeTimeProvider();
        var browser = new ReplayBrowser("new") { BlockCapture = !input, BlockInput = input };
        var pending = new ControlledReplay(time).RunAsync(browser, historical, Environment, Environment);
        Assert.False(pending.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(5));
        var replay = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        if (!input)
        {
            Assert.Equal(ReplayDisposition.EngineErrorBeforeAttempt, replay.Disposition);
            Assert.Equal("REPLAY_SOURCE_CAPTURE_TIMEOUT", replay.Code);
            Assert.Null(replay.Result);
            Assert.Empty(browser.Keys);
            browser.CaptureCompletion.SetResult(browser.Snapshot());
        }
        else
        {
            Assert.Equal(TraversalExecution.SystemError, replay.Result!.Execution);
            Assert.Equal("REPLAY_INPUT_UNCONFIRMED", replay.Code);
            Assert.Equal(TraversalReachability.Unknown, replay.Result.Reachability);
            Assert.True(replay.Result.Attempts[0].Step!.InputMayHaveOccurred);
            browser.InputCompletion.SetResult();
        }
    }

    [Fact]
    public async Task CancellationBeforeReplay_IsExplicitAndSendsNoInput()
    {
        var historical = await Historical();
        var browser = new ReplayBrowser("new");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var replay = await new ControlledReplay().RunAsync(browser, historical, Environment, Environment, cancellation.Token);
        Assert.Equal(ReplayDisposition.CancelledBeforeAttempt, replay.Disposition);
        Assert.Null(replay.Result);
        Assert.Equal(0, browser.Captures);
        Assert.Empty(browser.Keys);
    }

    internal static Task<TraversalReceipt> Historical() => new TraversalRunner().RunAsync(new ReplayBrowser("old"), ControlledReplay.Plan);

    internal sealed class ReplayBrowser(string identity) : IScreenReaderBrowser
    {
        public List<string> Keys { get; } = [];
        public int Captures { get; private set; }
        public int ChangeAtCapture { get; init; } = int.MaxValue;
        public bool DenyInput { get; init; }
        public bool FailOnEnter { get; init; }
        public bool BlockCapture { get; init; }
        public bool BlockInput { get; init; }
        public Action? AfterKey { get; init; }
        public TaskCompletionSource<AccessibilitySnapshot> CaptureCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource InputCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AccessibilitySnapshot Snapshot()
        {
            var root = identity + "-root";
            var document = identity + "-document";
            var frame = identity + "-frame";
            var nodes = new List<AccessibleNode>
            {
                new(root, "RootWebArea", "Synthetic shop fixture", IsFocused: Keys.Count == 0, DocumentId: document, FrameId: frame),
                new(identity + "-heading", "heading", "Synthetic shop", ParentId: root, DocumentId: document, FrameId: frame),
                new(identity + "-button", "button", Captures >= ChangeAtCapture ? "Changed action" : "Open checkout preview",
                    IsFocused: Keys.Count == 1, ParentId: root, DocumentId: document, FrameId: frame)
            };
            if (Keys.Count > 1) nodes.Add(new(identity + "-goal", "heading", "Checkout preview", IsFocused: true, ParentId: root, DocumentId: document, FrameId: frame));
            return new(document, [.. nodes], [])
            {
                CaptureEpoch = 1,
                DocumentUrl = "about:blank",
                Frames = [new(frame, null, null, document, root, FrameObservationStatus.Observed)]
            };
        }
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            Captures++;
            cancellationToken.ThrowIfCancellationRequested();
            return BlockCapture ? CaptureCompletion.Task : Task.FromResult(Snapshot());
        }
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            if (DenyInput) throw new BrowserInputDeniedException("CURRENT_POLICY_DENIED");
            if (FailOnEnter && key == "Enter") throw new BrowserOperationException("CURRENT_BROWSER_FAILURE");
            Keys.Add(key);
            AfterKey?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return BlockInput ? InputCompletion.Task : Task.CompletedTask;
        }
    }
}
