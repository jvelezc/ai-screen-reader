using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public static class CommandProgressProjection
{
    public static RetainedCommandProgress Project(CommandProgressSummary value, FixtureJob job, Guid worker)
    {
        if (value.Header.Job != job || value.Header.WorkerId != worker || worker == Guid.Empty)
            throw new BrowserOperationException("WORKER_PROGRESS_IDENTITY_UNCONFIRMED");
        var cp = value.Last;
        var result = new RetainedCommandProgress(RetainedCommandProgress.CurrentSchema, job, worker,
            value.Records, value.Intents, value.Results, cp?.Phase.ToString(), cp?.CommandIndex ?? 0,
            cp?.Instruction?.Command.ToString(), cp?.Instruction?.Key, cp?.Source.Id, cp?.Result?.Id, cp?.Attempt?.Step?.BrowserKeystrokes,
            cp?.Attempt?.Step?.Verification.ToString(), value.LastSha256, value.HasUncommittedWrite, value.Intents > 0);
        result.Validate(); return result;
    }
}
