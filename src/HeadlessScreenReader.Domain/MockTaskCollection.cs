using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>Closed embedded source identity. This is not a public URL or a finding.</summary>
public sealed record MockTaskSource(string Id, string Sha256)
{
    public string ExpectedHeading() => Id switch
    {
        "checkout-working" => "Reference checkout",
        "checkout-keyboard-blocked" => "Keyboard-blocked checkout",
        "control-named" => "Named details control",
        "control-unnamed" => "Unnamed details control",
        _ => throw new BrowserOperationException("MOCK_TASK_SOURCE_INVALID")
    };
    public string ExpectedGoal() => Id switch
    {
        "checkout-working" or "checkout-keyboard-blocked" => "Sample order placed",
        "control-named" or "control-unnamed" => "Sample details opened",
        _ => throw new BrowserOperationException("MOCK_TASK_SOURCE_INVALID")
    };
    public static MockTaskSource FromId(string id) => new(id, id switch
    {
        "checkout-working" => "70f779f8bb6ffeac7734677b05e3c5e13df28fa7aff6c095db7dcda2b10695bf",
        "checkout-keyboard-blocked" => "7729a19422eff5e111d16d2c4394724b6732cebde3f195dc6cbd9ce00785ad39",
        "control-named" => "8cb47651cf45f562dd0da7ef465fadc0f248e9ab27a420a7e3a1f6301f396bbc",
        "control-unnamed" => "711e83f678990642c42a5e1b267c1abecad3804af7a1b7007de2fe551d7bc4bb",
        _ => throw new BrowserOperationException("MOCK_TASK_SOURCE_INVALID")
    });
    public void Validate()
    {
        if (this != FromId(Id)) throw new BrowserOperationException("MOCK_TASK_SOURCE_INVALID");
    }
}

/// <summary>Nonsecret task identity. Generation one only: uncertain input cannot be automatically replayed.</summary>
public sealed record MockTaskJob(Guid OrganizationId, Guid ScanId, Guid JobId, long Generation,
    string Profile, MockTaskSource Source)
{
    public const string CurrentProfile = "hsr-embedded-mock-task-v1";
    public void Validate()
    {
        if (OrganizationId == Guid.Empty || ScanId == Guid.Empty || JobId == Guid.Empty || Generation != 1
            || Profile != CurrentProfile || Source is null)
            throw new BrowserOperationException("MOCK_TASK_JOB_INVALID");
        Source.Validate();
    }
}

/// <summary>Parent-owned execution, independent from trial document/run identities. No lease secret enters the child.</summary>
public sealed record MockTaskStart(string Schema, MockTaskJob Job, Guid WorkerId, Guid ExecutionId)
{
    public void Validate()
    {
        if (Schema != MockTaskControl.Schema || Job is null || WorkerId == Guid.Empty || ExecutionId == Guid.Empty)
            throw new BrowserOperationException("MOCK_TASK_START_INVALID");
        Job.Validate();
    }
}

public sealed record MockTaskBoundary(int Trial, string Action);
/// <summary>A request to advance, never confirmation that the input was delivered or a goal reached.</summary>
public sealed record MockTaskIntent(string Schema, MockTaskStart Start, int Sequence, MockTaskBoundary Boundary);
public sealed record MockTaskAck(string Schema, Guid ExecutionId, int Sequence, string IntentSha256);

/// <summary>Fixed input sequence and limits for this profile. Other protocols cannot authorize these tasks.</summary>
public static class MockTaskControl
{
    public const string Schema = "hsr-mock-task-control-v1";
    public const int MaximumFrameBytes = 4096;
    public static ImmutableArray<MockTaskBoundary> Boundaries =>
    [
        new(1, "Open"), new(1, "Tab"), new(1, "Enter"),
        new(2, "Open"), new(2, "Tab"), new(2, "Enter"),
        new(3, "Open"), new(3, "Tab"), new(3, "FocusMode"), new(3, "Space"),
        new(4, "Open"), new(4, "Pointer"), new(0, "Publish")
    ];

    public static void Validate(MockTaskIntent intent, MockTaskStart expected, int sequence)
    {
        expected.Validate();
        if (intent.Schema != Schema || intent.Start != expected || intent.Sequence != sequence
            || sequence < 1 || sequence > Boundaries.Length || intent.Boundary != Boundaries[sequence - 1])
            throw new BrowserOperationException("MOCK_TASK_INTENT_INVALID");
    }
}
