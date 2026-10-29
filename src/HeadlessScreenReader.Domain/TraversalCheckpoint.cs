namespace HeadlessScreenReader.Domain;

public enum TraversalCheckpointPhase { Baseline, Intent, Observed }

/// <summary>A retained prefix of one route. It is not a completed recording or an overall goal verdict.</summary>
public sealed record TraversalCheckpoint(TraversalCheckpointPhase Phase, int CommandIndex,
    TraversalObservation Source, TraversalInstruction? Instruction = null,
    TraversalAttempt? Attempt = null, TraversalObservation? Result = null);
