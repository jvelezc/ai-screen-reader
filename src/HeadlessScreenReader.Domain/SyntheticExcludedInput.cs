namespace HeadlessScreenReader.Domain;

public enum SyntheticInputAction { EnterCanary, ClearCanary }
/// <summary>Native authority for one excluded epoch and generation. Never persist its token.</summary>
public sealed record SyntheticInputLease(Guid Token, long Epoch, long Generation);
public sealed record SyntheticInputStatus(bool Supported, bool CanaryMayRemain, bool AuthorityActive, int ActiveOperations);
public sealed record SyntheticInputResult(SyntheticInputAction Action, int InputRequests, int CompletedInputCalls,
    bool InputUncertain, bool CanaryCleared, string? ErrorCode)
{
    public bool InputMayHaveOccurred => CompletedInputCalls > 0 || InputUncertain;
}

/// <summary>Fixed fixture simulation only. Accepts no caller text, credentials or arbitrary key.</summary>
public interface ISyntheticExcludedInput
{
    SyntheticInputStatus SyntheticStatus { get; }
    SyntheticInputLease GrantSyntheticInput(CaptureExclusionLease captureLease, long generation, TimeSpan? lifetime = null);
    Task<SyntheticInputResult> ExecuteSyntheticInputAsync(SyntheticInputLease lease, SyntheticInputAction action,
        CancellationToken cancellationToken = default);
    Task RevokeSyntheticInputAsync(SyntheticInputLease lease);
}
