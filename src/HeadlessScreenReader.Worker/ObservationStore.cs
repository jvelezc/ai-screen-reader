using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record StoredObservation(string Reference, string Sha256)
{
    public bool OffHostReadbackVerified { get; init; }
    public WorkerEvidenceArchive? WorkerArchive { get; init; }
}

/// <summary>Private local evidence. Flush and readback precede database completion.</summary>
public sealed class ObservationStore(string root, IObservationArchive? archive = null, WorkerEvidenceFinalizer? producer = null)
{
    public static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static FixtureObservation Validate(ReadOnlySpan<byte> bytes, FixtureJob expected)
    {
        if (bytes.Length is 0 or > FixtureObservation.MaximumBytes) throw new InvalidDataException("WORKER_OBSERVATION_SIZE_INVALID");
        using (var document = JsonDocument.Parse(bytes.ToArray(), new() { AllowDuplicateProperties = false }))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("Shutdown", out var shutdown) || shutdown.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("WORKER_SHUTDOWN_FACTS_MISSING");
            string[] facts = ["CollectorCallSettled", "CollectorStopAcknowledged", "TransportCallSettled", "ProcessCallSettled",
                "ProcessExitConfirmed", "ProcessExitConfirmationMilliseconds", "NativeElapsedMilliseconds", "BrowserDeadlineExceeded",
                "BrowserCallRejected", "OwnedProcessStopRequested", "OwnedProcessStopCallSettled"];
            if (facts.Any(name => !shutdown.TryGetProperty(name, out _))) throw new InvalidDataException("WORKER_SHUTDOWN_FACTS_MISSING");
        }
        var observation = JsonSerializer.Deserialize<FixtureObservation>(bytes, Json)
            ?? throw new InvalidDataException("WORKER_OBSERVATION_INVALID");
        observation.Validate(expected);
        return observation;
    }

    public Task<StoredObservation> PersistAsync(byte[] bytes, FixtureJob job, CancellationToken token, Guid? worker = null) =>
        PersistCoreAsync(bytes, job, token, worker, null);

    /// <summary>Storage-only finalization of validated complete output. Existing bytes may be reused exactly; normal persistence cannot overwrite.</summary>
    public Task<StoredObservation> RecoverAsync(RecordedOutputSnapshot output, Func<CancellationToken, Task<bool>> authorize, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(output); ArgumentNullException.ThrowIfNull(authorize);
        var descriptor = RecordedOutputProjection.Manifest(output, output.Header.Job, output.Header.WorkerId)
            ?? throw new BrowserOperationException("WORKER_PUBLICATION_CLEANUP_UNCONFIRMED");
        descriptor.Validate();
        return PersistCoreAsync(output.Bundle, descriptor.Job, token, descriptor.WorkerId, authorize);
    }

    private async Task<StoredObservation> PersistCoreAsync(byte[] bytes, FixtureJob job, CancellationToken token, Guid? worker,
        Func<CancellationToken, Task<bool>>? authorize)
    {
        bytes = bytes.ToArray();
        var keyboard = job.TraversalRoute > 0 ? KeyboardRecordingBundle.Decode(bytes, job) : null;
        var recording = job.RecordObservation && keyboard is null ? PassiveRecordingBundle.Decode(bytes, job) : null;
        if (keyboard is not null)
        {
            if (producer is null) throw new BrowserOperationException("WORKER_RECORDING_ARCHIVE_REQUIRED");
            bytes = keyboard.Observation;
        }
        if (recording is not null)
        {
            if (producer is null) throw new BrowserOperationException("WORKER_RECORDING_ARCHIVE_REQUIRED");
            bytes = recording.Observation;
        }
        Validate(bytes, job);
        var directory = Path.GetFullPath(root);
        var path = Path.Combine(directory, job.Reference.Replace('/', Path.DirectorySeparatorChar));
        if (authorize is not null)
        {
            await AuthorizedAsync(authorize, token).ConfigureAwait(false);
            RecoveryDirectory(directory, Path.GetDirectoryName(path)!);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Root is an operator-owned private directory, never supplied by a job or browser.
        var pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            if (authorize is not null && Path.Exists(path))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0
                    || new FileInfo(path).Length != bytes.Length) throw new BrowserOperationException("WORKER_PUBLICATION_ORIGINAL_CHANGED");
            }
            else
            {
                await using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await file.WriteAsync(bytes, token);
                    await file.FlushAsync(token);
                    file.Flush(flushToDisk: true);
                }
                File.Move(pending, path, overwrite: false);
            }
            var readback = await File.ReadAllBytesAsync(path, token);
            if (!bytes.AsSpan().SequenceEqual(readback)) throw new IOException("WORKER_EVIDENCE_READBACK_UNCONFIRMED");
            if (authorize is not null) await AuthorizedAsync(authorize, token).ConfigureAwait(false);
            var digest = Convert.ToHexStringLower(SHA256.HashData(readback));
            if (archive is not null)
                await archive.FinalizeAsync(job, readback, digest, token).ConfigureAwait(false);
            if (authorize is not null) await AuthorizedAsync(authorize, token).ConfigureAwait(false);
            var receipt = producer is null ? null : await producer.FinalizeAsync(job,
                worker ?? throw new BrowserOperationException("WORKER_EVIDENCE_PROVENANCE_UNCONFIRMED"), readback, digest, token, recording, keyboard).ConfigureAwait(false);
            if (authorize is not null) await AuthorizedAsync(authorize, token).ConfigureAwait(false);
            return new(job.Reference, digest) { OffHostReadbackVerified = archive is not null, WorkerArchive = receipt };
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    private static async Task AuthorizedAsync(Func<CancellationToken, Task<bool>> authorize, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!await authorize(token).ConfigureAwait(false)) throw new BrowserOperationException("WORKER_PUBLICATION_FENCE_UNCONFIRMED");
        token.ThrowIfCancellationRequested();
    }

    private static void RecoveryDirectory(string root, string destination)
    {
        var current = root;
        void EnsureDirectory()
        {
            Directory.CreateDirectory(current);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new BrowserOperationException("WORKER_PUBLICATION_PATH_UNCONFIRMED");
        }
        EnsureDirectory();
        foreach (var segment in Path.GetRelativePath(root, destination).Split(Path.DirectorySeparatorChar))
        {
            if (segment is ".." or "." or "") throw new BrowserOperationException("WORKER_PUBLICATION_PATH_UNCONFIRMED");
            current = Path.Combine(current, segment); EnsureDirectory();
        }
    }
}
