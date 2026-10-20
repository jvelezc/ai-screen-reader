using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class CaptureAdmissionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StalledDrainStopOrBaseline_FailsClosedAndLateCompletionCannotReopen(int stage)
    {
        var time = new FakeTimeProvider();
        var gate = new CaptureAdmission(time);
        var work = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? operation = stage == 0 ? gate.RunAsync(() => work.Task) : null;
        Task transition;
        if (stage == 2)
        {
            var lease = await gate.ExcludeAsync(() => Task.CompletedTask);
            transition = gate.ResumeAsync(lease, () => work.Task);
        }
        else transition = gate.ExcludeAsync(() => stage == 1 ? work.Task : Task.CompletedTask);
        time.Advance(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => transition.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(stage == 2 ? "CAPTURE_RESUME_UNCONFIRMED" : "CAPTURE_STOP_UNCONFIRMED", error.Code);
        Assert.Equal(CapturePhase.Faulted, gate.Status.Phase);
        work.SetResult(true);
        if (operation is not null) await Assert.ThrowsAsync<BrowserOperationException>(() => operation);
        Assert.Equal(CapturePhase.Faulted, gate.Status.Phase);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => gate.RunAsync(() => Task.FromResult(true), input: true));
    }

    [Fact]
    public async Task Exclusion_DrainsActualOperationAfterCallerCancellationAndRejectsLatePayload()
    {
        var gate = new CaptureAdmission();
        var work = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = gate.RunAsync(() => work.Task);
        using var cancellation = new CancellationTokenSource();
        var waiter = operation.WaitAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        var stopped = false;
        var exclusion = gate.ExcludeAsync(() => { stopped = true; return Task.CompletedTask; });
        Assert.Equal(CapturePhase.Draining, gate.Status.Phase);
        Assert.False(stopped);
        Assert.False(exclusion.IsCompleted);
        Assert.Equal("CAPTURE_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => gate.RunAsync(() => Task.FromResult("new payload")))).Code);
        Assert.Equal("CAPTURE_EXCLUDED", (await Assert.ThrowsAsync<BrowserInputDeniedException>(
            () => gate.RunAsync(() => Task.FromResult(true), input: true))).Code);
        work.SetResult("late payload");
        Assert.Equal("CAPTURE_RESULT_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(() => operation)).Code);
        var lease = await exclusion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(stopped);
        Assert.NotEqual(Guid.Empty, lease.Token);
        Assert.Equal(CapturePhase.Excluded, gate.Status.Phase);
    }

    [Fact]
    public async Task StopAcknowledgement_IsRequiredBeforeExclusionLease()
    {
        var gate = new CaptureAdmission();
        var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exclusion = gate.ExcludeAsync(() => ack.Task);
        Assert.False(exclusion.IsCompleted);
        Assert.Equal(CapturePhase.Draining, gate.Status.Phase);
        ack.SetResult();
        await exclusion;
        Assert.Equal(CapturePhase.Excluded, gate.Status.Phase);
    }

    [Fact]
    public async Task FailedStop_FailsClosedWithoutPayloadInErrorOrLifecycle()
    {
        var gate = new CaptureAdmission();
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => gate.ExcludeAsync(
            () => throw new InvalidOperationException("synthetic-private-payload")));
        Assert.Equal("CAPTURE_STOP_UNCONFIRMED", error.Code);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("synthetic-private-payload", System.Text.Json.JsonSerializer.Serialize(gate.Status));
        Assert.Equal(CapturePhase.Faulted, gate.Status.Phase);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => gate.RunAsync(() => Task.FromResult(true), input: true));
        await Assert.ThrowsAsync<BrowserOperationException>(() => gate.ExcludeAsync(() => Task.CompletedTask));
    }

    [Fact]
    public async Task Resume_KeepsAdmissionClosedUntilFreshBaselineAndRotatesEpoch()
    {
        var gate = new CaptureAdmission();
        var lease = await gate.ExcludeAsync(() => Task.CompletedTask);
        var baseline = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = gate.ResumeAsync(lease, () => baseline.Task);
        Assert.Equal(CapturePhase.Resuming, gate.Status.Phase);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => gate.RunAsync(() => Task.FromResult(true), input: true));
        Assert.Equal(lease.Epoch, gate.Status.Epoch);
        baseline.SetResult("fresh baseline");
        Assert.Equal("fresh baseline", await resume);
        Assert.Equal(lease.Epoch + 1, gate.Status.Epoch);
        Assert.Equal("new observation", await gate.RunAsync(() => Task.FromResult("new observation")));
        Assert.Equal(new[] { CapturePhase.Draining, CapturePhase.Excluded, CapturePhase.Resuming, CapturePhase.Permitted },
            gate.Status.Events.Select(e => e.Phase));
    }

    [Fact]
    public async Task WrongForeignAndConsumedLeases_CannotReopenAdmission()
    {
        var gate = new CaptureAdmission();
        var lease = await gate.ExcludeAsync(() => Task.CompletedTask);
        var other = new CaptureAdmission();
        var foreign = await other.ExcludeAsync(() => Task.CompletedTask);
        foreach (var invalid in new[] { foreign, lease with { Epoch = lease.Epoch + 1 }, lease with { Token = Guid.Empty } })
            Assert.Equal("CAPTURE_LEASE_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(
                () => gate.ResumeAsync(invalid, () => Task.FromResult(true)))).Code);
        Assert.Equal(CapturePhase.Excluded, gate.Status.Phase);
        await gate.ResumeAsync(lease, () => Task.FromResult(true));
        var next = await gate.ExcludeAsync(() => Task.CompletedTask);
        await Assert.ThrowsAsync<BrowserOperationException>(() => gate.ResumeAsync(lease, () => Task.FromResult(true)));
        Assert.Equal(CapturePhase.Excluded, gate.Status.Phase);
        await gate.ResumeAsync(next, () => Task.FromResult(true));
    }

    [Fact]
    public async Task FailedBaseline_RemainsFaultedAndCannotReuseLease()
    {
        var gate = new CaptureAdmission();
        var lease = await gate.ExcludeAsync(() => Task.CompletedTask);
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => gate.ResumeAsync<bool>(lease,
            () => throw new InvalidOperationException("synthetic-private-payload")));
        Assert.Equal("CAPTURE_RESUME_UNCONFIRMED", error.Code);
        Assert.Null(error.InnerException);
        Assert.Equal(CapturePhase.Faulted, gate.Status.Phase);
        Assert.Equal(lease.Epoch, gate.Status.Epoch);
        await Assert.ThrowsAsync<BrowserOperationException>(() => gate.RunAsync(() => Task.FromResult(true)));
        await Assert.ThrowsAsync<BrowserOperationException>(() => gate.ResumeAsync(lease, () => Task.FromResult(true)));
    }

    [Fact]
    public async Task FailedOperation_ReleasesDrainWithoutPretendingCollectorStopped()
    {
        var gate = new CaptureAdmission();
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync<bool>(() => throw new InvalidOperationException()));
        var stopped = 0;
        await gate.ExcludeAsync(() => { stopped++; return Task.CompletedTask; }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, stopped);
        await Assert.ThrowsAsync<BrowserOperationException>(() => gate.ExcludeAsync(() => { stopped++; return Task.CompletedTask; }));
        Assert.Equal(1, stopped);
    }

    [Fact]
    public async Task Close_AbortsUnderlyingWorkBeforeWaitingAndNeverReopens()
    {
        var gate = new CaptureAdmission();
        var work = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = gate.RunAsync(() => work.Task);
        await gate.CloseAsync(() => { work.SetException(new InvalidOperationException()); return Task.CompletedTask; })
            .WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
        Assert.Equal(CapturePhase.Closed, gate.Status.Phase);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => gate.RunAsync(() => Task.FromResult(true), input: true));
        await Assert.ThrowsAsync<BrowserOperationException>(() => gate.ExcludeAsync(() => Task.CompletedTask));
    }

    [Fact]
    public async Task Lifecycle_IsPayloadFreeMonotonicAndExplicitlyBounded()
    {
        var time = new FakeTimeProvider();
        var gate = new CaptureAdmission(time);
        for (var i = 0; i < 65; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            var lease = await gate.ExcludeAsync(() => Task.CompletedTask);
            await gate.ResumeAsync(lease, () => Task.FromResult(true));
        }
        var status = gate.Status;
        Assert.True(status.EventsTruncated);
        Assert.Equal(256, status.Events.Length);
        Assert.Equal(5, status.Events[0].Sequence);
        Assert.Equal(260, status.Events[^1].Sequence);
        Assert.Equal(65_000, status.Events[^1].ElapsedMilliseconds);
        Assert.Equal(status.Events.OrderBy(e => e.ElapsedMilliseconds), status.Events);
    }
}
