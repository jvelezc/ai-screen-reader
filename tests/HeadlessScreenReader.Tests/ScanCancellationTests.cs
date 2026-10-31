using System.Net;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class ScanCancellationTests
{
    private static readonly DateTimeOffset Requested = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
    private static PortalIdentity Actor => new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task StopRequestPreservesActualAttemptWithoutClaimingCleanupOrZeroLeasedInput(int generation)
    {
        var project = new Project(generation);
        var value = await new PortalScanCancellation(project).RequestAsync(Actor, project.Organization, project.Scan, default);
        Assert.Equal("Requested", value.Status); Assert.Equal(project.JobId, value.JobId); Assert.Equal(generation, value.Generation);
        Assert.Equal(generation == 0 ? "NoAttemptStarted" : "NotAssessed", value.InputAssessment);
        Assert.Equal(generation == 0 ? null : project.Worker, value.Producer);
        Assert.Equal(generation == 0 ? 0 : 1, project.ProducerReads); Assert.Equal(3, project.ScanReads);
        Assert.Equal(1, project.CancellationCalls); Assert.Equal(Requested, value.RequestedAt);
        Assert.Equal("blocked", project.Record.State); Assert.Equal("WORKER_CANCELLED_BY_CALLER", project.Job.ErrorCode);
        var wire = JsonSerializer.SerializeToElement(value, PortalJson.Options);
        Assert.Equal("NotAssessed", wire.GetProperty("nativeCleanup").GetString());
        Assert.Equal("NotAssessed", wire.GetProperty("goalVerification").GetString());
        Assert.False(wire.GetProperty("canReplay").GetBoolean()); Assert.False(wire.GetProperty("isCompletionReceipt").GetBoolean());
        Assert.DoesNotContain(PortalProjectClientTests.Bearer, wire.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClaimBetweenInitialReadAndCancellationIsReportedAsUnassessedInput()
    {
        var project = new Project(0) { ClaimDuringRequest = true };
        var value = await new PortalScanCancellation(project).RequestAsync(Actor, project.Organization, project.Scan, default);
        Assert.Equal(1, value.Generation); Assert.Equal(project.Worker, value.Producer);
        Assert.Equal("NotAssessed", value.InputAssessment); Assert.Equal(1, project.ProducerReads);
    }

    [Fact]
    public async Task CompletedReceiptIsAnUnchangedNoopAndNotANewCompletionOrGoalClaim()
    {
        var project = new Project(1) { AlreadyCompleted = true };
        project.Complete(); var before = project.Job;
        var value = await new PortalScanCancellation(project).RequestAsync(Actor, project.Organization, project.Scan, default);
        Assert.Equal("AlreadyCompleted", value.Status); Assert.Null(value.RequestedAt); Assert.Equal(before, project.Job);
        Assert.Equal("NotAssessed", value.InputAssessment); Assert.False(value.IsCompletionReceipt); Assert.False(value.CanReplay);
    }

    [Theory]
    [InlineData("after-request")]
    [InlineData("after-producer")]
    public async Task CurrentCallerLossAfterTheMutationDoesNotReleaseItsScopeMetadata(string boundary)
    {
        var project = new Project(1) { RevokeAfterRequest = boundary == "after-request", RevokeAfterProducer = boundary == "after-producer" };
        var failure = await Assert.ThrowsAsync<PortalFault>(() => new PortalScanCancellation(project)
            .RequestAsync(Actor, project.Organization, project.Scan, default));
        Assert.Equal(404, failure.Status); Assert.Equal("CONTROL_SCAN_NOT_FOUND", failure.Code);
        Assert.Equal(1, project.CancellationCalls); Assert.Equal("blocked", project.Job.State);
    }

    [Fact]
    public async Task DifferentActualProducerCannotAuthorizeAnOtherwiseValidCancellationReply()
    {
        var project = new Project(1) { DifferentProducer = true };
        var failure = await Assert.ThrowsAsync<PortalFault>(() => new PortalScanCancellation(project)
            .RequestAsync(Actor, project.Organization, project.Scan, default));
        Assert.Equal(503, failure.Status); Assert.Equal("CONTROL_CANCELLATION_PRODUCER_UNCONFIRMED", failure.Code);
        Assert.Equal(1, project.CancellationCalls);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("job")]
    [InlineData("generation")]
    [InlineData("producer")]
    [InlineData("zero-input")]
    [InlineData("missing-time")]
    [InlineData("status")]
    public async Task ForgedProviderReplyCannotChangeScopeAttemptOrCancellationMeaning(string part)
    {
        var project = new Project(1)
        {
            ChangeReply = value => part switch
            {
                "organization" => value with { OrganizationId = Guid.NewGuid() },
                "scan" => value with { ScanId = Guid.NewGuid() },
                "job" => value with { JobId = Guid.NewGuid() },
                "generation" => value with { Generation = 4 },
                "producer" => value with { Producer = Guid.Empty },
                "zero-input" => value with { InputAssessment = "NoAttemptStarted" },
                "missing-time" => value with { RequestedAt = null },
                "status" => value with { Status = "Stopped" },
                _ => throw new InvalidOperationException()
            }
        };
        var failure = await Assert.ThrowsAsync<PortalFault>(() => new PortalScanCancellation(project)
            .RequestAsync(Actor, project.Organization, project.Scan, default));
        Assert.Equal(503, failure.Status); Assert.Equal("CONTROL_CANCELLATION_RESPONSE_UNCONFIRMED", failure.Code);
        Assert.Equal(1, project.CancellationCalls);
    }

    [Fact]
    public async Task CancelledCallerTokenCannotTurnACommittedMutationIntoASuccessResponse()
    {
        using var stop = new CancellationTokenSource();
        var project = new Project(1) { OnRequest = stop.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PortalScanCancellation(project)
            .RequestAsync(Actor, project.Organization, project.Scan, stop.Token));
        Assert.Equal(1, project.CancellationCalls); Assert.Equal("blocked", project.Job.State);
    }

    [Theory]
    [InlineData(false, "{}", 401)]
    [InlineData(true, "{\"producer\":\"not-caller-authority\"}", 400)]
    [InlineData(true, "[]", 400)]
    public async Task HttpAuthenticationAndClosedBodyRejectBeforeAnyCancellation(bool authenticated, string body, int status)
    {
        var project = new Project(0); await using var server = await Start(project);
        using var http = authenticated ? PortalHttpTests.Authorized(server) : new HttpClient { BaseAddress = server.BaseAddress };
        using var response = await http.PostAsync(Path(project), new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(status, (int)response.StatusCode); Assert.Equal(0, project.CancellationCalls);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Accepted)]
    [InlineData(true, HttpStatusCode.OK)]
    public async Task ActualHttpReturnsRequestedOrAlreadyCompletedWithoutReplayingInput(bool completed, HttpStatusCode status)
    {
        var project = new Project(completed ? 1 : 0) { AlreadyCompleted = completed };
        if (completed) project.Complete();
        await using var server = await Start(project); using var http = PortalHttpTests.Authorized(server);
        using var response = await http.PostAsync(Path(project), new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(status, response.StatusCode); Assert.Equal(1, project.CancellationCalls);
        using var value = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(project.JobId, value.RootElement.GetProperty("jobId").GetGuid());
        Assert.Equal(completed ? "AlreadyCompleted" : "Requested", value.RootElement.GetProperty("status").GetString());
        Assert.Equal("NotAssessed", value.RootElement.GetProperty("nativeCleanup").GetString());
        Assert.False(value.RootElement.GetProperty("isCompletionReceipt").GetBoolean());
    }

    private static string Path(Project project) => $"/api/workspaces/{project.Organization:D}/scans/{project.Scan:D}/cancellation";

    [Fact]
    public void PublishedCancellationContractDistinguishesAcceptedRequestAndCompletedNoop()
    {
        using var document = JsonDocument.Parse(PortalOpenApi.Document); var spec = document.RootElement;
        var operation = spec.GetProperty("paths").GetProperty("/api/workspaces/{organization}/scans/{scan}/cancellation").GetProperty("post");
        Assert.Equal("RequestScanCancellation", operation.GetProperty("operationId").GetString());
        foreach (var status in new[] { "200", "202" })
            Assert.Equal("#/components/schemas/ScanCancellation", operation.GetProperty("responses").GetProperty(status)
                .GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/EmptyRequest", operation.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        var schema = spec.GetProperty("components").GetProperty("schemas").GetProperty("ScanCancellation");
        var properties = schema.GetProperty("properties");
        Assert.Equal(13, properties.EnumerateObject().Count()); Assert.Equal(13, schema.GetProperty("required").GetArrayLength());
        Assert.Equal("NotAssessed", properties.GetProperty("nativeCleanup").GetProperty("const").GetString());
        Assert.False(properties.GetProperty("isCompletionReceipt").GetProperty("const").GetBoolean());
        Assert.False(properties.GetProperty("canReplay").GetProperty("const").GetBoolean());
        Assert.Equal(0, properties.GetProperty("generation").GetProperty("minimum").GetInt32());
        Assert.Equal(3, properties.GetProperty("generation").GetProperty("maximum").GetInt32());
        Assert.Equal(2, schema.GetProperty("allOf").GetArrayLength());
    }

    private static Task<HostedControlServer> Start(Project project) => HostedControlServer.StartAsync(
        new(PortalProjectClientTests.Key, System.IO.Path.GetFullPath("artifacts/cancellation-validation/evidence"), new string('a', 40), 0),
        project, new PortalObservationStub());

    // Provider controls for deterministic boundaries, not actual provider-revocation or browser evidence.
    private sealed class Project : IPortalProjectClient
    {
        public Guid Organization { get; } = PortalProjectClientTests.Organization;
        public Guid Scan { get; } = PortalProjectClientTests.Scan;
        public Guid JobId { get; } = PortalProjectClientTests.Job;
        public Guid Worker { get; } = Guid.NewGuid();
        public PortalScanResponse Record { get; private set; }
        public PortalJobResult Job { get; private set; }
        public bool AlreadyCompleted { get; init; }
        public bool ClaimDuringRequest { get; init; }
        public bool RevokeAfterRequest { get; init; }
        public bool RevokeAfterProducer { get; init; }
        public bool DifferentProducer { get; init; }
        public Action? OnRequest { get; init; }
        public Func<PortalScanCancellationResponse, PortalScanCancellationResponse>? ChangeReply { get; init; }
        public int CancellationCalls { get; private set; }
        public int ScanReads { get; private set; }
        public int ProducerReads { get; private set; }
        private bool _revoked;

        public Project(int generation)
        {
            Record = new(Organization, Scan, "synthetic-shop", "hsr-desktop-reader-v13", generation == 0 ? "queued" : "leased", Requested);
            Job = new(Organization, Scan, JobId, generation == 0 ? "queued" : "leased", generation, null, null, null)
            { RecordObservation = true, TraversalRoute = 1 };
        }

        public void Complete()
        {
            Record = Record with { State = "observed" };
            Job = Job with { State = "completed", Digest = new string('a', 64), Reference = new FixtureJob(Organization, Scan, JobId, 1).Reference };
        }

        public Task<PortalScanCancellationResponse> RequestScanCancellationAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); CancellationCalls++;
            if (ClaimDuringRequest) Job = Job with { Generation = 1 };
            if (!AlreadyCompleted) { Record = Record with { State = "blocked" }; Job = Job with { State = "blocked", ErrorCode = "WORKER_CANCELLED_BY_CALLER" }; }
            var value = new PortalScanCancellationResponse(Organization, Scan, JobId, Job.Generation,
                Job.Generation == 0 ? null : Worker, AlreadyCompleted ? "AlreadyCompleted" : "Requested",
                Job.Generation == 0 ? "NoAttemptStarted" : "NotAssessed", AlreadyCompleted ? null : Requested);
            if (RevokeAfterRequest) _revoked = true;
            OnRequest?.Invoke(); return Task.FromResult(ChangeReply?.Invoke(value) ?? value);
        }

        public Task<PortalScanResponse?> ScanAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
        { token.ThrowIfCancellationRequested(); ScanReads++; return Task.FromResult<PortalScanResponse?>(_revoked ? null : Record); }
        public Task<PortalJobResult?> JobAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token) => Task.FromResult<PortalJobResult?>(Job);
        public Task<Guid?> AttemptWorkerAsync(PortalIdentity actor, FixtureJob job, CancellationToken token)
        { ProducerReads++; if (RevokeAfterProducer) _revoked = true; return Task.FromResult<Guid?>(DifferentProducer ? Guid.NewGuid() : Worker); }
        public Task<PortalIdentity> AuthenticateAsync(string bearer, CancellationToken token) =>
            bearer == PortalProjectClientTests.Bearer ? Task.FromResult(Actor) : throw new PortalFault(401, "CONTROL_AUTHENTICATION_REQUIRED");
        public Task<PortalWorkspaceBootstrap> EnsureWorkspaceAsync(PortalIdentity actor, CancellationToken token) => throw new InvalidOperationException("No workspace creation during cancellation");
        public Task<PortalWorkspaceResponse?> WorkspaceAsync(PortalIdentity actor, Guid organization, CancellationToken token) => throw new InvalidOperationException();
        public Task<PortalScanResponse[]> ScansAsync(PortalIdentity actor, Guid organization, CancellationToken token) => throw new InvalidOperationException();
        public Task<PortalScanCreatedResponse> CreateScanAsync(PortalIdentity actor, Guid organization, Guid request, CancellationToken token) => throw new InvalidOperationException("Cancellation cannot admit another run");
    }
}
