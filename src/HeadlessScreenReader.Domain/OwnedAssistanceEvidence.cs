namespace HeadlessScreenReader.Domain;

/// <summary>Payload-free simulated assistance. No secret, browser key, observation or human-acceptance claim.</summary>
public sealed record OwnedAssistanceIntent(string ScanId, long Ordinal, long Epoch, long ControllerGeneration,
    SyntheticInputAction Action, double StartedElapsedMilliseconds)
{
    public string Schema => "hsr-simulated-assistance-intent-v1";
    public ControllerRole Origin => ControllerRole.SimulatedAssistance;
}
public sealed record OwnedAssistanceCompletion(OwnedAssistanceIntent Intent, SyntheticInputResult Input, double CompletedElapsedMilliseconds)
{
    public string Schema => "hsr-simulated-assistance-completion-v1";
    public bool HumanAcceptancePerformed => false;
}
public sealed record OwnedAssistanceReceipt(OwnedAssistanceCompletion Completion, OwnedWriteReceipt IntentWrite, OwnedWriteReceipt CompletionWrite)
{
    public string Schema => "hsr-simulated-assistance-receipt-v1";
}

/// <summary>Optional typed metadata capability. Unsupported stores cannot admit simulated input.</summary>
public interface IOwnedAssistanceStore
{
    Task<OwnedWriteReceipt> WriteAssistanceIntentAsync(OwnedAssistanceIntent intent);
    Task<OwnedWriteReceipt> WriteAssistanceCompletionAsync(OwnedAssistanceCompletion completion);
}
