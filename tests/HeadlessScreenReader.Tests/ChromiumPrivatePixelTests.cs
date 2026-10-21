using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumPrivatePixelTests
{
    [Theory]
    [InlineData(BundledFixture.Foundation)]
    [InlineData(BundledFixture.SyntheticHandoff)]
    public async Task DefaultChromiumFixture_DeniesPixelsWithoutSeparateHostOptIn(BundledFixture fixture)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: fixture);
        var snapshot = await browser.CaptureAsync();
        Assert.Equal("PIXEL_VIEW_NOT_ENABLED", (await Assert.ThrowsAsync<BrowserOperationException>(() => browser.CapturePixelsAsync(1, snapshot.DocumentId))).Code);
        Assert.Equal(CapturePhase.Permitted, browser.CaptureStatus.Phase);
    }
    [Fact]
    public async Task ActualChromiumAndHttp_PixelsDecodeCorrectlyWithoutViewerInputAcrossExcludedSyntheticGap()
    {
        var proof = await PrivatePixelDemo.ExecuteAsync(Path.Combine("artifacts", "private-pixel-tests"));
        Assert.True(proof.Ready, proof.Stage + ":" + proof.ErrorCode);
        Assert.True(proof.ActualPixelsVerified); Assert.True(proof.InputAndClipboardDenied); Assert.True(proof.NoViewerInput);
        Assert.True(proof.ExcludedRedPositiveControl); Assert.True(proof.GapDenied); Assert.True(proof.StaleEpochDenied);
        Assert.True(proof.RevokedReadDenied); Assert.True(proof.SameProcessContextDocument); Assert.True(proof.PayloadsAbsentFromOwnedFiles);
        Assert.True(proof.ServerCloseConfirmed); Assert.True(proof.BrowserCloseConfirmed);
        Assert.True(proof.Shutdown is
        {
            Phase: BrowserShutdownPhase.Closed, ActiveCalls: 0, BrowserCloseAcknowledged: true,
            BrowserDeadlineExceeded: false, BrowserCallRejected: false, TransportDisposed: true, ProcessExitConfirmed: true, ProcessCallSettled: true
        });
        Assert.NotEqual(proof.BeforeSha256, proof.AfterSha256); Assert.Matches("^[a-f0-9]{64}$", proof.BeforeSha256!);
        Assert.True(proof.BeforePixels is { Red: 0, Green: > 38400, Width: 320, Height: 240 });
        Assert.True(proof.AfterPixels is { Red: 0, Blue: > 38400, Width: 320, Height: 240 });
        Assert.False(proof.HumanAcceptancePerformed); Assert.False(proof.RemoteIdentityProviderValidated); Assert.False(proof.PublicListenerEnabled);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(proof.Report)!, "*.png", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task OptedInNativePixels_RejectExcludedStaleEpochAndChangedDocumentBeforeDelivery()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchPrivatePixelFixtureAsync();
        var baseline = await browser.CaptureAsync();
        var gap = await browser.ExcludeCaptureAsync();
        Assert.Equal("CAPTURE_EXCLUDED", (await Assert.ThrowsAsync<BrowserOperationException>(() => browser.CapturePixelsAsync(1, baseline.DocumentId))).Code);
        var resumed = await browser.ResumeCaptureAsync(gap);
        Assert.Equal("PIXEL_CAPTURE_DENIED", (await Assert.ThrowsAsync<BrowserOperationException>(() => browser.CapturePixelsAsync(1, baseline.DocumentId))).Code);
        await browser.DiagnosticPage.ReloadAsync();
        Assert.Equal("PIXEL_DOCUMENT_CHANGED", (await Assert.ThrowsAsync<BrowserOperationException>(() => browser.CapturePixelsAsync(2, resumed.DocumentId))).Code);
        Assert.Equal(2, browser.CaptureStatus.Epoch);
    }
}
