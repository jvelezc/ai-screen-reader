using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class SyntheticSearchTaskStatusTests
{
    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-11-16T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static SyntheticSearchTaskStatus Queued() => new(SyntheticSearchTaskStatus.CurrentSchema,
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, SyntheticSearchTaskJob.CurrentProfile, SyntheticSearchTaskSource.FromId("search-working")),
        Guid.NewGuid(), Guid.NewGuid(), Created, Created, "queued", "Queued", null, null, null, null);
    private static SyntheticSearchTaskPublication Publication() => new(SyntheticSearchTaskPublication.CurrentSchema, Guid.NewGuid(),
        new('a', 64), 30000, new('b', 64), 1000000, 100, 900000, "AllTestedKeyboardGoalsObserved", true, true, new('c', 64));

    [Fact]
    public void SearchStatusKeepsIndependentIdentityAndDoesNotClaimWebsiteOrLegalAssessment()
    {
        var queued = Queued(); queued.Validate(queued.Job.OrganizationId, queued.Job.JobId);
        var completed = queued with
        {
            State = "completed",
            Phase = "Finished",
            UpdatedAt = Created.AddSeconds(30),
            FinishedAt = Created.AddSeconds(30),
            ProducerId = Guid.NewGuid(),
            Publication = Publication()
        };
        completed.Validate(queued.Job.OrganizationId, queued.Job.JobId);
        var json = JsonSerializer.SerializeToElement(completed, ReaderWorkerWire.Json);
        Assert.Equal("ControlledEmbeddedSearchTaskExperiments", json.GetProperty("Publication").GetProperty("Scope").GetString());
        Assert.Equal("NotAssessed", json.GetProperty("Publication").GetProperty("WebsiteEvidenceAssessment").GetString());
        Assert.False(json.GetProperty("Publication").GetProperty("LegalCaseDetermined").GetBoolean());
        Assert.False(json.TryGetProperty("SessionId", out _));
        Assert.False(json.TryGetProperty("Token", out _));
        Assert.Equal(completed, JsonSerializer.Deserialize<SyntheticSearchTaskStatus>(json, ReaderWorkerWire.Json));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("request")]
    [InlineData("creator")]
    [InlineData("unknown-state")]
    [InlineData("queued-producer")]
    [InlineData("leased-without-owner")]
    [InlineData("completed-without-originals")]
    [InlineData("finished-before-created")]
    [InlineData("interrupted-with-completion-code")]
    [InlineData("queued-with-terminal-phase")]
    public void SearchStatusRejectsInconsistentExecutionFacts(string mutation)
    {
        var original = Queued();
        var changed = mutation switch
        {
            "schema" => original with { Schema = "hsr-public-task-status-v1" },
            "request" => original with { RequestId = Guid.Empty },
            "creator" => original with { CreatedBy = Guid.Empty },
            "unknown-state" => original with { State = "WebsiteCompliant" },
            "queued-producer" => original with { ProducerId = Guid.NewGuid() },
            "leased-without-owner" => original with { State = "leased", Phase = "Collecting" },
            "completed-without-originals" => original with { State = "completed", Phase = "Finished", ProducerId = Guid.NewGuid(), FinishedAt = Created.AddMinutes(1) },
            "finished-before-created" => original with { State = "cancelled", ErrorCode = "SEARCH_TASK_CANCELLED", FinishedAt = Created.AddSeconds(-1) },
            "interrupted-with-completion-code" => original with { State = "interrupted", ErrorCode = "SEARCH_TASK_COMPLETED", FinishedAt = Created.AddMinutes(1) },
            _ => original with { Phase = "Finished" }
        };
        Assert.Equal("SEARCH_TASK_STATUS_INVALID", Assert.Throws<BrowserOperationException>(() => changed.Validate(original.Job.OrganizationId)).Code);
    }

    [Fact]
    public void SearchStatusCannotCrossWorkspaceOrJobScope()
    {
        var original = Queued();
        Assert.Equal("SEARCH_TASK_STATUS_INVALID", Assert.Throws<BrowserOperationException>(() => original.Validate(Guid.NewGuid())).Code);
        Assert.Equal("SEARCH_TASK_STATUS_INVALID", Assert.Throws<BrowserOperationException>(() => original.Validate(original.Job.OrganizationId, Guid.NewGuid())).Code);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("execution")]
    [InlineData("manifest-hash")]
    [InlineData("manifest-size")]
    [InlineData("archive-size")]
    [InlineData("archive-smaller-than-originals")]
    [InlineData("files")]
    [InlineData("original-size")]
    [InlineData("native-removal")]
    [InlineData("original-readback")]
    [InlineData("observation")]
    public void SearchPublicationCannotInventOrOmitOriginalCorrespondence(string mutation)
    {
        var original = Publication(); original.Validate();
        var changed = mutation switch
        {
            "schema" => original with { Schema = "hsr-public-task-publication-v1" },
            "execution" => original with { ExecutionId = Guid.Empty },
            "manifest-hash" => original with { ManifestSha256 = new('A', 64) },
            "manifest-size" => original with { ManifestBytes = 524289 },
            "archive-size" => original with { ArchiveBytes = 27262977 },
            "archive-smaller-than-originals" => original with { ArchiveBytes = 899999 },
            "files" => original with { Files = 1 },
            "original-size" => original with { Bytes = 26214401 },
            "native-removal" => original with { NativeRemoved = false },
            "original-readback" => original with { OriginalsVerified = false },
            _ => original with { Observation = "WebsiteCompliant" }
        };
        Assert.Equal("SEARCH_TASK_PUBLICATION_INVALID", Assert.Throws<BrowserOperationException>(changed.Validate).Code);
    }
}
