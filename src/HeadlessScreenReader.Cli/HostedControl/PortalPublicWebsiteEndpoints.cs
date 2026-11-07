using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static class PortalPublicWebsiteEndpoints
{
    internal static void MapPublicWebsites(this WebApplication app)
    {
        const string root = "/api/workspaces/{organization:guid}/website-scans";
        app.MapGet(root + "/{scan:guid}/screening/{digest}", async (HttpContext context, IPortalPublicWebsites service,
            Guid organization, Guid scan, string digest, CancellationToken token) =>
            TypedResults.Json(await service.ScreeningAsync(HostedControlServer.Actor(context), organization, scan, digest, token).ConfigureAwait(false), PortalPublicWebsiteService.ScreeningJson))
            .WithName("ReadPublicWebsiteScreening").WithSummary("Inspect verified captured control names and suggest untested service journeys")
            .WithDescription("A digest-pinned diagnostic screen, not task execution, a legal case decision or complete accessibility evaluation. Current tenant and original checks remain required; omitted items are explicit.")
            .Produces<PortalWebsiteScreeningResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        app.MapPost(root, async (HttpContext context, IPortalPublicWebsites service, Guid organization, CancellationToken token) =>
        {
            var request = await HostedControlServer.BodyAsync<PortalCreatePublicWebsiteRequest>(context, token).ConfigureAwait(false);
            var value = await service.AdmitAsync(HostedControlServer.Actor(context), organization, request, token).ConfigureAwait(false);
            context.Response.Headers.Location = $"/api/workspaces/{organization:D}/website-scans/{value.ScanId:D}";
            return TypedResults.Json(value, PortalJson.Options, statusCode: StatusCodes.Status202Accepted);
        }).WithName("AdmitPublicWebsite").WithSummary("Queue an approved public HTTPS source with an idempotent request UUID")
            .WithDescription("Admission acknowledges durable work only. Passive page observations do not establish a task outcome, blocking barrier or complete evidence package.")
            .Produces<PortalPublicWebsiteCreatedResponse>(202).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404)
            .ProducesProblem(409).ProducesProblem(429).ProducesProblem(503);
        app.MapGet(root, async (HttpContext context, IPortalPublicWebsites service, Guid organization, CancellationToken token) =>
            TypedResults.Json(await service.ListAsync(HostedControlServer.Actor(context), organization, token).ConfigureAwait(false), PortalJson.Options))
            .WithName("ListPublicWebsites").WithSummary("Read bounded recent source metadata in the current workspace")
            .Produces<PortalPublicWebsiteListResponse>().ProducesProblem(401).ProducesProblem(404).ProducesProblem(503);
        app.MapGet(root + "/{scan:guid}", async (HttpContext context, IPortalPublicWebsites service, Guid organization, Guid scan, CancellationToken token) =>
            TypedResults.Json(await service.StatusAsync(HostedControlServer.Actor(context), organization, scan, token).ConfigureAwait(false), PortalJson.Options))
            .WithName("ReadPublicWebsiteStatus").WithSummary("Read a fresh scoped work and clock-bound permission snapshot without originals")
            .Produces<PortalPublicWebsiteStatusResponse>().ProducesProblem(401).ProducesProblem(404).ProducesProblem(503);
        app.MapGet(root + "/{scan:guid}/observations/{capture:int}/{offset:int}/{limit:int}", NodesAsync)
            .WithName("ReadPublicWebsiteFirstNodes").WithSummary("Read the first bounded node page from one verified original capture")
            .Produces<PortalPublicWebsiteNodePageResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        app.MapGet(root + "/{scan:guid}/observations/{capture:int}/{offset:int}/{limit:int}/{digest}", NodesAsync)
            .WithName("ReadPublicWebsitePinnedNodes").WithSummary("Read a node page pinned to an immutable original SHA-256")
            .Produces<PortalPublicWebsiteNodePageResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        app.MapGet(root + "/{scan:guid}/original/{offset:int}/{digest}", async (HttpContext context, IPortalPublicWebsites service,
            Guid organization, Guid scan, int offset, string digest, CancellationToken token) =>
            TypedResults.Json(await service.OriginalAsync(HostedControlServer.Actor(context), organization, scan, offset, digest, token).ConfigureAwait(false), PortalJson.Options))
            .WithName("ReadPublicWebsiteOriginal").WithSummary("Read at most 64 KiB of original observation JSON with current authorization and byte hashes")
            .WithDescription("Offsets are 64 KiB boundaries or exact EOF. Reauthorize exact EOF before presenting a complete saved original. Data is base64 JSON; no signed storage URL is returned.")
            .Produces<PortalPublicWebsiteOriginalPageResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
    }

    private static async Task<IResult> NodesAsync(HttpContext context, IPortalPublicWebsites service, Guid organization, Guid scan,
        int capture, int offset, int limit, CancellationToken token) =>
        TypedResults.Json(await service.NodesAsync(HostedControlServer.Actor(context), organization, scan,
            capture, offset, limit, context.Request.RouteValues["digest"] as string, token).ConfigureAwait(false), PortalJson.Options);
}
