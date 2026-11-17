using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using Xunit;

namespace HeadlessScreenReader.Tests;

public sealed class PublicReviewAssessmentTests
{
    [Fact]
    public void EmptyCoverageCannotBecomeANoBarrierOrReadyFinding()
    {
        var set = PublicReviewTestData.Set(0);
        var value = PublicReviewTestData.Assessment(set, []);
        PublicReviewTestData.Validate(value);
        Assert.Equal("InsufficientEvidence", value.Outcome);
        Assert.Empty(value.BarrierJobIds);
        Assert.Equal(0, value.VerifiedTaskCount);
        Assert.True(value.IsAssessment);
        Assert.False(value.TaskSet.IsAssessment);
        Assert.False(value.LegalCaseDetermined);
        Assert.False(value.FullSiteCoverageEstablished);
        Assert.False(value.WebsiteInputStartedByAssessment);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void AllVerifiedSuccessfulTasksSupportOnlyTheirDeclaredTaskScope(int count)
    {
        var set = PublicReviewTestData.Set(count);
        var value = PublicReviewTestData.Assessment(set, [.. set.Tasks.Select(task => PublicReviewTestData.Verified(task, false))]);
        PublicReviewTestData.Validate(value);
        Assert.Equal("NoBlockingBarrierInTestedTasks", value.Outcome);
        Assert.Equal(count, value.VerifiedTaskCount);
        Assert.Empty(value.BarrierJobIds);
        Assert.Equal("CapturedPageServiceLinks", value.Scope);
    }

    [Fact]
    public void OneIndependentCompleteBarrierRetainsOtherMissingEvidenceExplicitly()
    {
        var set = PublicReviewTestData.Set(2);
        var stopped = set.Tasks[1] with { State = "blocked", ReportSha256 = null, ErrorCode = "PUBLIC_TASK_UNCONFIRMED" };
        set = set with { Tasks = [set.Tasks[0], stopped] };
        var value = PublicReviewTestData.Assessment(set, [PublicReviewTestData.Verified(set.Tasks[0], true), PublicReviewTestData.Missing(stopped, "Stopped")]);
        PublicReviewTestData.Validate(value);
        Assert.Equal("WebsiteEvidenceReady", value.Outcome);
        Assert.Equal(new[] { set.Tasks[0].Job.JobId }, value.BarrierJobIds.ToArray());
        Assert.Equal(1, value.VerifiedTaskCount);
        Assert.Null(value.Tasks[1].Assessment);
        Assert.Empty(value.Tasks[1].OriginalFiles);
    }

    [Fact]
    public void TrueConditionsFromDifferentTasksCannotBeCombinedIntoABarrier()
    {
        var set = PublicReviewTestData.Set(2);
        var left = PublicReviewTestData.Verified(set.Tasks[0], true);
        var right = PublicReviewTestData.Verified(set.Tasks[1], false);
        left = left with { Assessment = new("InsufficientEvidence", [.. left.Assessment!.Conditions.Select(condition => condition with { Satisfied = condition.Id != "ReproducibleEnvironment" })]) };
        var value = PublicReviewTestData.Assessment(set, [left, right]);
        PublicReviewTestData.Validate(value);
        Assert.Equal("InsufficientEvidence", value.Outcome);
        Assert.Empty(value.BarrierJobIds);
        Assert.Equal(2, value.VerifiedTaskCount);
    }

    [Theory]
    [InlineData("OriginalsUnavailable")]
    [InlineData("OriginalsNotRead")]
    public void MissingOriginalsCannotBecomeACleanTaskSet(string availability)
    {
        var set = PublicReviewTestData.Set(2);
        var value = PublicReviewTestData.Assessment(set, [PublicReviewTestData.Verified(set.Tasks[0], false), PublicReviewTestData.Missing(set.Tasks[1], availability)]);
        PublicReviewTestData.Validate(value);
        Assert.Equal("InsufficientEvidence", value.Outcome);
        Assert.Equal(1, value.VerifiedTaskCount);
        Assert.Null(value.Tasks[1].ReportSha256);
    }

    [Theory]
    [InlineData("job")]
    [InlineData("digest")]
    [InlineData("execution")]
    [InlineData("outcome")]
    [InlineData("conditions")]
    [InlineData("files")]
    [InlineData("filename")]
    [InlineData("duplicate")]
    [InlineData("goal")]
    public void ContradictoryVerifiedEvidenceIsRejected(string change)
    {
        var set = PublicReviewTestData.Set(1);
        var proof = PublicReviewTestData.Verified(set.Tasks[0], true);
        proof = change switch
        {
            "job" => proof with { JobId = set.SetId },
            "digest" => proof with { ReportSha256 = new('f', 64) },
            "execution" => proof with { ExecutionId = Guid.Empty },
            "outcome" => proof with { Assessment = proof.Assessment! with { Outcome = "Compliant" } },
            "conditions" => proof with { Assessment = proof.Assessment! with { Conditions = proof.Assessment.Conditions.RemoveAt(0) } },
            "files" => proof with { OriginalFiles = [] },
            "filename" => proof with { OriginalFiles = proof.OriginalFiles.SetItem(0, proof.OriginalFiles[0] with { Path = "../secret" }) },
            "duplicate" => proof with { OriginalFiles = proof.OriginalFiles.SetItem(0, proof.OriginalFiles[1]) },
            "goal" => proof with { Goal = proof.Goal! with { Kind = "Help" } },
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<BrowserOperationException>(() => PublicReviewTestData.Validate(PublicReviewTestData.Assessment(set, [proof])));
    }

    [Fact]
    public void ForeignScopeAndReorderedMembersCannotBorrowVerifiedEvidence()
    {
        var set = PublicReviewTestData.Set(2);
        var value = PublicReviewTestData.Assessment(set, [.. set.Tasks.Select(task => PublicReviewTestData.Verified(task, true))]);
        Assert.Throws<BrowserOperationException>(() => PublicReviewTestData.Validate(value with { Tasks = [value.Tasks[1], value.Tasks[0]] }));
        Assert.Throws<BrowserOperationException>(() => value.Validate(set.OrganizationId, set.SetId, set.CreatedBy, set.RequestId));
        Assert.Throws<BrowserOperationException>(() => value.Validate(set.OrganizationId, set.ParentScanId, set.SetId, set.RequestId));
        Assert.Throws<BrowserOperationException>(() => value.Validate(set.OrganizationId, set.ParentScanId, set.CreatedBy, set.SetId));
    }
}

internal static class PublicReviewTestData
{
    internal static PublicTaskSet Set(int count)
    {
        var organization = Guid.Parse("31000000-0000-4000-8000-000000000001");
        var parent = Guid.Parse("31000000-0000-4000-8000-000000000002");
        var actor = Guid.Parse("31000000-0000-4000-8000-000000000003");
        var source = PublicReaderSource.FromUrl("https://example.com/");
        var time = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
        return new(PublicTaskSet.CurrentSchema, organization, Guid.Parse("31000000-0000-4000-8000-000000000004"), parent,
            Guid.Parse("31000000-0000-4000-8000-000000000005"), actor, time, new('a', 64), 4,
            [.. PublicTaskSet.SupportedKinds.Take(count)], source,
            [.. Enumerable.Range(0, count).Select(index => new PublicTaskSetMember(PublicTaskSetMember.CurrentSchema,
                new(organization, parent, Guid.Parse($"32000000-0000-4000-8000-{index + 1:D12}"), Guid.Parse($"33000000-0000-4000-8000-{index + 1:D12}"),
                    1, source, new('a', 64)) { GoalKind = PublicTaskSet.SupportedKinds[index] }, "completed", "Finished", actor,
                time.AddSeconds(1), time.AddSeconds(30), time.AddSeconds(30), new((char)('b' + index), 64), null))]);
    }

    internal static PublicReviewAssessment Assessment(PublicTaskSet set, ImmutableArray<PublicReviewTaskEvidence> tasks) => new(PublicReviewAssessment.CurrentSchema, set, tasks);
    internal static void Validate(PublicReviewAssessment value) => value.Validate(value.TaskSet.OrganizationId, value.TaskSet.ParentScanId, value.TaskSet.CreatedBy, value.TaskSet.RequestId);
    internal static PublicReviewTaskEvidence Missing(PublicTaskSetMember task, string availability) => new(PublicReviewTaskEvidence.CurrentSchema, task.Job.JobId, availability, null, null, null, null, []);
    internal static PublicReviewTaskEvidence Verified(PublicTaskSetMember task, bool barrier)
    {
        var conditions = PublicReviewTaskEvidence.ConditionIds.Select(id => new PublicTaskCondition(id, barrier || id is not ("RecordedBarrier" or "ExactTrigger" or "RepeatedFailure")));
        return new(PublicReviewTaskEvidence.CurrentSchema, task.Job.JobId, "VerifiedOriginals", task.ReportSha256, Guid.Parse("34000000-0000-4000-8000-000000000001"),
            new(task.Job.GoalKind!, "Reach public information", "Information", "https://example.com/" + task.Job.GoalKind!.ToLowerInvariant()),
            new(barrier ? "WebsiteEvidenceReady" : "NoBlockingBarrierInTestedTask", [.. conditions]),
            [.. Enumerable.Range(1, 3).SelectMany(index => new PublicTaskFile[] { new($"attempt-{index}.webm", new('e', 64), 40, "video/webm"),
                new($"attempt-{index}-before.json", new('e', 64), 40, "application/json"), new($"attempt-{index}-after.json", new('e', 64), 40, "application/json") })]);
    }
}
