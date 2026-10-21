using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using Microsoft.Extensions.Time.Testing;
using static HeadlessScreenReader.Tests.OwnedScanSessionTests;

namespace HeadlessScreenReader.Tests;

public sealed class ControllerShutdownDiagnosticsTests
{
    [Fact]
    public async Task ControllerClose_KeepsFiveSecondDeadlineAndExposesActualPendingClosureWithoutLateRecovery()
    {
        var time = new FakeTimeProvider(); var context = Signal(); var transport = Signal();
        var browser = new ClosingBrowser(time, context, transport);
        var control = new ControlledScanSession(browser, new RecordingStore(), time);
        var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1));
        await control.ExecuteAsync(ai, 1, ReaderCommand.ReadCurrent); await control.ReleaseAsync(ai);
        var close = control.DisposeAsync().AsTask(); time.Advance(TimeSpan.FromSeconds(1)); await transport.Task;
        Assert.False(close.IsCompleted); Assert.Equal(1, control.Status.Shutdown!.ActiveCalls);
        Assert.Equal(BrowserShutdownPhase.DrainingCalls, control.Status.Shutdown.Phase);
        time.Advance(TimeSpan.FromMilliseconds(3999)); Assert.False(close.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("CONTROL_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => close)).Code);
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase); Assert.Equal(1, control.Status.Shutdown.ActiveCalls);
        context.SetResult(); await browser.Native.CloseAsync();
        Assert.Equal(0, control.Status.Shutdown.ActiveCalls); Assert.Equal(BrowserShutdownPhase.Closed, control.Status.Shutdown.Phase);
        Assert.Equal(ControllerPhase.Faulted, control.Status.Phase);
        await Assert.ThrowsAsync<BrowserOperationException>(() => control.AcquireAsync(TimeSpan.FromMinutes(1)));
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class ClosingBrowser : IScreenReaderBrowser, ICaptureControl, IAsyncDisposable, IBrowserShutdownDiagnostics
    {
        private readonly OwnedBrowser _inner = new();
        public ChromiumShutdown Native { get; }
        public ClosingBrowser(TimeProvider time, TaskCompletionSource context, TaskCompletionSource transport) =>
            Native = new(() => context.Task, () => _inner.DisposeAsync().AsTask(), () => transport.SetResult(), time);
        public BrowserShutdownStatus ShutdownStatus => Native.Status;
        public CaptureStatus CaptureStatus => _inner.CaptureStatus;
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => _inner.CaptureAsync(cancellationToken);
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) => _inner.PressKeyAsync(key, cancellationToken);
        public Task<CaptureExclusionLease> ExcludeCaptureAsync() => _inner.ExcludeCaptureAsync();
        public Task<AccessibilitySnapshot> ResumeCaptureAsync(CaptureExclusionLease lease) => _inner.ResumeCaptureAsync(lease);
        public ValueTask DisposeAsync() => new(Native.CloseAsync());
    }
}
