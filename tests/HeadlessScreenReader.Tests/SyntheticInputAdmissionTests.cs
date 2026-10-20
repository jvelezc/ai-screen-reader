using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class SyntheticInputAdmissionTests
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("", "0", true)]
    [InlineData("pw:protocol", null, false)]
    [InlineData("*", null, false)]
    [InlineData(null, "1", false)]
    [InlineData(null, "console", false)]
    public void DiagnosticLogging_IsRejectedForTheFixedSyntheticProof(string? debug, string? pwdebug, bool expected) =>
        Assert.Equal(expected, ChromiumFixtureBrowser.SyntheticDiagnosticsPermitted(debug, pwdebug));
    [Fact]
    public async Task NativeGrant_RequiresAcknowledgedGapAndKeepsOrdinaryInputClosed()
    {
        var gate = new CaptureAdmission();
        Assert.Throws<BrowserOperationException>(() => gate.GrantSyntheticInput(new(Guid.NewGuid(), 1), 1));
        var gap = await gate.ExcludeAsync(() => Task.CompletedTask);
        var lease = gate.GrantSyntheticInput(gap, 1);
        Assert.True(gate.SyntheticState.Active);
        Assert.Equal(7, await gate.RunSyntheticInputAsync(lease, () => Task.FromResult(7)));
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => gate.RunAsync(() => Task.FromResult(0), input: true));
        await Assert.ThrowsAsync<BrowserOperationException>(() => gate.RunAsync(() => Task.FromResult(0)));
        Assert.Equal("SYNTHETIC_INPUT_STILL_OWNED", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => gate.ResumeAsync(gap, () => Task.FromResult(0)))).Code);
        Assert.Equal(CapturePhase.Excluded, gate.Status.Phase);
        await gate.RevokeSyntheticInputAsync(lease);
        Assert.False(gate.SyntheticState.Active);
        Assert.Equal(0, gate.SyntheticState.Operations);
        Assert.Equal(9, await gate.ResumeAsync(gap, () => Task.FromResult(9)));
        Assert.Equal(2, gate.Status.Epoch);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("epoch")]
    [InlineData("generation")]
    public async Task ForeignNativeAuthority_CannotDispatchOrRevokeCurrentOwner(string field)
    {
        var gate = new CaptureAdmission(); var gap = await gate.ExcludeAsync(() => Task.CompletedTask);
        var lease = gate.GrantSyntheticInput(gap, 1);
        var foreign = field switch
        {
            "token" => lease with { Token = Guid.NewGuid() },
            "epoch" => lease with { Epoch = 2 },
            _ => lease with { Generation = 2 }
        };
        var calls = 0;
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => gate.RunSyntheticInputAsync(foreign, () => Task.FromResult(++calls)));
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => gate.RevokeSyntheticInputAsync(foreign));
        Assert.Equal(0, calls); Assert.True(gate.SyntheticState.Active);
        await gate.RevokeSyntheticInputAsync(lease);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task NonpositiveGeneration_CannotGrantNativeAuthority(int generation)
    {
        var gate = new CaptureAdmission(); var gap = await gate.ExcludeAsync(() => Task.CompletedTask);
        Assert.Throws<BrowserOperationException>(() => gate.GrantSyntheticInput(gap, generation));
        Assert.False(gate.SyntheticState.Active);
    }

    [Fact]
    public async Task CancelledWaiterAndQueuedInput_DoNotReleaseActualNativeDrain()
    {
        var gate = new CaptureAdmission(); var gap = await gate.ExcludeAsync(() => Task.CompletedTask);
        var lease = gate.GrantSyntheticInput(gap, 1);
        var native = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = gate.RunSyntheticInputAsync(lease, () => native.Task);
        var queuedCalls = 0;
        var queued = gate.RunSyntheticInputAsync(lease, () => Task.FromResult(++queuedCalls));
        using var cancellation = new CancellationTokenSource();
        var wait = first.WaitAsync(cancellation.Token); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        var revoke = gate.RevokeSyntheticInputAsync(lease);
        Assert.False(revoke.IsCompleted); Assert.False(gate.SyntheticState.Active); Assert.Equal(2, gate.SyntheticState.Operations);
        Assert.Throws<BrowserOperationException>(() => gate.GrantSyntheticInput(gap, 2));
        native.SetResult(1); Assert.Equal(1, await first);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => queued);
        await revoke;
        Assert.Equal(0, queuedCalls); Assert.Equal(0, gate.SyntheticState.Operations);
        var next = gate.GrantSyntheticInput(gap, 2);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => gate.DispatchSyntheticInputAsync(lease, () => Task.CompletedTask));
        await gate.RevokeSyntheticInputAsync(next);
        Assert.Throws<BrowserOperationException>(() => gate.GrantSyntheticInput(gap, 2));
    }

    [Fact]
    public async Task NativeDrainTimeout_FaultsClosedAndLateCompletionCannotRegrantOrResume()
    {
        var time = new FakeTimeProvider(); var gate = new CaptureAdmission(time);
        var gap = await gate.ExcludeAsync(() => Task.CompletedTask); var lease = gate.GrantSyntheticInput(gap, 1);
        var work = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = gate.RunSyntheticInputAsync(lease, () => work.Task);
        var revoke = gate.RevokeSyntheticInputAsync(lease); time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("SYNTHETIC_STOP_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => revoke)).Code);
        Assert.Equal(CapturePhase.Faulted, gate.Status.Phase); Assert.Equal(1, gate.SyntheticState.Operations);
        work.SetResult(1); await actual;
        Assert.Equal(0, gate.SyntheticState.Operations); Assert.False(gate.SyntheticState.Active);
        Assert.Throws<BrowserOperationException>(() => gate.GrantSyntheticInput(gap, 2));
        await Assert.ThrowsAsync<BrowserOperationException>(() => gate.ResumeAsync(gap, () => Task.FromResult(1)));
    }

    [Fact]
    public async Task Close_ClosesNativeAuthorityBeforeAbortingAndDrainingActualWork()
    {
        var gate = new CaptureAdmission(); var gap = await gate.ExcludeAsync(() => Task.CompletedTask);
        var lease = gate.GrantSyntheticInput(gap, 1); var work = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = gate.RunSyntheticInputAsync(lease, () => work.Task);
        var close = gate.CloseAsync(() => Task.CompletedTask);
        Assert.Equal(CapturePhase.Closed, gate.Status.Phase); Assert.False(gate.SyntheticState.Active);
        Assert.False(close.IsCompleted); Assert.Equal(1, gate.SyntheticState.Operations);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => gate.DispatchSyntheticInputAsync(lease, () => Task.CompletedTask));
        work.SetResult(1); await actual; await close;
        Assert.Equal(0, gate.SyntheticState.Operations);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(120001)]
    public async Task InvalidNativeLifetime_CannotConsumeAGeneration(int milliseconds)
    {
        var gate = new CaptureAdmission(); var gap = await gate.ExcludeAsync(() => Task.CompletedTask);
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.GrantSyntheticInput(gap, 1, TimeSpan.FromMilliseconds(milliseconds)));
        Assert.Equal(1, gate.GrantSyntheticInput(gap, 1).Generation);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(120000)]
    public async Task NativeAuthority_ExpiresAtExactBoundariesAndStopRemainsIdempotent(int milliseconds)
    {
        var time = new FakeTimeProvider(); var gate = new CaptureAdmission(time);
        var gap = await gate.ExcludeAsync(() => Task.CompletedTask);
        var lease = gate.GrantSyntheticInput(gap, 1, TimeSpan.FromMilliseconds(milliseconds));
        time.Advance(TimeSpan.FromMilliseconds(milliseconds - 1));
        Assert.True(gate.SyntheticState.Active);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(gate.SyntheticState.Active);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => gate.DispatchSyntheticInputAsync(lease, () => Task.CompletedTask));
        await gate.RevokeSyntheticInputAsync(lease); await gate.RevokeSyntheticInputAsync(lease);
        var next = gate.GrantSyntheticInput(gap, 2);
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => gate.RevokeSyntheticInputAsync(lease));
        await gate.RevokeSyntheticInputAsync(next);
    }

    [Fact]
    public async Task NativeExpiry_ClosesAdmissionButCannotAcknowledgeAnActualCallEarly()
    {
        var time = new FakeTimeProvider(); var gate = new CaptureAdmission(time);
        var gap = await gate.ExcludeAsync(() => Task.CompletedTask); var lease = gate.GrantSyntheticInput(gap, 1, TimeSpan.FromSeconds(1));
        var work = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = gate.RunSyntheticInputAsync(lease, () => work.Task);
        time.Advance(TimeSpan.FromSeconds(1));
        var stop = gate.RevokeSyntheticInputAsync(lease);
        Assert.False(gate.SyntheticState.Active); Assert.Equal(1, gate.SyntheticState.Operations); Assert.False(stop.IsCompleted);
        Assert.Throws<BrowserOperationException>(() => gate.GrantSyntheticInput(gap, 2));
        work.SetResult(1); await actual; await stop;
        Assert.Equal(0, gate.SyntheticState.Operations);
    }
}
