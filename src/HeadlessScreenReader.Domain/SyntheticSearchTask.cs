using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>Closed source identity for the independent search-task worker profile.</summary>
public sealed record SyntheticSearchTaskSource(string Id, string Sha256)
{
    public static SyntheticSearchTaskSource FromId(string id) => new(id, id switch
    {
        "search-working" => "b6de520a94841c634d567a30a3a5809a7d51f0dee43354c1e6f6297ef35a2f12",
        "search-keyboard-blocked" => "e486f3e0e3fd694cd80fbad8ab6955e29ae19b41b52f98bafab7fd0af5b19d9f",
        _ => throw new BrowserOperationException("SEARCH_TASK_SOURCE_INVALID")
    });

    public void Validate()
    {
        if (this != FromId(Id)) throw new BrowserOperationException("SEARCH_TASK_SOURCE_INVALID");
    }
}

/// <summary>Generation one only. Interrupted physical input cannot be automatically replayed.</summary>
public sealed record SyntheticSearchTaskJob(Guid OrganizationId, Guid ScanId, Guid JobId, long Generation,
    string Profile, SyntheticSearchTaskSource Source)
{
    public const string CurrentProfile = "hsr-embedded-search-task-v1";
    public void Validate()
    {
        if (OrganizationId == Guid.Empty || ScanId == Guid.Empty || JobId == Guid.Empty || Generation != 1
            || Profile != CurrentProfile || Source is null)
            throw new BrowserOperationException("SEARCH_TASK_JOB_INVALID");
        Source.Validate();
    }
}

public sealed record SyntheticSearchTaskStart(string Schema, SyntheticSearchTaskJob Job, Guid WorkerId, Guid ExecutionId)
{
    public void Validate()
    {
        if (Schema != SyntheticSearchTaskControl.Schema || Job is null || WorkerId == Guid.Empty || ExecutionId == Guid.Empty)
            throw new BrowserOperationException("SEARCH_TASK_START_INVALID");
        Job.Validate();
    }
}

public sealed record SyntheticSearchTaskBoundary(int Trial, string Action, int NativeSequence = 0,
    SyntheticSearchInputKind? Kind = null, string? NativeKey = null, int CharacterIndex = 0);
public sealed record SyntheticSearchTaskIntent(string Schema, SyntheticSearchTaskStart Start, int Sequence,
    SyntheticSearchTaskBoundary Boundary, SyntheticSearchInputIntent? Input);
public sealed record SyntheticSearchTaskAck(string Schema, Guid ExecutionId, int Sequence, string IntentSha256,
    SyntheticSearchInputDecision Decision);

/// <summary>Five fresh contexts, including field submission. A permission request is not completed input.</summary>
public static class SyntheticSearchTaskControl
{
    public const string Schema = "hsr-search-task-control-v1";
    public const int MaximumFrameBytes = 8192;
    public static ImmutableArray<SyntheticSearchTaskBoundary> Boundaries { get; } = Build();

    public static TraversalPlan KeyboardPlan(int trial) => trial switch
    {
        1 or 2 => new("Synthetic course search: field Enter", new("heading", "Find a sample course"),
            new("heading", "Sample course result", MustBeFocused: true),
            [new(ReaderCommand.Tab), new(ReaderCommand.EnterSearchText, SyntheticSearchPolicy.Query), new(ReaderCommand.PressKey, "Enter")]),
        3 => SyntheticSearchPolicy.KeyboardPlan("Enter"),
        4 => SyntheticSearchPolicy.KeyboardPlan("Space"),
        _ => throw new BrowserOperationException("SEARCH_TASK_TRIAL_INVALID")
    };

    public static void Validate(SyntheticSearchTaskIntent intent, SyntheticSearchTaskStart expected, int sequence)
    {
        expected.Validate();
        if (intent.Schema != Schema || intent.Start != expected || intent.Sequence != sequence
            || sequence < 1 || sequence > Boundaries.Length || intent.Boundary != Boundaries[sequence - 1])
            throw new BrowserOperationException("SEARCH_TASK_INTENT_INVALID");
        var boundary = intent.Boundary;
        if (boundary.Action != "Native")
        {
            if (intent.Input is not null) throw new BrowserOperationException("SEARCH_TASK_INTENT_INVALID");
            return;
        }
        if (intent.Input is not { } input) throw new BrowserOperationException("SEARCH_TASK_INTENT_INVALID");
        input.Validate();
        if (input.NativeSequence != boundary.NativeSequence || input.Kind != boundary.Kind
            || input.NativeKey != boundary.NativeKey || input.CharacterIndex != boundary.CharacterIndex
            || input.SourceSha256 != expected.Job.Source.Sha256 || input.CaptureEpoch != 1)
            throw new BrowserOperationException("SEARCH_TASK_INTENT_INVALID");
    }

    private static ImmutableArray<SyntheticSearchTaskBoundary> Build()
    {
        var result = ImmutableArray.CreateBuilder<SyntheticSearchTaskBoundary>();
        for (var trial = 1; trial <= 5; trial++)
        {
            result.Add(new(trial, "Open"));
            var native = 0;
            void Input(SyntheticSearchInputKind kind, string? key = null, int character = 0) =>
                result.Add(new(trial, "Native", ++native, kind, key, character));
            Input(trial == 5 ? SyntheticSearchInputKind.MouseFocus : SyntheticSearchInputKind.Key, trial == 5 ? null : "Tab");
            if (trial == 5) result.Add(new(trial, "FocusMode"));
            for (var index = 0; index < SyntheticSearchPolicy.Query.Length; index++)
                Input(SyntheticSearchInputKind.Character, SyntheticSearchPolicy.Query[index] == ' ' ? "Space" : SyntheticSearchPolicy.Query[index].ToString(), index + 1);
            if (trial is 3 or 4) Input(SyntheticSearchInputKind.Key, "Tab");
            if (trial == 4) result.Add(new(trial, "FocusMode"));
            Input(trial == 5 ? SyntheticSearchInputKind.MouseActivation : SyntheticSearchInputKind.Key, trial == 5 ? null : trial == 4 ? "Space" : "Enter");
        }
        result.Add(new(0, "Publish"));
        return result.ToImmutable();
    }
}
