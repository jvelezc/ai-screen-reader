using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HeadlessScreenReader.Cli.HostedControl;

namespace HeadlessScreenReader.Tests;

public sealed class PortalOpenApiTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactProgressAndMissingPrefixMatchThePublishedSchema(bool available)
    {
        var organization = PortalProjectClientTests.Organization; var scan = PortalProjectClientTests.Scan;
        var job = new HeadlessScreenReader.Domain.FixtureJob(organization, scan, PortalProjectClientTests.Job, 1)
        { RecordObservation = true, TraversalRoute = 1 };
        var worker = Guid.NewGuid();
        var progress = available ? new HeadlessScreenReader.Domain.RetainedCommandProgress(
            HeadlessScreenReader.Domain.RetainedCommandProgress.CurrentSchema, job, worker, 2, 1, 0, "Intent", 1,
            "Tab", null, "o1", null, null, null, new string('a', 64), false, true) : null;
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new PortalProgressResponse(
            new(organization, scan, "synthetic-shop", "hsr-desktop-reader-v13", "blocked", DateTimeOffset.UtcNow),
            job.JobId, 1, worker, available ? "Available" : "NotRetained", progress), PortalJson.Options));
        using var spec = JsonDocument.Parse(PortalOpenApi.Document);
        Assert.True(Matches(body.RootElement, Component(spec.RootElement, "RecordedProgress"), spec.RootElement));
        if (available)
        {
            Assert.Equal("Intent", body.RootElement.GetProperty("progress").GetProperty("lastPhase").GetString());
            Assert.False(body.RootElement.GetProperty("progress").GetProperty("isCompletionReceipt").GetBoolean());
        }
        Assert.DoesNotContain("capability", body.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("origin")]
    [InlineData("files")]
    [InlineData("bytes")]
    [InlineData("readback")]
    public void ProducerReceiptSchemaPreservesReadbackAndObservationOnlyBounds(string change)
    {
        using var spec = JsonDocument.Parse(PortalOpenApi.Document);
        var project = new PortalProjectStub();
        var job = new HeadlessScreenReader.Domain.FixtureJob(project.Job.OrganizationId, project.Job.ScanId, project.Job.JobId, 1);
        var value = JsonSerializer.SerializeToNode(new PortalReportResponse(project.Record with { State = "observed" },
            "Available", null, Observation())
        { WorkerArchive = WorkerEvidenceTests.Receipt(job) }, PortalJson.Options)!;
        switch (change)
        {
            case "origin": value["workerArchive"]!["origin"] = "OperatorImport"; break;
            case "files": value["workerArchive"]!["receipt"]!["files"] = 3; break;
            case "bytes": value["workerArchive"]!["receipt"]!["bytes"] = 1052673; break;
            case "readback": value["workerArchive"]!["receipt"]!["objectReadbackVerified"] = false; break;
        }
        using var body = JsonDocument.Parse(value.ToJsonString());
        Assert.Equal(change == "valid", Matches(body.RootElement, Component(spec.RootElement, "Report"), spec.RootElement));
    }

    [Fact]
    public async Task ArchivePayloadsAndOptionalOffsetMatchPublishedContract()
    {
        var fixture = await ArchiveHttpFixture.Create(); await using var server = await fixture.Start();
        using var http = PortalHttpTests.Authorized(server);
        using var spec = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
        foreach (var operation in new[] { "manifest", "file" })
        {
            using var response = await http.PostAsync(fixture.Path + "/" + operation, Json(operation == "manifest"
                ? JsonSerializer.Serialize(new { fixture.Identity.IndexSha256 }, PortalJson.Options) : JsonSerializer.Serialize(new { fixture.Identity.IndexSha256, path = "original-000.bin" }, PortalJson.Options)));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(Matches(payload.RootElement, Component(spec.RootElement, operation == "manifest" ? "ArchiveManifest" : "ArchiveFile"), spec.RootElement));
            var path = spec.RootElement.GetProperty("paths").GetProperty("/api/workspaces/{organization}/archives/{archive}/" + operation).GetProperty("post");
            Assert.Equal(new[] { "organization", "archive" }, path.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
            Assert.Single(path.GetProperty("security").EnumerateArray());
            Assert.DoesNotContain("offset", Component(spec.RootElement, operation == "manifest" ? "ArchiveManifestRequest" : "ArchiveFileRequest").GetProperty("required").EnumerateArray().Select(p => p.GetString()));
        }
    }

    [Fact]
    public async Task ReaderLifecycleHttpPayloadsMatchPublishedContractsAndTruthConstraints()
    {
        var project = new ReaderProjectStub();
        var readers = new PortalReaderSessions(project, TimeProvider.System,
            () => Task.FromResult<IPortalReaderBrowser>(new ReaderBrowserStub()));
        await using var server = await PortalReaderHttpTests.Start(project, readers);
        using var http = PortalReaderHttpTests.Client(server);
        using var spec = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
        using var created = await http.PostAsync(PortalReaderHttpTests.Collection, Json("{}"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var opened = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.True(Matches(opened.RootElement, Component(spec.RootElement, "ReaderSession"), spec.RootElement));
        using var captured = await http.PostAsync(created.Headers.Location + "/observations", Json("{}"));
        Assert.Equal(HttpStatusCode.OK, captured.StatusCode);
        using var snapshot = JsonDocument.Parse(await captured.Content.ReadAsStringAsync());
        Assert.True(Matches(snapshot.RootElement, Component(spec.RootElement, "ReaderSnapshot"), spec.RootElement));
        var forged = JsonNode.Parse(snapshot.RootElement.GetRawText())!;
        forged["goalVerified"] = true;
        using var invalid = JsonDocument.Parse(forged.ToJsonString());
        Assert.False(Matches(invalid.RootElement, Component(spec.RootElement, "ReaderSnapshot"), spec.RootElement));
        using var command = await http.PostAsync(created.Headers.Location + "/commands", Json("{\"command\":\"NextHeading\",\"expectedDocumentId\":\"document\"}"));
        Assert.Equal(HttpStatusCode.OK, command.StatusCode);
        using var step = JsonDocument.Parse(await command.Content.ReadAsStringAsync());
        Assert.True(Matches(step.RootElement, Component(spec.RootElement, "ReaderCommand"), spec.RootElement));
        using var timeline = await http.PostAsync(created.Headers.Location + "/timeline", Json("{\"afterSequence\":0,\"limit\":100}"));
        Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
        using var events = JsonDocument.Parse(await timeline.Content.ReadAsStringAsync());
        Assert.True(Matches(events.RootElement, Component(spec.RootElement, "ReaderTimeline"), spec.RootElement));
        using var traceResponse = await http.PostAsync(created.Headers.Location + "/trace", Json("{\"afterSequence\":0,\"limit\":20}"));
        Assert.Equal(HttpStatusCode.OK, traceResponse.StatusCode);
        using var trace = JsonDocument.Parse(await traceResponse.Content.ReadAsStringAsync());
        Assert.True(Matches(trace.RootElement, Component(spec.RootElement, "ReaderTrace"), spec.RootElement));
        var falseInput = JsonNode.Parse(trace.RootElement.GetRawText())!;
        falseInput["entries"]![0]!["edge"]!["browserKeystrokes"] = 1;
        using var invalidInput = JsonDocument.Parse(falseInput.ToJsonString());
        Assert.False(Matches(invalidInput.RootElement, Component(spec.RootElement, "ReaderTrace"), spec.RootElement));
        var falseHistory = JsonNode.Parse(trace.RootElement.GetRawText())!;
        falseHistory["traceTruncated"] = true;
        using var invalidHistory = JsonDocument.Parse(falseHistory.ToJsonString());
        Assert.False(Matches(invalidHistory.RootElement, Component(spec.RootElement, "ReaderTrace"), spec.RootElement));
        using var closed = await http.DeleteAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        using var receipt = JsonDocument.Parse(await closed.Content.ReadAsStringAsync());
        Assert.True(Matches(receipt.RootElement, Component(spec.RootElement, "ReaderClosed"), spec.RootElement));
        var parameters = spec.RootElement.GetProperty("paths").GetProperty("/api/workspaces/{organization}/reader-sessions/{session}")
            .GetProperty("get").GetProperty("parameters");
        Assert.Equal(new[] { "organization", "session" }, parameters.EnumerateArray().Select(value => value.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task PublishedContractIncludesEveryConsumerResponseAndAuthenticationBoundary()
    {
        await using var server = await PortalHttpTests.Start(new());
        using var http = new HttpClient { BaseAddress = server.BaseAddress };
        using var response = await http.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var spec = document.RootElement;
        Assert.Equal("3.1.0", spec.GetProperty("openapi").GetString());
        Assert.Equal("1.10.0", spec.GetProperty("info").GetProperty("version").GetString());
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", spec.GetProperty("jsonSchemaDialect").GetString());
        Assert.Equal("http", spec.GetProperty("components").GetProperty("securitySchemes").GetProperty("bearerAuth").GetProperty("type").GetString());
        var operations = new[]
        {
            ("/api/config", "get", "200", "Configuration", false),
            ("/internal/ready", "get", "200", "Readiness", false),
            ("/api/session", "get", "200", "Session", true),
            ("/api/workspaces/{organization}/archives/{archive}/manifest", "post", "200", "ArchiveManifest", true),
            ("/api/workspaces/{organization}/archives/{archive}/file", "post", "200", "ArchiveFile", true),
            ("/api/workspace", "post", "201", "Workspace", true),
            ("/api/workspaces/{organization}", "get", "200", "Workspace", true),
            ("/api/workspaces/{organization}/scans", "get", "200", "ScanList", true),
            ("/api/workspaces/{organization}/scans", "post", "201", "ScanCreated", true),
            ("/api/workspaces/{organization}/scans/{scan}", "get", "200", "Report", true),
            ("/api/workspaces/{organization}/scans/{scan}/progress", "get", "200", "RecordedProgress", true),
            ("/api/workspaces/{organization}/reader-sessions", "post", "201", "ReaderSession", true),
            ("/api/workspaces/{organization}/reader-sessions/{session}", "get", "200", "ReaderSession", true),
            ("/api/workspaces/{organization}/reader-sessions/{session}", "delete", "200", "ReaderClosed", true),
            ("/api/workspaces/{organization}/reader-sessions/{session}/observations", "post", "200", "ReaderSnapshot", true),
            ("/api/workspaces/{organization}/reader-sessions/{session}/commands", "post", "200", "ReaderCommand", true),
            ("/api/workspaces/{organization}/reader-sessions/{session}/timeline", "post", "200", "ReaderTimeline", true),
            ("/api/workspaces/{organization}/reader-sessions/{session}/trace", "post", "200", "ReaderTrace", true)
        };
        foreach (var (path, method, status, schema, authenticated) in operations)
        {
            var operation = spec.GetProperty("paths").GetProperty(path).GetProperty(method);
            Assert.Equal("#/components/schemas/" + schema, operation.GetProperty("responses").GetProperty(status)
                .GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
            Assert.Equal(authenticated ? 1 : 0, operation.GetProperty("security").GetArrayLength());
            if (authenticated) Assert.Empty(operation.GetProperty("security")[0].GetProperty("bearerAuth").EnumerateArray());
            foreach (var code in new[] { "400", "401", "403", "404", "409", "413", "415", "503" })
                Assert.Equal("#/components/schemas/Problem", operation.GetProperty("responses").GetProperty(code)
                    .GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString());
        }
        Assert.Equal("object", Component(spec, "EmptyRequest").GetProperty("type").GetString());
        Assert.False(Component(spec, "EmptyRequest").GetProperty("additionalProperties").GetBoolean());
        var request = Component(spec, "CreateScan");
        Assert.Equal(new[] { "requestId" }, request.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("uuid", request.GetProperty("properties").GetProperty("requestId").GetProperty("format").GetString());
        Assert.DoesNotContain("token", Component(spec, "Session").GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.False(spec.GetProperty("components").GetProperty("schemas").TryGetProperty("PortalIdentity", out _));
    }

    [Fact]
    public async Task RealHttpSuccessBodiesAndCreationHeadersMatchThePublishedShapes()
    {
        var project = new PortalProjectStub();
        await using var server = await PortalHttpTests.Start(project);
        using var http = PortalHttpTests.Authorized(server);
        using var spec = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
        foreach (var (path, schema) in new[] { ("/api/config", "Configuration"), ("/internal/ready", "Readiness"),
            ("/api/session", "Session") })
        {
            using var response = await http.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(Matches(payload.RootElement, Component(spec.RootElement, schema), spec.RootElement), schema);
        }
        using var create = await http.PostAsync("/api/workspace", Json("{}"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal("/api/workspaces/" + PortalProjectClientTests.Organization.ToString("D"), create.Headers.Location!.OriginalString);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        Assert.True(Matches(created.RootElement, Component(spec.RootElement, "Workspace"), spec.RootElement));
        using var resume = await http.PostAsync("/api/workspace", Json("{}"));
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
        using var resumed = JsonDocument.Parse(await resume.Content.ReadAsStringAsync());
        Assert.True(Matches(resumed.RootElement, Component(spec.RootElement, "Workspace"), spec.RootElement));
        using var admit = await http.PostAsync(PortalHttpTests.Prefix, Json("{\"requestId\":\"" + Guid.NewGuid().ToString("D") + "\"}"));
        Assert.Equal(HttpStatusCode.Created, admit.StatusCode);
        Assert.Equal(PortalHttpTests.Prefix + "/" + PortalProjectClientTests.Scan.ToString("D"), admit.Headers.Location!.OriginalString);
        using var admission = JsonDocument.Parse(await admit.Content.ReadAsStringAsync());
        Assert.True(Matches(admission.RootElement, Component(spec.RootElement, "ScanCreated"), spec.RootElement));
        foreach (var (path, schema) in new[] { ("/api/workspaces/" + PortalProjectClientTests.Organization.ToString("D"), "Workspace"), (PortalHttpTests.Prefix, "ScanList") })
        {
            using var response = await http.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(Matches(payload.RootElement, Component(spec.RootElement, schema), spec.RootElement), schema);
        }
        foreach (var path in new[] { "/api/workspace", "/api/workspaces/{organization}/scans" })
            Assert.Equal("uri-reference", spec.RootElement.GetProperty("paths").GetProperty(path).GetProperty("post")
                .GetProperty("responses").GetProperty("201").GetProperty("headers").GetProperty("Location").GetProperty("schema").GetProperty("format").GetString());
        Assert.True(spec.RootElement.GetProperty("paths").GetProperty("/api/workspace").GetProperty("post").GetProperty("responses").TryGetProperty("200", out _));
    }

    [Theory]
    [InlineData("queued", "Pending", false)]
    [InlineData("leased", "Pending", false)]
    [InlineData("blocked", "NotCollected", false)]
    [InlineData("completed", "Unavailable", false)]
    [InlineData("completed", "Available", true)]
    public async Task ReportSchemasPreserveExecutionEvidenceAndNullableSemantics(string jobState, string evidenceState, bool available)
    {
        var project = new PortalProjectStub();
        project.Job = project.Job with { State = jobState };
        project.Record = project.Record with { State = jobState == "completed" ? "observed" : jobState };
        var reader = new PortalObservationStub { Value = available ? Observation() : null };
        await using var server = await PortalHttpTests.Start(project, reader);
        using var http = PortalHttpTests.Authorized(server);
        using var spec = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
        using var body = JsonDocument.Parse(await http.GetStringAsync(PortalHttpTests.Prefix + "/" + PortalProjectClientTests.Scan.ToString("D")));
        Assert.True(Matches(body.RootElement, Component(spec.RootElement, "Report"), spec.RootElement));
        Assert.Equal(evidenceState, body.RootElement.GetProperty("evidenceState").GetString());
        if (available)
        {
            var result = body.RootElement.GetProperty("observation");
            Assert.False(result.GetProperty("goalVerified").GetBoolean());
            Assert.Equal(0, result.GetProperty("dispatchedInputs").GetInt32());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("nodes")[0].GetProperty("required").ValueKind);
            Assert.False(result.GetProperty("nodes")[1].GetProperty("required").GetBoolean());
            Assert.Equal("LocalFallback", result.GetProperty("evidenceStorage").GetString());
        }
        else Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("observation").ValueKind);
    }

    [Theory]
    [InlineData("unauthorized", 401, "CONTROL_AUTHENTICATION_REQUIRED")]
    [InlineData("foreign-workspace", 404, "CONTROL_WORKSPACE_NOT_FOUND")]
    [InlineData("bad-json", 400, "CONTROL_REQUEST_INVALID")]
    [InlineData("wrong-media", 415, "CONTROL_JSON_REQUIRED")]
    [InlineData("origin", 403, "CONTROL_ORIGIN_DENIED")]
    [InlineData("oversized", 413, "CONTROL_REQUEST_INVALID")]
    [InlineData("unconfirmed", 503, "CONTROL_STATE_UNCONFIRMED")]
    public async Task RealHttpProblemsMatchSafeSchemasAndPreserveDenialStatus(string scenario, int status, string code)
    {
        var project = new PortalProjectStub();
        await using var server = await PortalHttpTests.Start(project);
        using var http = PortalHttpTests.Authorized(server);
        using var spec = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
        if (scenario == "unauthorized") http.DefaultRequestHeaders.Authorization = null;
        if (scenario == "origin") http.DefaultRequestHeaders.Add("Origin", "https://unexpected.example");
        if (scenario == "unconfirmed") project.Job = project.Job with { State = "unexpected" };
        using var response = scenario switch
        {
            "foreign-workspace" => await http.GetAsync("/api/workspaces/" + Guid.NewGuid().ToString("D")),
            "bad-json" => await http.PostAsync("/api/workspace", Json("{")),
            "wrong-media" => await http.PostAsync("/api/workspace", new StringContent("{}")),
            "oversized" => await http.PostAsync("/api/workspace", Json("{}" + new string(' ', 4096))),
            "unconfirmed" => await http.GetAsync(PortalHttpTests.Prefix + "/" + PortalProjectClientTests.Scan.ToString("D")),
            _ => await http.GetAsync("/api/session")
        };
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(Matches(problem.RootElement, Component(spec.RootElement, "Problem"), spec.RootElement));
        Assert.Equal(status, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(problem.RootElement.GetProperty("title").GetString(), problem.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain(PortalProjectClientTests.Bearer, problem.RootElement.GetRawText());
        if (status == 401) Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal("Bearer", spec.RootElement.GetProperty("paths").GetProperty("/api/session").GetProperty("get")
            .GetProperty("responses").GetProperty("401").GetProperty("headers").GetProperty("WWW-Authenticate").GetProperty("schema").GetProperty("const").GetString());
    }

    [Theory]
    [InlineData("goal")]
    [InlineData("input")]
    [InlineData("required-type")]
    [InlineData("required-missing")]
    [InlineData("profile")]
    [InlineData("digest")]
    [InlineData("extra")]
    public void PublishedSchemasRejectConcreteWireDrift(string change)
    {
        using var spec = JsonDocument.Parse(PortalOpenApi.Document);
        var value = JsonSerializer.SerializeToNode(Observation(), PortalJson.Options)!;
        switch (change)
        {
            case "goal": value["goalVerified"] = true; break;
            case "input": value["dispatchedInputs"] = 1; break;
            case "required-type": value["nodes"]![0]!["required"] = "false"; break;
            case "required-missing": value["nodes"]![0]!.AsObject().Remove("required"); break;
            case "profile": value["readerProfile"] = "future-profile"; break;
            case "digest": value["digest"] = "unverified"; break;
            case "extra": value["unexpectedCredential"] = "not-permitted"; break;
        }
        using var payload = JsonDocument.Parse(value.ToJsonString());
        Assert.False(Matches(payload.RootElement, Component(spec.RootElement, "Observation"), spec.RootElement));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2048, true)]
    [InlineData(2049, false)]
    public void ObservationSchemaPinsTheActualNodeCeiling(int count, bool accepted)
    {
        using var spec = JsonDocument.Parse(PortalOpenApi.Document);
        var source = Observation();
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(source with { Nodes = Enumerable.Repeat(source.Nodes[0], count).ToArray() }, PortalJson.Options));
        Assert.Equal(accepted, Matches(body.RootElement, Component(spec.RootElement, "Observation"), spec.RootElement));
    }

    [Fact]
    public void RequestSchemasDenyActorInjectionAndEmptyRequestIdentity()
    {
        using var spec = JsonDocument.Parse(PortalOpenApi.Document);
        foreach (var (schema, body, accepted) in new[]
        {
            ("EmptyRequest", "{}", true), ("EmptyRequest", "{\"actorId\":\"injected\"}", false),
            ("CreateScan", "{\"requestId\":\"11111111-1111-4111-8111-111111111111\"}", true),
            ("CreateScan", "{\"requestId\":\"00000000-0000-0000-0000-000000000000\"}", false),
            ("CreateScan", "{\"requestId\":\"malformed\"}", false), ("CreateScan", "{}", false)
        })
        {
            using var value = JsonDocument.Parse(body);
            Assert.Equal(accepted, Matches(value.RootElement, Component(spec.RootElement, schema), spec.RootElement));
        }
    }

    [Theory]
    [InlineData("ReaderCommandRequest", "{\"command\":\"NextHeading\",\"expectedDocumentId\":\"document\"}", true)]
    [InlineData("ReaderCommandRequest", "{\"command\":\"Activate\",\"expectedDocumentId\":\"document\"}", false)]
    [InlineData("ReaderCommandRequest", "{\"command\":\"NextHeading\",\"expectedDocumentId\":\" \"}", false)]
    [InlineData("ReaderCommandRequest", "{\"command\":\"NextHeading\"}", false)]
    [InlineData("ReaderCommandRequest", "{\"command\":\"NextHeading\",\"expectedDocumentId\":\"document\",\"key\":\"Enter\"}", false)]
    [InlineData("ReaderTimelineRequest", "{\"afterSequence\":0,\"limit\":100}", true)]
    [InlineData("ReaderTimelineRequest", "{\"afterSequence\":-1,\"limit\":1}", false)]
    [InlineData("ReaderTimelineRequest", "{\"afterSequence\":0,\"limit\":101}", false)]
    public void NavigationRequestSchemasMatchTheReadOnlyPolicyAndPagingBounds(string schema, string json, bool accepted)
    {
        using var spec = JsonDocument.Parse(PortalOpenApi.Document);
        using var body = JsonDocument.Parse(json);
        Assert.Equal(accepted, Matches(body.RootElement, Component(spec.RootElement, schema), spec.RootElement));
    }

    [Theory]
    [InlineData("available-missing")]
    [InlineData("available-queued")]
    [InlineData("available-error")]
    [InlineData("unavailable-observation")]
    [InlineData("pending-observed")]
    [InlineData("not-collected-observed")]
    [InlineData("invalid-queued")]
    public void ReportSchemaRejectsContradictoryEvidenceClaims(string change)
    {
        using var spec = JsonDocument.Parse(PortalOpenApi.Document);
        var record = new PortalProjectStub().Record with { State = "observed" };
        var value = JsonSerializer.SerializeToNode(new PortalReportResponse(record, "Available", null, Observation()), PortalJson.Options)!;
        switch (change)
        {
            case "available-missing": value["observation"] = null; break;
            case "available-queued": value["scan"]!["state"] = "queued"; break;
            case "available-error": value["errorCode"] = "CONTROL_EVIDENCE_UNCONFIRMED"; break;
            case "unavailable-observation": value["evidenceState"] = "Unavailable"; break;
            case "pending-observed": value["evidenceState"] = "Pending"; value["observation"] = null; break;
            case "not-collected-observed": value["evidenceState"] = "NotCollected"; value["observation"] = null; break;
            case "invalid-queued": value["evidenceState"] = "Invalid"; value["observation"] = null; value["scan"]!["state"] = "queued"; break;
        }
        using var body = JsonDocument.Parse(value.ToJsonString());
        Assert.False(Matches(body.RootElement, Component(spec.RootElement, "Report"), spec.RootElement));
    }

    [Fact]
    public async Task InvalidEvidenceRemainsAReadableReportWithNoObservationOrGoalClaim()
    {
        var project = new PortalProjectStub();
        project.Job = project.Job with { State = "completed" };
        project.Record = project.Record with { State = "observed" };
        await using var server = await HostedControlServer.StartAsync(new(PortalProjectClientTests.Key,
            Path.GetFullPath("artifacts/portal-validation/evidence"), new string('a', 40), 0), project, new InvalidObservation());
        using var http = PortalHttpTests.Authorized(server);
        using var spec = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
        using var response = await http.GetAsync(PortalHttpTests.Prefix + "/" + PortalProjectClientTests.Scan.ToString("D"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(Matches(body.RootElement, Component(spec.RootElement, "Report"), spec.RootElement));
        Assert.Equal("Invalid", body.RootElement.GetProperty("evidenceState").GetString());
        Assert.Equal("CONTROL_EVIDENCE_UNCONFIRMED", body.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("observation").ValueKind);
    }

    private sealed class InvalidObservation : IPortalObservationReader
    {
        public Task<PortalObservationResponse?> ReadAsync(PortalJobResult job, CancellationToken token) =>
            throw new PortalFault(503, "CONTROL_EVIDENCE_UNCONFIRMED");
    }

    private static PortalObservationResponse Observation() => new("fixture", new string('a', 64), "hsr-desktop-reader-v13", 0, false,
        [new("n1", null, "button", "Unknown required", false, false, null, null, null, null, null, null, null),
         new("n2", null, "textbox", "Observed optional", false, false, null, null, null, null, false, "Aria", null)],
        ["Input-free fixture; no goal claim"], "LocalFallback");
    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");
    private static JsonElement Component(JsonElement spec, string name) => spec.GetProperty("components").GetProperty("schemas").GetProperty(name);

    // Independent, bounded evaluator for the keywords emitted by these DTO schemas.
    // Drift cases above prove that successful serialization alone is not the assertion.
    private static bool Matches(JsonElement value, JsonElement schema, JsonElement spec)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var target = spec;
            foreach (var segment in reference.GetString()![2..].Split('/')) target = target.GetProperty(segment.Replace("~1", "/").Replace("~0", "~"));
            return Matches(value, target, spec);
        }
        if (schema.TryGetProperty("type", out var type))
        {
            var names = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(item => item.GetString()) : [type.GetString()];
            if (!names.Any(name => name switch
            {
                "null" => value.ValueKind == JsonValueKind.Null,
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                "string" => value.ValueKind == JsonValueKind.String,
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
                "number" => value.ValueKind == JsonValueKind.Number,
                _ => false
            })) return false;
        }
        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(value, constant)) return false;
        if (schema.TryGetProperty("not", out var forbidden) && Matches(value, forbidden, spec)) return false;
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(item => JsonElement.DeepEquals(value, item))) return false;
        if (schema.TryGetProperty("allOf", out var clauses) && clauses.EnumerateArray().Any(clause => !Matches(value, clause, spec))) return false;
        if (schema.TryGetProperty("if", out var condition))
        {
            var branch = Matches(value, condition, spec) ? "then" : "else";
            if (schema.TryGetProperty(branch, out var consequence) && !Matches(value, consequence, spec)) return false;
        }
        if (value.ValueKind == JsonValueKind.Null) return true;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required) && required.EnumerateArray().Any(key => !value.TryGetProperty(key.GetString()!, out _))) return false;
            var hasProperties = schema.TryGetProperty("properties", out var properties);
            foreach (var property in value.EnumerateObject())
            {
                if (hasProperties && properties.TryGetProperty(property.Name, out var child)) { if (!Matches(property.Value, child, spec)) return false; }
                else if (schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False) return false;
            }
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            if (schema.TryGetProperty("maxItems", out var maximum) && value.GetArrayLength() > maximum.GetInt32()) return false;
            if (schema.TryGetProperty("items", out var items) && value.EnumerateArray().Any(item => !Matches(item, items, spec))) return false;
        }
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (schema.TryGetProperty("minimum", out var minimum) && value.GetDecimal() < minimum.GetDecimal()) return false;
            if (schema.TryGetProperty("maximum", out var maximum) && value.GetDecimal() > maximum.GetDecimal()) return false;
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (schema.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(text, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) return false;
            if (schema.TryGetProperty("format", out var format))
                return format.GetString() switch
                {
                    "uuid" => Guid.TryParseExact(text, "D", out _),
                    "date-time" => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                    "uri" => Uri.TryCreate(text, UriKind.Absolute, out _),
                    _ => true
                };
        }
        return true;
    }
}
