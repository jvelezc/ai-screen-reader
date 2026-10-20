using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class ReaderCaptureExclusionTests
{
    [Fact]
    public async Task WholeCommandAndQueuedCommand_DrainWithoutLateResultsOrRetainedAnnouncements()
    {
        var browser = new ControlledCaptureBrowser { CaptureWork = new() };
        using var reader = new ScreenReaderSession(browser);
        var first = reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        await browser.CaptureStarted.Task;
        var queued = reader.ExecuteAsync(ReaderCommand.Tab);
        var exclusion = reader.ExcludeCaptureAsync();
        Assert.Equal(CapturePhase.Draining, reader.CaptureStatus.Phase);
        Assert.Equal(CapturePhase.Excluded, browser.CaptureStatus.Phase);
        Assert.False(exclusion.IsCompleted);
        Assert.Equal("READER_CAPTURE_EXCLUDED", Assert.Throws<BrowserOperationException>(() => reader.Timeline).Code);
        browser.CaptureWork.SetResult(Snapshot(1, "late-private-canary"));
        foreach (var operation in new[] { first, queued })
            Assert.Equal("READER_CAPTURE_RESULT_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(() => operation)).Code);
        var lease = await exclusion;
        Assert.Equal(1, browser.Captures);
        Assert.Equal(0, browser.Keys);
        Assert.Equal(CapturePhase.Excluded, reader.CaptureStatus.Phase);
        browser.CaptureWork = null;
        await reader.ResumeCaptureAsync(lease);
        var current = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.DoesNotContain("private-canary", JsonSerializer.Serialize(new { current, reader.Timeline, reader.CaptureStatus }));
        Assert.DoesNotContain(reader.Timeline, e => e.Command == ReaderCommand.Tab);
        Assert.Single(reader.Timeline, e => e.Kind == ReaderEventKind.Announcement);
    }

    [Fact]
    public async Task CancelledWaiter_DoesNotReleaseWholeOperationOrAcknowledgeExclusion()
    {
        var browser = new ControlledCaptureBrowser { CaptureWork = new() };
        using var reader = new ScreenReaderSession(browser);
        var operation = reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        await browser.CaptureStarted.Task;
        using var cancellation = new CancellationTokenSource();
        var waiter = operation.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        var exclusion = reader.ExcludeCaptureAsync();
        Assert.False(exclusion.IsCompleted);
        browser.CaptureWork.SetResult(Snapshot(1, "late-private-canary"));
        await Assert.ThrowsAsync<BrowserOperationException>(() => operation);
        await exclusion;
        Assert.Equal(CapturePhase.Excluded, reader.CaptureStatus.Phase);
    }

    [Fact]
    public async Task EveryCommandIdleStreamAndTimeline_DenyDuringExclusionWithoutConsumingBudget()
    {
        var browser = new ControlledCaptureBrowser();
        using var reader = new ScreenReaderSession(browser, maxCommands: 1);
        var lease = await reader.ExcludeCaptureAsync();
        foreach (var command in Enum.GetValues<ReaderCommand>())
            Assert.Equal("READER_CAPTURE_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(
                () => reader.ExecuteAsync(command, "pe"))).Code);
        await using var stream = reader.ObserveChangesAsync().GetAsyncEnumerator();
        Assert.Equal("READER_CAPTURE_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => stream.MoveNextAsync().AsTask())).Code);
        Assert.Throws<BrowserOperationException>(() => reader.Timeline);
        Assert.Equal(0, browser.Captures);
        Assert.Equal(0, browser.Keys);
        await reader.ResumeCaptureAsync(lease);
        Assert.Equal(ReaderOutcome.Observed, (await reader.ExecuteAsync(ReaderCommand.ReadCurrent)).Outcome);
        Assert.Single(reader.Timeline, e => e.Kind == ReaderEventKind.CommandStarted);
    }

    [Fact]
    public async Task InFlightIdleBatch_IsDrainedAndCannotYieldOrRetainLatePayload()
    {
        var browser = new ControlledCaptureBrowser { CaptureWork = new() };
        using var reader = new ScreenReaderSession(browser);
        await using var stream = reader.ObserveChangesAsync().GetAsyncEnumerator();
        var batch = stream.MoveNextAsync().AsTask();
        await browser.CaptureStarted.Task;
        var exclusion = reader.ExcludeCaptureAsync();
        Assert.False(exclusion.IsCompleted);
        browser.CaptureWork.SetResult(Snapshot(1, "late-private-canary"));
        Assert.Equal("READER_CAPTURE_RESULT_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(() => batch)).Code);
        var lease = await exclusion;
        browser.CaptureWork = null;
        await reader.ResumeCaptureAsync(lease);
        Assert.DoesNotContain("late-private-canary", JsonSerializer.Serialize(reader.Timeline));
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.Observation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdlePollWaitAndPausedIterator_CannotContinueAcrossExclusion(bool waiting)
    {
        var browser = new ControlledCaptureBrowser();
        using var reader = new ScreenReaderSession(browser);
        await using var stream = reader.ObserveChangesAsync(new()
        { Duration = TimeSpan.FromMinutes(1), PollInterval = TimeSpan.FromSeconds(30), MaxCaptures = 2 }).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        Task<bool>? pending = waiting ? stream.MoveNextAsync().AsTask() : null;
        var lease = await reader.ExcludeCaptureAsync().WaitAsync(TimeSpan.FromSeconds(2));
        if (pending is not null)
            Assert.Equal("READER_CAPTURE_RESULT_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(() => pending)).Code);
        await reader.ResumeCaptureAsync(lease);
        if (!waiting)
            Assert.Equal("READER_CAPTURE_EPOCH_CHANGED", (await Assert.ThrowsAsync<BrowserOperationException>(
                () => stream.MoveNextAsync().AsTask())).Code);
        Assert.Equal(1, browser.Captures);
    }

    [Fact]
    public async Task DisposedReader_ClosesTimelineAndCaptureAuthority()
    {
        var reader = new ScreenReaderSession(new ControlledCaptureBrowser());
        await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        reader.Dispose();
        Assert.Equal(CapturePhase.Closed, reader.CaptureStatus.Phase);
        Assert.Throws<BrowserOperationException>(() => reader.Timeline);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => reader.ExcludeCaptureAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => reader.ExecuteAsync(ReaderCommand.ReadCurrent));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StalledReaderStopOrResume_FaultsClosedAndLateCompletionCannotReopen(int stage)
    {
        var time = new FakeTimeProvider();
        var browser = new ControlledCaptureBrowser();
        using var reader = new ScreenReaderSession(browser, timeProvider: time);
        Task<ReaderStep>? operation = null;
        Task transition;
        if (stage == 0)
        {
            browser.CaptureWork = new();
            operation = reader.ExecuteAsync(ReaderCommand.ReadCurrent);
            await browser.CaptureStarted.Task;
        }
        if (stage == 1) browser.StopWork = new();
        if (stage == 2)
        {
            var lease = await reader.ExcludeCaptureAsync();
            browser.ResumeWork = new();
            transition = reader.ResumeCaptureAsync(lease);
        }
        else transition = reader.ExcludeCaptureAsync();
        time.Advance(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => transition.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(stage == 2 ? "READER_CAPTURE_RESUME_UNCONFIRMED" : "READER_CAPTURE_STOP_UNCONFIRMED", error.Code);
        Assert.Equal(CapturePhase.Faulted, reader.CaptureStatus.Phase);
        browser.CaptureWork?.SetResult(Snapshot(1, "late-private-canary"));
        browser.StopWork?.SetResult();
        browser.ResumeWork?.SetResult();
        if (operation is not null) await Assert.ThrowsAsync<BrowserOperationException>(() => operation);
        await Assert.ThrowsAsync<BrowserOperationException>(() => reader.ExecuteAsync(ReaderCommand.ReadCurrent));
        await Assert.ThrowsAsync<BrowserOperationException>(() => reader.ExcludeCaptureAsync());
        Assert.Throws<BrowserOperationException>(() => reader.Timeline);
        Assert.Equal(CapturePhase.Faulted, reader.CaptureStatus.Phase);
    }

    [Fact]
    public async Task ForeignStaleAndConsumedLeases_CannotResumeTheSession()
    {
        using var reader = new ScreenReaderSession(new ControlledCaptureBrowser());
        using var other = new ScreenReaderSession(new ControlledCaptureBrowser());
        var lease = await reader.ExcludeCaptureAsync();
        var foreign = await other.ExcludeCaptureAsync();
        foreach (var invalid in new[] { foreign, lease with { Epoch = lease.Epoch + 1 }, lease with { Token = Guid.Empty } })
            Assert.Equal("READER_CAPTURE_LEASE_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(
                () => reader.ResumeCaptureAsync(invalid))).Code);
        await reader.ResumeCaptureAsync(lease);
        await Assert.ThrowsAsync<BrowserOperationException>(() => reader.ResumeCaptureAsync(lease));
        var next = await reader.ExcludeCaptureAsync();
        await Assert.ThrowsAsync<BrowserOperationException>(() => reader.ResumeCaptureAsync(lease));
        await reader.ResumeCaptureAsync(next);
        Assert.Equal(3, reader.CaptureStatus.Epoch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidAdapterAcknowledgementOrBaseline_FailsClosedWithoutPayload(bool resume)
    {
        var browser = new ControlledCaptureBrowser { InvalidStop = !resume };
        using var reader = new ScreenReaderSession(browser);
        Task operation;
        if (resume)
        {
            var lease = await reader.ExcludeCaptureAsync();
            browser.InvalidBaseline = true;
            operation = reader.ResumeCaptureAsync(lease);
        }
        else operation = reader.ExcludeCaptureAsync();
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => operation);
        Assert.Null(error.InnerException);
        Assert.Equal(CapturePhase.Faulted, reader.CaptureStatus.Phase);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(reader.CaptureStatus));
        await Assert.ThrowsAsync<BrowserOperationException>(() => reader.ExecuteAsync(ReaderCommand.ReadCurrent));
    }

    [Fact]
    public async Task AdapterFailure_IsSanitizedAndUnsupportedBrowserLeavesOrdinaryReadingAvailable()
    {
        var browser = new ControlledCaptureBrowser { StopFailure = true };
        using var reader = new ScreenReaderSession(browser);
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => reader.ExcludeCaptureAsync());
        Assert.Equal("READER_CAPTURE_STOP_UNCONFIRMED", error.Code);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("private-canary", JsonSerializer.Serialize(reader.CaptureStatus));
        using var ordinary = new ScreenReaderSession(new ReadOnlyBrowser());
        Assert.Equal("READER_CAPTURE_CONTROL_UNAVAILABLE", (await Assert.ThrowsAsync<BrowserOperationException>(() => ordinary.ExcludeCaptureAsync())).Code);
        Assert.Equal(CapturePhase.Permitted, ordinary.CaptureStatus.Phase);
        Assert.Equal(ReaderOutcome.Observed, (await ordinary.ExecuteAsync(ReaderCommand.ReadCurrent)).Outcome);
    }

    [Fact]
    public async Task RepeatedExclusions_BoundPayloadFreeLifecycleAndRotateAuthority()
    {
        using var reader = new ScreenReaderSession(new ControlledCaptureBrowser());
        for (var i = 0; i < 65; i++) await reader.ResumeCaptureAsync(await reader.ExcludeCaptureAsync());
        var status = reader.CaptureStatus;
        Assert.True(status.EventsTruncated);
        Assert.Equal(256, status.Events.Length);
        Assert.Equal(66, status.Epoch);
        Assert.Equal(Enumerable.Range(5, 256).Select(i => (long)i), status.Events.Select(e => e.Sequence));
    }

    private static AccessibilitySnapshot Snapshot(long epoch, string name = "Safe") =>
        new("same-document", [new("heading", "heading", name)], []) { CaptureEpoch = epoch };

    private sealed class ReadOnlyBrowser : IScreenReaderBrowser
    {
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot(1));
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class ControlledCaptureBrowser : IScreenReaderBrowser, ICaptureControl
    {
        public TaskCompletionSource<AccessibilitySnapshot>? CaptureWork { get; set; }
        public TaskCompletionSource? StopWork { get; set; }
        public TaskCompletionSource? ResumeWork { get; set; }
        public TaskCompletionSource CaptureStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool StopFailure { get; set; }
        public bool InvalidStop { get; set; }
        public bool InvalidBaseline { get; set; }
        public int Captures { get; private set; }
        public int Keys { get; private set; }
        public CaptureStatus CaptureStatus { get; private set; } = new(CapturePhase.Permitted, 1, [], false);
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            Captures++;
            CaptureStarted.TrySetResult();
            return CaptureWork?.Task ?? Task.FromResult(Snapshot(CaptureStatus.Epoch));
        }
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            if (CaptureStatus.Phase != CapturePhase.Permitted) throw new BrowserInputDeniedException("CAPTURE_EXCLUDED");
            Keys++;
            return Task.CompletedTask;
        }
        public async Task<CaptureExclusionLease> ExcludeCaptureAsync()
        {
            if (StopFailure) throw new InvalidOperationException("private-canary");
            if (StopWork is not null) await StopWork.Task;
            if (!InvalidStop) CaptureStatus = CaptureStatus with { Phase = CapturePhase.Excluded };
            return new(Guid.NewGuid(), CaptureStatus.Epoch);
        }
        public async Task<AccessibilitySnapshot> ResumeCaptureAsync(CaptureExclusionLease lease)
        {
            if (ResumeWork is not null) await ResumeWork.Task;
            CaptureStatus = CaptureStatus with { Phase = CapturePhase.Permitted, Epoch = CaptureStatus.Epoch + 1 };
            return Snapshot(InvalidBaseline ? lease.Epoch : CaptureStatus.Epoch);
        }
    }
}
