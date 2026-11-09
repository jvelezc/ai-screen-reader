using System.Net;
using System.Net.Http.Headers;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Private content-addressed task originals. Writes retain a local copy and verify provider readback.</summary>
public sealed class PublicTaskEvidenceStore : IDisposable
{
    private readonly string _root;
    private readonly S3EvidenceConfiguration? _configuration;
    private readonly HttpClient? _http;
    public PublicTaskEvidenceStore(string evidence, S3EvidenceConfiguration? configuration)
    { _root = Path.Combine(Path.GetFullPath(evidence), "public-task-originals"); _configuration = configuration; _http = configuration?.CreateClient(); }
    private string Local(PublicTaskJob job, string digest)
    {
        job.Validate(); if (!PublicTaskRules.Digest(digest)) throw new BrowserOperationException("PUBLIC_TASK_ORIGINAL_INVALID");
        return Path.Combine(_root, job.OrganizationId.ToString("N"), job.JobId.ToString("N"), digest);
    }
    private Uri Remote(PublicTaskJob job, string digest) => new(_configuration!.Endpoint,
        $"{_configuration.Bucket}/public-tasks/{job.OrganizationId:N}/{job.JobId:N}/{digest}");
    public async Task WriteAsync(PublicTaskJob job, string digest, byte[] bytes, CancellationToken token)
    {
        if (bytes.Length is < 1 or > PublicTaskReport.MaximumArchiveBytes || PublicTaskRules.Hash(bytes) != digest)
            throw new BrowserOperationException("PUBLIC_TASK_ORIGINAL_INVALID");
        var path = Local(job, digest); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            var prior = await ReadLocalAsync(path, bytes.Length, token).ConfigureAwait(false);
            if (!bytes.AsSpan().SequenceEqual(prior)) throw new IOException();
        }
        else
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await file.WriteAsync(bytes, token).ConfigureAwait(false); await file.FlushAsync(token).ConfigureAwait(false); file.Flush(true);
        }
        if (_http is null) return;
        var existing = await ReadAsync(job, digest, bytes.Length, token, missingAllowed: true).ConfigureAwait(false);
        if (existing is not null) return;
        using var request = new HttpRequestMessage(HttpMethod.Put, Remote(job, digest)) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new("application/octet-stream");
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", digest);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK) throw new BrowserOperationException("PUBLIC_TASK_STORAGE_UNAVAILABLE");
        _ = await ReadAsync(job, digest, bytes.Length, token).ConfigureAwait(false);
    }

    public async Task<byte[]?> ReadAsync(PublicTaskJob job, string digest, int size, CancellationToken token, bool missingAllowed = false)
    {
        var path = Local(job, digest);
        if (size is < 1 or > PublicTaskReport.MaximumArchiveBytes) throw new BrowserOperationException("PUBLIC_TASK_ORIGINAL_INVALID");
        byte[] bytes;
        if (_http is null)
        {
            if (missingAllowed && !File.Exists(path)) return null;
            bytes = await ReadLocalAsync(path, size, token).ConfigureAwait(false);
        }
        else
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Remote(job, digest));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound && missingAllowed) return null;
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength != size) throw new BrowserOperationException("PUBLIC_TASK_STORAGE_UNAVAILABLE");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            if (stream.ReadByte() != -1) throw new IOException();
        }
        if (bytes.Length != size || PublicTaskRules.Hash(bytes) != digest) throw new BrowserOperationException("PUBLIC_TASK_ORIGINAL_CHANGED");
        return bytes;
    }

    public async Task<byte[]> PageAsync(PublicTaskJob job, PublicTaskFile original, int offset, CancellationToken token)
    {
        _ = Local(job, original.Sha256);
        if (original.Bytes is < 1 or > PublicTaskReport.MaximumArchiveBytes || offset < 0 || offset > original.Bytes
            || offset != original.Bytes && offset % 65536 != 0) throw new BrowserOperationException("PUBLIC_TASK_ORIGINAL_INVALID");
        var count = Math.Min(65536, original.Bytes - offset);
        if (count == 0) return [];
        if (_http is null)
        {
            var whole = await ReadAsync(job, original.Sha256, original.Bytes, token).ConfigureAwait(false);
            return whole!.AsSpan(offset, count).ToArray();
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, Remote(job, original.Sha256));
        request.Headers.Range = new RangeHeaderValue(offset, offset + count - 1);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        var range = response.Content.Headers.ContentRange;
        if (response.StatusCode != HttpStatusCode.PartialContent || range?.From != offset || range.To != offset + count - 1
            || range.Length != original.Bytes || response.Content.Headers.ContentLength != count) throw new BrowserOperationException("PUBLIC_TASK_ORIGINAL_CHANGED");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var data = new byte[count]; await input.ReadExactlyAsync(data, token).ConfigureAwait(false);
        if (input.ReadByte() != -1) throw new IOException(); return data;
    }
    private static async Task<byte[]> ReadLocalAsync(string path, int size, CancellationToken token)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null || info.Length != size) throw new BrowserOperationException("PUBLIC_TASK_ORIGINAL_CHANGED");
        return await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
    }
    public void Dispose() => _http?.Dispose();
}
