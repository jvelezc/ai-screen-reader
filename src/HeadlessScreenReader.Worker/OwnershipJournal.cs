using System.Text.Json;
using System.Text.RegularExpressions;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

public sealed record NativeOwnership(string Schema, Guid Owner, FixtureJob Job, string Runtime, string Image)
{
    public const string CurrentSchema = "hsr-native-ownership-v1";
    public string ContainerName => "hsr-observation-" + Owner.ToString("N");
}
/// <summary>Actual container absence, never an inferred timely Chromium exit or completed observation.</summary>
public sealed record NativeRemoval(string Schema, NativeOwnership Intent, DateTimeOffset ObservedAt,
    bool ContainerAbsenceConfirmed, bool RecoveredAfterRestart);

public sealed class OwnershipJournal
{
    private readonly string _root;
    public string IntentsDirectory { get; }
    public string RemovalsDirectory { get; }
    public OwnershipJournal(string root)
    {
        _root = Path.GetFullPath(root);
        IntentsDirectory = Path.Combine(_root, "intents");
        RemovalsDirectory = Path.Combine(_root, "removals");
        Directory.CreateDirectory(IntentsDirectory); Directory.CreateDirectory(RemovalsDirectory);
    }
    public FileStream AcquireExclusive() => new(Path.Combine(_root, "worker.lock"), FileMode.OpenOrCreate,
        FileAccess.ReadWrite, FileShare.None);

    public async Task<NativeOwnership> BeginAsync(FixtureJob job, string runtime, string image, CancellationToken token)
    {
        var intent = new NativeOwnership(NativeOwnership.CurrentSchema, Guid.NewGuid(), job, runtime, image);
        Validate(intent);
        await WriteNewAsync(IntentPath(intent), intent, token);
        return intent;
    }
    public IReadOnlyList<NativeOwnership> ReadPending()
    {
        var result = new List<NativeOwnership>();
        // Validate the entire inventory before the caller may stop even one container.
        foreach (var path in Directory.EnumerateFiles(IntentsDirectory))
        {
            if (Path.GetExtension(path) != ".json") throw new InvalidDataException("WORKER_NATIVE_INTENT_INVALID");
            var intent = Read<NativeOwnership>(path);
            Validate(intent);
            if (Path.GetFileNameWithoutExtension(path) != intent.Owner.ToString("N"))
                throw new InvalidDataException("WORKER_NATIVE_INTENT_IDENTITY_MISMATCH");
            result.Add(intent);
        }
        return result;
    }
    public async Task RecordRemovalAsync(NativeOwnership intent, bool recovered, CancellationToken token)
    {
        Validate(intent);
        var path = Path.Combine(RemovalsDirectory, intent.Owner.ToString("N") + ".json");
        if (File.Exists(path))
        {
            var prior = Read<NativeRemoval>(path);
            if (prior.Schema != "hsr-native-removal-v1" || prior.Intent != intent || !prior.ContainerAbsenceConfirmed)
                throw new InvalidDataException("WORKER_NATIVE_REMOVAL_IDENTITY_MISMATCH");
        }
        else await WriteNewAsync(path, new NativeRemoval("hsr-native-removal-v1", intent, DateTimeOffset.UtcNow, true, recovered), token);
        // A durable acknowledgement precedes retirement. Restart can reconcile an interrupted retirement.
        File.Delete(IntentPath(intent));
    }
    private string IntentPath(NativeOwnership intent) => Path.Combine(IntentsDirectory, intent.Owner.ToString("N") + ".json");
    public static void Validate(NativeOwnership intent)
    {
        if (intent.Schema != NativeOwnership.CurrentSchema || intent.Owner == Guid.Empty || intent.Job is null
            || intent.Runtime is not ("podman" or "docker") || intent.Image is null
            || !Regex.IsMatch(intent.Image, "^sha256:[a-f0-9]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidDataException("WORKER_NATIVE_INTENT_INVALID");
        intent.Job.Validate();
    }
    private static T Read<T>(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is 0 or > 8192) throw new InvalidDataException("WORKER_NATIVE_RECORD_SIZE_INVALID");
        var bytes = new byte[8193];
        var length = file.ReadAtLeast(bytes, 8193, throwOnEndOfStream: false);
        if (length > 8192) throw new InvalidDataException("WORKER_NATIVE_RECORD_SIZE_INVALID");
        return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, length), ObservationStore.Json)
            ?? throw new InvalidDataException("WORKER_NATIVE_RECORD_INVALID");
    }
    private static async Task WriteNewAsync<T>(string path, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, ObservationStore.Json);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await file.WriteAsync(bytes, token); await file.FlushAsync(token); file.Flush(flushToDisk: true);
    }
}
