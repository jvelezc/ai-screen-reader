using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record FixtureProgressStart(string Schema, FixtureJob Job, Guid ExecutionId)
{
    public bool RetainSourceFrames { get; init; }
}
public sealed record FixtureProgressMessage(string Schema, Guid ExecutionId, int Sequence, TraversalCheckpoint Checkpoint);
public sealed record FixtureProgressAck(string Schema, Guid ExecutionId, int Sequence);

/// <summary>Framed progress uses stderr; stdout remains the original recording bundle.</summary>
public static class FixtureProgressProtocol
{
    public const string Schema = "hsr-fixture-command-progress-v1";
    public const int MaximumFrameBytes = 1024 * 1024;
    public static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, ReaderWorkerWire.Json);
    public static async Task<T> ReadAsync<T>(Stream input, int limit, CancellationToken token)
    {
        var bytes = await ReaderWorkerWire.ReadAsync(input, limit, token).ConfigureAwait(false)
            ?? throw new BrowserOperationException("WORKER_PROGRESS_DISCONNECTED");
        return JsonSerializer.Deserialize<T>(bytes, ReaderWorkerWire.Json)
            ?? throw new BrowserOperationException("WORKER_PROGRESS_INVALID");
    }
    public static void Validate(FixtureProgressStart start)
    {
        Require(start.Job is not null); start.Job.Validate();
        Require(start.Schema == Schema && start.ExecutionId != Guid.Empty && start.Job.RecordObservation
            && start.Job.TraversalRoute is >= 1 and <= 4);
    }
    public static void Validate(FixtureProgressMessage value, FixtureProgressStart start, FixtureProgressMessage? prior)
    {
        Validate(start);
        var cp = value.Checkpoint;
        var plan = AuthoredKeyboardRoutes.Plans[start.Job.TraversalRoute - 1];
        Require(value.Schema == Schema && value.ExecutionId == start.ExecutionId && cp is not null
            && value.Sequence == (prior?.Sequence ?? 0) + 1 && value.Sequence <= 1 + 2 * plan.Instructions.Length);
        Observation(cp.Source);
        if (prior is null)
        {
            Require(cp.Phase == TraversalCheckpointPhase.Baseline && cp.CommandIndex == 0 && cp.Instruction is null
                && cp.Attempt is null && cp.Result is null && cp.Source.Id == "o1");
            return;
        }
        if (cp.Phase == TraversalCheckpointPhase.Intent)
        {
            Require(prior.Checkpoint.Phase is TraversalCheckpointPhase.Baseline or TraversalCheckpointPhase.Observed
                && cp.CommandIndex == prior.Checkpoint.CommandIndex + 1 && cp.CommandIndex <= plan.Instructions.Length
                && cp.Instruction == plan.Instructions[cp.CommandIndex - 1] && cp.Attempt is null && cp.Result is null
                && Same(cp.Source, prior.Checkpoint.Result ?? prior.Checkpoint.Source));
            return;
        }
        Require(cp.Phase == TraversalCheckpointPhase.Observed && prior.Checkpoint.Phase == TraversalCheckpointPhase.Intent
            && cp.CommandIndex == prior.Checkpoint.CommandIndex && cp.Instruction == prior.Checkpoint.Instruction
            && Same(cp.Source, prior.Checkpoint.Source) && cp.Attempt is not null);
        var attempt = cp.Attempt;
        Require(attempt.Id == $"a{cp.CommandIndex}" && attempt.Instruction == cp.Instruction
            && attempt.SourceObservationId == cp.Source.Id && attempt.ResultObservationId == cp.Result?.Id
            && !attempt.Events.IsDefault && attempt.Events.Length <= 1024
            && double.IsFinite(attempt.StartedMilliseconds) && attempt.StartedMilliseconds >= 0
            && double.IsFinite(attempt.FinishedMilliseconds) && attempt.FinishedMilliseconds >= attempt.StartedMilliseconds
            && (attempt.Step is null || attempt.Step.Command == cp.Instruction!.Command));
        if (cp.Result is not null) Observation(cp.Result);
    }
    public static void Validate(FixtureProgressAck ack, Guid execution, int sequence) =>
        Require(ack.Schema == Schema && ack.ExecutionId == execution && ack.Sequence == sequence);
    private static void Observation(TraversalObservation value) => Require(value is not null && value.Snapshot is not null
        && value.Snapshot.DocumentUrl is null && !value.Snapshot.Nodes.IsDefaultOrEmpty && value.Snapshot.Nodes.Length <= 2048
        && value.Snapshot.DocumentId is { Length: > 0 and <= 2048 } && value.Id is { Length: > 0 and <= 32 }
        && value.Fingerprint is { Length: 64 } && value.Fingerprint.All(char.IsAsciiHexDigitLower)
        && double.IsFinite(value.ElapsedMilliseconds) && value.ElapsedMilliseconds >= 0);
    internal static bool Same<T>(T left, T right) => JsonElement.DeepEquals(
        JsonSerializer.SerializeToElement(left, ReaderWorkerWire.Json), JsonSerializer.SerializeToElement(right, ReaderWorkerWire.Json));
    internal static void Require([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition) throw new BrowserOperationException("WORKER_PROGRESS_INVALID");
    }
}
