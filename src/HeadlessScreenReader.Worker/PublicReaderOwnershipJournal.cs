using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>One declared public reader and its exact three-container/two-network ownership group.</summary>
public sealed record PublicReaderOwnership(string Schema, Guid Owner, Guid OrganizationId, Guid SessionId,
    string Runtime, string Image, PublicReaderSource Source)
{
    public const string CurrentSchema = "hsr-public-reader-ownership-v1";
    [JsonIgnore] public string Prefix => "hsr-public-" + Owner.ToString("N");
    [JsonIgnore] public string Worker => Prefix + "-worker";
    [JsonIgnore] public string Proxy => Prefix + "-proxy";
    [JsonIgnore] public string Fence => Prefix + "-fence";
    [JsonIgnore] public string InternalNetwork => Prefix + "-private";
    [JsonIgnore] public string EgressNetwork => Prefix + "-egress";
}

/// <summary>Independent resource absence; not a timely browser exit, observed page or completed task.</summary>
public sealed record PublicReaderRemoval(string Schema, PublicReaderOwnership Intent, DateTimeOffset ObservedAt,
    bool ContainersAbsenceConfirmed, bool NetworksAbsenceConfirmed, bool RecoveredAfterRestart);

/// <summary>The supervisor holds the exclusive lock from recovery through final owned-resource retirement.</summary>
public sealed class PublicReaderOwnershipJournal
{
    public const int MaximumRecordBytes = 16384;
    private readonly string _root;
    private readonly TimeProvider _time;
    public string IntentsDirectory { get; }
    public string RemovalsDirectory { get; }

    public PublicReaderOwnershipJournal(string root, TimeProvider? time = null)
    {
        _root = Path.GetFullPath(root); _time = time ?? TimeProvider.System;
        IntentsDirectory = Path.Combine(_root, "intents"); RemovalsDirectory = Path.Combine(_root, "removed");
        Directory.CreateDirectory(IntentsDirectory); Directory.CreateDirectory(RemovalsDirectory);
    }

    public FileStream AcquireExclusive() => new(Path.Combine(_root, "public-reader.lock"), FileMode.OpenOrCreate,
        FileAccess.ReadWrite, FileShare.None);

    public async Task<PublicReaderOwnership> BeginAsync(Guid organization, Guid session, PublicReaderSource source,
        string runtime, string image, CancellationToken token = default)
    {
        var intent = new PublicReaderOwnership(PublicReaderOwnership.CurrentSchema, Guid.NewGuid(), organization, session, runtime, image, source);
        Validate(intent); token.ThrowIfCancellationRequested();
        await WriteNewAsync(IntentPath(intent), intent, token).ConfigureAwait(false);
        return intent;
    }

    public IReadOnlyList<PublicReaderOwnership> ReadPending()
    {
        var inventory = new List<PublicReaderOwnership>();
        // Validate the complete inventory before callers may remove any native resource.
        foreach (var path in Directory.EnumerateFiles(IntentsDirectory))
        {
            if (Path.GetExtension(path) != ".json") throw new InvalidDataException("PUBLIC_READER_OWNERSHIP_INVALID");
            var intent = Read<PublicReaderOwnership>(path); Validate(intent);
            if (Path.GetFileNameWithoutExtension(path) != intent.Owner.ToString("N"))
                throw new InvalidDataException("PUBLIC_READER_OWNERSHIP_IDENTITY_MISMATCH");
            inventory.Add(intent);
        }
        return inventory;
    }

    /// <summary>Call only after independent confirmation that all three containers and both networks are absent.</summary>
    public async Task RecordRemovalAsync(PublicReaderOwnership intent, bool recovered, CancellationToken token = default)
    {
        Validate(intent);
        var intentPath = IntentPath(intent);
        var removalPath = Path.Combine(RemovalsDirectory, intent.Owner.ToString("N") + ".json");
        PublicReaderRemoval? prior = null;
        if (File.Exists(removalPath))
        {
            prior = Read<PublicReaderRemoval>(removalPath);
            if (prior.Schema != "hsr-public-reader-removal-v1" || prior.Intent != intent || prior.ObservedAt == default
                || !prior.ContainersAbsenceConfirmed || !prior.NetworksAbsenceConfirmed)
                throw new InvalidDataException("PUBLIC_READER_REMOVAL_IDENTITY_MISMATCH");
        }
        if (File.Exists(intentPath))
        {
            var saved = Read<PublicReaderOwnership>(intentPath); Validate(saved);
            if (saved != intent) throw new InvalidDataException("PUBLIC_READER_OWNERSHIP_IDENTITY_MISMATCH");
        }
        else if (prior is null) throw new InvalidDataException("PUBLIC_READER_OWNERSHIP_MISSING");
        if (prior is null)
            await WriteNewAsync(removalPath, new PublicReaderRemoval("hsr-public-reader-removal-v1", intent,
                _time.GetUtcNow(), true, true, recovered), token).ConfigureAwait(false);
        // Durable absence acknowledgement precedes exact intent retirement. Retry can reconcile an interrupted delete.
        File.Delete(intentPath);
    }

    public static void Validate(PublicReaderOwnership intent)
    {
        if (intent.Schema != PublicReaderOwnership.CurrentSchema || intent.Owner == Guid.Empty || intent.OrganizationId == Guid.Empty
            || intent.SessionId == Guid.Empty || intent.Source is null || intent.Runtime is not ("podman" or "docker")
            || intent.Image is null || !Regex.IsMatch(intent.Image, "\\Asha256:[a-f0-9]{64}\\z", RegexOptions.NonBacktracking))
            throw new InvalidDataException("PUBLIC_READER_OWNERSHIP_INVALID");
        try { intent.Source.Validate(); }
        catch (BrowserOperationException) { throw new InvalidDataException("PUBLIC_READER_OWNERSHIP_INVALID"); }
    }

    private string IntentPath(PublicReaderOwnership intent) => Path.Combine(IntentsDirectory, intent.Owner.ToString("N") + ".json");

    private static T Read<T>(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is 0 or > MaximumRecordBytes) throw new InvalidDataException("PUBLIC_READER_OWNERSHIP_SIZE_INVALID");
        var bytes = new byte[MaximumRecordBytes + 1];
        var count = file.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (count > MaximumRecordBytes) throw new InvalidDataException("PUBLIC_READER_OWNERSHIP_SIZE_INVALID");
        try
        {
            return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, count), ObservationStore.Json)
                ?? throw new InvalidDataException("PUBLIC_READER_OWNERSHIP_INVALID");
        }
        catch (JsonException) { throw new InvalidDataException("PUBLIC_READER_OWNERSHIP_INVALID"); }
    }

    private static async Task WriteNewAsync<T>(string path, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, ObservationStore.Json);
        if (bytes.Length > MaximumRecordBytes) throw new InvalidDataException("PUBLIC_READER_OWNERSHIP_SIZE_INVALID");
        token.ThrowIfCancellationRequested();
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await file.WriteAsync(bytes, token).ConfigureAwait(false);
        await file.FlushAsync(token).ConfigureAwait(false); file.Flush(flushToDisk: true);
    }
}
