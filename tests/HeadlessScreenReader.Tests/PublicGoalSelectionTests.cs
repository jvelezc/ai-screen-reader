using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PublicGoalSelectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Pricing")]
    [InlineData("Catalog")]
    [InlineData("Contact")]
    [InlineData("Help")]
    public void SelectedGoalIsClosedAndSurvivesTheNativeWire(string? kind)
    {
        var job = PublicTaskTests.Start().Job with { GoalKind = kind };
        job.Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(job, ReaderWorkerWire.Json);
        Assert.Equal(job, JsonSerializer.Deserialize<PublicTaskJob>(bytes, ReaderWorkerWire.Json));
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(kind is not null, json.RootElement.TryGetProperty("GoalKind", out var selector));
        if (kind is not null) Assert.Equal(kind, selector.GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("pricing")]
    [InlineData("Contact ")]
    [InlineData("Search")]
    [InlineData("Checkout")]
    public void UnsupportedGoalCannotAcquireAJob(string kind)
    {
        var job = PublicTaskTests.Start().Job with { GoalKind = kind };
        Assert.Equal("PUBLIC_TASK_JOB_INVALID", Assert.Throws<BrowserOperationException>(job.Validate).Code);
    }

    [Fact]
    public void DefaultGoalPreservesTheExistingConstructorDeconstructionAndSerializedJob()
    {
        var original = PublicTaskTests.Start().Job;
        var (organization, parent, id, request, generation, source, digest) = original;
        var reconstructed = new PublicTaskJob(organization, parent, id, request, generation, source, digest);
        var legacy = new
        {
            OrganizationId = organization,
            ParentScanId = parent,
            JobId = id,
            RequestId = request,
            Generation = generation,
            Source = source,
            ParentOriginalSha256 = digest
        };
        Assert.Equal(original, reconstructed);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(legacy, ReaderWorkerWire.Json),
            JsonSerializer.SerializeToUtf8Bytes(reconstructed, ReaderWorkerWire.Json));
        var historical = PublicTaskTests.Report();
        historical.Validate(historical.Start);
    }

    [Fact]
    public void ReportCannotSubstituteAnotherGoalForTheAdmittedSelection()
    {
        var original = PublicTaskTests.Report();
        var selected = original.Start with { Job = original.Start.Job with { GoalKind = "Contact" } };
        var changed = original with { Start = selected };
        Assert.Equal("PUBLIC_TASK_REPORT_INVALID",
            Assert.Throws<BrowserOperationException>(() => changed.Validate(selected)).Code);
        var pricing = original.Start with { Job = original.Start.Job with { GoalKind = "Pricing" } };
        (original with { Start = pricing }).Validate(pricing);
    }

    [Fact]
    public async Task WrongSelectedGoalIsDeniedBeforeAuthorityWriteOrAcknowledgement()
    {
        var original = PublicTaskTests.Start();
        var selected = original with { Job = original.Job with { GoalKind = "Contact" } };
        var root = Path.GetFullPath(Path.Combine("artifacts", "public-goal-tests", Guid.NewGuid().ToString("N")));
        var calls = 0;
        var progress = new PublicTaskProgress(selected, root, (_, _) => { calls++; return Task.FromResult(true); });
        using var opened = new MemoryStream();
        await progress.HandleAsync(JsonSerializer.SerializeToUtf8Bytes(new PublicTaskIntent(selected, 1, 1, "Open", null), ReaderWorkerWire.Json), opened, default);
        Assert.True(opened.Length > 0);
        using var denied = new MemoryStream();
        var goal = new PublicTaskGoal("Pricing", "Reach pricing information", "View pricing", "https://example.com/pricing");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new PublicTaskIntent(selected, 2, 1, "Tab", goal), ReaderWorkerWire.Json);
        Assert.Equal("PUBLIC_TASK_CONTROL_UNCONFIRMED",
            (await Assert.ThrowsAsync<BrowserOperationException>(() => progress.HandleAsync(bytes, denied, default))).Code);
        Assert.Equal(0, denied.Length);
        Assert.Equal(2, calls);
        Assert.Single(Directory.GetFiles(root));
    }
}
