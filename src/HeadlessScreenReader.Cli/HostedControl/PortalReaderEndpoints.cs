using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static class PortalReaderEndpoints
{
    public static void MapReaderSessions(this WebApplication app)
    {
        const string collection = "/api/workspaces/{organization:guid}/reader-sessions";
        const string resource = collection + "/{session:guid}";
        app.MapPost(collection, async (HttpContext context, PortalReaderSessions readers,
            Guid organization, CancellationToken token) =>
        {
            await HostedControlServer.BodyAsync<PortalEmptyRequest>(context, token).ConfigureAwait(false);
            var response = await readers.OpenAsync(HostedControlServer.Actor(context), organization, token).ConfigureAwait(false);
            return TypedResults.Created($"/api/workspaces/{organization:D}/reader-sessions/{response.SessionId:D}", response);
        }).WithName("OpenReaderSession").WithSummary("Open one bounded read-only Foundation fixture owned by this subject and workspace");
        app.MapGet(resource, async (HttpContext context, PortalReaderSessions readers,
            Guid organization, Guid session, CancellationToken token) =>
            TypedResults.Json(await readers.ReadAsync(HostedControlServer.Actor(context), organization, session, token).ConfigureAwait(false), PortalJson.Options))
            .WithName("GetReaderSession").WithSummary("Read your live session metadata after fresh workspace authorization");
        app.MapPost(resource + "/observations", async (HttpContext context, PortalReaderSessions readers,
            Guid organization, Guid session, CancellationToken token) =>
        {
            await HostedControlServer.BodyAsync<PortalEmptyRequest>(context, token).ConfigureAwait(false);
            return TypedResults.Json(await readers.ObserveAsync(HostedControlServer.Actor(context), organization, session, token).ConfigureAwait(false), PortalJson.Options);
        }).WithName("ObserveReaderSession").WithSummary("Capture exposed fixture semantics without input, recording or a verified goal claim");
        app.MapPost(resource + "/commands", async (HttpContext context, PortalReaderSessions readers,
            Guid organization, Guid session, CancellationToken token) =>
        {
            var request = await HostedControlServer.BodyAsync<PortalReaderCommandRequest>(context, token).ConfigureAwait(false);
            return TypedResults.Json(await readers.CommandAsync(HostedControlServer.Actor(context), organization, session, request, token).ConfigureAwait(false), PortalJson.Options);
        }).WithName("NavigateReaderSession").WithSummary("Navigate exposed semantics using a fresh capture and an observed document fence; no browser input");
        app.MapPost(resource + "/timeline", async (HttpContext context, PortalReaderSessions readers,
            Guid organization, Guid session, CancellationToken token) =>
        {
            var request = await HostedControlServer.BodyAsync<PortalReaderTimelineRequest>(context, token).ConfigureAwait(false);
            return TypedResults.Json(await readers.TimelineAsync(HostedControlServer.Actor(context), organization, session, request, token).ConfigureAwait(false), PortalJson.Options);
        }).WithName("GetReaderTimeline").WithSummary("Page transient reader events; hasMore and timelineTruncated distinguish paging from incomplete retained history");
        app.MapPost(resource + "/trace", async (HttpContext context, PortalReaderSessions readers,
            Guid organization, Guid session, CancellationToken token) =>
        {
            var request = await HostedControlServer.BodyAsync<PortalReaderTraceRequest>(context, token).ConfigureAwait(false);
            return TypedResults.Json(await readers.TraceAsync(HostedControlServer.Actor(context), organization, session, request, token).ConfigureAwait(false), PortalJson.Options);
        }).WithName("GetReaderTrace").WithSummary("Page observed state nodes and reader-cursor command edges with fresh capture evidence; no browser input or goal claim");
        app.MapDelete(resource, async (HttpContext context, PortalReaderSessions readers,
            Guid organization, Guid session, CancellationToken token) =>
            TypedResults.Json(await readers.CloseAsync(HostedControlServer.Actor(context), organization, session, token).ConfigureAwait(false), PortalJson.Options))
            .WithName("CloseReaderSession").WithSummary("Close your browser and join native cleanup before acknowledging or releasing capacity");
    }
}
