using System.Collections.Immutable;

namespace HeadlessScreenReader.Domain;

/// <summary>Redacted observations, never request headers, bodies, query values or exception messages.</summary>
public sealed record PublicTaskNetworkEvent(int Sequence, int RequestId, double ElapsedMilliseconds,
    string Phase, string Method, string ResourceType, string? Origin, string UrlSha256, int? Status, string? Code);

public sealed record PublicTaskNetworkTrace(string Schema, string Policy, DateTimeOffset StartedAt,
    int Requests, int PolicyDenials, int DroppedEvents, ImmutableArray<PublicTaskNetworkEvent> Events)
{
    public const string CurrentSchema = "hsr-public-task-network-v1";
    public const string PublicDependencyPolicy = "PublicHttpsDependenciesV1";
    public const int MaximumEvents = 256;

    public void Validate()
    {
        void Require(bool condition) { if (!condition) throw new BrowserOperationException("PUBLIC_TASK_NETWORK_TRACE_INVALID"); }
        Require(Schema == CurrentSchema && Policy is PublicDependencyPolicy or "ExactHostLegacy"
            && StartedAt != default && Requests is >= 0 and <= 100000 && PolicyDenials is >= 0 and <= 100000
            && DroppedEvents is >= 0 and <= 1000000 && !Events.IsDefault && Events.Length <= MaximumEvents);
        var sequence = 0; double elapsed = -1;
        foreach (var entry in Events)
        {
            Require(entry.Sequence > sequence && entry.RequestId > 0 && entry.RequestId <= Requests
                && double.IsFinite(entry.ElapsedMilliseconds) && entry.ElapsedMilliseconds >= elapsed && entry.ElapsedMilliseconds <= 180000
                && entry.Phase is "Request" or "Response" or "Finished" or "Failed" or "PolicyDenied" or "WebSocketRequested"
                && entry.Method.Length is > 0 and <= 16 && entry.Method.All(c => char.IsAsciiLetterUpper(c) || c == '-')
                && entry.ResourceType is "document" or "stylesheet" or "image" or "media" or "font" or "script" or "texttrack"
                    or "xhr" or "fetch" or "eventsource" or "websocket" or "manifest" or "beacon" or "ping" or "cspreport" or "other" or "connect"
                && PublicTaskRules.Digest(entry.UrlSha256)
                && (entry.Origin is null || IsOrigin(entry.Origin))
                && (entry.Status is null || entry.Status is >= 100 and <= 599)
                && (entry.Code is null || entry.Code.Length is > 0 and <= 100 && entry.Code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_'))
                && (entry.Phase != "Response" || entry.Status is not null)
                && (entry.Phase is not ("Failed" or "PolicyDenied") || entry.Code is not null));
            sequence = entry.Sequence; elapsed = entry.ElapsedMilliseconds;
        }
    }

    /// <summary>Origin only; even a malicious URL cannot place credentials, path or query data in diagnostics.</summary>
    public static string? OriginOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0
            || uri.Scheme is not ("https" or "wss") || uri.HostNameType != UriHostNameType.Dns
            || uri.IdnHost.Length > 253 || uri.Port != 443) return null;
        return $"{uri.Scheme}://{uri.IdnHost.ToLowerInvariant()}";
    }

    private static bool IsOrigin(string value) => value.Length <= 261 && OriginOf(value) == value
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0;
}
