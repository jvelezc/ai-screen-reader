using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HeadlessScreenReader.Cli.HostedControl;

internal static class PortalWorkspaceCatalog
{
    internal const int PageSize = 50;

    internal static void ValidateCursor(Guid? after)
    {
        if (after == Guid.Empty) throw new PortalFault(400, "CONTROL_WORKSPACE_CURSOR_INVALID");
    }

    internal static PortalWorkspacePageResponse Page(PortalWorkspaceResponse[] rows, Guid? after)
    {
        ValidateCursor(after);
        if (rows.Length > PageSize + 1) throw Invalid();
        var previous = after?.ToString("D");
        foreach (var row in rows)
        {
            if (row.Id == Guid.Empty || string.IsNullOrWhiteSpace(row.Name) || row.Name.Length > 120 || row.Name.Any(char.IsControl))
                throw Invalid();
            var id = row.Id.ToString("D");
            if (previous is not null && string.CompareOrdinal(id, previous) <= 0) throw Invalid();
            previous = id;
        }
        var visible = rows.Take(PageSize).ToArray();
        return new(visible, rows.Length > PageSize ? visible[^1].Id : null);
    }

    public static void MapWorkspaceCatalog(this WebApplication app)
    {
        app.MapGet("/api/workspaces", async (HttpContext context, IPortalProjectClient project, CancellationToken token) =>
            TypedResults.Json(await project.WorkspacesAsync(HostedControlServer.Actor(context), null, token).ConfigureAwait(false), PortalJson.Options))
            .WithName("ListMyWorkspaces").WithSummary("Read up to fifty workspaces visible through the current caller's RLS; no creation or membership restoration")
            .Produces<PortalWorkspacePageResponse>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(503);
        app.MapGet("/api/workspaces/page/{after:guid}", async (HttpContext context, IPortalProjectClient project, Guid after, CancellationToken token) =>
        {
            ValidateCursor(after);
            return TypedResults.Json(await project.WorkspacesAsync(HostedControlServer.Actor(context), after, token).ConfigureAwait(false), PortalJson.Options);
        }).WithName("ListMoreMyWorkspaces").WithSummary("Read the next current-caller RLS page after a workspace UUID; cursor is not access authority")
            .Produces<PortalWorkspacePageResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(503);
    }

    private static PortalFault Invalid() => new(503, "CONTROL_PROVIDER_RESPONSE_UNCONFIRMED");
}
