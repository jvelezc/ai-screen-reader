using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.Worker;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HeadlessScreenReader.Tests;

public sealed class PublicReviewAssessmentApiTests
{
    [Theory]
    [InlineData(true, "WebsiteEvidenceReady")]
    [InlineData(false, "NoBlockingBarrierInTestedTasks")]
    public async Task OnlyIndependentlyVerifiedOriginalsDetermineTheCurrentReview(bool barrier, string expected)
    {
        using var rig = await Rig.Create(2, barrier);
        var value = await rig.Read();
        Assert.Equal(expected, value.Outcome);
        Assert.Equal(2, value.VerifiedTaskCount);
        Assert.Equal(rig.Set.Tasks.Select(task => task.Job.JobId), value.Tasks.Select(task => task.JobId));
        Assert.All(value.Tasks, task => Assert.Equal(9, task.OriginalFiles.Length));
        Assert.Equal(2, rig.SetReads);
        Assert.Equal(8, rig.TaskReads);
        Assert.Equal(0, rig.Creates);
        Assert.Equal(0, rig.ReadinessReads);
        Assert.False(value.WebsiteInputStartedByAssessment);
        Assert.False(value.LegalCaseDetermined);
    }

    [Fact]
    public async Task EmptyAndPendingSetsRemainInsufficientWithoutReadingOriginalsOrStartingTasks()
    {
        using var rig = await Rig.Create(1);
        var member = rig.Set.Tasks[0];
        rig.Set = rig.Set with { Tasks = [member with { State = "queued", Phase = "Queued", FinishedAt = null, ReportSha256 = null }] };
        var pending = await rig.Read();
        Assert.Equal("InsufficientEvidence", pending.Outcome);
        Assert.Equal("Pending", Assert.Single(pending.Tasks).Availability);
        Assert.Equal(0, rig.TaskReads);
        rig.Set = rig.Set with { GoalKinds = [], Tasks = [] };
        var empty = await rig.Read();
        Assert.Empty(empty.Tasks);
        Assert.Equal("InsufficientEvidence", empty.Outcome);
        Assert.Equal(0, rig.Creates);
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("cancelled")]
    [InlineData("interrupted")]
    public async Task StoppedTaskIsAMissingRecordRatherThanABarrier(string state)
    {
        using var rig = await Rig.Create(1);
        rig.Set = rig.Set with { Tasks = [rig.Set.Tasks[0] with { State = state, ReportSha256 = null, ErrorCode = "PUBLIC_TASK_UNCONFIRMED" }] };
        var value = await rig.Read();
        Assert.Equal("InsufficientEvidence", value.Outcome);
        Assert.Equal("Stopped", Assert.Single(value.Tasks).Availability);
        Assert.Null(value.Tasks[0].Assessment);
        Assert.Equal(0, rig.TaskReads);
        Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task CorruptSupportingOriginalCannotQualifyACompletedTask()
    {
        using var rig = await Rig.Create(1);
        await rig.Corrupt(0);
        var value = await rig.Read();
        Assert.Equal("InsufficientEvidence", value.Outcome);
        var task = Assert.Single(value.Tasks);
        Assert.Equal("OriginalsUnavailable", task.Availability);
        Assert.Null(task.Assessment);
        Assert.Null(task.ExecutionId);
        Assert.Empty(task.OriginalFiles);
        Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task OneReadyTaskNeverBorrowsProofFromAnUnavailableSibling()
    {
        using var rig = await Rig.Create(2);
        await rig.Corrupt(1);
        var value = await rig.Read();
        Assert.Equal("WebsiteEvidenceReady", value.Outcome);
        Assert.Equal(new[] { rig.Set.Tasks[0].Job.JobId }, value.BarrierJobIds.ToArray());
        Assert.Equal(1, value.VerifiedTaskCount);
        Assert.Equal("OriginalsUnavailable", value.Tasks[1].Availability);
        Assert.Null(value.Tasks[1].Assessment);
    }

    [Fact]
    public async Task ATaskFinishingDuringTheReadRemainsExplicitlyUnverified()
    {
        using var rig = await Rig.Create(1);
        var completed = rig.Set;
        rig.Set = completed with { Tasks = [completed.Tasks[0] with { State = "queued", Phase = "Queued", ReportSha256 = null, FinishedAt = null }] };
        rig.FinalSet = completed;
        var value = await rig.Read();
        Assert.Equal("completed", value.TaskSet.Tasks[0].State);
        Assert.Equal("OriginalsNotRead", value.Tasks[0].Availability);
        Assert.Equal("InsufficientEvidence", value.Outcome);
        Assert.Equal(0, rig.TaskReads);
    }

    [Fact]
    public async Task FinalCurrentAccessLossDropsAllPreviouslyVerifiedEvidence()
    {
        using var rig = await Rig.Create(1);
        rig.DenyFinalSet = true;
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Read());
        Assert.Equal(403, fault.Status);
        Assert.Equal(2, rig.SetReads);
        Assert.Equal(4, rig.TaskReads);
        Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task ChangedSetIdentityCannotReuseAlreadyVerifiedTaskFiles()
    {
        using var rig = await Rig.Create(1);
        rig.FinalSet = rig.Set with { SetId = Guid.Parse("3f000000-0000-4000-8000-000000000001") };
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Read());
        Assert.Equal(409, fault.Status);
        Assert.Equal("CONTROL_PUBLIC_REVIEW_CHANGED", fault.Code);
        Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task MissingSetAndCancellationCannotCreateAReplacementOrCleanFinding()
    {
        using var rig = await Rig.Create(0);
        rig.MissingSet = true;
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Read());
        Assert.Equal(404, fault.Status);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Read(cancelled.Token));
        Assert.Equal(0, rig.Creates);
        Assert.Equal(0, rig.TaskReads);
    }

    [Fact]
    public async Task HttpAndIndependentPrivateConsumerReadTheSameVerifiedFixtureWithoutAdmission()
    {
        using var rig = await Rig.Create(2);
        var diagnostic = new TaskCompletionSource<PortalRequestDiagnostic>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await rig.Server(value => { if (value.Operation == "ReadPublicReviewAssessment") diagnostic.TrySetResult(value); });
        using var http = rig.Http(server);
        using var response = await http.GetAsync(rig.Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = (await response.Content.ReadFromJsonAsync<PublicReviewAssessment>(PortalJson.Options))!;
        PublicReviewTestData.Validate(value);
        Assert.Equal("WebsiteEvidenceReady", value.Outcome);
        var logged = await diagnostic.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(response.Headers.GetValues("X-HSR-Request-Id").Single(), logged.SupportReference);
        Assert.Equal(rig.Set.SetId, logged.ReviewEvidence!.SetId);
        Assert.Equal(rig.Set.RequestId, logged.ReviewEvidence.RequestId);
        Assert.Equal(value.Tasks.Select(task => task.ReportSha256), logged.ReviewEvidence.Tasks.Select(task => task.ReportSha256));
        Assert.DoesNotContain(rig.Actor.Token, JsonSerializer.Serialize(logged), StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Actor.Email, JsonSerializer.Serialize(logged), StringComparison.Ordinal);
        await using var client = new HostedScanClient(new(server.BaseAddress, rig.Set.OrganizationId, rig.Actor.Token));
        var reply = await client.PublicReviewAssessmentAsync(rig.Set.ParentScanId.ToString(), rig.Set.RequestId.ToString());
        Assert.Equal("public-review-assessment", reply.Status);
        Assert.Contains(rig.Set.SetId.ToString(), reply.ToJson(), StringComparison.Ordinal);
        Assert.Equal(0, rig.Creates);
    }

    [Theory]
    [InlineData(false, false, 401)]
    [InlineData(true, true, 400)]
    public async Task HttpDeniesUnboundRequestsBeforeAnyEvidenceRead(bool authenticated, bool query, int expected)
    {
        using var rig = await Rig.Create(1);
        await using var server = await rig.Server(); using var http = rig.Http(server, authenticated);
        using var response = await http.GetAsync(rig.Path + (query ? "?actor=forged" : ""));
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(0, rig.SetReads);
        Assert.Equal(0, rig.TaskReads);
        Assert.Equal(0, rig.Creates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableOriginalDiagnosticsRetainExactFailureWithoutDisclosingItInTheResponse(bool report)
    {
        using var rig = await Rig.Create(1);
        var member = rig.Set.Tasks[0];
        var original = await rig.CorruptOriginal(report);
        var diagnostic = new TaskCompletionSource<PortalRequestDiagnostic>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await rig.Server(value => diagnostic.TrySetResult(value));
        using var http = rig.Http(server);
        using var response = await http.GetAsync(rig.Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var value = JsonSerializer.Deserialize<PublicReviewAssessment>(body, PortalJson.Options)!;
        Assert.Equal("InsufficientEvidence", value.Outcome);
        Assert.Equal("OriginalsUnavailable", Assert.Single(value.Tasks).Availability);
        var logged = await diagnostic.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var detail = Assert.Single(logged.ReviewEvidence!.Tasks).OriginalFailure!;
        Assert.Equal(member.Job.JobId, detail.JobId);
        Assert.Equal(report ? "ReportVerification" : "SupportingOriginalVerification", detail.Stage);
        Assert.Equal(original.Path, detail.File);
        Assert.Equal(original.Sha256, detail.Sha256);
        Assert.Equal(original.Bytes, detail.Bytes);
        Assert.NotEqual(Guid.Empty, detail.ExpectedExecutionId);
        Assert.DoesNotContain("OriginalFailure", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(detail.Stage, body, StringComparison.Ordinal);
        var safe = JsonSerializer.Serialize(logged);
        Assert.DoesNotContain(rig.Actor.Token, safe, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Actor.Email, safe, StringComparison.Ordinal);
        Assert.DoesNotContain("https://example.com", safe, StringComparison.Ordinal);
        Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task ExistingSingleTaskFailureKeepsItsPublicCodeAndSafeOriginalFingerprint()
    {
        using var rig = await Rig.Create(1);
        var expected = await rig.CorruptOriginal(false);
        var diagnostic = new TaskCompletionSource<PortalRequestDiagnostic>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await rig.Server(value => diagnostic.TrySetResult(value));
        using var http = rig.Http(server);
        var job = rig.Set.Tasks[0].Job;
        using var response = await http.GetAsync($"/api/workspaces/{job.OrganizationId:D}/website-scans/{job.ParentScanId:D}/tasks/{job.JobId:D}/result");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("CONTROL_PUBLIC_TASK_ORIGINAL_UNAVAILABLE", body, StringComparison.Ordinal);
        var logged = await diagnostic.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(job.JobId, logged.OriginalFailure!.JobId);
        Assert.Equal(expected.Sha256, logged.OriginalFailure.Sha256);
        Assert.Equal(expected.Path, logged.OriginalFailure.File);
        Assert.DoesNotContain(expected.Sha256, body, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Actor.Token, JsonSerializer.Serialize(logged), StringComparison.Ordinal);
        Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task FailedDiagnosticSinkCannotChangeTheAssessmentOrRepeatItsReads()
    {
        using var rig = await Rig.Create(1);
        var value = await new PortalPublicTasks(rig.Project, rig.Store, () => throw new InvalidOperationException())
            .ReviewAssessmentAsync(rig.Actor, rig.Set.OrganizationId, rig.Set.ParentScanId, rig.Set.RequestId, default,
                _ => throw new InvalidOperationException("fixture diagnostic sink"));
        Assert.Equal("WebsiteEvidenceReady", value.Outcome);
        Assert.Equal(2, rig.SetReads);
        Assert.Equal(4, rig.TaskReads);
        Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task ActualPrivateStdioReadsTheCurrentReviewWithoutAdmissionAndDeniesRevokedAccess()
    {
        using var rig = await Rig.Create(1);
        await using var server = await rig.Server();
        var assembly = typeof(PublicReviewAssessmentApiTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(value => value.Key == "McpServerAssembly").Value!;
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = [assembly, "--hosted"],
            WorkingDirectory = System.IO.Path.GetDirectoryName(assembly),
            Name = "review evidence independent consumer",
            EnvironmentVariables = new Dictionary<string, string?>
            { ["HSR_MCP_API_ORIGIN"] = server.BaseAddress.ToString(), ["HSR_MCP_ORGANIZATION"] = rig.Set.OrganizationId.ToString(), ["HSR_MCP_ACCESS_TOKEN"] = rig.Actor.Token }
        }));
        var tools = await client.ListToolsAsync();
        Assert.Equal(48, tools.Count);
        Assert.Single(tools, value => value.Name == "website_review_assessment");
        Assert.Single(tools, value => value.Name == "website_case_assessment");
        var args = new Dictionary<string, object?> { ["scanId"] = rig.Set.ParentScanId.ToString(), ["requestId"] = rig.Set.RequestId.ToString() };
        var reply = await client.CallToolAsync("website_review_assessment", args);
        Assert.False(reply.IsError ?? false);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(reply.Content)).Text;
        using var json = JsonDocument.Parse(text);
        Assert.True(JsonElement.DeepEquals(json.RootElement, reply.StructuredContent!.Value));
        Assert.Equal("WebsiteEvidenceReady", json.RootElement.GetProperty("data").GetProperty("outcome").GetString());
        Assert.Contains(rig.Set.Tasks[0].ReportSha256!, text, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Actor.Token, text, StringComparison.Ordinal);
        var input = rig.CaseInput();
        var caseArgs = new Dictionary<string, object?>(args)
        {
            ["jobId"] = input.JobId,
            ["reportSha256"] = input.ReportSha256,
            ["profile"] = input.Profile,
            ["coverage"] = "Unassessed",
            ["websiteConnection"] = input.WebsiteConnection,
            ["claimantAccess"] = input.ClaimantAccess,
            ["standing"] = input.Standing
        };
        var screening = await client.CallToolAsync("website_case_assessment", caseArgs);
        Assert.False(screening.IsError ?? false);
        var caseData = screening.StructuredContent!.Value.GetProperty("data");
        Assert.Equal("EvidenceGaps", caseData.GetProperty("outcome").GetString());
        Assert.Equal("Unassessed", caseData.GetProperty("criteria")[0].GetProperty("status").GetString());
        Assert.Equal("Supported", caseData.GetProperty("criteria")[2].GetProperty("status").GetString());
        rig.DenyFinalSet = true;
        var denied = await client.CallToolAsync("website_review_assessment", args);
        Assert.True(denied.IsError);
        Assert.DoesNotContain(rig.Set.Tasks[0].ReportSha256!, Assert.IsType<TextContentBlock>(Assert.Single(denied.Content)).Text, StringComparison.Ordinal);
        var deniedScreening = await client.CallToolAsync("website_case_assessment", caseArgs);
        Assert.True(deniedScreening.IsError);
        Assert.DoesNotContain(input.ReportSha256!, Assert.IsType<TextContentBlock>(Assert.Single(deniedScreening.Content)).Text, StringComparison.Ordinal);
        Assert.Equal(0, rig.Creates);
        Assert.Equal(0, rig.ReadinessReads);
    }

    [Fact]
    public async Task OpenApiPinsTheReadOnlySourceBoundEvidenceContract()
    {
        using var rig = await Rig.Create(0);
        await using var server = await rig.Server(); using var http = rig.Http(server);
        using var document = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        Assert.Equal("1.30.0", root.GetProperty("info").GetProperty("version").GetString());
        var operation = root.GetProperty("paths").GetProperty("/api/workspaces/{organization}/website-scans/{scan}/tasks/sets/{request}/assessment").GetProperty("get");
        Assert.Equal("ReadPublicReviewAssessment", operation.GetProperty("operationId").GetString());
        Assert.True(operation.GetProperty("security")[0].TryGetProperty("bearerAuth", out _));
        var shape = root.GetProperty("components").GetProperty("schemas").GetProperty("PublicReviewAssessment").GetProperty("properties");
        Assert.True(shape.GetProperty("isAssessment").GetProperty("const").GetBoolean());
        Assert.False(shape.GetProperty("websiteInputStartedByAssessment").GetProperty("const").GetBoolean());
        Assert.False(shape.GetProperty("legalCaseDetermined").GetProperty("const").GetBoolean());
        Assert.Equal(4, shape.GetProperty("tasks").GetProperty("maxItems").GetInt32());
        Assert.Equal(0, rig.TaskReads);
    }

    [Theory]
    [InlineData(true, "Supported", "Supported", "CounselReview", "Supported")]
    [InlineData(true, "Unassessed", "Supported", "EvidenceGaps", "Supported")]
    [InlineData(true, "Unsupported", "Unassessed", "ContraryEvidence", "Supported")]
    [InlineData(true, "Unsupported", "Conflicting", "ConflictingEvidence", "Supported")]
    [InlineData(false, "Supported", "Supported", "ContraryEvidence", "Unsupported")]
    public async Task CaseScreeningDistinguishesCounselSelectionsFromRecordedBarrier(
        bool barrier, string coverage, string standing, string outcome, string observed)
    {
        using var rig = await Rig.Create(1, barrier);
        await using var server = await rig.Server(); using var http = rig.Http(server);
        var input = rig.CaseInput() with { Coverage = coverage, Standing = standing };
        using var response = await http.PostAsJsonAsync(rig.CasePath, input, PortalJson.Options);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = Assert.IsType<PublicCaseAssessment>(await response.Content.ReadFromJsonAsync<PublicCaseAssessment>(PortalJson.Options));
        Assert.Equal(PublicCaseAssessment.CurrentSchema, value.Schema);
        Assert.Equal((rig.Set.OrganizationId, rig.Set.ParentScanId, rig.Set.RequestId, input.JobId, input.ReportSha256, input.Profile),
            (value.OrganizationId, value.ScanId, value.RequestId, value.JobId, value.ReportSha256, value.Profile));
        Assert.Equal(outcome, value.Outcome);
        Assert.Equal(new[] { "Coverage", "WebsiteConnection", "Barrier", "ClaimantAccess", "Standing" },
            value.Criteria.Select(item => item.Id));
        Assert.Equal(new[] { coverage, "Supported", observed, "Supported", standing }, value.Criteria.Select(item => item.Status));
        Assert.Equal(new[] { "Counsel", "Counsel", "RecordedTask", "Counsel", "Counsel" }, value.Criteria.Select(item => item.Source));
        Assert.All(value.Criteria, item =>
        {
            Assert.StartsWith("https://", item.Authority.Url, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(item.Authority.Pinpoint));
        });
        Assert.Equal("https://cdn.ca9.uscourts.gov/datastore/opinions/2019/01/15/17-55504.pdf",
            value.Criteria[2].Authority.Url);
        Assert.Equal(0, rig.Creates);
        Assert.Equal(0, rig.ReadinessReads);
    }

    [Fact]
    public async Task CaseScreeningCannotBorrowBarrierEvidenceFromAnotherTask()
    {
        using var rig = await Rig.Create(2);
        await rig.Corrupt(1);
        await using var server = await rig.Server(); using var http = rig.Http(server);
        var input = rig.CaseInput(1) with { ReportSha256 = null };
        using var response = await http.PostAsJsonAsync(rig.CasePath, input, PortalJson.Options);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = Assert.IsType<PublicCaseAssessment>(await response.Content.ReadFromJsonAsync<PublicCaseAssessment>(PortalJson.Options));
        Assert.Equal(input.JobId, value.JobId);
        Assert.Null(value.ReportSha256);
        Assert.Equal("EvidenceGaps", value.Outcome);
        Assert.Equal("Unassessed", Assert.Single(value.Criteria, item => item.Id == "Barrier").Status);
        Assert.Equal(0, rig.Creates);
    }

    [Theory]
    [InlineData("foreign-job", 404)]
    [InlineData("changed-original", 409)]
    [InlineData("missing-original-pin", 409)]
    [InlineData("lost-access", 403)]
    public async Task CaseScreeningRejectsForeignOrChangedEvidenceAndDropsResultsOnAccessLoss(string variation, int expected)
    {
        using var rig = await Rig.Create(1);
        await using var server = await rig.Server(); using var http = rig.Http(server);
        var input = rig.CaseInput();
        input = variation switch
        {
            "foreign-job" => input with { JobId = Guid.NewGuid() },
            "changed-original" => input with { ReportSha256 = new('0', 64) },
            "missing-original-pin" => input with { ReportSha256 = null },
            _ => input
        };
        rig.DenyFinalSet = variation == "lost-access";
        using var response = await http.PostAsJsonAsync(rig.CasePath, input, PortalJson.Options);
        Assert.Equal(expected, (int)response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(PublicCaseAssessment.CurrentSchema, problem, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.CaseInput().ReportSha256!, problem, StringComparison.Ordinal);
        Assert.Equal(0, rig.Creates);
    }

    [Theory]
    [InlineData("authentication", 401)]
    [InlineData("query", 400)]
    [InlineData("profile", 400)]
    [InlineData("criterion-status", 400)]
    [InlineData("absent-digest-field", 400)]
    [InlineData("unexpected-private-field", 400)]
    public async Task CaseScreeningDeniesUnauthenticatedOrMalformedRequestsBeforeReadingEvidence(string variation, int expected)
    {
        using var rig = await Rig.Create(1);
        await using var server = await rig.Server(); using var http = rig.Http(server, variation != "authentication");
        var input = JsonSerializer.SerializeToNode(rig.CaseInput(), PortalJson.Options)!.AsObject();
        const string privateValue = "synthetic-private-input";
        switch (variation)
        {
            case "profile": input["profile"] = "UnsupportedProfile"; break;
            case "criterion-status": input["coverage"] = privateValue; break;
            case "absent-digest-field": input.Remove("reportSha256"); break;
            case "unexpected-private-field": input["notes"] = privateValue; break;
        }
        using var response = await http.PostAsJsonAsync(rig.CasePath + (variation == "query" ? "?token=untrusted" : ""),
            input, PortalJson.Options);
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.DoesNotContain(privateValue, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, rig.SetReads);
        Assert.Equal(0, rig.TaskReads);
        Assert.Equal(0, rig.Creates);
    }

    [Fact]
    public async Task OpenApiAdvertisesOnlyProtectedPinnedCaseScreeningWithClosedInput()
    {
        using var rig = await Rig.Create(0);
        await using var server = await rig.Server(); using var http = rig.Http(server);
        using var document = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        var path = root.GetProperty("paths").GetProperty("/api/workspaces/{organization}/website-scans/{scan}/tasks/sets/{request}/case-assessment");
        var operation = path.GetProperty("post");
        Assert.Equal("AssessPublicReviewCase", operation.GetProperty("operationId").GetString());
        Assert.True(operation.GetProperty("security")[0].TryGetProperty("bearerAuth", out _));
        Assert.Equal(new[] { "organization", "scan", "request" },
            operation.GetProperty("parameters").EnumerateArray().Select(item => item.GetProperty("name").GetString()));
        Assert.True(operation.GetProperty("requestBody").GetProperty("required").GetBoolean());
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("PublicCaseAssessmentRequest");
        Assert.False(request.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains(request.GetProperty("required").EnumerateArray(), field => field.GetString() == "reportSha256");
        var properties = request.GetProperty("properties");
        Assert.Equal(PublicCaseAssessmentRequest.SupportedProfile, properties.GetProperty("profile").GetProperty("const").GetString());
        Assert.Equal(4, properties.GetProperty("standing").GetProperty("enum").GetArrayLength());
        Assert.Equal(5, schemas.GetProperty("PublicCaseAssessment").GetProperty("properties").GetProperty("criteria").GetProperty("maxItems").GetInt32());
        Assert.Equal(0, rig.SetReads);
    }

    private sealed class Rig : HttpMessageHandler
    {
        internal readonly string Root = System.IO.Path.GetFullPath(System.IO.Path.Combine("artifacts", "public-review-api-contract", Guid.NewGuid().ToString("N")));
        internal readonly PortalIdentity Actor = new(PortalProjectClientTests.Actor, "test@example.invalid", PortalProjectClientTests.Bearer);
        internal readonly PublicTaskEvidenceStore Store;
        internal readonly SupabasePortalClient Project;
        private readonly HttpClient _provider;
        internal PublicTaskSet Set = null!;
        internal PublicTaskSet? FinalSet;
        internal bool DenyFinalSet, MissingSet;
        internal int SetReads, TaskReads, Creates, ReadinessReads;
        private readonly Dictionary<Guid, PublicTaskStatus> _statuses = [];
        private readonly Dictionary<Guid, byte[]> _videos = [];
        private Rig() { _provider = new(this, false); Project = new(_provider, PortalProjectClientTests.Key); Store = new(Root, null); }
        private string ReviewPath => $"/api/workspaces/{Set.OrganizationId:D}/website-scans/{Set.ParentScanId:D}/tasks/sets/{Set.RequestId:D}";
        internal string Path => ReviewPath + "/assessment";
        internal string CasePath => ReviewPath + "/case-assessment";
        internal PublicCaseAssessmentRequest CaseInput(int index = 0) => new(PublicCaseAssessmentRequest.SupportedProfile,
            Set.Tasks[index].Job.JobId, Set.Tasks[index].ReportSha256, "Supported", "Supported", "Supported", "Supported");
        internal Task<PublicReviewAssessment> Read(CancellationToken token = default) => new PortalPublicTasks(Project, Store,
            () => { ReadinessReads++; return Task.FromResult(false); }).ReviewAssessmentAsync(Actor, Set.OrganizationId, Set.ParentScanId, Set.RequestId, token);
        internal Task<HostedControlServer> Server(Action<PortalRequestDiagnostic>? diagnostic = null) => HostedControlServer.StartAsync(
            new(PortalProjectClientTests.Key, Root, new('a', 40), 0), Project, new PortalObservationStub(),
            publicWebsiteRuntimeReady: () => Task.FromResult(false), publicTaskEvidence: Store, diagnostic: diagnostic);
        internal HttpClient Http(HostedControlServer server, bool authenticated = true)
        { var http = new HttpClient { BaseAddress = server.BaseAddress }; if (authenticated) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Actor.Token); return http; }
        internal static async Task<Rig> Create(int count, bool barrier = true)
        {
            var rig = new Rig(); var set = PublicReviewTestData.Set(count);
            set = set with { CreatedBy = rig.Actor.UserId, Tasks = [.. set.Tasks.Select(task => task with { CreatedBy = rig.Actor.UserId })] };
            for (var i = 0; i < count; i++)
            {
                var member = set.Tasks[i]; var start = new PublicTaskStart(PublicTaskStart.CurrentSchema, member.Job, Guid.NewGuid(), Guid.NewGuid());
                var destination = member.Job.Source.TargetUrl + member.Job.GoalKind!.ToLowerInvariant();
                var focus = new PublicTaskFocus("source-link", "link", member.Job.GoalKind, destination);
                var report = PublicTaskTests.Report(start);
                report = report with
                {
                    Goal = new(member.Job.GoalKind, "Reach public information", member.Job.GoalKind, destination),
                    Attempts = [.. report.Attempts.Select(attempt => attempt with {
                        Outcome = barrier && attempt.Number < 3 ? "NotReached" : "Reached",
                        FinalUrl = barrier && attempt.Number < 3 ? member.Job.Source.TargetUrl : destination,
                        Events = [new(1, 1000, attempt.Number < 3 ? "Enter" : "Pointer", focus, focus, member.Job.Source.TargetUrl, true, true)] })]
                };
                var inventory = ImmutableArray.CreateBuilder<PublicTaskFile>();
                foreach (var attempt in report.Attempts)
                    foreach (var name in new[] { attempt.Video, attempt.BeforeSnapshot, attempt.AfterSnapshot })
                    {
                        var video = name.EndsWith(".webm", StringComparison.Ordinal);
                        byte[] bytes = video ? [0x1a, 0x45, 0xdf, 0xa3] : JsonSerializer.SerializeToUtf8Bytes(
                            new AccessibilitySnapshot("fixture", [new("heading", "heading", "Saved public information")], []) { CaptureEpoch = 1, DocumentUrl = member.Job.Source.TargetUrl }, ReaderWorkerWire.Json);
                        if (video) rig._videos[member.Job.JobId] = bytes;
                        var file = new PublicTaskFile(name, PublicTaskRules.Hash(bytes), bytes.Length, video ? "video/webm" : "application/json");
                        inventory.Add(file); await rig.Store.WriteAsync(member.Job, file.Sha256, bytes, default);
                    }
                report = report with { Files = inventory.ToImmutable() }; report.Validate(start);
                var raw = JsonSerializer.SerializeToUtf8Bytes(report, ReaderWorkerWire.Json); var digest = PublicTaskRules.Hash(raw);
                await rig.Store.WriteAsync(member.Job, digest, raw, default);
                var publication = new PublicTaskPublication("hsr-public-task-publication-v1", member.Job.ParentOriginalSha256,
                    digest, raw.Length, start.ExecutionId, true, true, PublicTaskAssessment.Evaluate(report, true, true));
                rig._statuses.Add(member.Job.JobId, new("hsr-public-task-status-v1", member.Job, "completed", "Finished", rig.Actor.UserId,
                    member.CreatedAt, member.UpdatedAt, member.FinishedAt, start.WorkerId, digest, publication, null));
                set = set with { Tasks = set.Tasks.SetItem(i, member with { ReportSha256 = digest }) };
            }
            rig.Set = set; return rig;
        }
        internal Task Corrupt(int index)
        {
            var job = Set.Tasks[index].Job;
            return File.WriteAllBytesAsync(System.IO.Path.Combine(Root, "public-task-originals", job.OrganizationId.ToString("N"), job.JobId.ToString("N"), PublicTaskRules.Hash(_videos[job.JobId])), [0, 0, 0, 0]);
        }
        internal async Task<PublicTaskFile> CorruptOriginal(bool report)
        {
            var job = Set.Tasks[0].Job; var status = _statuses[job.JobId];
            var publication = status.Publication!;
            var original = report ? new PublicTaskFile("report.json", publication.ReportSha256, publication.ReportBytes, "application/json")
                : new PublicTaskFile("keyboard-1.webm", PublicTaskRules.Hash(_videos[job.JobId]), _videos[job.JobId].Length, "video/webm");
            if (!report)
            {
                var raw = await Store.ReadAsync(job, publication.ReportSha256, publication.ReportBytes, default);
                var saved = JsonSerializer.Deserialize<PublicTaskReport>(raw!, ReaderWorkerWire.Json)!;
                original = saved.Files.First(file => file.MediaType == "video/webm");
            }
            await File.WriteAllBytesAsync(System.IO.Path.Combine(Root, "public-task-originals", job.OrganizationId.ToString("N"), job.JobId.ToString("N"), original.Sha256), [0, 0, 0, 0]);
            return original;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(Actor.Token, request.Headers.Authorization?.Parameter);
            object? value;
            if (request.RequestUri!.AbsolutePath == "/auth/v1/user") value = JsonSerializer.Deserialize<object>(PortalProjectClientTests.UserJson);
            else
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var function = request.RequestUri.AbsolutePath.Split('/')[^1];
                if (function == "read_public_task_set")
                {
                    SetReads++;
                    if (DenyFinalSet && SetReads > 1) return new(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { code = "42501" }) };
                    value = MissingSet ? null : SetReads > 1 && FinalSet is not null ? FinalSet : Set;
                }
                else if (function == "read_public_task")
                { TaskReads++; value = _statuses[json.RootElement.GetProperty("p_job_id").GetGuid()]; }
                else { Creates++; throw new InvalidOperationException("Unexpected admission in readonly fixture"); }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, ReaderWorkerWire.Json), Encoding.UTF8, "application/json") };
        }
        protected override void Dispose(bool disposing) { if (disposing) { Store.Dispose(); _provider.Dispose(); } base.Dispose(disposing); }
    }
}
