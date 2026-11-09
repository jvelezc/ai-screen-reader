using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PublicTaskHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingTaskIsAnExplicitJsonNullForLatestAndExactStatus(bool exact)
    {
        using var rig = await Rig.Create(); rig.Missing = true;
        await using var server = await rig.Server(); using var http = rig.Http(server);
        using var response = await http.GetAsync(exact ? rig.Path : rig.Collection);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("null", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task SameReviewResultEventsGraphAndOriginalPagesAreAvailableWithoutRepeatingInput()
    {
        using var rig = await Rig.Create(); await using var server = await rig.Server(); using var http = rig.Http(server);
        using var response = await http.GetAsync(rig.Path + "/result"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<PortalTaskResult>(PortalJson.Options))!;
        Assert.Equal("WebsiteEvidenceReady", result.Assessment.Outcome); Assert.Equal(rig.Status.Job, result.Status.Job);
        Assert.False(result.LegalCaseDetermined); Assert.Equal("NotPerformed", result.AssistiveTechnologyComparison);
        using var events = await http.GetAsync(rig.Path + $"/events/1/0/{rig.Status.ReportSha256}");
        Assert.Equal("Enter", (await events.Content.ReadFromJsonAsync<PortalTaskEventPage>(PortalJson.Options))!.Events.Single().Action);
        using var graph = await http.GetAsync(rig.Path + $"/graph/1/0/{rig.Status.ReportSha256}");
        Assert.Single((await graph.Content.ReadFromJsonAsync<PortalTaskGraphPage>(PortalJson.Options))!.Edges);
        using var original = await http.GetAsync(rig.Path + $"/originals/attempt-1.webm/0/{rig.Status.ReportSha256}");
        var page = (await original.Content.ReadFromJsonAsync<PortalTaskOriginalPage>(PortalJson.Options))!;
        Assert.Equal(rig.Files["attempt-1.webm"], page.Data); Assert.Equal(PublicTaskRules.Hash(page.Data), page.PageSha256);
        using var eof = await http.GetAsync(rig.Path + $"/originals/attempt-1.webm/{page.File.Bytes}/{rig.Status.ReportSha256}");
        Assert.Empty((await eof.Content.ReadFromJsonAsync<PortalTaskOriginalPage>(PortalJson.Options))!.Data);
        Assert.Equal(0, rig.Creates); Assert.True(response.Headers.CacheControl!.NoStore);
    }
    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("foreign-review", 404)]
    [InlineData("query", 400)]
    [InlineData("digest", 409)]
    [InlineData("path", 400)]
    public async Task HttpRejectsUnboundScopeAndOriginalSelectors(string scenario, int expected)
    {
        using var rig = await Rig.Create(); await using var server = await rig.Server(); using var http = rig.Http(server, scenario != "anonymous");
        var path = rig.Path + $"/originals/attempt-1.webm/0/{rig.Status.ReportSha256}";
        if (scenario == "foreign-review") path = path.Replace(rig.Status.Job.ParentScanId.ToString("D"), Guid.NewGuid().ToString("D"), StringComparison.Ordinal);
        if (scenario == "query") path += "?actor=forged";
        if (scenario == "digest") path = path[..^64] + new string('d', 64);
        if (scenario == "path") path = path.Replace("attempt-1.webm", "credentials.env", StringComparison.Ordinal);
        using var response = await http.GetAsync(path); Assert.Equal(expected, (int)response.StatusCode);
        Assert.DoesNotContain("Actual task page", await response.Content.ReadAsStringAsync()); Assert.Equal(0, rig.Creates);
    }
    [Fact]
    public async Task LostAdmissionCanReconcileOriginalRequestWhileRuntimeIsUnavailable()
    {
        using var rig = await Rig.Create(); rig.Ready = false; await using var server = await rig.Server(); using var http = rig.Http(server);
        using var response = await http.PostAsJsonAsync(rig.Collection, new { requestId = rig.Status.Job.RequestId, parentOriginalSha256 = rig.Status.Job.ParentOriginalSha256 });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); Assert.Equal(0, rig.Creates);
        var status = await response.Content.ReadFromJsonAsync<PublicTaskStatus>(PortalJson.Options);
        Assert.Equal(rig.Status.Job.JobId, status!.Job.JobId); Assert.Equal(rig.Status.Job.RequestId, status.Job.RequestId);
    }
    [Fact]
    public async Task RevocationDuringOriginalVerificationDropsTheResponseIncludingAtEof()
    {
        using var rig = await Rig.Create();
        var service = new PortalPublicTasks(rig.Project, rig.Store, () => Task.FromResult(true)); rig.DenyAfter = 1;
        var fault = await Assert.ThrowsAsync<PortalFault>(() => service.OriginalAsync(rig.Actor, rig.Status.Job.OrganizationId,
            rig.Status.Job.ParentScanId, rig.Status.Job.JobId, "attempt-1.webm", 4, rig.Status.ReportSha256!, default));
        Assert.Equal(403, fault.Status); Assert.Equal(2, rig.Reads);
    }
    [Fact]
    public async Task CorruptOriginalCannotBecomeAResultOrRecording()
    {
        using var rig = await Rig.Create();
        var file = rig.Status.Job; var bytes = rig.Files["attempt-1.webm"];
        var path = System.IO.Path.Combine(rig.Root, "public-task-originals", file.OrganizationId.ToString("N"), file.JobId.ToString("N"), PublicTaskRules.Hash(bytes));
        await File.WriteAllBytesAsync(path, [0, 0, 0, 0]);
        var service = new PortalPublicTasks(rig.Project, rig.Store, () => Task.FromResult(true));
        var fault = await Assert.ThrowsAsync<PortalFault>(() => service.ResultAsync(rig.Actor, file.OrganizationId, file.ParentScanId, file.JobId, default));
        Assert.Equal("CONTROL_PUBLIC_TASK_ORIGINAL_UNAVAILABLE", fault.Code);
    }

    internal sealed class Rig : HttpMessageHandler
    {
        internal readonly string Root = System.IO.Path.GetFullPath(System.IO.Path.Combine("artifacts", "public-task-http", Guid.NewGuid().ToString("N")));
        internal readonly PortalIdentity Actor = new(PortalProjectClientTests.Actor, "test@example.invalid", PortalProjectClientTests.Bearer);
        internal readonly SupabasePortalClient Project;
        internal readonly PublicTaskEvidenceStore Store;
        internal PublicTaskStatus Status = null!;
        internal ImmutableDictionary<string, byte[]> Files = ImmutableDictionary<string, byte[]>.Empty;
        internal int Creates, Reads, DenyAfter = int.MaxValue; internal bool Ready = true, Missing;
        private readonly HttpClient _provider;
        private Rig() { _provider = new(this, false); Project = new(_provider, PortalProjectClientTests.Key); Store = new(Root, null); }
        internal string Collection => $"/api/workspaces/{Status.Job.OrganizationId:D}/website-scans/{Status.Job.ParentScanId:D}/tasks";
        internal string Path => Collection + $"/{Status.Job.JobId:D}";
        internal static async Task<Rig> Create()
        {
            var rig = new Rig(); var report = PublicTaskTests.Report();
            var files = ImmutableDictionary.CreateBuilder<string, byte[]>(); var inventory = ImmutableArray.CreateBuilder<PublicTaskFile>();
            foreach (var attempt in report.Attempts)
                foreach (var name in new[] { attempt.BeforeSnapshot, attempt.AfterSnapshot, attempt.Video })
                {
                    var video = name.EndsWith(".webm", StringComparison.Ordinal);
                    var snapshot = new AccessibilitySnapshot($"attempt-{attempt.Number}", [new("heading", "heading", "Actual task page")], [])
                    { CaptureEpoch = 1, DocumentUrl = name == attempt.AfterSnapshot ? attempt.FinalUrl : report.Start.Job.Source.TargetUrl };
                    byte[] bytes = video ? [0x1a, 0x45, 0xdf, 0xa3] : JsonSerializer.SerializeToUtf8Bytes(snapshot, ReaderWorkerWire.Json);
                    files.Add(name, bytes); inventory.Add(new(name, PublicTaskRules.Hash(bytes), bytes.Length, video ? "video/webm" : "application/json"));
                }
            report = report with { Files = inventory.ToImmutable() }; files.Add("report.json", JsonSerializer.SerializeToUtf8Bytes(report, ReaderWorkerWire.Json));
            rig.Files = new PublicTaskBundle(report, files.ToImmutable()).Files;
            foreach (var pair in rig.Files) await rig.Store.WriteAsync(report.Start.Job, PublicTaskRules.Hash(pair.Value), pair.Value, default);
            var publication = new PublicTaskPublication("hsr-public-task-publication-v1", report.Start.Job.ParentOriginalSha256,
                PublicTaskRules.Hash(rig.Files["report.json"]), rig.Files["report.json"].Length, report.Start.ExecutionId, true, true, PublicTaskAssessment.Evaluate(report, true, true));
            rig.Status = new("hsr-public-task-status-v1", report.Start.Job, "completed", "Finished", rig.Actor.UserId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow, report.Start.WorkerId, publication.ReportSha256, publication, null);
            return rig;
        }
        internal Task<HostedControlServer> Server() => HostedControlServer.StartAsync(new(PortalProjectClientTests.Key, Root, new string('a', 40), 0), Project,
            new PortalObservationStub(), publicWebsiteRuntimeReady: () => Task.FromResult(Ready), publicTaskEvidence: Store);
        internal HttpClient Http(HostedControlServer server, bool authenticated = true)
        { var http = new HttpClient { BaseAddress = server.BaseAddress }; if (authenticated) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Actor.Token); return http; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            object? body;
            if (request.RequestUri!.AbsolutePath == "/auth/v1/user") body = JsonSerializer.Deserialize<object>(PortalProjectClientTests.UserJson);
            else
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                if (request.RequestUri.AbsolutePath.EndsWith("read_public_task", StringComparison.Ordinal))
                {
                    if (++Reads > DenyAfter) return new(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { code = "42501" }) };
                    body = Missing || json.RootElement.GetProperty("p_organization_id").GetGuid() != Status.Job.OrganizationId
                        || json.RootElement.GetProperty("p_parent_scan_id").GetGuid() != Status.Job.ParentScanId ? null : Status;
                }
                else { Creates++; body = Status; }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body, ReaderWorkerWire.Json), Encoding.UTF8, "application/json") };
        }
        protected override void Dispose(bool disposing) { if (disposing) { Store.Dispose(); _provider.Dispose(); } base.Dispose(disposing); }
    }
}
