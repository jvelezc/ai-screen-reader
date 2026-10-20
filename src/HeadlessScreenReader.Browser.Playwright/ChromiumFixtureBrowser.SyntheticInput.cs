using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

public sealed partial class ChromiumFixtureBrowser : ISyntheticExcludedInput
{
    private readonly string _syntheticCanary = "synthetic-private-" + Guid.NewGuid().ToString("N");
    private volatile bool _syntheticCanaryMayRemain;
    internal string SyntheticCanaryForAudit => _syntheticCanary;
    public SyntheticInputStatus SyntheticStatus
    {
        get
        {
            var state = _captureAdmission.SyntheticState;
            return new(_fixture == BundledFixture.SyntheticHandoff && _publicPolicy is null,
                _syntheticCanaryMayRemain, state.Active, state.Operations);
        }
    }
    public SyntheticInputLease GrantSyntheticInput(CaptureExclusionLease captureLease, long generation, TimeSpan? lifetime = null)
    {
        EnsureSyntheticFixture();
        EnsureSyntheticDiagnosticsDisabled();
        return _captureAdmission.GrantSyntheticInput(captureLease, generation, lifetime);
    }
    public Task RevokeSyntheticInputAsync(SyntheticInputLease lease)
    {
        EnsureSyntheticFixture();
        return _captureAdmission.RevokeSyntheticInputAsync(lease);
    }
    public Task<SyntheticInputResult> ExecuteSyntheticInputAsync(SyntheticInputLease lease, SyntheticInputAction action,
        CancellationToken cancellationToken = default)
    {
        EnsureSyntheticFixture();
        if (!Enum.IsDefined(action)) throw new BrowserInputDeniedException("SYNTHETIC_ACTION_DENIED");
        return _captureAdmission.RunSyntheticInputAsync<SyntheticInputResult>(lease, async () =>
        {
            var requested = 0; var completed = 0; var uncertain = false;
            string? code = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await ScopeMatchesAsync().ConfigureAwait(false))
                    throw new BrowserInputDeniedException("SYNTHETIC_FOCUS_REQUIRED");
                if (action == SyntheticInputAction.EnterCanary)
                {
                    EnsureSyntheticDiagnosticsDisabled();
                    if (_syntheticCanaryMayRemain) throw new BrowserInputDeniedException("SYNTHETIC_CLEAR_REQUIRED");
                    await SendAsync(() => _page.Keyboard.InsertTextAsync(_syntheticCanary), entering: true).ConfigureAwait(false);
                }
                else
                {
                    await SendAsync(() => _page.Keyboard.PressAsync("ControlOrMeta+A")).ConfigureAwait(false);
                    await SendAsync(() => _page.Keyboard.PressAsync("Backspace")).ConfigureAwait(false);
                    if (!await _page.EvaluateAsync<bool>("() => document.querySelector('#synthetic-sensitive')?.value === ''").ConfigureAwait(false))
                        throw new BrowserOperationException("SYNTHETIC_CLEAR_UNCONFIRMED");
                    await _page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))").ConfigureAwait(false);
                    _syntheticCanaryMayRemain = false;
                }
            }
            catch (OperationCanceledException) { code = "SYNTHETIC_INPUT_CANCELLED"; }
            catch (BrowserInputDeniedException exception) { code = exception.Code; }
            catch { code = "SYNTHETIC_INPUT_UNCONFIRMED"; }
            return new(action, requested, completed, uncertain, !_syntheticCanaryMayRemain, code);

            async Task SendAsync(Func<Task> send, bool entering = false)
            {
                cancellationToken.ThrowIfCancellationRequested();
                requested++;
                if (!await ScopeMatchesAsync().ConfigureAwait(false))
                    throw new BrowserInputDeniedException("SYNTHETIC_FOCUS_REQUIRED");
                try
                {
                    await _captureAdmission.DispatchSyntheticInputAsync(lease, () =>
                    {
                        if (entering) _syntheticCanaryMayRemain = true;
                        return send();
                    }).ConfigureAwait(false);
                    completed++;
                }
                catch (BrowserInputDeniedException) { throw; }
                catch { uncertain = true; throw; }
            }
            Task<bool> ScopeMatchesAsync() => _page.EvaluateAsync<bool>(
                "() => document.activeElement?.id === 'synthetic-sensitive' && document.activeElement.type === 'password' && !document.activeElement.disabled && !document.activeElement.readOnly");
        });
    }
    private void EnsureSyntheticFixture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_fixture != BundledFixture.SyntheticHandoff || _publicPolicy is not null)
            throw new BrowserInputDeniedException("SYNTHETIC_FIXTURE_REQUIRED");
    }
    internal static bool SyntheticDiagnosticsPermitted(string? debug, string? pwdebug) =>
        string.IsNullOrEmpty(debug) && (string.IsNullOrEmpty(pwdebug) || pwdebug == "0");
    internal static void EnsureSyntheticDiagnosticsDisabled()
    {
        if (!SyntheticDiagnosticsPermitted(System.Environment.GetEnvironmentVariable("DEBUG"), System.Environment.GetEnvironmentVariable("PWDEBUG")))
            throw new BrowserInputDeniedException("SYNTHETIC_DIAGNOSTICS_DENIED");
    }
}
