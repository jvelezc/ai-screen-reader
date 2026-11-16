using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser
{
    private Func<SyntheticSearchInputIntent, CancellationToken, Task<SyntheticSearchInputDecision>>? _syntheticSearchAuthority;
    private long _syntheticSearchInputSequence;

    internal void BindSyntheticSearchAuthority(Func<SyntheticSearchInputIntent, CancellationToken, Task<SyntheticSearchInputDecision>> authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSyntheticSearchFixture();
        if (_captureAdmission.Status.Phase != CapturePhase.Permitted
            || Interlocked.CompareExchange(ref _syntheticSearchAuthority, authority, null) is not null)
            throw new BrowserInputDeniedException("SYNTHETIC_SEARCH_AUTHORITY_ALREADY_BOUND");
    }

    private async Task<SyntheticSearchInputGrant?> AuthorizeSyntheticSearchInputAsync(SyntheticSearchInputKind kind, string? key, int character,
        AccessibilitySnapshot before, string? target, CancellationToken token)
    {
        if (_syntheticSearchAuthority is not { } authority) return null;
        EnsureSyntheticSearchFixture();
        var intent = new SyntheticSearchInputIntent(Interlocked.Increment(ref _syntheticSearchInputSequence), kind, key, character,
            Environment.Content!.ContentSha256, before.DocumentId, before.CaptureEpoch, target);
        intent.Validate();
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            SyntheticSearchInputDecision decision;
            try { decision = await authority(intent, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { throw new BrowserInputDeniedException("SYNTHETIC_SEARCH_AUTHORITY_UNCONFIRMED"); }
            token.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested) throw new BrowserInputDeniedException("SYNTHETIC_SEARCH_AUTHORITY_UNCONFIRMED");
            if (decision != SyntheticSearchInputDecision.Granted)
                throw new BrowserInputDeniedException(decision switch
                {
                    SyntheticSearchInputDecision.Revoked => "SYNTHETIC_SEARCH_AUTHORITY_REVOKED",
                    SyntheticSearchInputDecision.Expired => "SYNTHETIC_SEARCH_AUTHORITY_EXPIRED",
                    _ => "SYNTHETIC_SEARCH_AUTHORITY_UNCONFIRMED"
                });
            if (_captureAdmission.Status.Phase != CapturePhase.Permitted)
                throw new BrowserInputDeniedException("CAPTURE_EXCLUDED");
            // Authority I/O is not an atomic browser observation. Recheck the actual
            // context and target before any native input can follow the grant.
            var current = (await CaptureCoreAsync().ConfigureAwait(false)) with { CaptureEpoch = _captureAdmission.Status.Epoch };
            var originalTarget = before.Nodes.FirstOrDefault(n => n.Id == target);
            var currentTarget = current.Nodes.FirstOrDefault(n => n.Id == target);
            if (current.DocumentId != before.DocumentId || current.CaptureEpoch != before.CaptureEpoch
                || current.FocusedNode?.Id != before.FocusedNode?.Id
                || target is not null && (originalTarget is null || currentTarget is null || currentTarget.Role != originalTarget.Role
                    || currentTarget.Name != originalTarget.Name || currentTarget.IsDisabled))
                throw new BrowserInputDeniedException("SEARCH_SCOPE_CHANGED");
            token.ThrowIfCancellationRequested();
            var grant = new SyntheticSearchInputGrant(deadline, _captureAdmission, before.CaptureEpoch);
            grant.EnsureCurrent();
            return grant;
        }
        catch { deadline.Dispose(); throw; }
    }

    private sealed class SyntheticSearchInputGrant(CancellationTokenSource deadline, CaptureAdmission admission, long epoch) : IDisposable
    {
        internal void EnsureCurrent()
        {
            if (deadline.IsCancellationRequested) throw new BrowserInputDeniedException("SYNTHETIC_SEARCH_AUTHORITY_UNCONFIRMED");
            var current = admission.Status;
            if (current.Phase != CapturePhase.Permitted || current.Epoch != epoch)
                throw new BrowserInputDeniedException("CAPTURE_EXCLUDED");
        }
        public void Dispose() => deadline.Dispose();
    }
}
