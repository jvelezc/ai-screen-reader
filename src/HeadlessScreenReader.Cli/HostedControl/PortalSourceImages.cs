using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HeadlessScreenReader.Cli.HostedControl;

internal interface IPortalSourceImagesReader
{
    Task<RecordedSourceSnapshot?> ReadAsync(FixtureJob job, Guid producer, CancellationToken token);
    Task<byte[]> ReadImageAsync(FixtureJob job, Guid producer, string prefix, RetainedSourceImageFile image, CancellationToken token);
}

internal sealed class PortalSourceImagesReader(string evidence) : IPortalSourceImagesReader
{
    private string Root => Path.Combine(evidence, ".runtime", "recorded-sources");
    private string DirectoryFor(FixtureJob job) => Path.Combine(Root, $"{job.OrganizationId:N}-{job.JobId:N}-{job.Generation}");
    public Task<RecordedSourceSnapshot?> ReadAsync(FixtureJob job, Guid producer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!DirectoryPresent(evidence)) throw new PortalFault(503, "CONTROL_SOURCE_IMAGES_UNAVAILABLE");
        foreach (var path in new[] { Path.Combine(evidence, ".runtime"), Root, DirectoryFor(job) })
            if (!DirectoryPresent(path)) return Task.FromResult<RecordedSourceSnapshot?>(null);
        var snapshot = RecordedSourceJournal.Read(Root, job.OrganizationId, job.JobId, job.Generation, producer, token);
        _ = SourceImagesProjection.Manifest(snapshot, job, producer);
        return Task.FromResult<RecordedSourceSnapshot?>(snapshot);
    }
    public async Task<byte[]> ReadImageAsync(FixtureJob job, Guid producer, string prefix, RetainedSourceImageFile image, CancellationToken token)
    {
        image.Validate();
        var snapshot = await ReadAsync(job, producer, token).ConfigureAwait(false)
            ?? throw new PortalFault(409, "CONTROL_SOURCE_IMAGES_CHANGED");
        var manifest = SourceImagesProjection.Manifest(snapshot, job, producer);
        if (manifest.PrefixSha256 != prefix || !manifest.Files.Contains(image)) throw new PortalFault(409, "CONTROL_SOURCE_IMAGES_CHANGED");
        var path = Path.Combine(DirectoryFor(job), image.File);
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new PortalFault(409, "CONTROL_SOURCE_IMAGES_INVALID");
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (input.Length != image.Bytes || input.Length is < 4 or > RetainedSourceImageFile.MaximumBytes)
            throw new PortalFault(409, "CONTROL_SOURCE_IMAGES_CHANGED");
        var bytes = new byte[image.Bytes]; await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (input.ReadByte() != -1 || TraversalArchive.Hash(bytes) != image.Sha256)
            throw new PortalFault(409, "CONTROL_SOURCE_IMAGES_CHANGED");
        token.ThrowIfCancellationRequested(); return bytes;
    }
    private static bool DirectoryPresent(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw new PortalFault(409, "CONTROL_SOURCE_IMAGES_INVALID");
            return true;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }
}

/// <summary>Availability of committed source images for the admitted attempt, independent of final media, execution or evidence completeness.</summary>
internal sealed record PortalSourceImagesResponse(PortalScanResponse Scan, Guid JobId, long Generation, Guid? WorkerId,
    string SourceState, RetainedSourceImages? Sources)
{
    public string NextAction => SourceState switch
    {
        "NotApplicable" => "This attempt did not request a recorded keyboard route.",
        "NotStarted" => "No worker attempt has started. Admission is not image collection.",
        "NotRetained" => "No original-image journal was retained. Missing media cannot be inferred from command diagnostics.",
        "Empty" => "The image journal exists, but no source image is committed. Pending files cannot establish zero input or a complete recording.",
        "Available" => "Read exact bounded image pages under this source prefix and actual producer. Verify page and whole-image hashes, then reauthorize at exact EOF. Images remain a subset, not a final video or replay/completion authority.",
        _ => "Original-image availability is unconfirmed."
    };
}

/// <summary>Exact retained source version and image identity, with a64KiB-aligned offset or exact image EOF. No caller-selected producer or path.</summary>
internal sealed record PortalSourceImageRequest(string PrefixSha256, int SourceSequence, string ImageSha256, int Offset = 0);

internal sealed partial class PortalProgressService
{
    private sealed record AuthorizedSourceImages(PortalScanResponse Scan, FixtureJob Job, Guid? Producer,
        RetainedSourceImages? Sources, RetainedSourceImagePage? Page);

    public async Task<PortalSourceImagesResponse> SourceManifestAsync(PortalIdentity actor, Guid organization, Guid scan, CancellationToken token)
    {
        var value = await ReadSourceImagesAsync(actor, organization, scan, null, token).ConfigureAwait(false);
        return new(value.Scan, value.Job.JobId, value.Job.Generation, value.Producer,
            value.Job.TraversalRoute == 0 ? "NotApplicable" : value.Producer is null ? "NotStarted"
            : value.Sources is null ? "NotRetained" : value.Sources.Files.Length == 0 ? "Empty" : "Available", value.Sources);
    }

    public async Task<RetainedSourceImagePage> SourceImageAsync(PortalIdentity actor, Guid organization, Guid scan,
        PortalSourceImageRequest request, CancellationToken token)
    {
        if (!SourceDigest(request.PrefixSha256) || !SourceDigest(request.ImageSha256)
            || request.SourceSequence is < 1 or > RetainedSourceImages.MaximumFrames
            || request.Offset is < 0 or > RetainedSourceImageFile.MaximumBytes)
            throw new PortalFault(400, "CONTROL_SOURCE_IMAGE_PAGE_INVALID");
        var value = await ReadSourceImagesAsync(actor, organization, scan, request, token).ConfigureAwait(false);
        return value.Page ?? throw new PortalFault(404, "CONTROL_SOURCE_IMAGES_NOT_AVAILABLE");
    }

    private async Task<AuthorizedSourceImages> ReadSourceImagesAsync(PortalIdentity actor, Guid organization, Guid scan,
        PortalSourceImageRequest? request, CancellationToken token)
    {
        if (organization == Guid.Empty || scan == Guid.Empty) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
        token.ThrowIfCancellationRequested();
        var before = await ScopeAsync(actor, organization, scan, token).ConfigureAwait(false);
        RetainedSourceImages? sources = null; RetainedSourceImagePage? page = null; PortalFault? failure = null;
        try
        {
            if (before.Job.TraversalRoute > 0 && before.Worker is { } producer)
            {
                var storage = sourceImagesReader ?? throw new PortalFault(503, "CONTROL_SOURCE_IMAGES_UNAVAILABLE");
                var job = Descriptor(before.Job);
                var snapshot = await storage.ReadAsync(job, producer, token).ConfigureAwait(false);
                if (snapshot is not null) sources = SourceImagesProjection.Manifest(snapshot, job, producer);
                if (request is not null && sources is not null)
                {
                    if (sources.PrefixSha256 != request.PrefixSha256) throw new PortalFault(409, "CONTROL_SOURCE_IMAGES_CHANGED");
                    var image = sources.Files.SingleOrDefault(file => file.SourceSequence == request.SourceSequence)
                        ?? throw new PortalFault(404, "CONTROL_SOURCE_IMAGES_NOT_AVAILABLE");
                    if (image.Sha256 != request.ImageSha256) throw new PortalFault(409, "CONTROL_SOURCE_IMAGES_CHANGED");
                    if (request.Offset > image.Bytes || request.Offset != image.Bytes && request.Offset % RetainedSourceImagePage.PageBytes != 0)
                        throw new PortalFault(400, "CONTROL_SOURCE_IMAGE_PAGE_INVALID");
                    var original = await storage.ReadImageAsync(job, producer, sources.PrefixSha256, image, token).ConfigureAwait(false);
                    page = SourceImagesProjection.Page(sources, request.SourceSequence, original, request.Offset);
                }
            }
        }
        catch (PortalFault error) { failure = error; }
        catch (Exception error) when (error is BrowserOperationException or JsonException or InvalidDataException)
        { failure = new(409, "CONTROL_SOURCE_IMAGES_INVALID"); }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
        { failure = new(503, "CONTROL_SOURCE_IMAGES_UNAVAILABLE"); }
        var after = await ScopeAsync(actor, organization, scan, token).ConfigureAwait(false);
        if (Descriptor(before.Job) != Descriptor(after.Job) || before.Worker != after.Worker)
            throw new PortalFault(409, "CONTROL_SOURCE_IMAGES_ATTEMPT_CHANGED");
        token.ThrowIfCancellationRequested();
        if (failure is not null) throw failure;
        return new(after.Scan, Descriptor(after.Job), after.Worker, sources, page);
    }
    private static bool SourceDigest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
}

internal static class PortalSourceImagesEndpoints
{
    public static void MapSourceImages(this WebApplication app)
    {
        app.MapGet("/api/workspaces/{organization:guid}/scans/{scan:guid}/source-images", async (HttpContext context,
            PortalProgressService service, Guid organization, Guid scan, CancellationToken token) =>
            TypedResults.Ok(await service.SourceManifestAsync(HostedControlServer.Actor(context), organization, scan, token).ConfigureAwait(false)))
            .WithName("ReadRetainedSourceImages").WithSummary("Read the admitted producer's committed original-image subset")
            .WithDescription("Explicit unrecorded, unstarted, missing and empty states; uncommitted files remain separate. Source sequence and raw presentation time are retained without claiming final video, decoding, speech, completion, custody or replay. Current original caller and producer checked before and after storage.")
            .Produces<PortalSourceImagesResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        app.MapPost("/api/workspaces/{organization:guid}/scans/{scan:guid}/source-images/bytes", async (HttpContext context,
            PortalProgressService service, Guid organization, Guid scan, CancellationToken token) =>
        {
            var request = await HostedControlServer.BodyAsync<PortalSourceImageRequest>(context, token).ConfigureAwait(false);
            return TypedResults.Ok(await service.SourceImageAsync(HostedControlServer.Actor(context), organization, scan, request, token).ConfigureAwait(false));
        }).WithName("ReadRetainedSourceImageBytes").WithSummary("Read a bounded page of one exact original source JPEG")
            .WithDescription("Requires source prefix, source sequence and whole-image digest. At most64KiB; aligned offsets or exact EOF. Verify page/whole-image hashes and reauthorize at EOF before delivery. Original caller/producer rechecked after every read including storage failures. No caller-selected native path, input, final-video or completion authority.")
            .Accepts<PortalSourceImageRequest>("application/json").Produces<RetainedSourceImagePage>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
    }
}
