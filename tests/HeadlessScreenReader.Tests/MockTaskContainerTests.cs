using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed partial class MockTaskWorkerTests
{
    private static (NativeOwnership Intent, JsonObject Inspection) Inspection(bool stringEntry)
    {
        var job = Start().Job;
        var intent = new NativeOwnership(NativeOwnership.CurrentSchema, Guid.NewGuid(),
            new(job.OrganizationId, job.ScanId, job.JobId, job.Generation), "podman", "sha256:" + new string('a', 64));
        var config = new JsonObject
        {
            ["Entrypoint"] = stringEntry ? JsonValue.Create("dotnet") : new JsonArray("dotnet"),
            ["Cmd"] = new JsonArray("/app/cli/HeadlessScreenReader.Cli.dll", "--worker-mock-tasks"),
            ["Labels"] = new JsonObject
            {
                ["hsr.observation.organization"] = job.OrganizationId.ToString("N"),
                ["hsr.observation.scan"] = job.ScanId.ToString("N"),
                ["hsr.observation.job"] = job.JobId.ToString("N"),
                ["hsr.observation.generation"] = "1"
            }
        };
        return (intent, new() { ["Image"] = intent.Image, ["Config"] = config });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MockContainerAcceptsOnlyTheExactImmutableImageCommandAndScope(bool podmanStringEntry)
    {
        var (intent, item) = Inspection(podmanStringEntry);
        IsolatedFixtureExecutor.ValidateMockContainer(JsonSerializer.SerializeToElement(item), intent);
        Assert.Equal(1, intent.Job.Generation); Assert.False(intent.Job.RecordObservation); Assert.Equal(0, intent.Job.TraversalRoute);
    }

    [Theory]
    [InlineData("image")]
    [InlineData("command")]
    [InlineData("entrypoint")]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("job")]
    [InlineData("generation")]
    public void MockContainerCannotAcquireAnotherProfileOrNativeIdentity(string mutation)
    {
        var (intent, item) = Inspection(false);
        if (mutation == "image") item["Image"] = "sha256:" + new string('b', 64);
        else if (mutation == "command") item["Config"]!["Cmd"] = new JsonArray("/app/cli/HeadlessScreenReader.Cli.dll", "--worker-fixture");
        else if (mutation == "entrypoint") item["Config"]!["Entrypoint"] = new JsonArray("sh", "-c");
        else item["Config"]!["Labels"]!["hsr.observation." + mutation] = mutation == "generation" ? "2" : Guid.NewGuid().ToString("N");
        Assert.Equal("MOCK_TASK_CONTAINER_IDENTITY_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() =>
            IsolatedFixtureExecutor.ValidateMockContainer(JsonSerializer.SerializeToElement(item), intent)).Code);
    }
}
