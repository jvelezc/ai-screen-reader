using System.Collections.Immutable;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>Every page requires the caller-retained original index digest; no latest-object fallback.</summary>
internal sealed record PortalArchiveManifestRequest(string IndexSha256, int Offset = 0);
/// <summary>Read one exact original path at a bounded byte offset, never an arbitrary object-store key.</summary>
internal sealed record PortalArchiveFileRequest(string IndexSha256, string Path, long Offset = 0);

/// <summary>Organization members may read explicit operator imports in their workspace; no scan admission binding is implied.</summary>
internal sealed class PortalArchiveReader(IPortalProjectClient project, TraversalArchive? archive)
{
    internal const int ManifestFiles = 32;

    public Task<TraversalArchiveManifestPage> ManifestAsync(PortalIdentity actor, Guid organization, Guid id,
        PortalArchiveManifestRequest request, CancellationToken token) => AuthorizedAsync(actor, organization, id, request.IndexSha256,
        async (reader, identity) =>
        {
            if (request.Offset < 0) throw new PortalFault(400, "CONTROL_ARCHIVE_PAGE_INVALID");
            var index = await reader.ReadIndexAsync(identity, token).ConfigureAwait(false);
            if (request.Offset > index.Files.Length) throw new PortalFault(400, "CONTROL_ARCHIVE_PAGE_INVALID");
            var files = index.Files.Skip(request.Offset).Take(ManifestFiles).Select(f => new TraversalArchiveEntry(f.Path, f.Bytes, f.Sha256)).ToImmutableArray();
            var next = request.Offset + files.Length;
            return new TraversalArchiveManifestPage(identity, index.SourceManifestSha256, index.Files.Length,
                index.Files.Sum(f => f.Bytes), request.Offset, files, next < index.Files.Length ? next : null);
        }, token);

    public Task<TraversalArchiveReadPage> FileAsync(PortalIdentity actor, Guid organization, Guid id,
        PortalArchiveFileRequest request, CancellationToken token) => AuthorizedAsync(actor, organization, id, request.IndexSha256,
        async (reader, identity) =>
        {
            if (request.Path is not { Length: > 0 and <= 240 } || request.Offset < 0)
                throw new PortalFault(400, "CONTROL_ARCHIVE_PAGE_INVALID");
            var page = await reader.ReadFileAsync(identity, request.Path, request.Offset, token).ConfigureAwait(false);
            return new TraversalArchiveReadPage(page, TraversalArchive.Hash(page.Data));
        }, token);

    private async Task<T> AuthorizedAsync<T>(PortalIdentity actor, Guid organization, Guid id, string digest,
        Func<TraversalArchive, TraversalArchiveIdentity, Task<T>> read, CancellationToken token)
    {
        async Task Authorize()
        {
            if (await project.WorkspaceAsync(actor, organization, token).ConfigureAwait(false) is null)
                throw new PortalFault(404, "CONTROL_WORKSPACE_NOT_FOUND");
        }
        await Authorize().ConfigureAwait(false);
        var identity = new TraversalArchiveIdentity(organization, id, digest);
        try { TraversalArchive.ValidateIdentity(identity); }
        catch (TraversalArchiveException) { throw new PortalFault(400, "CONTROL_ARCHIVE_IDENTITY_INVALID"); }
        if (archive is null) throw new PortalFault(503, "CONTROL_ARCHIVE_UNAVAILABLE");
        T result;
        try { result = await read(archive, identity).ConfigureAwait(false); }
        catch (TraversalArchiveException error)
        {
            throw error.Failure switch
            {
                TraversalArchiveFailure.Missing => new PortalFault(404, "CONTROL_ARCHIVE_NOT_FOUND"),
                TraversalArchiveFailure.Invalid => new PortalFault(409, "CONTROL_ARCHIVE_INVALID"),
                _ => new PortalFault(503, "CONTROL_ARCHIVE_UNAVAILABLE")
            };
        }
        // A page that completed after membership loss must not escape with old authority.
        await Authorize().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return result;
    }
}

internal static class PortalArchiveEndpoints
{
    public static void MapArchiveReads(this WebApplication app)
    {
        const string resource = "/api/workspaces/{organization:guid}/archives/{archive:guid}";
        app.MapPost(resource + "/manifest", async (HttpContext context, PortalArchiveReader reader,
            Guid organization, Guid archive, CancellationToken token) =>
        {
            var input = await HostedControlServer.BodyAsync<PortalArchiveManifestRequest>(context, token).ConfigureAwait(false);
            return TypedResults.Json(await reader.ManifestAsync(HostedControlServer.Actor(context), organization, archive, input, token).ConfigureAwait(false), PortalJson.Options);
        }).WithName("ReadArchiveManifest").WithSummary("Read up to32 original-file metadata entries after fresh caller/workspace authorization; not file availability or custody proof");
        app.MapPost(resource + "/file", async (HttpContext context, PortalArchiveReader reader,
            Guid organization, Guid archive, CancellationToken token) =>
        {
            var input = await HostedControlServer.BodyAsync<PortalArchiveFileRequest>(context, token).ConfigureAwait(false);
            return TypedResults.Json(await reader.FileAsync(HostedControlServer.Actor(context), organization, archive, input, token).ConfigureAwait(false), PortalJson.Options);
        }).WithName("ReadArchiveFile").WithSummary("Read at most64KiB of an exact original with part integrity checked; all pages and final file hash are required for whole-file verification");
    }
}
