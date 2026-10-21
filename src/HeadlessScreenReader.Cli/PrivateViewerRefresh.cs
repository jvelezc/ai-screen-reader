using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

/// <summary>Sequential native reads; rechecks local ownership and UI revision before publication.</summary>
public sealed class PrivateViewerRefresh(PrivateViewerHttpClient client, ControlledScanSession control, ChromiumPrivateViewer viewer)
{
    public async Task<bool> RefreshAsync(CancellationToken token = default)
    {
        var intent = viewer.Intent;
        if (intent.Action is PrivateViewAction.Pause or PrivateViewAction.End) return false;
        var before = control.Status; var epoch = before.Owned.Capture.Epoch;
        if (before.Phase != ControllerPhase.Active || before.Role != ControllerRole.Automation || before.Owned.Capture.Phase != CapturePhase.Permitted)
            return await viewer.RenderAsync(intent.Revision, PrivateViewPhase.Excluded, cancellationToken: token).ConfigureAwait(false);
        try
        {
            var content = await client.ReadAsync(epoch, token).ConfigureAwait(false); var after = control.Status;
            if (viewer.Intent != intent) return false;
            if (after.Phase != ControllerPhase.Active || after.Role != ControllerRole.Automation || after.Generation != before.Generation
                || after.Owned.Capture is not { Phase: CapturePhase.Permitted } || after.Owned.Capture.Epoch != epoch)
                return await viewer.RenderAsync(intent.Revision, PrivateViewPhase.Excluded, cancellationToken: token).ConfigureAwait(false);
            return await viewer.RenderAsync(intent.Revision, PrivateViewPhase.Viewing, content, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            var phase = exception is BrowserOperationException { Code: "VIEW_CLIENT_ENDED" } ? PrivateViewPhase.Ended
                : exception is BrowserOperationException { Code: "VIEW_CLIENT_EXCLUDED" } ? PrivateViewPhase.Excluded : PrivateViewPhase.Unavailable;
            return await viewer.RenderAsync(intent.Revision, phase, cancellationToken: token).ConfigureAwait(false);
        }
    }
}
