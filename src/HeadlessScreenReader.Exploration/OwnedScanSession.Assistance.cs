using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Exploration;

public sealed partial class OwnedScanSession
{
    private bool _simulationIntroduced;
    internal bool SupportsAssistance => _store is IOwnedAssistanceStore;

    private void EnsureExcludedLease(CaptureExclusionLease lease)
    {
        if (_closing || _phase != CapturePhase.Excluded || lease.Token != _token || lease.Epoch != _epoch || _fault is not null)
            throw new BrowserOperationException("OWNED_ASSISTANCE_GAP_INVALID");
    }
    internal CaptureExclusionLease RequireAdapterGapAuthority(CaptureExclusionLease lease)
    {
        lock (_sync)
        {
            EnsureExcludedLease(lease);
            if (_active != 0 || _writes != 0 || _observed.AdapterLease is not { } adapter || adapter.Epoch != _epoch)
                throw new BrowserOperationException("OWNED_ASSISTANCE_GAP_UNCONFIRMED");
            return adapter;
        }
    }
    internal Task<OwnedAssistanceReceipt> ExecuteAssistanceTrackedAsync(CaptureExclusionLease gap, long generation,
        SyntheticInputAction action, Func<CancellationToken, Task<SyntheticInputResult>> input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_store is not IOwnedAssistanceStore store) throw new BrowserOperationException("OWNED_ASSISTANCE_STORE_UNAVAILABLE");
        if (!Enum.IsDefined(action) || generation < 1) throw new BrowserOperationException("OWNED_ASSISTANCE_INVALID");
        lock (_sync)
        {
            EnsureExcludedLease(gap);
            if (_active++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        var actual = AssistanceCoreAsync(store, gap, generation, action, input, cancellationToken);
        ObserveFault(actual); return actual;
    }
    private async Task<OwnedAssistanceReceipt> AssistanceCoreAsync(IOwnedAssistanceStore store, CaptureExclusionLease gap, long generation,
        SyntheticInputAction action, Func<CancellationToken, Task<SyntheticInputResult>> input, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            await _commands.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                OwnedAssistanceIntent intent;
                lock (_sync)
                {
                    EnsureExcludedLease(gap);
                    if (_ordinal >= _maximumCommands) throw new BrowserOperationException("OWNED_COMMAND_BUDGET_EXHAUSTED");
                    intent = new(_store.ScanId, ++_ordinal, _epoch, generation, action, Elapsed());
                }
                OwnedWriteReceipt? intentWrite = null;
                var invoked = false;
                SyntheticInputResult result;
                try
                {
                    intentWrite = await WriteAsync(() => store.WriteAssistanceIntentAsync(intent)).ConfigureAwait(false);
                    lock (_sync) { EnsureExcludedLease(gap); _simulationIntroduced = true; }
                    linked.Token.ThrowIfCancellationRequested();
                    invoked = true;
                    result = await input(linked.Token).ConfigureAwait(false);
                    if (!ValidAssistanceResult(action, result))
                    {
                        Fault("OWNED_ASSISTANCE_RESULT_INVALID");
                        throw new BrowserOperationException("OWNED_ASSISTANCE_RESULT_INVALID");
                    }
                }
                catch (OperationCanceledException) { result = new(action, invoked ? 1 : 0, 0, invoked, false, "SYNTHETIC_INPUT_CANCELLED"); }
                catch (BrowserInputDeniedException) { result = new(action, 0, 0, false, false, "SYNTHETIC_INPUT_FENCED"); }
                catch (BrowserOperationException) { result = new(action, invoked ? 1 : 0, 0, invoked, false, "SYNTHETIC_INPUT_UNCONFIRMED"); }
                catch
                {
                    if (!invoked) Fault("OWNED_ASSISTANCE_WRITE_UNCONFIRMED");
                    result = new(action, invoked ? 1 : 0, 0, invoked, false, "SYNTHETIC_INPUT_UNCONFIRMED");
                }
                var completion = new OwnedAssistanceCompletion(intent, result, Elapsed());
                OwnedWriteReceipt completionWrite;
                try { completionWrite = await WriteAsync(() => store.WriteAssistanceCompletionAsync(completion)).ConfigureAwait(false); }
                catch { Fault("OWNED_ASSISTANCE_COMPLETION_UNCONFIRMED"); throw new BrowserOperationException("OWNED_ASSISTANCE_COMPLETION_UNCONFIRMED"); }
                lock (_sync)
                    if (intentWrite is null || _fault is not null) throw new BrowserOperationException(_fault ?? "OWNED_ASSISTANCE_WRITE_UNCONFIRMED");
                return new(completion, intentWrite, completionWrite);
            }
            finally { _commands.Release(); }
        }
        finally { lock (_sync) if (--_active == 0) _drained.TrySetResult(); }
    }
    private static bool ValidAssistanceResult(SyntheticInputAction action, SyntheticInputResult? result) => result is not null && result.Action == action
        && result.InputRequests >= 0 && result.InputRequests <= (action == SyntheticInputAction.EnterCanary ? 1 : 2)
        && result.CompletedInputCalls >= 0 && result.CompletedInputCalls <= result.InputRequests
        && (result.ErrorCode is null or "SYNTHETIC_INPUT_CANCELLED" or "SYNTHETIC_FOCUS_REQUIRED" or "SYNTHETIC_CLEAR_REQUIRED"
            or "SYNTHETIC_INPUT_FENCED" or "SYNTHETIC_INPUT_UNCONFIRMED" or "SYNTHETIC_DIAGNOSTICS_DENIED")
        && (result.ErrorCode is not null || result.InputRequests == (action == SyntheticInputAction.EnterCanary ? 1 : 2)
            && result.CompletedInputCalls == result.InputRequests && !result.InputUncertain
            && result.CanaryCleared == (action == SyntheticInputAction.ClearCanary));
}
