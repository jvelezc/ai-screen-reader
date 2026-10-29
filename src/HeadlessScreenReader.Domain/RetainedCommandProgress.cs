namespace HeadlessScreenReader.Domain;

/// <summary>A compact diagnostic prefix for one admitted keyboard attempt, never a completion or replay receipt.</summary>
public sealed record RetainedCommandProgress(string Schema, FixtureJob Job, Guid WorkerId,
    int Records, int Intents, int Results, string? LastPhase, int LastCommandIndex,
    string? Command, string? Key, string? SourceObservationId, string? ResultObservationId,
    int? LastCompletedInputCalls, string? LastCommandVerification, string PrefixSha256,
    bool HasUncommittedWrite, bool InputMayHaveOccurred, bool IsCompletionReceipt = false, bool CanReplay = false, string GoalVerification = "NotAssessed")
{
    public const string CurrentSchema = "hsr-retained-command-progress-v1";

    public void Validate()
    {
        void Require(bool value) { if (!value) throw new BrowserOperationException("WORKER_PROGRESS_INVALID"); }
        Require(Job is not null); Job!.Validate();
        Require(Schema == CurrentSchema && WorkerId != Guid.Empty && Job.RecordObservation && Job.TraversalRoute is >= 1 and <= 4
            && !IsCompletionReceipt && !CanReplay && GoalVerification == "NotAssessed" && InputMayHaveOccurred == (Intents > 0)
            && PrefixSha256 is { Length: 64 } && PrefixSha256.All(char.IsAsciiHexDigitLower));
        var plan = AuthoredKeyboardRoutes.Plans[Job.TraversalRoute - 1];
        Require(Records >= 0 && Records <= 1 + 2 * plan.Instructions.Length && Intents == Records / 2
            && Results == Math.Max(0, (Records - 1) / 2) && LastCommandIndex == Records / 2);
        var phase = Records == 0 ? null : Records == 1 ? "Baseline" : Records % 2 == 0 ? "Intent" : "Observed";
        Require(LastPhase == phase && (Records == 0 ? SourceObservationId is null : Id(SourceObservationId))
            && (ResultObservationId is null || phase == "Observed" && Id(ResultObservationId)));
        Require(LastCommandIndex == 0 ? Command is null && Key is null
            : Command == plan.Instructions[LastCommandIndex - 1].Command.ToString() && Key == plan.Instructions[LastCommandIndex - 1].Key);
        Require(phase == "Observed"
            ? (LastCompletedInputCalls is null || LastCompletedInputCalls is >= 0 and <= 16)
                && (LastCommandVerification is null || Enum.GetNames<VerificationStatus>().Contains(LastCommandVerification, StringComparer.Ordinal))
            : LastCompletedInputCalls is null && LastCommandVerification is null);
    }

    private static bool Id(string? value) => value is { Length: > 0 and <= 32 } && !value.Any(char.IsControl);
}
