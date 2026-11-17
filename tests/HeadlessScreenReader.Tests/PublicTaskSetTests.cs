using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Tests;

public sealed class PublicTaskSetTests
{
    private static readonly Guid Organization = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Parent = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Actor = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Request = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Set = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Created = DateTimeOffset.UnixEpoch.AddDays(20000);
    private static readonly PublicReaderSource Source = PublicReaderSource.FromUrl("https://example.com/");

    internal static PublicTaskSet Sample(params string[] kinds)
    {
        var tasks = kinds.Select((kind, i) => new PublicTaskSetMember(PublicTaskSetMember.CurrentSchema,
            new(Organization, Parent, new Guid(i + 1, 0, 0, new byte[8]), new Guid(i + 101, 0, 0, new byte[8]),
                1, Source, new('a', 64))
            { GoalKind = kind },
            "queued", "Queued", Actor, Created.AddSeconds(1), Created.AddSeconds(1), null, null, null)).ToImmutableArray();
        return new(PublicTaskSet.CurrentSchema, Organization, Set, Parent, Request, Actor, Created,
            new('a', 64), 4, kinds.ToImmutableArray(), Source, tasks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void ASetBindsIndependentJobsWithoutBecomingAnEvidenceAssessment(int count)
    {
        string[] kinds = ["Pricing", "Catalog", "Contact", "Help"];
        var value = Sample(kinds.Take(count).ToArray());
        value.Validate(Organization, Parent, Actor, Request);
        Assert.Equal(count, value.Tasks.Length);
        Assert.Equal(kinds.Take(count), value.Tasks.Select(task => task.Job.GoalKind));
        Assert.Equal(count, value.Tasks.Select(task => task.Job.JobId).Distinct().Count());
        Assert.Equal(count, value.Tasks.Select(task => task.Job.RequestId).Distinct().Count());
        Assert.Equal("CapturedPageServiceLinks", value.Scope);
        Assert.False(value.IsAssessment);
        Assert.True(value.TaskResultsRequireOriginalRead);
    }

    [Theory]
    [InlineData("Schema")]
    [InlineData("Organization")]
    [InlineData("Set")]
    [InlineData("Parent")]
    [InlineData("Request")]
    [InlineData("Actor")]
    [InlineData("Created")]
    [InlineData("Digest")]
    [InlineData("Source")]
    [InlineData("MaximumZero")]
    [InlineData("MaximumFive")]
    [InlineData("DefaultKinds")]
    [InlineData("RepeatedKinds")]
    [InlineData("ReorderedKinds")]
    [InlineData("UnsupportedKinds")]
    [InlineData("DefaultTasks")]
    [InlineData("MissingTask")]
    [InlineData("NullTask")]
    public void MalformedSetHeadersCannotBorrowAnotherIdentityOrPlan(string boundary)
    {
        var value = Sample("Pricing", "Catalog");
        value = boundary switch
        {
            "Schema" => value with { Schema = "unknown" },
            "Organization" => value with { OrganizationId = Guid.Empty },
            "Set" => value with { SetId = Guid.Empty },
            "Parent" => value with { ParentScanId = Guid.Empty },
            "Request" => value with { RequestId = Guid.Empty },
            "Actor" => value with { CreatedBy = Guid.Empty },
            "Created" => value with { CreatedAt = default },
            "Digest" => value with { ParentOriginalSha256 = new('A', 64) },
            "Source" => value with { Source = null! },
            "MaximumZero" => value with { MaximumTasks = 0 },
            "MaximumFive" => value with { MaximumTasks = 5 },
            "DefaultKinds" => value with { GoalKinds = default },
            "RepeatedKinds" => value with { GoalKinds = ["Pricing", "Pricing"] },
            "ReorderedKinds" => value with { GoalKinds = ["Catalog", "Pricing"] },
            "UnsupportedKinds" => value with { GoalKinds = ["Pricing", "Search"] },
            "DefaultTasks" => value with { Tasks = default },
            "MissingTask" => value with { Tasks = [value.Tasks[0]] },
            "NullTask" => value with { Tasks = [null!, value.Tasks[1]] },
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        Assert.Throws<BrowserOperationException>(() => value.Validate(Organization, Parent, Actor, Request));
    }

    [Theory]
    [InlineData("Organization")]
    [InlineData("Parent")]
    [InlineData("Actor")]
    [InlineData("Request")]
    public void ValidMetadataStillRequiresTheExactCurrentCallerScope(string boundary)
    {
        var value = Sample("Pricing");
        Assert.Throws<BrowserOperationException>(() => value.Validate(boundary == "Organization" ? Set : Organization,
            boundary == "Parent" ? Set : Parent, boundary == "Actor" ? Set : Actor, boundary == "Request" ? Set : Request));
    }

    [Theory]
    [InlineData("Creator")]
    [InlineData("Schema")]
    [InlineData("CreatedBeforeSet")]
    [InlineData("Original")]
    [InlineData("Source")]
    [InlineData("Goal")]
    [InlineData("JobId")]
    [InlineData("RequestId")]
    [InlineData("Generation")]
    [InlineData("InvalidCompletion")]
    [InlineData("MissingJob")]
    public void SetMembersCannotBorrowAnotherJobSourceCreatorOrOutcome(string boundary)
    {
        var value = Sample("Pricing", "Catalog");
        var task = value.Tasks[1];
        task = boundary switch
        {
            "Creator" => task with { CreatedBy = Set },
            "Schema" => task with { Schema = "hsr-public-task-status-v1" },
            "CreatedBeforeSet" => task with { CreatedAt = Created.AddSeconds(-1) },
            "Original" => task with { Job = task.Job with { ParentOriginalSha256 = new('b', 64) } },
            "Source" => task with { Job = task.Job with { Source = PublicReaderSource.FromUrl("https://other.example/") } },
            "Goal" => task with { Job = task.Job with { GoalKind = "Help" } },
            "JobId" => task with { Job = task.Job with { JobId = value.Tasks[0].Job.JobId } },
            "RequestId" => task with { Job = task.Job with { RequestId = value.Tasks[0].Job.RequestId } },
            "Generation" => task with { Job = task.Job with { Generation = 2 } },
            "InvalidCompletion" => task with { State = "completed" },
            "MissingJob" => task with { Job = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        value = value with { Tasks = value.Tasks.SetItem(1, task) };
        Assert.Throws<BrowserOperationException>(() => value.Validate(Organization, Parent, Actor, Request));
    }

    [Fact]
    public void ABlockedChildRemainsItsOwnStateRatherThanAGroupBarrierFinding()
    {
        var value = Sample("Pricing", "Help");
        value = value with
        {
            Tasks = value.Tasks.SetItem(1, value.Tasks[1] with
            { State = "blocked", ErrorCode = "PUBLIC_TASK_EXECUTION_UNCONFIRMED", FinishedAt = Created.AddMinutes(1) })
        };
        value.Validate(Organization, Parent, Actor, Request);
        Assert.Equal(["queued", "blocked"], value.Tasks.Select(task => task.State));
        Assert.False(value.IsAssessment);
        Assert.True(value.TaskResultsRequireOriginalRead);
    }

    [Theory]
    [InlineData("UnknownState")]
    [InlineData("UnknownPhase")]
    [InlineData("OldUpdate")]
    [InlineData("OldFinish")]
    [InlineData("RawError")]
    [InlineData("WrongErrorPrefix")]
    [InlineData("OversizeError")]
    [InlineData("QueuedDigest")]
    [InlineData("CompletionWithoutTime")]
    [InlineData("CompletionWithError")]
    public void CompactMemberRejectsImpossibleTimesClaimsAndUnsafeDiagnosticStrings(string boundary)
    {
        var value = Sample("Pricing"); var task = value.Tasks[0];
        task = boundary switch
        {
            "UnknownState" => task with { State = "approved" },
            "UnknownPhase" => task with { Phase = "WholeSiteCompliant" },
            "OldUpdate" => task with { UpdatedAt = Created.AddSeconds(-1) },
            "OldFinish" => task with { FinishedAt = Created.AddSeconds(-1) },
            "RawError" => task with { ErrorCode = "PUBLIC_TASK_private url?secret=canary" },
            "WrongErrorPrefix" => task with { ErrorCode = "OTHER_ERROR" },
            "OversizeError" => task with { ErrorCode = "PUBLIC_TASK_" + new string('A', 101) },
            "QueuedDigest" => task with { ReportSha256 = new('b', 64) },
            "CompletionWithoutTime" => task with { State = "completed", ReportSha256 = new('b', 64) },
            "CompletionWithError" => task with { State = "completed", FinishedAt = Created.AddSeconds(2), ReportSha256 = new('b', 64), ErrorCode = "PUBLIC_TASK_ERROR" },
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        Assert.Throws<BrowserOperationException>(() => (value with { Tasks = [task] }).Validate(Organization, Parent, Actor, Request));
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("interrupted")]
    public void CompactMemberRetainsIndependentCompletionAndExpiredAuthorityWithoutInventingCleanup(string state)
    {
        var value = Sample("Pricing"); var completed = state == "completed";
        value = value with
        {
            Tasks = [value.Tasks[0] with
        {
            State = state, Phase = completed ? "Finished" : "Starting",
            ReportSha256 = completed ? new('b', 64) : null, FinishedAt = completed ? Created.AddSeconds(2) : null,
            ErrorCode = completed ? null : "PUBLIC_TASK_PERMISSION_EXPIRED"
        }]
        };
        value.Validate(Organization, Parent, Actor, Request);
        Assert.Equal(state, value.Tasks[0].State); Assert.Equal(completed, value.Tasks[0].ReportSha256 is not null);
        Assert.False(value.IsAssessment); Assert.Equal(completed, value.Tasks[0].FinishedAt is not null);
    }

    [Theory]
    [InlineData(1, "Pricing")]
    [InlineData(2, "Pricing,Catalog")]
    [InlineData(3, "Pricing,Catalog,Contact")]
    [InlineData(4, "Pricing,Catalog,Contact,Help")]
    public void PlanningUsesADeclaredBudgetAndCanonicalKindsRatherThanPageOrder(int maximum, string expected)
    {
        var original = new AccessibilitySnapshot("original-document",
            [new("help", "link", "Help"), new("contact", "link", "Contact us"),
             new("catalog", "link", "Course catalog"), new("pricing", "link", "Pricing"),
             new("search", "searchbox", "Search"), new("account", "link", "Sign in")], [])
        { CaptureEpoch = 1 };
        var coverage = WebsiteTaskCoverage.Inspect(original);
        Assert.Equal(expected, string.Join(',', WebsiteTaskSetPlanning.Select(coverage, maximum)));
        Assert.Equal(8, coverage.Groups.Length);
        Assert.Equal("NotStartedByCoverage", coverage.TaskExecution);
        Assert.False(coverage.Groups[4].ServiceLinkProfileAvailable);
        Assert.False(coverage.FullSiteCoverageEstablished);
    }

    [Fact]
    public void UnsupportedDisabledAndButtonOnlyHintsCannotQueueAServiceLinkTask()
    {
        var snapshot = new AccessibilitySnapshot("source",
            [new("disabled", "link", "Pricing", IsDisabled: true), new("button", "button", "Course catalog"),
             new("search", "searchbox", "Search"), new("email", "textbox", "Email"), new("account", "link", "Sign in")], [])
        { CaptureEpoch = 1 };
        var coverage = WebsiteTaskCoverage.Inspect(snapshot);
        Assert.Empty(WebsiteTaskSetPlanning.Select(coverage, 4));
        Assert.Equal(1, coverage.Groups[0].ObservedControls);
        Assert.Equal(0, coverage.Groups[0].EnabledControls);
        Assert.Equal(1, coverage.Groups[1].EnabledControls);
        Assert.Equal(0, coverage.Groups[1].ServiceLinkCandidates);
        Assert.Equal(1, coverage.Groups[4].ObservedControls);
        Assert.False(coverage.LegalCaseDetermined);
    }

    [Theory]
    [InlineData("ZeroBudget")]
    [InlineData("FiveBudget")]
    [InlineData("Schema")]
    [InlineData("Epoch")]
    [InlineData("Document")]
    [InlineData("DefaultGroups")]
    [InlineData("ReorderedGroups")]
    [InlineData("InflatedCount")]
    [InlineData("NegativeCount")]
    [InlineData("UnsupportedCandidates")]
    [InlineData("SupportFlag")]
    public void PlanningRejectsUnboundMalformedOrInflatedCoverage(string boundary)
    {
        var coverage = WebsiteTaskCoverage.Inspect(new("source", [new("pricing", "link", "Pricing")], []) { CaptureEpoch = 1 });
        coverage = boundary switch
        {
            "Schema" => coverage with { Schema = "unknown" },
            "Epoch" => coverage with { CaptureEpoch = 0 },
            "Document" => coverage with { DocumentId = "" },
            "DefaultGroups" => coverage with { Groups = default },
            "ReorderedGroups" => coverage with { Groups = coverage.Groups.Reverse().ToImmutableArray() },
            "InflatedCount" => coverage with { Groups = coverage.Groups.SetItem(0, coverage.Groups[0] with { ServiceLinkCandidates = 2 }) },
            "NegativeCount" => coverage with { Groups = coverage.Groups.SetItem(0, coverage.Groups[0] with { EnabledControls = -1 }) },
            "UnsupportedCandidates" => coverage with
            {
                Groups = coverage.Groups.SetItem(4,
                coverage.Groups[4] with { ObservedControls = 1, EnabledControls = 1, ServiceLinkCandidates = 1 })
            },
            "SupportFlag" => coverage with { Groups = coverage.Groups.SetItem(0, coverage.Groups[0] with { ServiceLinkProfileAvailable = false }) },
            "ZeroBudget" or "FiveBudget" => coverage,
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        Assert.Throws<ArgumentException>(() => WebsiteTaskSetPlanning.Select(coverage,
            boundary == "ZeroBudget" ? 0 : boundary == "FiveBudget" ? 5 : 4));
    }
}
