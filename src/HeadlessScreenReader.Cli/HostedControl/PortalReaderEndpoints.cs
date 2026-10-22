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
        app.MapDelete(resource, async (HttpContext context, PortalReaderSessions readers,
            Guid organization, Guid session, CancellationToken token) =>
            TypedResults.Json(await readers.CloseAsync(HostedControlServer.Actor(context), organization, session, token).ConfigureAwait(false), PortalJson.Options))
            .WithName("CloseReaderSession").WithSummary("Close your browser and join native cleanup before acknowledging or releasing capacity");
    }
}
