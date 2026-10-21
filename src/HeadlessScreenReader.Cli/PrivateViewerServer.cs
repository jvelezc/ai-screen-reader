using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BadHttpRequestException = Microsoft.AspNetCore.Http.BadHttpRequestException;

namespace HeadlessScreenReader.Cli;

/// <summary>Single-exchange request. Identity comes from the authorization header, never this body.</summary>
public sealed record ExchangeViewerRequest(string ScanId, string SessionId, string Bootstrap);
/// <summary>Ephemeral read credential. It must not be stored in reports or logs.</summary>
public sealed record ViewerConnectionResponse(string ConnectionId, string Token, DateTimeOffset ExpiresAt);
/// <summary>A retained permitted observation, not a fresh capture or input authority.</summary>
public sealed record ViewerObservationResponse(string ScanId, string SessionId, long Epoch, long Ordinal, string ObservationKind,
    AccessibilitySnapshot Snapshot, bool PriorSimulatedAssistance);

/// <summary>Read-only application service. No browser, controller bearer or arbitrary input endpoint is exposed.</summary>
public interface IPrivateViewerService
{
    ViewerConnectionResponse Exchange(Guid subject, ExchangeViewerRequest request);
    Task DeliverAsync(Guid subject, ViewerScope scope, Guid id, string secret, long epoch, HttpResponse response, CancellationToken cancellationToken);
    Task RevokeAsync(Guid subject, ViewerScope scope, Guid id, string secret, CancellationToken cancellationToken);
}
public sealed class PrivateViewerService(PrivateViewerBroker broker,
    Func<long, Func<RetainedViewerObservation, Task>, CancellationToken, Task> source) : IPrivateViewerService
{
    public PrivateViewerService(PrivateViewerBroker broker, ControlledScanSession controller)
        : this(broker, controller.DeliverRetainedObservationAsync)
    {
        if (broker.Status.Scope != new ViewerScope(controller.Status.ScanId, controller.SessionId))
            throw new ArgumentException("VIEWER_OWNER_SCOPE_MISMATCH");
    }
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 64
    };
    public ViewerConnectionResponse Exchange(Guid subject, ExchangeViewerRequest request)
    {
        var lease = broker.Exchange(subject, new(request.ScanId, request.SessionId), request.Bootstrap, TimeSpan.FromMinutes(2));
        return new(lease.Id.ToString("N"), lease.Secret.Value, lease.ExpiresAt);
    }
    public Task DeliverAsync(Guid subject, ViewerScope scope, Guid id, string secret, long epoch, HttpResponse response, CancellationToken cancellationToken)
        => broker.DeliverAsync(subject, scope, id, secret, epoch, (send, token) => source(epoch, send, token), (observation, token) =>
        {
            var dto = new ViewerObservationResponse(observation.ScanId, scope.SessionId, observation.Epoch, observation.Ordinal,
                observation.ObservationKind, observation.Snapshot, observation.PriorSimulatedAssistance);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
            if (bytes.Length > 4 * 1024 * 1024) throw new BrowserOperationException("VIEWER_RESPONSE_LIMIT");
            response.ContentType = "application/json"; response.ContentLength = bytes.Length;
            return WriteActualAsync(response, bytes, token);
        }, cancellationToken);
    private static async Task WriteActualAsync(HttpResponse response, byte[] bytes, CancellationToken token)
    {
        await response.Body.WriteAsync(bytes, token).ConfigureAwait(false);
        await response.Body.FlushAsync(token).ConfigureAwait(false);
        await response.CompleteAsync().ConfigureAwait(false);
    }
    public Task RevokeAsync(Guid subject, ViewerScope scope, Guid id, string secret, CancellationToken cancellationToken)
        => broker.RevokeAsync(subject, scope, id, secret, cancellationToken);
}

/// <summary>Loopback-only feasibility transport for header-capable clients. No public listener or browser UI.</summary>
public sealed class PrivateViewerServer : IAsyncDisposable
{
    private readonly WebApplication _application;
    public Uri BaseAddress { get; }
    private PrivateViewerServer(WebApplication application, Uri address) { _application = application; BaseAddress = address; }
    public static async Task<PrivateViewerServer> StartAsync(IPrivateViewerIdentity identity, IPrivateViewerService service,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(service);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        { Args = [], EnvironmentName = "Production", ApplicationName = typeof(PrivateViewerServer).Assembly.GetName().Name });
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http1);
            options.Limits.MaxRequestBodySize = 4096; options.Limits.MaxConcurrentConnections = 8;
            options.Limits.MaxRequestHeadersTotalSize = 4096; options.Limits.MaxRequestHeaderCount = 24;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5); options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(15);
        });
        builder.Services.AddSingleton(service); builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
            options.SerializerOptions.PropertyNameCaseInsensitive = false;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            SetPrivateHeaders(context.Response);
            try
            {
                if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote)
                    || context.Request.Host.Host != "127.0.0.1" || context.Request.Host.Port != context.Connection.LocalPort
                    || context.Request.Headers.ContainsKey("Origin") || context.Request.Headers.ContainsKey("Cookie"))
                { await FailAsync(context, 403, "VIEWER_CLIENT_CONTEXT_DENIED"); return; }
                if (context.Request.Headers.Authorization.Count != 1 || !identity.TryAuthenticate(context.Request.Headers.Authorization[0], out var subject))
                { context.Response.Headers.WWWAuthenticate = "Bearer"; await FailAsync(context, 401, "VIEWER_IDENTITY_REQUIRED"); return; }
                context.User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject.ToString("N"))], "ProcessProof"));
                await next(context);
            }
            catch (OperationCanceledException) { if (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested) await FailAsync(context, 409, "VIEWER_DELIVERY_CANCELLED"); else context.Abort(); }
            catch (BrowserOperationException exception)
            {
                var code = exception.Code switch
                {
                    "VIEWER_AUTHORITY_DENIED" => "VIEWER_AUTHORITY_DENIED",
                    "VIEWER_READ_BUSY" => "VIEWER_READ_BUSY",
                    "VIEWER_EPOCH_INVALID" => "VIEWER_EPOCH_INVALID",
                    "OWNED_SCAN_EPOCH_STALE" => "VIEWER_EPOCH_INVALID",
                    _ => "VIEWER_OBSERVATION_UNAVAILABLE"
                };
                await FailAsync(context, code == "VIEWER_AUTHORITY_DENIED" ? 401 : code == "VIEWER_EPOCH_INVALID" ? 400 : 409, code);
            }
            catch (BadHttpRequestException exception)
            { await FailAsync(context, exception.StatusCode is 413 or 415 ? exception.StatusCode : 400, "VIEWER_REQUEST_INVALID"); }
            catch (JsonException) { await FailAsync(context, 400, "VIEWER_REQUEST_INVALID"); }
            catch { await FailAsync(context, 503, "VIEWER_OPERATION_UNCONFIRMED"); }
        });
        app.MapPrivateViewer();
        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new Uri(address));
        }
        catch { await app.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private static void SetPrivateHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store"; response.Headers.Pragma = "no-cache";
        response.Headers["X-Content-Type-Options"] = "nosniff"; response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    }
    private static Task FailAsync(HttpContext context, int status, string code)
    {
        if (context.Response.HasStarted) { context.Abort(); return Task.CompletedTask; }
        context.Response.ContentLength = null;
        if (status == 401) context.Response.Headers.WWWAuthenticate = "Bearer";
        return TypedResults.Problem(statusCode: status, title: code, detail: code).ExecuteAsync(context);
    }
    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await _application.StopAsync(deadline.Token).ConfigureAwait(false); }
        finally { await _application.DisposeAsync().ConfigureAwait(false); }
    }
}

internal static class PrivateViewerEndpoints
{
    public static void MapPrivateViewer(this WebApplication app)
    {
        app.MapGet("/openapi/v1.json", (HttpContext context, CancellationToken token) =>
        {
            NoQuery(context);
            return TypedResults.Text(PrivateViewerOpenApi.Document, "application/json");
        }).WithName("PrivateViewerOpenApi").WithSummary("Describe the bounded loopback-only proof protocol");
        app.MapPost("/viewer/exchange", async (HttpContext context, IPrivateViewerService service, CancellationToken token) =>
        {
            NoQuery(context);
            if (!context.Request.HasJsonContentType()) throw new BadHttpRequestException("VIEWER_REQUEST_INVALID", 415);
            var request = await JsonSerializer.DeserializeAsync<ExchangeViewerRequest>(context.Request.Body, PrivateViewerService.JsonOptions, token);
            if (request is null || request.ScanId is null || request.SessionId is null || request.Bootstrap is null) throw new BadHttpRequestException("VIEWER_REQUEST_INVALID");
            return TypedResults.Ok(service.Exchange(Subject(context), request));
        }).WithName("ExchangePrivateViewerBootstrap").WithSummary("Exchange one scoped bootstrap once for a read-only connection")
            .Produces<ViewerConnectionResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(413).ProducesProblem(415);
        app.MapGet("/viewer/{scanId}/{sessionId}/observation", async (HttpContext context, IPrivateViewerService service, string scanId, string sessionId, CancellationToken token) =>
        {
            if (context.Request.Query.Count != 1 || context.Request.Query["epoch"].Count != 1
                || !long.TryParse(context.Request.Query["epoch"][0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var epoch))
                throw new BadHttpRequestException("VIEWER_REQUEST_INVALID");
            var (id, secret) = Connection(context);
            await service.DeliverAsync(Subject(context), new(scanId, sessionId), id, secret, epoch, context.Response, token);
        }).WithName("ReadPrivateViewerObservation").WithSummary("Deliver the last acknowledged permitted observation without input or new capture")
            .Produces<ViewerObservationResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409);
        app.MapPost("/viewer/{scanId}/{sessionId}/revoke", async (HttpContext context, IPrivateViewerService service, string scanId, string sessionId, CancellationToken token) =>
        {
            NoQuery(context);
            if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding")) throw new BadHttpRequestException("VIEWER_REQUEST_INVALID");
            var (id, secret) = Connection(context);
            await service.RevokeAsync(Subject(context), new(scanId, sessionId), id, secret, token);
            return TypedResults.NoContent();
        }).WithName("RevokePrivateViewerConnection").WithSummary("Close read admission and acknowledge actual source and delivery drain")
            .Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409);
    }
    private static void NoQuery(HttpContext context) { if (context.Request.QueryString.HasValue) throw new BadHttpRequestException("VIEWER_REQUEST_INVALID"); }
    private static Guid Subject(HttpContext context) => Guid.ParseExact(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!, "N");
    private static (Guid Id, string Secret) Connection(HttpContext context)
    {
        var id = context.Request.Headers["X-Viewer-Id"]; var secret = context.Request.Headers["X-Viewer-Token"];
        if (id.Count != 1 || secret.Count != 1 || !Guid.TryParseExact(id[0], "N", out var parsed)) throw new BrowserOperationException("VIEWER_AUTHORITY_DENIED");
        return (parsed, secret[0]!);
    }
}
