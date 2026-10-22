using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BadHttpRequestException = Microsoft.AspNetCore.Http.BadHttpRequestException;

namespace HeadlessScreenReader.Cli.HostedControl;

/// <summary>JSON object with no caller-selected identity or tenant bootstrap fields.</summary>
internal sealed record PortalEmptyRequest;

/// <summary>Private control plane. All tenant operations revalidate identity and use RLS.</summary>
internal sealed class HostedControlServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    public Uri BaseAddress { get; }
    private HostedControlServer(WebApplication app, Uri address) { _app = app; BaseAddress = address; }

    public static async Task<HostedControlServer> StartAsync(PortalOptions options, IPortalProjectClient project,
        IPortalObservationReader observations, CancellationToken token = default)
    {
        options.Validate();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        { Args = [], EnvironmentName = "Production", ApplicationName = typeof(HostedControlServer).Assembly.GetName().Name });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Listen(IPAddress.Loopback, options.Port, listen => listen.Protocols = HttpProtocols.Http1);
            kestrel.Limits.MaxRequestBodySize = 4096;
            kestrel.Limits.MaxConcurrentConnections = 16;
            kestrel.Limits.MaxRequestHeadersTotalSize = 16384;
            kestrel.Limits.MaxRequestHeaderCount = 32;
            kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            kestrel.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(15);
        });
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(project);
        builder.Services.AddSingleton<IPortalScanService>(new PortalScanService(project, observations));
        builder.Services.AddProblemDetails();
        var app = builder.Build();
        Uri? bound = null;
        app.Use(async (context, next) =>
        {
            Headers(context.Response);
            try
            {
                var publicOrigin = new Uri(options.PublicOrigin).GetLeftPart(UriPartial.Authority);
                var authority = context.Request.Host.Value;
                if (!string.Equals(authority, new Uri(publicOrigin).Authority, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(authority, bound?.Authority, StringComparison.OrdinalIgnoreCase))
                    throw new PortalFault(403, "CONTROL_HOST_DENIED");
                if (context.Request.Headers.TryGetValue("Origin", out var origin)
                    && (origin.Count != 1 || !string.Equals(origin[0], publicOrigin, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(origin[0], bound?.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)))
                    throw new PortalFault(403, "CONTROL_ORIGIN_DENIED");
                if (context.Request.Path.StartsWithSegments("/api") && context.Request.Path != "/api/config")
                {
                    NoQuery(context);
                    var values = context.Request.Headers.Authorization;
                    if (values.Count != 1 || !AuthenticationHeaderValue.TryParse(values[0], out var header)
                        || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || header.Parameter is null)
                        throw new PortalFault(401, "CONTROL_AUTHENTICATION_REQUIRED");
                    context.Items[typeof(PortalIdentity)] = await project.AuthenticateAsync(header.Parameter, context.RequestAborted).ConfigureAwait(false);
                }
                await next(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Abort(); }
            catch (PortalFault fault) { await FailAsync(context, fault.Status, fault.Code).ConfigureAwait(false); }
            catch (BadHttpRequestException exception)
            { await FailAsync(context, exception.StatusCode is 413 or 415 ? exception.StatusCode : 400, "CONTROL_REQUEST_INVALID").ConfigureAwait(false); }
            catch (JsonException) { await FailAsync(context, 400, "CONTROL_REQUEST_INVALID").ConfigureAwait(false); }
            catch { await FailAsync(context, 503, "CONTROL_OPERATION_UNCONFIRMED").ConfigureAwait(false); }
        });
        app.MapPortal(options);
        try
        {
            await app.StartAsync(token).ConfigureAwait(false);
            bound = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            return new(app, bound);
        }
        catch { await app.DisposeAsync().ConfigureAwait(false); throw; }
    }

    internal static PortalIdentity Actor(HttpContext context) => context.Items[typeof(PortalIdentity)] as PortalIdentity
        ?? throw new PortalFault(401, "CONTROL_AUTHENTICATION_REQUIRED");
    internal static void NoQuery(HttpContext context)
    {
        if (context.Request.QueryString.HasValue) throw new PortalFault(400, "CONTROL_QUERY_DENIED");
    }
    internal static async Task<T> BodyAsync<T>(HttpContext context, CancellationToken token)
    {
        if (!context.Request.HasJsonContentType()) throw new PortalFault(415, "CONTROL_JSON_REQUIRED");
        return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, PortalJson.Options, token).ConfigureAwait(false)
            ?? throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
    }
    private static void Headers(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
        response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; "
            + "connect-src 'self' " + PortalOptions.Project.GetLeftPart(UriPartial.Authority)
            + "; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
    }
    private static Task FailAsync(HttpContext context, int status, string code)
    {
        if (context.Response.HasStarted) { context.Abort(); return Task.CompletedTask; }
        context.Response.ContentLength = null;
        if (status == 401) context.Response.Headers.WWWAuthenticate = "Bearer";
        return TypedResults.Problem(statusCode: status, title: code, detail: code,
            extensions: new Dictionary<string, object?> { ["supportReference"] = Guid.NewGuid().ToString("N") }).ExecuteAsync(context);
    }
    public async ValueTask DisposeAsync()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await _app.StopAsync(stop.Token).ConfigureAwait(false); }
        finally { await _app.DisposeAsync().ConfigureAwait(false); }
    }
}

internal static class PortalEndpoints
{
    public static void MapPortal(this WebApplication app, PortalOptions options)
    {
        app.MapGet("/", (HttpContext context, CancellationToken token) =>
        {
            // Auth codes are one-use PKCE values; access/refresh bearers in URLs are denied.
            if (context.Request.Query.Any(pair => pair.Value.Count != 1 || pair.Value[0]?.Length > 2048
                || pair.Key is not ("code" or "error" or "error_code" or "error_description")))
                throw new PortalFault(400, "CONTROL_QUERY_DENIED");
            return TypedResults.Text(PortalAssets.Html, "text/html");
        }).WithName("PortalHome").WithSummary("Sign in and use your private fixture workspace");
        app.MapGet("/portal.js", (HttpContext context, CancellationToken token) =>
        { HostedControlServer.NoQuery(context); return TypedResults.Text(PortalAssets.JavaScript, "text/javascript"); });
        app.MapGet("/portal.css", (HttpContext context, CancellationToken token) =>
        { HostedControlServer.NoQuery(context); return TypedResults.Text(PortalAssets.Css, "text/css"); });
        app.MapGet("/api/config", (HttpContext context, CancellationToken token) =>
        {
            HostedControlServer.NoQuery(context);
            return TypedResults.Json(new PortalConfigurationResponse(PortalOptions.Project.GetLeftPart(UriPartial.Authority),
                options.PublishableKey, "synthetic-shop", FixtureObservation.Profile), PortalJson.Options);
        }).WithName("PortalConfiguration").WithSummary("Public project settings; contains no privileged credential");
        app.MapGet("/internal/ready", (HttpContext context, CancellationToken token) =>
        {
            HostedControlServer.NoQuery(context);
            return TypedResults.Json(new PortalReadinessResponse("hsr-control-readiness-v1", "Listening",
                options.ReleaseCommit, false, false), PortalJson.Options);
        }).WithName("PortalReadiness").WithSummary("Actual loopback listener readiness; not a tenant sign-in assertion");
        app.MapGet("/api/session", (HttpContext context, CancellationToken token) =>
        {
            var actor = HostedControlServer.Actor(context);
            return TypedResults.Json(new PortalSessionResponse(actor.UserId, actor.Email), PortalJson.Options);
        }).WithName("PortalSession").WithSummary("Validate the current user with the selected Auth server");
        app.MapPost("/api/workspace", async Task<Results<Created<PortalWorkspaceResponse>, Ok<PortalWorkspaceResponse>>> (
            HttpContext context, IPortalProjectClient project, CancellationToken token) =>
        {
            await HostedControlServer.BodyAsync<PortalEmptyRequest>(context, token).ConfigureAwait(false);
            var actor = HostedControlServer.Actor(context);
            var receipt = await project.EnsureWorkspaceAsync(actor, token).ConfigureAwait(false);
            var workspace = await project.WorkspaceAsync(actor, receipt.Id, token).ConfigureAwait(false)
                ?? throw new PortalFault(403, "CONTROL_WORKSPACE_ACCESS_DENIED");
            return receipt.Created ? TypedResults.Created("/api/workspaces/" + receipt.Id.ToString("D"), workspace) : TypedResults.Ok(workspace);
        }).WithName("EnsurePersonalWorkspace").WithSummary("Create or resume your own workspace; never restore revoked membership")
            .Produces<PortalWorkspaceResponse>(201).Produces<PortalWorkspaceResponse>(200).ProducesProblem(401).ProducesProblem(403);
        app.MapGet("/api/workspaces/{organization:guid}", async (HttpContext context, IPortalProjectClient project,
            Guid organization, CancellationToken token) =>
        {
            var workspace = await project.WorkspaceAsync(HostedControlServer.Actor(context), organization, token).ConfigureAwait(false)
                ?? throw new PortalFault(404, "CONTROL_WORKSPACE_NOT_FOUND");
            return TypedResults.Json(workspace, PortalJson.Options);
        }).WithName("GetPortalWorkspace").WithSummary("Read a workspace visible through current tenant RLS");
        app.MapGet("/api/workspaces/{organization:guid}/scans", async (HttpContext context, IPortalProjectClient project,
            Guid organization, CancellationToken token) =>
        {
            var actor = HostedControlServer.Actor(context);
            if (await project.WorkspaceAsync(actor, organization, token).ConfigureAwait(false) is null)
                throw new PortalFault(404, "CONTROL_WORKSPACE_NOT_FOUND");
            var scans = await project.ScansAsync(actor, organization, token).ConfigureAwait(false);
            return TypedResults.Json(new PortalScanListResponse(scans.Take(50).ToArray(), scans.Length > 50), PortalJson.Options);
        }).WithName("ListPortalScans").WithSummary("Read up to fifty recent fixture runs; additional results remain explicit");
        app.MapPost("/api/workspaces/{organization:guid}/scans", async (HttpContext context, IPortalProjectClient project,
            Guid organization, CancellationToken token) =>
        {
            var input = await HostedControlServer.BodyAsync<PortalCreateScanRequest>(context, token).ConfigureAwait(false);
            if (organization == Guid.Empty || input.RequestId == Guid.Empty) throw new PortalFault(400, "CONTROL_REQUEST_INVALID");
            var receipt = await project.CreateScanAsync(HostedControlServer.Actor(context), organization, input.RequestId, token).ConfigureAwait(false);
            return TypedResults.Created("/api/workspaces/" + organization.ToString("D") + "/scans/" + receipt.ScanId.ToString("D"), receipt);
        }).WithName("CreatePortalScan").WithSummary("Atomically admit the fixed input-free shop fixture using an idempotent request ID")
            .Produces<PortalScanCreatedResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409);
        app.MapGet("/api/workspaces/{organization:guid}/scans/{scan:guid}", async (HttpContext context, IPortalScanService reports,
            Guid organization, Guid scan, CancellationToken token) =>
        {
            var report = await reports.ReportAsync(HostedControlServer.Actor(context), organization, scan, token).ConfigureAwait(false);
            return TypedResults.Json(report, PortalJson.Options);
        }).WithName("GetPortalReport").WithSummary("Read an authorized result only after exact artifact hash and identity validation");
        app.MapGet("/openapi/v1.json", (HttpContext context, CancellationToken token) =>
        { HostedControlServer.NoQuery(context); return TypedResults.Text(PortalAssets.OpenApi, "application/json"); })
            .WithName("PortalOpenApi").WithSummary("Describe the bounded private portal protocol");
    }
}
