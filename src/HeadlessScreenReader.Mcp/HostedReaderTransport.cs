using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeadlessScreenReader.Mcp;

/// <summary>No redirects or retries; a separate deadline joins headers and the actual bounded body.</summary>
internal sealed class HostedReaderTransport : IDisposable
{
    internal const int MaximumBytes = 262144;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };
    private readonly HostedReaderOptions _options;
    private readonly HttpClient _http;

    internal HostedReaderTransport(HostedReaderOptions options, HttpMessageHandler? handler)
    {
        _options = options;
        handler ??= new SocketsHttpHandler
        { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
        _http = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    internal async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body = null, int expectedStatus = 200)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(method, new Uri(_options.Origin, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Bearer);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        // A redirect cannot become a second credential-bearing request.
        if ((int)response.StatusCode is >= 300 and < 400) throw new HostedReaderFault("HOSTED_REDIRECT_DENIED");
        var bytes = await ReadBoundedAsync(response, MaximumBytes, deadline.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string? code = null; string? support = null;
            try
            {
                using var problem = JsonDocument.Parse(bytes, new() { AllowDuplicateProperties = false });
                if (problem.RootElement.TryGetProperty("title", out var title))
                {
                    var value = title.GetString();
                    if (value is { Length: > 8 and <= 80 } && value.StartsWith("CONTROL_", StringComparison.Ordinal)
                        && value.All(c => c is >= 'A' and <= 'Z' or '_')) code = value;
                }
                if (problem.RootElement.TryGetProperty("supportReference", out var reference)
                    && reference.ValueKind == JsonValueKind.String && Guid.TryParseExact(reference.GetString(), "N", out var id)) support = id.ToString("N");
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException) { }
            throw new HostedReaderFault(code ?? "HOSTED_REQUEST_UNCONFIRMED", new { StatusCode = (int)response.StatusCode, SupportReference = support }, (int)response.StatusCode);
        }
        if ((int)response.StatusCode != expectedStatus || response.Headers.CacheControl?.NoStore != true || response.Content.Headers.ContentType?.MediaType != "application/json")
            throw new HostedReaderFault("HOSTED_RESPONSE_INVALID");
        return JsonDocument.Parse(bytes, new() { AllowDuplicateProperties = false });
    }

    internal static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > limit) throw new HostedReaderFault("HOSTED_RESPONSE_TOO_LARGE");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var result = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0) return result.ToArray();
            if (result.Length + read > limit) throw new HostedReaderFault("HOSTED_RESPONSE_TOO_LARGE");
            result.Write(buffer, 0, read);
        }
    }
    public void Dispose() => _http.Dispose();
}
