using System.Security.Cryptography;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli;

/// <summary>New scan-owned local files. Pending data survives failure; no crash/hosted durability claim.</summary>
public sealed class OwnedEvidenceFileStore : IOwnedEvidenceStore
{
    private readonly object _sync = new();
    private long _reservedBytes;
    private int _reservedFiles;
    public string ScanId { get; } = Guid.NewGuid().ToString("N");
    public string Folder { get; }
    public const int MaximumFileBytes = 4 * 1024 * 1024;
    public const long MaximumScanBytes = 64 * 1024 * 1024;
    public const int MaximumFiles = 1024;

    public OwnedEvidenceFileStore(string root)
    {
        Folder = Path.GetFullPath(Path.Combine(root, ScanId));
        Directory.CreateDirectory(Folder);
        using var manifest = new FileStream(Path.Combine(Folder, "scan.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(manifest, new { Schema = "hsr-owned-scan-v1", ScanId });
    }

    public Task<OwnedWriteReceipt> WriteIntentAsync(OwnedCommandIntent intent)
    {
        Validate(intent);
        return WriteAsync($"command-{intent.Ordinal:D6}.intent.json", intent);
    }
    public Task<OwnedWriteReceipt> WriteResultAsync(OwnedCommandEvidence evidence)
    {
        Validate(evidence.Intent);
        if (evidence.Snapshot is { } snapshot && snapshot.CaptureEpoch != evidence.Intent.Epoch)
            throw new IOException("OWNED_EVIDENCE_EPOCH_MISMATCH");
        return WriteAsync($"command-{evidence.Intent.Ordinal:D6}.result.json", evidence);
    }
    public Task<OwnedWriteReceipt> WriteCompletionAsync(OwnedCommandCompletion completion)
    {
        Validate(completion.Intent);
        return WriteAsync($"command-{completion.Intent.Ordinal:D6}.completion.json", completion);
    }
    public Task<OwnedWriteReceipt> WriteBaselineAsync(OwnedScanBaseline baseline)
    {
        if (baseline.ScanId != ScanId || baseline.Epoch < 2 || baseline.Snapshot.CaptureEpoch != baseline.Epoch)
            throw new IOException("OWNED_BASELINE_INVALID");
        return WriteAsync($"baseline-{baseline.Epoch:D6}.json", baseline);
    }
    public Task<OwnedWriteReceipt> WriteLifecycleAsync(OwnedScanLifecycle lifecycle)
    {
        if (lifecycle.ScanId != ScanId || lifecycle.Event.Sequence < 1 || lifecycle.Event.Epoch < 1)
            throw new IOException("OWNED_LIFECYCLE_INVALID");
        return WriteAsync($"lifecycle-{lifecycle.Event.Sequence:D6}.json", lifecycle);
    }
    private void Validate(OwnedCommandIntent intent)
    {
        if (intent.ScanId != ScanId || intent.Ordinal < 1 || intent.Epoch < 1)
            throw new IOException("OWNED_INTENT_INVALID");
    }
    private async Task<OwnedWriteReceipt> WriteAsync<T>(string name, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, TraversalDemo.JsonOptions);
        lock (_sync)
        {
            if (bytes.Length > MaximumFileBytes || _reservedBytes + bytes.Length > MaximumScanBytes || _reservedFiles >= MaximumFiles)
                throw new IOException("OWNED_STORE_LIMIT");
            _reservedBytes += bytes.Length;
            _reservedFiles++;
        }
        var path = Path.Combine(Folder, name);
        if (File.Exists(path)) throw new IOException("OWNED_ARTIFACT_EXISTS");
        var pending = path + ".pending";
        await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await stream.WriteAsync(bytes).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        File.Move(pending, path);
        return new(name, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}
