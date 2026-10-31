using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>One canonical checkpoint export. Its hash is distinct from the sealed journal entry's source hash.</summary>
public sealed record RetainedCheckpointDescriptor(int Sequence, string SourceSha256, string CheckpointSha256,
    int Bytes, TraversalCheckpointPhase Phase, int CommandIndex);

/// <summary>An actual retained observation, linked to the checkpoint that first exposes its complete semantics.</summary>
public sealed record RetainedProgressNode(string ObservationId, string DocumentId, string Fingerprint, int CheckpointSequence);

/// <summary>An intended command and its optional observed result. Missing input counts remain unknown.</summary>
public sealed record RetainedProgressEdge(int CommandIndex, int IntentSequence, int? ObservedSequence,
    string SourceObservationId, string? ResultObservationId, TraversalInstruction Instruction,
    int? CompletedInputCalls, VerificationStatus? Verification, ReaderOutcome? Outcome, bool TimelineTruncated,
    bool InputMayHaveOccurred = true);

/// <summary>A diagnostic execution prefix, including unresolved intent. No completed recording, barrier or overall goal is inferred.</summary>
public sealed record RetainedProgressGraph(string Schema, RetainedCommandProgress Progress,
    ImmutableArray<RetainedCheckpointDescriptor> Checkpoints, ImmutableArray<RetainedProgressNode> Nodes,
    ImmutableArray<RetainedProgressEdge> Edges)
{
    public const string CurrentSchema = "hsr-retained-progress-graph-v1";

    public void Validate()
    {
        Require(Progress is not null); Progress!.Validate();
        Require(Schema == CurrentSchema && !Checkpoints.IsDefault && !Nodes.IsDefault && !Edges.IsDefault
            && Checkpoints.Length == Progress.Records && Edges.Length == Progress.Intents
            && Nodes.Length <= Progress.Results + 1 && (Progress.Records == 0 ? Nodes.IsEmpty : !Nodes.IsEmpty));
        for (var i = 0; i < Checkpoints.Length; i++)
        {
            var file = Checkpoints[i];
            Require(file is not null && file.Sequence == i + 1 && Digest(file.SourceSha256) && Digest(file.CheckpointSha256)
                && file.Bytes is > 0 and <= 1048576 && file.CommandIndex == (i + 1) / 2
                && file.Phase == (i == 0 ? TraversalCheckpointPhase.Baseline : i % 2 == 1 ? TraversalCheckpointPhase.Intent : TraversalCheckpointPhase.Observed));
        }
        Require(Checkpoints.IsEmpty || Checkpoints[^1].SourceSha256 == Progress.PrefixSha256);
        Require(Nodes.All(n => n is not null && Id(n.ObservationId, 32) && Id(n.DocumentId, 2048) && Digest(n.Fingerprint)
                && n.CheckpointSequence >= 1 && n.CheckpointSequence <= Progress.Records && n.CheckpointSequence % 2 == 1)
            && Nodes.Select(n => n.ObservationId).Distinct(StringComparer.Ordinal).Count() == Nodes.Length);
        Require(Nodes.IsEmpty || Nodes[0].ObservationId == "o1" && Nodes[0].CheckpointSequence == 1);
        var plan = AuthoredKeyboardRoutes.Plans[Progress.Job.TraversalRoute - 1];
        for (var i = 0; i < Edges.Length; i++)
        {
            var edge = Edges[i]; var observed = 2 * i + 3 <= Progress.Records ? 2 * i + 3 : (int?)null;
            Require(edge is not null && edge.CommandIndex == i + 1 && edge.IntentSequence == 2 * i + 2
                && edge.ObservedSequence == observed && edge.Instruction == plan.Instructions[i] && edge.InputMayHaveOccurred
                && Nodes.Any(n => n.ObservationId == edge.SourceObservationId && n.CheckpointSequence < edge.IntentSequence)
                && (edge.ResultObservationId is null || observed is not null && Nodes.Any(n => n.ObservationId == edge.ResultObservationId && n.CheckpointSequence <= observed))
                && (edge.CompletedInputCalls is null || edge.CompletedInputCalls is >= 0 and <= 16)
                && (edge.Verification is null || Enum.IsDefined(edge.Verification.Value))
                && (edge.Outcome is null || Enum.IsDefined(edge.Outcome.Value))
                && (observed is not null || edge.ResultObservationId is null && edge.CompletedInputCalls is null
                    && edge.Verification is null && edge.Outcome is null && !edge.TimelineTruncated));
            Require(i == 0 ? edge.SourceObservationId == "o1" : edge.SourceObservationId == (Edges[i - 1].ResultObservationId ?? Edges[i - 1].SourceObservationId));
        }
        Require(Nodes.All(n => n.ObservationId == "o1" || Edges.Any(e => e.ResultObservationId == n.ObservationId)));
        if (!Edges.IsEmpty)
        {
            var last = Edges[^1];
            Require(last.SourceObservationId == Progress.SourceObservationId && last.ResultObservationId == Progress.ResultObservationId
                && last.CompletedInputCalls == Progress.LastCompletedInputCalls && last.Verification?.ToString() == Progress.LastCommandVerification);
        }
    }

    private static bool Id(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum && !value.Any(char.IsControl);
    internal static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition)
    { if (!condition) throw new BrowserOperationException("WORKER_PROGRESS_INVALID"); }
}

/// <summary>A fixed-size byte page of a canonical full checkpoint export, not a raw native journal file or media.</summary>
public sealed record RetainedCheckpointPage(RetainedCommandProgress Progress, int Sequence, string SourceSha256,
    string CheckpointSha256, int FileBytes, int Offset, byte[] Data, string PageSha256, int? NextOffset)
{
    public const int PageBytes = 16384;
}
