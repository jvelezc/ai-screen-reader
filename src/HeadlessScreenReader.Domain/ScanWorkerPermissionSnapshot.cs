namespace HeadlessScreenReader.Domain;

/// <summary>A scoped database-time permission observation. It grants no input, replay or completion authority.</summary>
public sealed record ScanWorkerPermissionSnapshot(string Schema, Guid OrganizationId, Guid ScanId, Guid JobId,
    long Generation, string Status, DateTimeOffset CheckedAt, DateTimeOffset? ExpiresAt)
{
    public const string CurrentSchema = "hsr-scan-worker-permission-v1";

    public void Validate(Guid organization, Guid scan)
    {
        var valid = Schema == CurrentSchema && organization != Guid.Empty && scan != Guid.Empty
            && OrganizationId == organization && ScanId == scan && JobId != Guid.Empty
            && Generation is >= 1 and <= 3 && CheckedAt != default
            && (ExpiresAt is null || ExpiresAt.Value != default)
            && (Status switch
            {
                "Active" => ExpiresAt is not null && ExpiresAt.Value > CheckedAt,
                "Expired" => ExpiresAt is not null && ExpiresAt.Value <= CheckedAt,
                "Missing" => ExpiresAt is null,
                _ => false
            });
        if (!valid) throw new BrowserOperationException("SCAN_WORKER_PERMISSION_INVALID");
    }
}
