using System.Diagnostics;
using System.Runtime.CompilerServices;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Browser.Playwright;

internal sealed class PublicTaskNetworkRecorder
{
    private sealed record Identity(int Id);
    private readonly ConditionalWeakTable<IRequest, Identity> _ids = new();
    private readonly object _gate = new();
    private readonly long _began = Stopwatch.GetTimestamp();
    private readonly PublicTaskNetworkLog _log = new(PublicTaskNetworkTrace.PublicDependencyPolicy, DateTimeOffset.UtcNow);
    private double Elapsed => Stopwatch.GetElapsedTime(_began).TotalMilliseconds;
    private int Id(IRequest request)
    {
        lock (_gate) return _ids.GetValue(request,
            r => new(_log.Begin(r.Url, r.Method, r.ResourceType, Elapsed))).Id;
    }
    public void Attach(IBrowserContext context)
    {
        context.Request += (_, request) => Id(request);
        context.Response += (_, response) => _log.Record(Id(response.Request), "Response", Elapsed, response.Status,
            response.Status >= 400 ? "HTTP_ERROR" : null);
        context.RequestFinished += (_, request) => _log.Record(Id(request), "Finished", Elapsed);
        context.RequestFailed += (_, request) => _log.Record(Id(request), "Failed", Elapsed, code: Failure(request.Failure));
    }
    public void Denied(IRequest request, string code) => _log.Record(Id(request), "PolicyDenied", Elapsed, code: code);
    public void WebSocket(string url, bool allowed)
    {
        var id = _log.Begin(url, "GET", "websocket", Elapsed);
        _log.Record(id, allowed ? "WebSocketRequested" : "PolicyDenied", Elapsed, code: allowed ? null : "WEBSOCKET_URL_DENIED");
    }
    public PublicTaskNetworkTrace Snapshot() => _log.Snapshot();
    private static string Failure(string? value) => value is { Length: <= 90 } && value.StartsWith("net::ERR_", StringComparison.Ordinal)
        && value[5..].All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_') ? value[5..] : "NETWORK_FAILED";
}
