using System.Collections.Immutable;
using System.Text.Json;
using System.Text;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>Read-only operational assessment under the original user's current workspace authority.</summary>
internal interface IPortalEvidenceReadiness
{
    Task<EvidenceReadinessResponse> AssessAsync(PortalIdentity actor, Guid organization, EvidenceReadinessRequest request, CancellationToken token);
}

internal sealed class PortalEvidenceReadiness(IPortalProjectClient project, TraversalArchive? archive) : IPortalEvidenceReadiness
{
    public const int MaximumRequestBytes = 131_072;
    public const long MaximumArtifactBytes = 25L * 1024 * 1024;

    public async Task<EvidenceReadinessResponse> AssessAsync(PortalIdentity actor, Guid organization, EvidenceReadinessRequest request, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var bounded = deadline.Token;
        async Task Authorize()
        {
            if (organization == Guid.Empty || await project.WorkspaceAsync(actor, organization, bounded).ConfigureAwait(false) is null)
                throw new PortalFault(404, "CONTROL_WORKSPACE_NOT_FOUND");
            bounded.ThrowIfCancellationRequested();
        }
        await Authorize().ConfigureAwait(false);
        if (request is null || request.TargetUrl is null || request.Scan is not null && request.ScanJson is not null
            || request.Example is not null && (request.Scan is not null || request.ScanJson is not null || request.Archive is not null))
            throw new PortalFault(400, "CONTROL_EVIDENCE_REQUEST_INVALID");
        EvidenceScanRecord? scan;
        string target;
        var illustrative = request.Example is not null;
        try
        {
            scan = illustrative ? EvidenceReadinessExamples.Create(request.Example!) : request.Scan;
            if (request.ScanJson is { } raw)
            {
                if (Encoding.UTF8.GetByteCount(raw) > EvidenceReadiness.MaximumInputBytes)
                    throw new PortalFault(413, "CONTROL_EVIDENCE_RECORD_TOO_LARGE");
                scan = JsonSerializer.Deserialize<EvidenceScanRecord>(raw, PortalJson.Options)
                    ?? throw new PortalFault(400, "CONTROL_EVIDENCE_RECORD_INVALID");
            }
            target = EvidenceReadiness.Website(scan?.Target ?? request.TargetUrl);
            if (request.TargetUrl.Length > 0 && EvidenceReadiness.Website(request.TargetUrl) != target)
                throw new PortalFault(409, "CONTROL_EVIDENCE_TARGET_MISMATCH");
            if (scan is not null)
            {
                if (JsonSerializer.SerializeToUtf8Bytes(scan, PortalJson.Options).Length > EvidenceReadiness.MaximumInputBytes)
                    throw new PortalFault(413, "CONTROL_EVIDENCE_RECORD_TOO_LARGE");
                EvidenceReadiness.Validate(scan);
            }
        }
        catch (ArgumentException) { throw new PortalFault(400, "CONTROL_EVIDENCE_RECORD_INVALID"); }
        catch (JsonException) { throw new PortalFault(400, "CONTROL_EVIDENCE_RECORD_INVALID"); }
        if (scan is null)
        {
            if (request.Archive is not null) throw new PortalFault(400, "CONTROL_EVIDENCE_REQUEST_INVALID");
            await Authorize().ConfigureAwait(false);
            return new(EvidenceReadinessResponse.CurrentSchema, organization, "None", "ScanResultsRequired", target, null, null, [],
                "No scan results were supplied for this URL. Import a scanner record; no website was visited and no findings were generated.");
        }
        var proofs = illustrative ? EvidenceReadinessExamples.IllustrativeProofs(scan)
            : scan.Artifacts.Select(a => new EvidenceArtifactProof(a.Id, "Unverified")).ToImmutableArray();
        if (!illustrative && request.Archive is { } reference)
        {
            var identity = new TraversalArchiveIdentity(organization, reference.ArchiveId, reference.IndexSha256);
            try { TraversalArchive.ValidateIdentity(identity); }
            catch (TraversalArchiveException) { throw new PortalFault(400, "CONTROL_ARCHIVE_IDENTITY_INVALID"); }
            var builder = proofs.ToBuilder();
            foreach (var type in EvidenceReadiness.RequiredArtifactTypes)
            {
                var artifact = scan.Artifacts.FirstOrDefault(a => a.Type == type && a.Original);
                if (artifact is null) continue;
                var number = scan.Artifacts.IndexOf(artifact);
                if (archive is null) { builder[number] = new(artifact.Id, "Unavailable"); continue; }
                try
                {
                    var verified = await archive.VerifyOriginalAsync(identity, artifact.Path, artifact.Sha256.ToLowerInvariant(), MaximumArtifactBytes, bounded).ConfigureAwait(false);
                    var header = verified.Header.AsSpan();
                    var videoFormat = type != "Video capture" || header.Length >= 4 && header[..4].SequenceEqual(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 })
                        || header.Length >= 8 && header.Slice(4, 4).SequenceEqual("ftyp"u8);
                    builder[number] = videoFormat ? new(artifact.Id, "HashVerified", verified.Sha256) : new(artifact.Id, "InvalidFormat");
                }
                catch (TraversalArchiveException error)
                {
                    builder[number] = new(artifact.Id, error.Failure switch
                    { TraversalArchiveFailure.Missing => "Missing", TraversalArchiveFailure.Invalid => "Invalid", _ => "Unavailable" });
                }
                await Authorize().ConfigureAwait(false);
            }
            proofs = builder.ToImmutable();
        }
        var assessment = EvidenceReadiness.Assess(scan, proofs, illustrative);
        await Authorize().ConfigureAwait(false);
        var response = new EvidenceReadinessResponse(EvidenceReadinessResponse.CurrentSchema, organization, illustrative ? "Example" : "Imported",
            "Assessed", target, assessment, scan, proofs, illustrative
                ? "Fictional example only. No authentic recordings are supplied and this result cannot authorize a real evidence package."
                : "Eight operational website-evidence conditions evaluated from the supplied record. Original-file integrity is checked separately; no live website scan or legal determination occurred.");
        if (JsonSerializer.SerializeToUtf8Bytes(response, PortalJson.Options).Length > 120_000)
            throw new PortalFault(413, "CONTROL_EVIDENCE_RESPONSE_TOO_LARGE");
        return response;
    }
}

internal static class PortalEvidenceReadinessEndpoints
{
    public static void MapEvidenceReadiness(this WebApplication app)
    {
        app.MapPost("/api/workspaces/{organization:guid}/evidence-readiness", async (HttpContext context, IPortalEvidenceReadiness service,
            Guid organization, CancellationToken token) =>
        {
            // Only this bounded record-evaluation route accepts more than the existing4KiB control bodies.
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
                feature.MaxRequestBodySize = PortalEvidenceReadiness.MaximumRequestBytes;
            var request = await HostedControlServer.BodyAsync<EvidenceReadinessRequest>(context, token).ConfigureAwait(false);
            return TypedResults.Json(await service.AssessAsync(HostedControlServer.Actor(context), organization, request, token).ConfigureAwait(false), PortalJson.Options);
        }).WithName("AssessWebsiteEvidence").WithSummary("Evaluate eight operational website-evidence conditions; no live scan, human-review task or plaintiff-specific legal determination")
            .Produces<EvidenceReadinessResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(413);
    }
}
