using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli;

/// <summary>Native fixed-scope header client. Credentials never enter the rendering page.</summary>
public sealed class PrivateViewerHttpClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly ViewerScope _scope;
    private readonly string _identity;
    private ViewerConnectionResponse? _connection;
    private int _requests;
    public int Requests => Volatile.Read(ref _requests);
    public PrivateViewerHttpClient(Uri address, ViewerScope scope, string identity)
        : this(address, scope, identity, new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false }) { }
    internal PrivateViewerHttpClient(Uri address, ViewerScope scope, string identity, HttpMessageHandler handler)
    {
        if (address.Scheme != "http" || address.Host != "127.0.0.1" || address.Port is < 1 or > 65535 || address.AbsolutePath != "/"
            || address.Query.Length != 0 || address.Fragment.Length != 0 || address.UserInfo.Length != 0
            || !Guid.TryParseExact(scope.ScanId, "N", out _) || !Guid.TryParseExact(scope.SessionId, "N", out _) || string.IsNullOrWhiteSpace(identity))
            throw new ArgumentException("VIEW_CLIENT_SCOPE_INVALID");
        _scope = scope; _identity = identity; _http = new(handler) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
    }
    private HttpRequestMessage Request(string path, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, path); request.Headers.Authorization = new("Bearer", _identity);
        if (_connection is { } connection) { request.Headers.Add("X-Viewer-Id", connection.ConnectionId); request.Headers.Add("X-Viewer-Token", connection.Token); }
        return request;
    }
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Interlocked.Increment(ref _requests);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { response.Dispose(); throw new BrowserOperationException("VIEW_CLIENT_ENDED"); }
        if (response.StatusCode == HttpStatusCode.Conflict) { response.Dispose(); throw new BrowserOperationException("VIEW_CLIENT_EXCLUDED"); }
        if (!response.IsSuccessStatusCode || response.Headers.CacheControl?.NoStore != true)
        { response.Dispose(); throw new BrowserOperationException("VIEW_CLIENT_RESPONSE_INVALID"); }
        return response;
    }
    internal static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maximum, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength is { } length && (length < 0 || length > maximum)) throw new BrowserOperationException("VIEW_CLIENT_RESPONSE_LIMIT");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false); if (read == 0) break;
            if (output.Length + read > maximum) throw new BrowserOperationException("VIEW_CLIENT_RESPONSE_LIMIT");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
    public async Task ExchangeAsync(string bootstrap, CancellationToken token = default)
    {
        if (_connection is not null) throw new BrowserOperationException("VIEW_CLIENT_ALREADY_CONNECTED");
        using var request = Request("/viewer/exchange", HttpMethod.Post);
        request.Content = new StringContent(JsonSerializer.Serialize(new ExchangeViewerRequest(_scope.ScanId, _scope.SessionId, bootstrap)), Encoding.UTF8, "application/json");
        using var response = await SendAsync(request, token).ConfigureAwait(false);
        if (response.Content.Headers.ContentType?.MediaType != "application/json") throw new BrowserOperationException("VIEW_CLIENT_RESPONSE_INVALID");
        var connection = JsonSerializer.Deserialize<ViewerConnectionResponse>(await ReadBoundedAsync(response, 4096, token).ConfigureAwait(false), PrivateViewerService.JsonOptions);
        if (connection is null || !Guid.TryParseExact(connection.ConnectionId, "N", out _) || string.IsNullOrWhiteSpace(connection.Token) || connection.Token.Length > 256)
            throw new BrowserOperationException("VIEW_CLIENT_RESPONSE_INVALID");
        _connection = connection;
    }
    private string Path(string suffix) => $"/viewer/{_scope.ScanId}/{_scope.SessionId}/{suffix}";
    public async Task<PrivateViewContent> ReadAsync(long epoch, CancellationToken token = default)
    {
        if (_connection is null || epoch < 1) throw new BrowserOperationException("VIEW_CLIENT_NOT_CONNECTED");
        var suffix = "?epoch=" + epoch.ToString(CultureInfo.InvariantCulture);
        using var semanticsRequest = Request(Path("observation" + suffix));
        using var semantics = await SendAsync(semanticsRequest, token).ConfigureAwait(false);
        if (semantics.Content.Headers.ContentType?.MediaType != "application/json") throw new BrowserOperationException("VIEW_CLIENT_RESPONSE_INVALID");
        var observation = JsonSerializer.Deserialize<ViewerObservationResponse>(await ReadBoundedAsync(semantics, ViewerPixelFrame.MaximumBytes, token).ConfigureAwait(false), PrivateViewerService.JsonOptions);
        if (observation is null || observation.ScanId != _scope.ScanId || observation.SessionId != _scope.SessionId || observation.Epoch != epoch
            || observation.Snapshot is null || observation.Snapshot.CaptureEpoch != epoch || string.IsNullOrEmpty(observation.Snapshot.DocumentId)
            || observation.ObservationKind != "LastPersistedObservation") throw new BrowserOperationException("VIEW_CLIENT_BINDING_INVALID");
        using var pixelsRequest = Request(Path("pixels" + suffix)); using var pixels = await SendAsync(pixelsRequest, token).ConfigureAwait(false);
        string Header(string name) => pixels.Headers.TryGetValues(name, out var values) && values.Count() == 1 ? values.Single() : "";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(observation.Snapshot.DocumentId)));
        if (pixels.Content.Headers.ContentType?.MediaType != "image/png" || Header("X-Viewer-Epoch") != epoch.ToString(CultureInfo.InvariantCulture)
            || Header("X-Viewer-Document-Sha256") != hash || Header("X-Viewer-Pixel-Kind") != "FreshFixturePixels"
            || Header("X-Viewer-Simulated-Assistance") != (observation.PriorSimulatedAssistance ? "true" : "false")
            || !long.TryParse(Header("X-Viewer-Baseline-Ordinal"), NumberStyles.None, CultureInfo.InvariantCulture, out var baseline))
            throw new BrowserOperationException("VIEW_CLIENT_BINDING_INVALID");
        return PrivateViewContent.Create(observation.Snapshot, observation.Ordinal, baseline,
            ImmutableArray.CreateRange(await ReadBoundedAsync(pixels, ViewerPixelFrame.MaximumBytes, token).ConfigureAwait(false)), observation.PriorSimulatedAssistance);
    }
    public async Task RevokeAsync(CancellationToken token = default)
    {
        if (_connection is null) return;
        using var request = Request(Path("revoke"), HttpMethod.Post); using var response = await SendAsync(request, token).ConfigureAwait(false);
    }
    public void Dispose() => _http.Dispose();
}
