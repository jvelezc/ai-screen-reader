using System.Text.Json;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using static HeadlessScreenReader.Tests.OwnedScanSessionTests;

namespace HeadlessScreenReader.Tests;

public sealed class ControllerDemoFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledControllerDemo_StillClosesAndKeepsCancellationSeparateFromCleanupFailure(bool failClose)
    {
        var browser = new DemoBrowser(false, failClose); var store = new RecordingStore();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = await ControllerDemo.RunCoreAsync(browser, store, cancellation.Token);
        Assert.False(result.Ready); Assert.Equal("CONTROL_DEMO_CANCELLED", result.ExecutionErrorCode);
        Assert.Equal(failClose ? "CONTROL_CLOSE_UNCONFIRMED" : null, result.CleanupErrorCode);
        Assert.Equal(1, browser.CloseCalls); Assert.Equal(0, browser.Keys);
        Assert.Empty(store.Records.OfType<OwnedCommandIntent>());
        using var metadata = JsonDocument.Parse(ControllerDemo.CreateMetadata(result));
        Assert.Equal("CONTROL_DEMO_CANCELLED", metadata.RootElement.GetProperty("ErrorCode").GetString());
        Assert.Equal(failClose ? "CONTROL_CLOSE_UNCONFIRMED" : null, metadata.RootElement.GetProperty("CleanupErrorCode").GetString());
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ControllerDemo_CoreRetainsExecutionAndCleanupFailuresSeparatelyWithoutRealIo(bool failExecution, bool failClose)
    {
        var browser = new DemoBrowser(failExecution, failClose); var store = new RecordingStore();
        var result = await ControllerDemo.RunCoreAsync(browser, store);
        Assert.Equal(!failExecution && !failClose, result.Ready);
        Assert.Equal(failExecution ? "CONTROL_DEMO_EXECUTION_UNCONFIRMED" : null, result.ExecutionErrorCode);
        Assert.Equal(failClose ? "CONTROL_CLOSE_UNCONFIRMED" : null, result.CleanupErrorCode);
        Assert.Equal(1, browser.CloseCalls);
        Assert.Equal(failClose ? BrowserShutdownPhase.Faulted : BrowserShutdownPhase.Closed, result.Status.Shutdown!.Phase);
        Assert.Equal(failExecution ? "CONTROL_OWNED_FAULTED" : failClose ? "CONTROL_CLOSE_UNCONFIRMED" : null, result.Status.FaultCode);
        var metadata = ControllerDemo.CreateMetadata(result);
        using var parsed = JsonDocument.Parse(metadata);
        Assert.Equal("hsr-controller-demo-v1", parsed.RootElement.GetProperty("Schema").GetString());
        Assert.Equal("controller.json", parsed.RootElement.GetProperty("StatusArtifact").GetString());
        Assert.Equal(!failExecution && !failClose, parsed.RootElement.GetProperty("Ready").GetBoolean());
        Assert.Equal(failExecution ? "execute" : failClose ? "close" : "completed", parsed.RootElement.GetProperty("Stage").GetString());
        Assert.Equal(failExecution ? "CONTROL_DEMO_EXECUTION_UNCONFIRMED" : failClose ? "CONTROL_CLOSE_UNCONFIRMED" : null,
            parsed.RootElement.GetProperty("ErrorCode").GetString());
        Assert.Equal(failExecution ? "CONTROL_DEMO_EXECUTION_UNCONFIRMED" : null, parsed.RootElement.GetProperty("ExecutionErrorCode").GetString());
        Assert.Equal(failClose ? "CONTROL_CLOSE_UNCONFIRMED" : null, parsed.RootElement.GetProperty("CleanupErrorCode").GetString());
        Assert.DoesNotContain("private-demo-failure", metadata);
        Assert.DoesNotContain("private-demo-failure", JsonSerializer.Serialize(result));
        Assert.DoesNotContain("private-demo-failure", JsonSerializer.Serialize(store.Records));
        Assert.Equal("hsr-local-controller-v1", result.Status.Schema);
        if (!failExecution)
        {
            Assert.Equal(3, store.Records.OfType<OwnedCommandCompletion>().Count());
            Assert.Equal(VerificationStatus.Satisfied, store.Records.OfType<OwnedCommandEvidence>().Last().Step.Verification);
        }
    }

    private sealed class DemoBrowser(bool failExecution, bool failClose) : IScreenReaderBrowser, ICaptureControl, IAsyncDisposable, IBrowserShutdownDiagnostics
    {
        private readonly OwnedBrowser _inner = new();
        public int Keys => _inner.Keys;
        public int CloseCalls { get; private set; }
        public BrowserShutdownStatus ShutdownStatus { get; private set; } = new(BrowserShutdownPhase.NotStarted, 0, false, false, false, false, false, false, null);
        public CaptureStatus CaptureStatus => _inner.CaptureStatus;
        private AccessibilitySnapshot Snapshot() => new("owned-document",
            [new("control", _inner.Keys >= 2 ? "heading" : "button", _inner.Keys >= 2 ? "Checkout preview" : "Open checkout preview",
                IsFocused: _inner.Keys > 0, DocumentId: "owned-document", FrameId: "main")], [])
        { CaptureEpoch = CaptureStatus.Epoch };
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => failExecution
            ? Task.FromException<AccessibilitySnapshot>(new IOException("private-demo-failure")) : Task.FromResult(Snapshot());
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) => _inner.PressKeyAsync(key, cancellationToken);
        public Task<CaptureExclusionLease> ExcludeCaptureAsync() => _inner.ExcludeCaptureAsync();
        public async Task<AccessibilitySnapshot> ResumeCaptureAsync(CaptureExclusionLease lease) { await _inner.ResumeCaptureAsync(lease); return Snapshot(); }
        public async ValueTask DisposeAsync()
        {
            CloseCalls++; await _inner.DisposeAsync();
            ShutdownStatus = new(failClose ? BrowserShutdownPhase.Faulted : BrowserShutdownPhase.Closed, 0, true, !failClose, true, true, true, false,
                failClose ? "CHROMIUM_BROWSER_CLOSE_UNCONFIRMED" : null);
            if (failClose) throw new IOException("private-demo-failure");
        }
    }
}
