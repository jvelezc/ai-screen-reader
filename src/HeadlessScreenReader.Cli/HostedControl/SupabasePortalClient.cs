using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

internal interface IPortalProjectClient
{
    Task<PortalIdentity> AuthenticateAsync(string bearer, CancellationToken token);
    Task<PortalWorkspaceBootstrap> EnsureWorkspaceAsync(PortalIdentity actor, CancellationToken token);
    Task<PortalWorkspaceResponse?> WorkspaceAsync(PortalIdentity actor, Guid organization, CancellationToken token);
    Task<PortalScanResponse[]> ScansAsync(PortalIdentity actor, Guid organization, CancellationToken token);
    Task<PortalScanCreatedResponse> CreateScanAsync(PortalIdentity actor, Guid organization, Guid request, CancellationToken token);
    Task<PortalScanCreatedResponse> CreateRecordedObservationAsync(PortalIdentity actor, Guid organization, Guid request, CancellationToken token) =>
        throw new PortalFault(503, "CONTROL_RECORDING_ADMISSION_UNAVAILABLE");
    Task<PortalScanCreatedResponse> CreateRecordedTraversalAsync(PortalIdentity actor, Guid organization, Guid request, int route, CancellationToken token) =>
        throw new PortalFault(503, "CONTROL_TRAVERSAL_ADMISSION_UNAVAILABLE");
    Task<PortalScanResponse?> ScanAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token);
    Task<PortalJobResult?> JobAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token);
}

/// <summary>Fixed-project Auth verification and RLS requests using the same original user bearer.</summary>
internal sealed class SupabasePortalClient(HttpClient http, string publishableKey) : IPortalProjectClient
{
    public async Task<PortalIdentity> AuthenticateAsync(string bearer, CancellationToken token)
    {
        if (bearer.Length is < 10 or > 8192 || !Regex.IsMatch(bearer, "\\A[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\z"))
            throw new PortalFault(401, "CONTROL_AUTHENTICATION_REQUIRED");
        using var request = Request(HttpMethod.Get, "auth/v1/user", bearer, false);
        using var json = await SendAsync(request, true, token).ConfigureAwait(false);
        var user = json.RootElement;
        var id = Id(user, "id");
        var email = Text(user, "email", 320);
        if (!user.TryGetProperty("email_confirmed_at", out var confirmed)
            || confirmed.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(confirmed.GetString(), out _)
            || user.TryGetProperty("is_anonymous", out var anonymous) && anonymous.ValueKind != JsonValueKind.False)
            throw new PortalFault(403, "CONTROL_EMAIL_CONFIRMATION_REQUIRED");
        return new(id, email, bearer);
    }

    public async Task<PortalWorkspaceBootstrap> EnsureWorkspaceAsync(PortalIdentity actor, CancellationToken token)
    {
        using var request = Request(HttpMethod.Post, "rest/v1/rpc/ensure_personal_workspace", actor.Token);
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var json = await SendAsync(request, false, token).ConfigureAwait(false);
        var row = Single(json.RootElement);
        if (!row.TryGetProperty("created", out var created) || created.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw InvalidResponse();
        return new(Id(row, "organization_id"), created.GetBoolean());
    }

    public async Task<PortalWorkspaceResponse?> WorkspaceAsync(PortalIdentity actor, Guid organization, CancellationToken token)
    {
        using var request = Request(HttpMethod.Get, "rest/v1/organizations?select=id,name&id=eq." + organization.ToString("D") + "&limit=2", actor.Token);
        using var json = await SendAsync(request, false, token).ConfigureAwait(false);
        if (Rows(json.RootElement).Length == 0) return null;
        var row = Single(json.RootElement);
        var id = Id(row, "id");
        if (id != organization) throw InvalidResponse();
        return new(id, Text(row, "name", 120));
    }

    public async Task<PortalScanResponse[]> ScansAsync(PortalIdentity actor, Guid organization, CancellationToken token)
    {
        using var request = Request(HttpMethod.Get, ScanPath(organization) + "&order=created_at.desc,id.desc&limit=51", actor.Token);
        using var json = await SendAsync(request, false, token).ConfigureAwait(false);
        var rows = Rows(json.RootElement);
        if (rows.Length > 51) throw InvalidResponse();
        return rows.Select(row => Scan(row, organization)).ToArray();
    }

    public async Task<PortalScanCreatedResponse> CreateScanAsync(PortalIdentity actor, Guid organization, Guid requestId, CancellationToken token)
    {
        using var request = Request(HttpMethod.Post, "rest/v1/rpc/create_scan", actor.Token);
        var body = new
        {
            p_organization_id = organization,
            p_request_id = requestId,
            p_fixture = "synthetic-shop",
            p_reader_profile = FixtureObservation.Profile
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        using var json = await SendAsync(request, false, token).ConfigureAwait(false);
        var row = Single(json.RootElement);
        return new(organization, Id(row, "scan_id"), Id(row, "job_id"));
    }

    public async Task<PortalScanCreatedResponse> CreateRecordedObservationAsync(PortalIdentity actor, Guid organization, Guid requestId, CancellationToken token)
    {
        using var request = Request(HttpMethod.Post, "rest/v1/rpc/create_recorded_observation", actor.Token);
        request.Content = new StringContent(JsonSerializer.Serialize(new { p_organization_id = organization, p_request_id = requestId }),
            System.Text.Encoding.UTF8, "application/json");
        using var json = await SendAsync(request, false, token).ConfigureAwait(false);
        var row = Single(json.RootElement);
        return new(organization, Id(row, "scan_id"), Id(row, "job_id"));
    }

    public async Task<PortalScanCreatedResponse> CreateRecordedTraversalAsync(PortalIdentity actor, Guid organization, Guid requestId, int route, CancellationToken token)
    {
        if (route is < 1 or > 4) throw new PortalFault(400, "CONTROL_TRAVERSAL_ROUTE_INVALID");
        using var request = Request(HttpMethod.Post, "rest/v1/rpc/create_recorded_traversal", actor.Token);
        request.Content = new StringContent(JsonSerializer.Serialize(new { p_organization_id = organization, p_request_id = requestId, p_route = route }),
            System.Text.Encoding.UTF8, "application/json");
        using var json = await SendAsync(request, false, token).ConfigureAwait(false);
        var row = Single(json.RootElement); return new(organization, Id(row, "scan_id"), Id(row, "job_id"));
    }

    public async Task<PortalScanResponse?> ScanAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
    {
        using var request = Request(HttpMethod.Get, ScanPath(organization) + "&id=eq." + scan.ToString("D") + "&limit=2", actor.Token);
        using var json = await SendAsync(request, false, token).ConfigureAwait(false);
        if (Rows(json.RootElement).Length == 0) return null;
        var result = Scan(Single(json.RootElement), organization);
        if (result.Id != scan) throw InvalidResponse();
        return result;
    }

    public async Task<PortalJobResult?> JobAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
    {
        var path = "rest/v1/scan_jobs?select=organization_id,scan_id,id,state,generation,result_digest,result_reference,last_error_code,archive_index_sha256,archive_manifest_sha256,archive_bytes,archive_worker_id,record_observation,traversal_route"
            + "&organization_id=eq." + organization.ToString("D") + "&scan_id=eq." + scan.ToString("D")
            + "&stage=eq.observe-fixture&limit=2";
        using var request = Request(HttpMethod.Get, path, actor.Token);
        using var json = await SendAsync(request, false, token).ConfigureAwait(false);
        if (Rows(json.RootElement).Length == 0) return null;
        var row = Single(json.RootElement);
        if (!row.TryGetProperty("generation", out var generation) || generation.ValueKind != JsonValueKind.Number
            || !generation.TryGetInt64(out var generationValue))
            throw InvalidResponse();
        var result = new PortalJobResult(Id(row, "organization_id"), Id(row, "scan_id"), Id(row, "id"),
            Text(row, "state", 20), generationValue, Optional(row, "result_digest", 64),
            Optional(row, "result_reference", 200), Optional(row, "last_error_code", 100));
        if (row.TryGetProperty("record_observation", out var mode))
        {
            if (mode.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw InvalidResponse();
            result = result with { RecordObservation = mode.GetBoolean() };
        }
        if (row.TryGetProperty("traversal_route", out var route))
        {
            if (route.ValueKind != JsonValueKind.Number || !route.TryGetInt32(out var number) || number is < 0 or > 4
                || number > 0 && !result.RecordObservation) throw InvalidResponse();
            result = result with { TraversalRoute = number };
        }
        if (result.OrganizationId != organization || result.ScanId != scan || result.Generation is < 0 or > 3
            || result.State is not ("queued" or "leased" or "completed" or "blocked")
            || result.ErrorCode is not null && !Regex.IsMatch(result.ErrorCode, "\\A[A-Z][A-Z0-9_]{2,99}\\z"))
            throw InvalidResponse();
        if (result.State == "completed")
        {
            var expected = new FixtureJob(organization, scan, result.JobId, result.Generation);
            if (result.Generation == 0 || result.Reference != expected.Reference || result.Digest is null
                || !Regex.IsMatch(result.Digest, "\\A[a-f0-9]{64}\\z")) throw InvalidResponse();
        }
        else if (result.Digest is not null || result.Reference is not null) throw InvalidResponse();
        var indexHash = Optional(row, "archive_index_sha256", 64);
        var manifestHash = Optional(row, "archive_manifest_sha256", 64);
        var workerId = Optional(row, "archive_worker_id", 36);
        var hasBytes = row.TryGetProperty("archive_bytes", out var archiveBytes) && archiveBytes.ValueKind != JsonValueKind.Null;
        if (indexHash is not null || manifestHash is not null || workerId is not null || hasBytes)
        {
            if (result.State != "completed" || indexHash is null || manifestHash is null
                || !Guid.TryParse(workerId, out var producer) || !hasBytes || !archiveBytes.TryGetInt64(out var length)) throw InvalidResponse();
            var expected = new FixtureJob(organization, scan, result.JobId, result.Generation) { RecordObservation = result.RecordObservation, TraversalRoute = result.TraversalRoute };
            var receipt = new WorkerEvidenceArchive(expected, producer,
                new(new(organization, result.JobId, indexHash), manifestHash, result.TraversalRoute > 0 ? 5 : result.RecordObservation ? 4 : 2, length, true, true))
            {
                Recording = result.TraversalRoute > 0 ? "KeyboardTraversal" : result.RecordObservation ? "PassiveObservation" : "NotCollected",
                Origin = result.TraversalRoute > 0 ? "QueueWorkerTraversal" : "QueueWorkerObservation"
            };
            try { receipt.Validate(expected); }
            catch (BrowserOperationException) { throw InvalidResponse(); }
            result = result with { WorkerArchive = receipt };
        }
        return result;
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string bearer, bool database = true)
    {
        var request = new HttpRequestMessage(method, new Uri(PortalOptions.Project, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.Add("apikey", publishableKey);
        if (database) request.Headers.Add(method == HttpMethod.Get ? "Accept-Profile" : "Content-Profile", "hsr");
        return request;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, bool authentication, CancellationToken token)
    {
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized)
                throw new PortalFault(401, "CONTROL_AUTHENTICATION_REQUIRED");
            if (response.StatusCode is HttpStatusCode.Forbidden)
                throw new PortalFault(authentication ? 401 : 403,
                    authentication ? "CONTROL_AUTHENTICATION_REQUIRED" : "CONTROL_WORKSPACE_ACCESS_DENIED");
            if (!response.IsSuccessStatusCode)
            {
                // Classify only an allowlisted SQL code; never reflect provider messages/hints.
                if (!authentication && response.StatusCode == HttpStatusCode.BadRequest)
                {
                    using var error = await ReadAsync(response, token).ConfigureAwait(false);
                    if (error.RootElement.ValueKind == JsonValueKind.Object && error.RootElement.TryGetProperty("code", out var code)
                        && code.ValueKind == JsonValueKind.String && code.GetString() == "22023")
                        throw new PortalFault(409, "CONTROL_REQUEST_CONFLICT");
                }
                throw new PortalFault(503, authentication ? "CONTROL_AUTH_PROVIDER_UNAVAILABLE" : "CONTROL_DATABASE_UNAVAILABLE");
            }
            return await ReadAsync(response, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (PortalFault) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or IOException)
        { throw new PortalFault(503, authentication ? "CONTROL_AUTH_PROVIDER_UNAVAILABLE" : "CONTROL_DATABASE_UNAVAILABLE"); }
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken token)
    {
        const int maximum = 131072;
        if (response.Content.Headers.ContentLength is > maximum) throw InvalidResponse();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (bytes.Length + count > maximum) throw InvalidResponse();
            bytes.Write(buffer, 0, count);
        }
        return JsonDocument.Parse(bytes.ToArray(), new() { AllowDuplicateProperties = false, MaxDepth = 32 });
    }

    private static string ScanPath(Guid organization) => "rest/v1/scans?select=organization_id,id,fixture,reader_profile,state,created_at&organization_id=eq." + organization.ToString("D");
    private static PortalScanResponse Scan(JsonElement row, Guid organization)
    {
        if (!row.TryGetProperty("created_at", out var created) || created.ValueKind != JsonValueKind.String
            || !created.TryGetDateTimeOffset(out var createdAt))
            throw InvalidResponse();
        var result = new PortalScanResponse(Id(row, "organization_id"), Id(row, "id"), Text(row, "fixture", 40),
            Text(row, "reader_profile", 64), Text(row, "state", 20), createdAt);
        if (result.OrganizationId != organization || result.Fixture != "synthetic-shop" || result.ReaderProfile != FixtureObservation.Profile
            || result.State is not ("queued" or "leased" or "observed" or "blocked")) throw InvalidResponse();
        return result;
    }
    private static JsonElement[] Rows(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) throw InvalidResponse();
        return root.EnumerateArray().ToArray();
    }
    private static JsonElement Single(JsonElement root)
    {
        var rows = Rows(root);
        if (rows.Length != 1 || rows[0].ValueKind != JsonValueKind.Object) throw InvalidResponse();
        return rows[0];
    }
    private static Guid Id(JsonElement row, string name) => Guid.TryParse(Text(row, name, 36), out var id) && id != Guid.Empty ? id : throw InvalidResponse();
    private static string Text(JsonElement row, string name, int maximum)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String) throw InvalidResponse();
        var value = property.GetString();
        return value is { Length: > 0 } && value.Length <= maximum ? value : throw InvalidResponse();
    }
    private static string? Optional(JsonElement row, string name, int maximum) =>
        row.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Null ? null : Text(row, name, maximum);
    private static PortalFault InvalidResponse() => new(503, "CONTROL_PROVIDER_RESPONSE_UNCONFIRMED");
}
