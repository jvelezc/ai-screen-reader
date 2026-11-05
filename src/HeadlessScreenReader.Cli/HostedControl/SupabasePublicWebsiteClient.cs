using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

internal sealed partial class SupabasePortalClient
{
    private const string PublicSummarySelect = "organization_id,id,request_id,created_by,target_url,allowed_host,reader_profile,state,created_at";
    private static string PublicSummaryPath(Guid organization) => "rest/v1/public_scans?select=" + PublicSummarySelect
        + "&organization_id=eq." + organization.ToString("D");

    public async Task<PortalPublicWebsiteCreatedResponse> CreatePublicWebsiteAsync(PortalIdentity actor, Guid organization,
        Guid requestId, PublicReaderSource source, CancellationToken token)
    {
        source.Validate();
        using var request = Request(HttpMethod.Post, "rest/v1/rpc/create_public_scan", actor.Token);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        { p_organization_id = organization, p_request_id = requestId, p_target_url = source.TargetUrl, p_allowed_host = source.AllowedHost }), Encoding.UTF8, "application/json");
        using var json = await SendAsync(request, false, token, publicAdmission: true).ConfigureAwait(false);
        var rows = PublicRows(json.RootElement, 1);
        if (rows.Length != 1) throw PortalPublicWebsiteContract.Invalid();
        var row = rows[0]; PublicFields(row, ["scan_id", "job_id"]);
        return new(organization, Id(row, "scan_id"), Id(row, "job_id"), requestId, source.TargetUrl, source.AllowedHost, source.Profile);
    }

    public async Task<PortalPublicWebsiteSummaryResponse[]> PublicWebsitesAsync(PortalIdentity actor, Guid organization, CancellationToken token)
    {
        using var request = Request(HttpMethod.Get, PublicSummaryPath(organization) + "&order=created_at.desc,id.desc&limit=21", actor.Token);
        using var json = await SendAsync(request, false, token).ConfigureAwait(false);
        var rows = PublicRows(json.RootElement, PortalPublicWebsiteContract.ListSize + 1);
        if (rows.Length > PortalPublicWebsiteContract.ListSize + 1) throw PortalPublicWebsiteContract.Invalid();
        var values = rows.Select(row => PublicSummary(row, organization)).ToArray();
        for (var index = 1; index < values.Length; index++)
            if (values[index].CreatedAt > values[index - 1].CreatedAt || values[index].CreatedAt == values[index - 1].CreatedAt
                && string.CompareOrdinal(values[index].Id.ToString("D"), values[index - 1].Id.ToString("D")) >= 0)
                throw PortalPublicWebsiteContract.Invalid();
        return values;
    }

    public async Task<PortalPublicWebsiteSummaryResponse?> PublicWebsiteRequestAsync(PortalIdentity actor, Guid organization,
        Guid requestId, CancellationToken token)
    {
        using var request = Request(HttpMethod.Get, PublicSummaryPath(organization) + "&request_id=eq." + requestId.ToString("D") + "&limit=2", actor.Token);
        using var json = await SendAsync(request, false, token).ConfigureAwait(false);
        var rows = PublicRows(json.RootElement, 1);
        if (rows.Length > 1) throw PortalPublicWebsiteContract.Invalid();
        if (rows.Length == 0) return null;
        var value = PublicSummary(rows[0], organization);
        if (value.RequestId != requestId) throw PortalPublicWebsiteContract.Invalid();
        return value;
    }

    public async Task<PortalPublicWebsiteStatusResponse?> PublicWebsiteStatusAsync(PortalIdentity actor, Guid organization,
        Guid scan, CancellationToken token)
    {
        using var request = Request(HttpMethod.Post, "rest/v1/rpc/read_public_scan_status", actor.Token);
        request.Content = new StringContent(JsonSerializer.Serialize(new { p_organization_id = organization, p_scan_id = scan }), Encoding.UTF8, "application/json");
        using var json = await SendAsync(request, false, token).ConfigureAwait(false);
        var row = json.RootElement;
        if (row.ValueKind == JsonValueKind.Null) return null;
        PublicFields(row, ["schema", "organization_id", "id", "request_id", "created_by", "target_url", "allowed_host", "reader_profile",
            "state", "created_at", "job_id", "generation", "producer_id", "result_digest", "last_error_code", "worker_permission"]);
        if (Text(row, "schema", 64) != "hsr-public-scan-status-v1") throw PortalPublicWebsiteContract.Invalid();
        ScanWorkerPermissionSnapshot? permission = null;
        var item = row.GetProperty("worker_permission");
        if (item.ValueKind != JsonValueKind.Null)
        {
            PublicFields(item, ["organization_id", "scan_id", "job_id", "generation", "status", "checked_at", "expires_at"]);
            permission = new(ScanWorkerPermissionSnapshot.CurrentSchema, Id(item, "organization_id"), Id(item, "scan_id"),
                Id(item, "job_id"), PublicGeneration(item), Text(item, "status", 16), PublicDate(item, "checked_at"),
                item.GetProperty("expires_at").ValueKind == JsonValueKind.Null ? null : PublicDate(item, "expires_at"));
        }
        var value = new PortalPublicWebsiteStatusResponse(Id(row, "organization_id"), Id(row, "id"), Id(row, "request_id"), Id(row, "created_by"),
            Text(row, "target_url", 2048), Text(row, "allowed_host", 253), Text(row, "reader_profile", 64), Text(row, "state", 16),
            PublicDate(row, "created_at"), Id(row, "job_id"), PublicGeneration(row),
            row.GetProperty("producer_id").ValueKind == JsonValueKind.Null ? null : Id(row, "producer_id"),
            PublicNullable(row, "result_digest", 64), PublicNullable(row, "last_error_code", 128), permission);
        value.Validate(organization, scan); return value;
    }

    public async Task<PortalPublicWebsiteStoredResult?> PublicWebsiteOriginalAsync(PortalIdentity actor,
        PortalPublicWebsiteStatusResponse expected, CancellationToken token)
    {
        using var request = Request(HttpMethod.Get, "rest/v1/public_scan_jobs?select=organization_id,scan_id,id,generation,state,result_worker_id,result_digest,result_text"
            + "&organization_id=eq." + expected.OrganizationId.ToString("D") + "&scan_id=eq." + expected.Id.ToString("D")
            + "&id=eq." + expected.JobId.ToString("D") + "&limit=2", actor.Token);
        // Only this exact original read has a larger success budget. Auth and
        // error bodies retain the existing128KiB ceiling.
        using var json = await SendAsync(request, false, token, maximum: 3 * PublicWebsiteObservation.MaximumSupportedBytes + 131072).ConfigureAwait(false);
        var rows = PublicRows(json.RootElement, 1);
        if (rows.Length > 1) throw PortalPublicWebsiteContract.Invalid();
        if (rows.Length == 0) return null;
        var row = rows[0];
        PublicFields(row, ["organization_id", "scan_id", "id", "generation", "state", "result_worker_id", "result_digest", "result_text"]);
        if (Text(row, "state", 16) != "completed") throw new PortalFault(409, "CONTROL_PUBLIC_WEBSITE_ORIGINAL_CHANGED");
        var value = new PortalPublicWebsiteStoredResult(Id(row, "organization_id"), Id(row, "scan_id"), Id(row, "id"), PublicGeneration(row),
            Id(row, "result_worker_id"), Text(row, "result_digest", 64), Text(row, "result_text", PublicWebsiteObservation.MaximumSupportedBytes));
        if (value.OrganizationId != expected.OrganizationId || value.ScanId != expected.Id || value.JobId != expected.JobId
            || value.Generation != expected.Generation || value.ProducerId != expected.ProducerId || value.Sha256 != expected.OriginalSha256)
            throw new PortalFault(409, "CONTROL_PUBLIC_WEBSITE_ORIGINAL_CHANGED");
        return value;
    }

    private static PortalPublicWebsiteSummaryResponse PublicSummary(JsonElement row, Guid organization)
    {
        PublicFields(row, PublicSummarySelect.Split(','));
        var value = new PortalPublicWebsiteSummaryResponse(Id(row, "organization_id"), Id(row, "id"), Id(row, "request_id"), Id(row, "created_by"),
            Text(row, "target_url", 2048), Text(row, "allowed_host", 253), Text(row, "reader_profile", 64), Text(row, "state", 16), PublicDate(row, "created_at"));
        value.Validate(organization); return value;
    }
    private static void PublicFields(JsonElement row, string[] fields)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(fields.Order(StringComparer.Ordinal), StringComparer.Ordinal)) throw PortalPublicWebsiteContract.Invalid();
    }
    private static JsonElement[] PublicRows(JsonElement root, int maximum)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > maximum) throw PortalPublicWebsiteContract.Invalid();
        return root.EnumerateArray().ToArray();
    }
    private static DateTimeOffset PublicDate(JsonElement row, string field) => row.GetProperty(field) is { ValueKind: JsonValueKind.String } value
        && value.TryGetDateTimeOffset(out var time) && time != default ? time : throw PortalPublicWebsiteContract.Invalid();
    private static long PublicGeneration(JsonElement row) => row.GetProperty("generation") is { ValueKind: JsonValueKind.Number } value
        && value.TryGetInt64(out var generation) ? generation : throw PortalPublicWebsiteContract.Invalid();
    private static string? PublicNullable(JsonElement row, string field, int maximum) => row.GetProperty(field).ValueKind == JsonValueKind.Null
        ? null : Text(row, field, maximum);
}
