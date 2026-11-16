using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class IsolatedSearchTaskTests
{
    [Theory]
    [InlineData("image")]
    [InlineData("entrypoint")]
    [InlineData("legacy-command")]
    [InlineData("extra-argument")]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("job")]
    [InlineData("generation")]
    public void SearchContainerCannotSubstituteImageCommandOrNativeScope(string change)
    {
        var native = Native(); var config = Configuration(native);
        IsolatedFixtureExecutor.ValidateSearchContainer(JsonSerializer.SerializeToElement(config), native);
        if (change == "image") config["Image"] = "sha256:" + new string('b', 64);
        else if (change == "entrypoint") config["Config"]!["Entrypoint"] = new JsonArray("sh");
        else if (change == "legacy-command") config["Config"]!["Cmd"]![1] = "--worker-mock-tasks";
        else if (change == "extra-argument") config["Config"]!["Cmd"]!.AsArray().Add("https://example.invalid/");
        else config["Config"]!["Labels"]!["hsr.observation." + change] = change == "generation" ? "2" : Guid.NewGuid().ToString("N");
        Assert.Equal("SEARCH_TASK_CONTAINER_IDENTITY_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() =>
            IsolatedFixtureExecutor.ValidateSearchContainer(JsonSerializer.SerializeToElement(config), native)).Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExactSearchContainerAcceptsBothNativeEntrypointRepresentations(bool scalar)
    {
        var native = Native(); var config = Configuration(native);
        if (scalar) { config["Config"]!["Entrypoint"] = "dotnet"; config["Image"] = native.Image[7..]; }
        Assert.Null(Record.Exception(() => IsolatedFixtureExecutor.ValidateSearchContainer(JsonSerializer.SerializeToElement(config), native)));
    }

    [Fact]
    public async Task InvalidOrDeniedSearchCannotAllocateNativeOwnershipOrAReplayFence()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "isolated-search-rejected", Guid.NewGuid().ToString("N")));
        var journal = new OwnershipJournal(root);
        var executor = new IsolatedFixtureExecutor("podman", "sha256:" + new string('a', 64), journal: journal);
        var job = new SyntheticSearchTaskJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            SyntheticSearchTaskJob.CurrentProfile, SyntheticSearchTaskSource.FromId("search-working"));
        Assert.Equal("SEARCH_TASK_AUTHORITY_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
            executor.ExecuteSearchTaskAsync(job, Guid.NewGuid(), _ => Task.FromResult(false)))).Code);
        var calls = 0;
        Assert.Equal("SEARCH_TASK_JOB_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
            executor.ExecuteSearchTaskAsync(job with { Generation = 2 }, Guid.NewGuid(), _ => { calls++; return Task.FromResult(true); }))).Code);
        Assert.Equal("SEARCH_TASK_WORKER_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() =>
            executor.ExecuteSearchTaskAsync(job, Guid.Empty, _ => { calls++; return Task.FromResult(true); }))).Code);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteSearchTaskAsync(job, Guid.NewGuid(),
            _ => { calls++; return Task.FromResult(true); }, stop.Token));
        Assert.Equal(0, calls);
        Assert.Empty(journal.ReadPending());
        Assert.Empty(Directory.GetFiles(journal.RemovalsDirectory));
        Assert.False(Directory.Exists(Path.Combine(root, "search-task-control")));
    }

    private static NativeOwnership Native() => new(NativeOwnership.CurrentSchema, Guid.NewGuid(),
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1), "podman", "sha256:" + new string('a', 64));
    private static JsonObject Configuration(NativeOwnership native) => JsonSerializer.SerializeToNode(new
    {
        Image = native.Image,
        Config = new
        {
            Entrypoint = new[] { "dotnet" },
            Cmd = new[] { "/app/cli/HeadlessScreenReader.Cli.dll", "--worker-search-tasks" },
            Labels = new Dictionary<string, string>
            {
                ["hsr.observation.owner"] = native.Owner.ToString("N"),
                ["hsr.observation.organization"] = native.Job.OrganizationId.ToString("N"),
                ["hsr.observation.scan"] = native.Job.ScanId.ToString("N"),
                ["hsr.observation.job"] = native.Job.JobId.ToString("N"),
                ["hsr.observation.generation"] = "1"
            }
        }
    })!.AsObject();
}
