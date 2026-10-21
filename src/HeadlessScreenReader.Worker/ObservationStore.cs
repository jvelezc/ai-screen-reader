using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record StoredObservation(string Reference, string Sha256);

/// <summary>Private local evidence. Flush and readback precede database completion.</summary>
public sealed class ObservationStore(string root)
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

    public async Task<StoredObservation> PersistAsync(byte[] bytes, FixtureJob job, CancellationToken token)
    {
        bytes = bytes.ToArray();
        Validate(bytes, job);
        var directory = Path.GetFullPath(root);
        var path = Path.Combine(directory, job.Reference.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Root is an operator-owned private directory, never supplied by a job or browser.
        var pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            await using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await file.WriteAsync(bytes, token);
                await file.FlushAsync(token);
                file.Flush(flushToDisk: true);
            }
            File.Move(pending, path, overwrite: false);
            var readback = await File.ReadAllBytesAsync(path, token);
            if (!bytes.AsSpan().SequenceEqual(readback)) throw new IOException("WORKER_EVIDENCE_READBACK_UNCONFIRMED");
            return new(job.Reference, Convert.ToHexStringLower(SHA256.HashData(readback)));
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
}
