using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Mcp;

/// <summary>Stateless bounded archive reads; every operation returns to core for current caller authorization.</summary>
public sealed class HostedArchiveClient : IAsyncDisposable
{
    private const long MaximumFileBytes = 128L * 1024 * 1024;
    private const int PageBytes = 65536;
    private readonly HostedReaderOptions _options;
    private readonly HostedReaderTransport _transport;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    public HostedArchiveClient(HostedReaderOptions options, HttpMessageHandler? transport = null)
    { _options = options; _transport = new(options, transport); }

    public Task<ToolReply> ManifestAsync(string archiveId, string indexSha256, int offset = 0, CancellationToken token = default)
    {
        if (!Identity(archiveId, indexSha256, out var identity)) return Task.FromResult(ToolReply.Error("ARCHIVE_IDENTITY_INVALID"));
        if (offset is < 0 or > 4097) return Task.FromResult(ToolReply.Error("ARCHIVE_PAGE_INVALID"));
        return ReadAsync(identity!, "manifest", new { indexSha256, offset }, document =>
        {
            var page = Parse<TraversalArchiveManifestPage>(document);
            Require(page.Identity == identity && Digest(page.SourceManifestSha256) && page.Offset == offset
                && page.TotalFiles is >= 1 and <= 4097 && offset <= page.TotalFiles
                && page.TotalBytes is >= 0 and <= 516L * 1024 * 1024
                && !page.Files.IsDefault && page.Files.Length == Math.Min(32, page.TotalFiles - offset));
            var next = offset + page.Files.Length;
            Require(page.NextOffset == (next < page.TotalFiles ? next : null)
                && page.Files.All(f => f is not null && Path(f.Path) && f.Bytes is >= 0 and <= MaximumFileBytes && Digest(f.Sha256))
                && page.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == page.Files.Length
                && page.Files.Select(f => f.Path).SequenceEqual(page.Files.Select(f => f.Path).Order(StringComparer.Ordinal))
                && page.Files.Sum(f => f.Bytes) <= page.TotalBytes);
            return new("archive-manifest", null, ScreenReaderSession.Profile, page);
        }, token);
    }

    public Task<ToolReply> FileAsync(string archiveId, string indexSha256, string path, string fileSha256,
        long offset = 0, CancellationToken token = default)
    {
        if (!Identity(archiveId, indexSha256, out var identity) || !Digest(fileSha256)) return Task.FromResult(ToolReply.Error("ARCHIVE_IDENTITY_INVALID"));
        if (!Path(path) || offset is < 0 or > MaximumFileBytes) return Task.FromResult(ToolReply.Error("ARCHIVE_PAGE_INVALID"));
        return ReadAsync(identity!, "file", new { indexSha256, path, offset }, document =>
        {
            var response = Parse<TraversalArchiveReadPage>(document); var page = response.Page;
            Require(page is not null && page.Identity == identity && page.Path == path && page.FileSha256 == fileSha256
                && page.Offset == offset && page.FileBytes is >= 0 and <= MaximumFileBytes && offset <= page.FileBytes
                && (offset == page.FileBytes || offset % PageBytes == 0)
                && page.Data is not null && page.Data.Length == Math.Min(PageBytes, page.FileBytes - offset)
                && Digest(response.PageSha256) && Hash(page.Data) == response.PageSha256);
            var next = offset + page!.Data.Length;
            Require(page.NextOffset == (next < page.FileBytes ? next : null));
            return new("archive-file", null, ScreenReaderSession.Profile, response);
        }, token);
    }

    private bool Identity(string archive, string digest, out TraversalArchiveIdentity? identity)
    {
        identity = null;
        if (!Guid.TryParse(archive, out var id) || id == Guid.Empty || !Digest(digest)) return false;
        identity = new(_options.Organization, id, digest); return true;
    }
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static bool Path(string? value) => value is { Length: > 0 and <= 240 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '.' or '-' or '_')
        && value.Split('/') is { Length: <= 8 } segments && segments.All(s => s.Length > 0 && s is not ("." or ".."));
    private static T Parse<T>(JsonDocument document)
    {
        try { return document.RootElement.Deserialize<T>(HostedReaderTransport.Json) ?? throw new HostedReaderFault("HOSTED_RESPONSE_INVALID"); }
        catch (JsonException) { throw new HostedReaderFault("HOSTED_RESPONSE_INVALID"); }
    }
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value)
    { if (!value) throw new HostedReaderFault("HOSTED_RESPONSE_INVALID"); }

    private async Task<ToolReply> ReadAsync(TraversalArchiveIdentity identity, string operation, object body,
        Func<JsonDocument, ToolReply> parse, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_disposed) return ToolReply.Error("WORKSPACE_CLOSED");
            token.ThrowIfCancellationRequested();
            using var document = await _transport.SendAsync(HttpMethod.Post,
                $"/api/workspaces/{identity.OrganizationId:D}/archives/{identity.ArchiveId:D}/{operation}", body).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var reply = parse(document);
            Require(JsonSerializer.SerializeToUtf8Bytes(reply, ToolReply.JsonOptions).Length <= 131072);
            return reply;
        }
        catch (HostedReaderFault fault)
        { return new("error", null, ScreenReaderSession.Profile, new { Upstream = fault.Facts, AutomaticRetry = false }, fault.Code); }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException
            or InvalidOperationException or NotSupportedException or IOException or ArgumentException)
        { return ToolReply.Error("HOSTED_ARCHIVE_READ_UNCONFIRMED"); }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (!_disposed) { _disposed = true; _transport.Dispose(); } }
        finally { _gate.Release(); }
    }
}
