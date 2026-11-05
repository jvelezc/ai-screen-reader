using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PublicWebsiteApiTests
{
    [Fact]
    public async Task PublicAdmissionQuotaBecomesASafe429WithoutLosingCurrentWorkspaceChecks()
    {
        using var rig = new Rig { RejectCreateQuota = true };
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.AdmitAsync(rig.Actor, rig.Job.OrganizationId,
            new(rig.RequestId, rig.Job.Source.TargetUrl), default));
        Assert.Equal(429, fault.Status); Assert.Equal("CONTROL_PUBLIC_WEBSITE_QUEUE_FULL", fault.Code);
        Assert.Equal(2, rig.Memberships); Assert.Equal(1, rig.Creates); Assert.Equal(0, rig.StatusReads);
        Assert.DoesNotContain("PRIVATE_PROVIDER_DETAIL", fault.Message);
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Expired")]
    [InlineData("Missing")]
    public async Task PublicStatusRetainsExactClockBoundWorkerPermissionWithoutLoadingOriginals(string status)
    {
        using var rig = new Rig { State = "leased" };
        var checkedAt = DateTimeOffset.Parse("2026-11-02T19:00:00Z");
        rig.StatusChange = row => row["worker_permission"] = new
        {
            organization_id = rig.Job.OrganizationId,
            scan_id = rig.Job.ScanId,
            job_id = rig.Job.JobId,
            generation = 1,
            status,
            checked_at = checkedAt,
            expires_at = status == "Missing" ? (DateTimeOffset?)null : checkedAt.AddSeconds(status == "Active" ? 30 : -1)
        };
        var response = await rig.Service.StatusAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, default);
        Assert.Equal(status, response.WorkerPermission!.Status); Assert.Equal(checkedAt, response.WorkerPermission.CheckedAt);
        Assert.Equal(rig.Job.JobId, response.WorkerPermission.JobId); Assert.Equal(1, response.WorkerPermission.Generation);
        Assert.Equal(2, rig.Memberships); Assert.Equal(0, rig.OriginalReads); Assert.Null(response.OriginalSha256);
    }
    [Fact]
    public async Task CanonicalAdmissionRetainsCallerSourceAndDurableIdentityWithoutExecutionClaim()
    {
        using var rig = new Rig { State = "queued" };
        var result = await rig.Service.AdmitAsync(rig.Actor, rig.Job.OrganizationId,
            new(rig.RequestId, "https://SCREENREADER.PRIVENSIC.COM:443/ci/v1/source"), default);
        Assert.Equal(rig.Job.OrganizationId, result.OrganizationId); Assert.Equal(rig.Job.ScanId, result.ScanId);
        Assert.Equal(rig.Job.JobId, result.JobId); Assert.Equal(rig.Job.Source.TargetUrl, result.TargetUrl);
        Assert.Equal(rig.RequestId, result.RequestId); Assert.Equal(1, rig.Creates); Assert.Equal(2, rig.Memberships);
        Assert.Equal(0, rig.OriginalReads); Assert.Equal(1, rig.StatusReads);
    }

    [Fact]
    public async Task UnavailablePublicProcessorCannotAcknowledgeNewAdmission()
    {
        using var rig = new Rig { ProcessorReady = false };
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.AdmitAsync(rig.Actor, rig.Job.OrganizationId,
            new(rig.RequestId, rig.Job.Source.TargetUrl), default));
        Assert.Equal("CONTROL_PUBLIC_WEBSITE_PROCESSOR_UNAVAILABLE", fault.Code);
        Assert.Equal(503, fault.Status); Assert.Equal(0, rig.Creates); Assert.Equal(2, rig.Memberships);
    }

    [Fact]
    public async Task MatchingAdmittedRequestReconcilesWhileProcessorIsOffline()
    {
        using var rig = new Rig { ExistingRequest = true, ProcessorReady = false, State = "queued" };
        var result = await rig.Service.AdmitAsync(rig.Actor, rig.Job.OrganizationId, new(rig.RequestId, rig.Job.Source.TargetUrl), default);
        Assert.Equal(rig.Job.JobId, result.JobId); Assert.Equal(0, rig.Creates); Assert.Equal(0, rig.ReadinessReads);
        Assert.Equal(2, rig.Memberships); Assert.Equal(0, rig.OriginalReads);
    }

    [Theory]
    [InlineData("creator")]
    [InlineData("source")]
    public async Task ExistingRequestCannotReconcileAnotherCreatorOrSource(string scenario)
    {
        using var rig = new Rig { ExistingRequest = true, ProcessorReady = false };
        rig.SummaryChange = row =>
        {
            if (scenario == "creator") row["created_by"] = Guid.NewGuid();
            else { row["target_url"] = "https://example.com/"; row["allowed_host"] = "example.com"; }
        };
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.AdmitAsync(rig.Actor, rig.Job.OrganizationId,
            new(rig.RequestId, rig.Job.Source.TargetUrl), default));
        Assert.Equal(409, fault.Status); Assert.Equal("CONTROL_REQUEST_CONFLICT", fault.Code);
        Assert.Equal(0, rig.Creates); Assert.Equal(0, rig.StatusReads);
    }

    [Fact]
    public async Task MissingWorkspaceDeniesBeforePublicDataRead()
    {
        using var rig = new Rig { Visible = false };
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.StatusAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, default));
        Assert.Equal(404, fault.Status); Assert.Equal("CONTROL_WORKSPACE_NOT_FOUND", fault.Code);
        Assert.Equal(1, rig.Memberships); Assert.Equal(0, rig.StatusReads); Assert.Equal(0, rig.OriginalReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentDenialWinsOverHeldStatusAndMalformedProviderData(bool malformed)
    {
        using var rig = new Rig();
        rig.AfterStatus = () => rig.Visible = false;
        if (malformed) rig.StatusChange = row => row["lease_token"] = "PRIVATE_CAPABILITY_CANARY";
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.StatusAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, default));
        Assert.Equal("CONTROL_WORKSPACE_NOT_FOUND", fault.Code); Assert.Equal(404, fault.Status);
        Assert.Equal(2, rig.Memberships); Assert.DoesNotContain("PRIVATE_CAPABILITY_CANARY", fault.Message);
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("scan")]
    [InlineData("source")]
    [InlineData("extra")]
    [InlineData("generation-type")]
    [InlineData("missing-producer")]
    public async Task StrictStatusCannotReleaseForeignMalformedOrUnboundMetadata(string scenario)
    {
        using var rig = new Rig();
        rig.StatusChange = row =>
        {
            switch (scenario)
            {
                case "organization": row["organization_id"] = Guid.NewGuid(); break;
                case "scan": row["id"] = Guid.NewGuid(); break;
                case "source": row["allowed_host"] = "different.example"; break;
                case "extra": row["lease_token"] = "PRIVATE_CAPABILITY_CANARY"; break;
                case "generation-type": row["generation"] = "1"; break;
                case "missing-producer": row["producer_id"] = null; break;
            }
        };
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.StatusAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, default));
        Assert.Equal(503, fault.Status); Assert.Equal(2, rig.Memberships); Assert.Equal(0, rig.OriginalReads);
        Assert.DoesNotContain("PRIVATE_CAPABILITY_CANARY", fault.Message);
    }

    [Fact]
    public async Task RecentListValidatesHiddenLookaheadBeforeReturningAnyRows()
    {
        using var rig = new Rig { ListCount = 21 };
        var result = await rig.Service.ListAsync(rig.Actor, rig.Job.OrganizationId, default);
        Assert.Equal(20, result.Items.Length); Assert.True(result.HasMore); Assert.Equal(0, rig.OriginalReads);
        rig.HideForeignLookahead = true;
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.ListAsync(rig.Actor, rig.Job.OrganizationId, default));
        Assert.Equal(503, fault.Status); Assert.Equal(4, rig.Memberships);
    }

    [Fact]
    public async Task EscapedSourceUrlsRespectActualListReplyBytesAndReportOmittedRows()
    {
        using var rig = new Rig { ListCount = 20, LargeListUrls = true };
        var result = await rig.Service.ListAsync(rig.Actor, rig.Job.OrganizationId, default);
        Assert.InRange(result.Items.Length, 1, 19); Assert.True(result.HasMore);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(result, PortalJson.Options).Length <= 131072);
        Assert.All(result.Items, item => Assert.Equal(rig.Job.OrganizationId, item.OrganizationId));
    }

    [Fact]
    public async Task VerifiedNodePageRetainsActualSemanticsAndLeavesTaskOutcomeUnassessed()
    {
        using var rig = new Rig();
        var result = await rig.Service.NodesAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, 1, 0, 100, null, default);
        Assert.Equal("Actual exposed name", Assert.Single(result.Nodes).Name);
        Assert.Equal("actual-document", result.DocumentId); Assert.Equal(rig.Digest, result.OriginalSha256);
        Assert.Equal("PageObservationsOnly", result.Scope); Assert.Equal("NotAssessed", result.GoalVerification);
        Assert.Equal(0, result.DispatchedInputs); Assert.Equal(2, result.Captures); Assert.Null(result.NextOffset);
        Assert.Equal(2, rig.StatusReads); Assert.Equal(2, rig.Memberships); Assert.Equal(1, rig.OriginalReads);
    }

    [Fact]
    public async Task OriginalBytePagesAndExactEofRetainHashesAndReauthorizeEachRead()
    {
        using var rig = new Rig();
        rig.SetText(rig.Text.PadRight(131089, ' '));
        var original = Encoding.UTF8.GetBytes(rig.Text); var combined = new List<byte>();
        foreach (var offset in new[] { 0, 65536, 131072, 131089 })
        {
            var page = await rig.Service.OriginalAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, offset, rig.Digest, default);
            Assert.Equal(offset, page.Offset); Assert.Equal(original.Length, page.TotalBytes); Assert.Equal(rig.Digest, page.OriginalSha256);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(page.Data)), page.PageSha256);
            Assert.Equal(original.Skip(offset).Take(Math.Min(65536, original.Length - offset)), page.Data);
            Assert.Equal(offset + page.Data.Length < original.Length ? offset + page.Data.Length : (int?)null, page.NextOffset);
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(page, PortalJson.Options).Length <= 131072);
            combined.AddRange(page.Data);
        }
        Assert.Equal(original, combined); Assert.Equal(8, rig.Memberships); Assert.Equal(8, rig.StatusReads);
    }

    [Theory]
    [InlineData(2_097_169)]
    [InlineData(4_194_304)]
    public async Task LargeProviderOriginalRetainsBoundedPagesAndFreshAuthorizationAtExactEof(int size)
    {
        using var rig = new Rig(); rig.SetText(rig.Text.PadRight(size, ' '));
        var offset = ((size - 1) / 65536) * 65536;
        var page = await rig.Service.OriginalAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, offset, rig.Digest, default);
        Assert.Equal(size, page.TotalBytes); Assert.Equal(offset, page.Offset);
        Assert.Equal(Encoding.UTF8.GetBytes(rig.Text[offset..]), page.Data);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(page.Data)), page.PageSha256);
        Assert.Null(page.NextOffset); Assert.Equal(rig.Digest, page.OriginalSha256);
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(page, PortalJson.Options).Length, 1, 131072);
        var eof = await rig.Service.OriginalAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, size, rig.Digest, default);
        Assert.Empty(eof.Data); Assert.Null(eof.NextOffset); Assert.Equal(size, eof.TotalBytes);
        Assert.Equal(4, rig.Memberships); Assert.Equal(4, rig.StatusReads); Assert.Equal(2, rig.OriginalReads);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("producer")]
    [InlineData("goal")]
    [InlineData("late-denial")]
    public async Task ChangedInvalidOrLateDeniedOriginalCannotReleaseNodes(string scenario)
    {
        using var rig = new Rig();
        if (scenario == "hash") rig.Text += " ";
        if (scenario == "producer") rig.AfterOriginal = () => rig.Producer = Guid.NewGuid();
        if (scenario == "goal") rig.SetText(rig.Text.Replace("\"GoalVerified\":false", "\"GoalVerified\":true", StringComparison.Ordinal));
        if (scenario == "late-denial") rig.AfterOriginal = () => rig.Visible = false;
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.NodesAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId, 0, 0, 1, null, default));
        Assert.Equal(scenario == "late-denial" ? 404 : 409, fault.Status);
        Assert.Equal(2, rig.Memberships); Assert.Equal(2, rig.StatusReads);
    }

    [Theory]
    [InlineData(-1, 0, 1)]
    [InlineData(2, 0, 1)]
    [InlineData(0, -1, 1)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, 101)]
    [InlineData(0, 1, 1)]
    public async Task InvalidNodePageSelectorsDenyBeforeProviderReads(int capture, int offset, int limit)
    {
        using var rig = new Rig();
        var fault = await Assert.ThrowsAsync<PortalFault>(() => rig.Service.NodesAsync(rig.Actor, rig.Job.OrganizationId, rig.Job.ScanId,
            capture, offset, limit, null, default));
        Assert.Equal(400, fault.Status); Assert.Equal(0, rig.Memberships); Assert.Equal(0, rig.OriginalReads);
    }

    internal sealed class Rig : HttpMessageHandler
    {
        internal readonly PublicWebsiteJob Job = PublicWebsiteObservationTests.Job();
        internal readonly PortalIdentity Actor = new(PortalProjectClientTests.Actor, "reader@example.invalid", PortalProjectClientTests.Bearer);
        internal readonly Guid RequestId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        internal Guid Producer = PublicWebsiteObservationTests.Worker;
        internal string Text, Digest, State = "completed";
        internal bool Visible = true, ProcessorReady = true, ExistingRequest, HideForeignLookahead, LargeListUrls, RejectCreateQuota;
        internal int Memberships, StatusReads, OriginalReads, Creates, ReadinessReads, ListCount = 1;
        internal Action? AfterStatus, AfterOriginal;
        internal Action<Dictionary<string, object?>>? StatusChange, SummaryChange;
        private readonly HttpClient _http;
        internal readonly SupabasePortalClient Project;
        internal readonly PortalPublicWebsiteService Service;
        internal Rig()
        {
            var stored = PublicWebsiteObservationCodec.Encode(PublicWebsiteObservationTests.Observation(), Job, Producer);
            Text = stored.Text; Digest = stored.Sha256;
            _http = new(this, false); Project = new(_http, PortalProjectClientTests.Key);
            Service = new(Project, Project, () => { ReadinessReads++; return Task.FromResult(ProcessorReady); });
        }
        internal void SetText(string text) { Text = text; Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))); }
        private Dictionary<string, object?> Summary(int index = 0)
        {
            var source = LargeListUrls ? PublicReaderSource.FromUrl("https://example.com/?" + new string('&', 1900)) : Job.Source;
            var row = new Dictionary<string, object?>
            {
                ["organization_id"] = HideForeignLookahead && index == 20 ? Guid.NewGuid() : Job.OrganizationId,
                ["id"] = index == 0 ? Job.ScanId : Guid.Parse($"77777777-7777-7777-7777-{index:D12}"),
                ["request_id"] = RequestId,
                ["created_by"] = Actor.UserId,
                ["target_url"] = source.TargetUrl,
                ["allowed_host"] = source.AllowedHost,
                ["reader_profile"] = source.Profile,
                ["state"] = State == "completed" ? "observed" : State,
                ["created_at"] = DateTimeOffset.Parse("2026-11-02T17:00:00Z").AddMinutes(-index)
            };
            SummaryChange?.Invoke(row); return row;
        }
        private Dictionary<string, object?> Status()
        {
            var row = Summary(); row["schema"] = "hsr-public-scan-status-v1"; row["state"] = State;
            row["job_id"] = Job.JobId; row["generation"] = State == "queued" ? 0 : 1;
            row["producer_id"] = State == "completed" ? Producer : null; row["result_digest"] = State == "completed" ? Digest : null;
            row["last_error_code"] = null; row["worker_permission"] = null;
            StatusChange?.Invoke(row); return row;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("https://gpnijaxrgtwzwtrqsbcu.supabase.co", request.RequestUri!.GetLeftPart(UriPartial.Authority));
            Assert.Equal(Actor.Token, request.Headers.Authorization?.Parameter);
            Assert.Equal(PortalProjectClientTests.Key, Assert.Single(request.Headers.GetValues("apikey")));
            object? body;
            switch (request.RequestUri.AbsolutePath)
            {
                case "/auth/v1/user": body = JsonSerializer.Deserialize<object>(PortalProjectClientTests.UserJson); break;
                case "/rest/v1/organizations":
                    Memberships++; body = Visible ? new object[] { new { id = Job.OrganizationId, name = "Caller workspace" } } : []; break;
                case "/rest/v1/public_scans":
                    Assert.Contains("organization_id=eq." + Job.OrganizationId.ToString("D"), request.RequestUri.Query);
                    body = request.RequestUri.Query.Contains("request_id=", StringComparison.Ordinal)
                        ? ExistingRequest ? new[] { Summary() } : [] : Enumerable.Range(0, ListCount).Select(Summary).ToArray(); break;
                case "/rest/v1/rpc/create_public_scan":
                    Creates++;
                    if (RejectCreateQuota) return new(HttpStatusCode.BadRequest)
                    { Content = new StringContent("{\"code\":\"54000\",\"message\":\"PRIVATE_PROVIDER_DETAIL\"}", Encoding.UTF8, "application/json") };
                    using (var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)))
                    {
                        Assert.Equal(Job.OrganizationId, json.RootElement.GetProperty("p_organization_id").GetGuid());
                        Assert.Equal(RequestId, json.RootElement.GetProperty("p_request_id").GetGuid());
                        Assert.Equal(Job.Source.TargetUrl, json.RootElement.GetProperty("p_target_url").GetString());
                        Assert.Equal(Job.Source.AllowedHost, json.RootElement.GetProperty("p_allowed_host").GetString());
                        Assert.Equal(4, json.RootElement.EnumerateObject().Count());
                    }
                    body = new[] { new { scan_id = Job.ScanId, job_id = Job.JobId } }; break;
                case "/rest/v1/rpc/read_public_scan_status":
                    StatusReads++; body = Status(); AfterStatus?.Invoke(); break;
                case "/rest/v1/public_scan_jobs":
                    OriginalReads++;
                    Assert.Contains("id=eq." + Job.JobId.ToString("D"), request.RequestUri.Query);
                    body = new[] { new { organization_id = Job.OrganizationId, scan_id = Job.ScanId, id = Job.JobId,
                        generation = Job.Generation, state = "completed", result_worker_id = Producer, result_digest = Digest, result_text = Text } };
                    AfterOriginal?.Invoke(); break;
                default: throw new InvalidOperationException("Unexpected fixed-project operation");
            }
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body,
                new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), Encoding.UTF8, "application/json")
            };
        }
        protected override void Dispose(bool disposing) { if (disposing) _http.Dispose(); base.Dispose(disposing); }
    }
}
