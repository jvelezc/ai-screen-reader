using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser
{
    public CaptureStatus CaptureStatus => _captureAdmission.Status;

    // Only the trusted test assembly can inspect the independent diagnostic surface.
    // It is never a reader observation or a production control API.
    internal IPage DiagnosticPage => _page;

    /// <summary>
    /// Local fixture feasibility only. No human input lease is granted. There are no
    /// video, trace, HAR, screenshot, clipboard or network-body collectors enabled.
    /// </summary>
    public Task<CaptureExclusionLease> ExcludeCaptureAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_publicPolicy is not null) throw new BrowserOperationException("PUBLIC_CAPTURE_CONTROL_UNAVAILABLE");
        return _captureAdmission.ExcludeAsync(async () =>
        {
            await _cdp.SendAsync("Accessibility.disable").ConfigureAwait(false);
        });
    }

    /// <summary>The fixture owner must clear sensitive content before requesting a fresh baseline.</summary>
    public Task<AccessibilitySnapshot> ResumeCaptureAsync(CaptureExclusionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _captureAdmission.ResumeAsync(lease, async () =>
        {
            await _cdp.SendAsync("Accessibility.enable").ConfigureAwait(false);
            return (await CaptureCoreAsync().ConfigureAwait(false)) with { CaptureEpoch = CaptureStatus.Epoch + 1 };
        });
    }
}
