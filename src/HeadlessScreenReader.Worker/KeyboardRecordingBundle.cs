using System.IO.Compression;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Four exact original files through bounded stdout; no extraction or executable path handling.</summary>
public sealed record KeyboardRecordingBundle(byte[] Observation, byte[] Video, byte[] Traversal, byte[] Sources)
{
    private const int Limit = PassiveObservationRecording.MaximumBundleBytes;
    private static string Hash(byte[] value) => TraversalArchive.Hash(value);
    public AdmittedKeyboardRecording Validate(FixtureJob job)
    {
        if (job.TraversalRoute is < 1 or > 4 || !job.RecordObservation || Video.Length < 4
            || !Video.AsSpan().StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 })
            || Traversal.Length is < 1 or > 2 * 1024 * 1024 || Sources.Length is < 1 or > Limit)
            throw new BrowserOperationException("WORKER_KEYBOARD_RECORDING_UNCONFIRMED");
        var baseline = ObservationStore.Validate(Observation, job);
        var recording = JsonSerializer.Deserialize<AdmittedKeyboardRecording>(Traversal, ObservationStore.Json)
            ?? throw new BrowserOperationException("WORKER_KEYBOARD_RECORDING_UNCONFIRMED");
        recording.Validate(baseline, Hash(Observation), Hash(Video), Video.Length, Hash(Sources));
        if (!baseline.RecordedSnapshots.IsDefault && !JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(baseline.RecordedSnapshots, ObservationStore.Json),
            JsonSerializer.SerializeToElement(recording.Receipt.Observations, ObservationStore.Json)))
            throw new BrowserOperationException("WORKER_KEYBOARD_SNAPSHOTS_CHANGED");
        if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(baseline.Snapshot, ObservationStore.Json),
                JsonSerializer.SerializeToElement(recording.Receipt.Observations[0].Snapshot, ObservationStore.Json)))
            throw new BrowserOperationException("WORKER_KEYBOARD_BASELINE_CHANGED");
        using var input = new MemoryStream(Sources, false); using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count != recording.SourceFiles.Length || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count)
            throw new BrowserOperationException("WORKER_KEYBOARD_SOURCE_FRAMES_UNCONFIRMED");
        long total = 0;
        foreach (var frame in recording.SourceFiles)
        {
            var entry = zip.GetEntry(frame.Path) ?? throw new BrowserOperationException("WORKER_KEYBOARD_SOURCE_FRAMES_UNCONFIRMED");
            var bytes = Read(entry, 2 * 1024 * 1024); total += bytes.Length;
            if (total > Limit || bytes.Length != frame.Bytes || Hash(bytes) != frame.Sha256
                || !bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }))
                throw new BrowserOperationException("WORKER_KEYBOARD_SOURCE_FRAMES_UNCONFIRMED");
        }
        return recording;
    }

    public byte[] Encode(FixtureJob job)
    {
        Validate(job);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var (name, bytes) in Files()) { using var target = zip.CreateEntry(name, CompressionLevel.NoCompression).Open(); target.Write(bytes); }
        if (output.Length > Limit) throw new BrowserOperationException("WORKER_KEYBOARD_RECORDING_TOO_LARGE");
        return output.ToArray();
    }
    private IEnumerable<(string Name, byte[] Bytes)> Files() => [("observation.json", Observation),
        (PassiveObservationRecording.VideoFile, Video), (AdmittedKeyboardRecording.RecordingFile, Traversal), (AdmittedKeyboardRecording.FramesFile, Sources)];

    public static KeyboardRecordingBundle Decode(byte[] bytes, FixtureJob job)
    {
        if (bytes.Length is < 1 or > Limit) throw new BrowserOperationException("WORKER_KEYBOARD_RECORDING_TOO_LARGE");
        using var input = new MemoryStream(bytes, false); using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        string[] names = ["observation.json", PassiveObservationRecording.VideoFile, AdmittedKeyboardRecording.RecordingFile, AdmittedKeyboardRecording.FramesFile];
        if (zip.Entries.Count != names.Length || !zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal)))
            throw new BrowserOperationException("WORKER_KEYBOARD_RECORDING_UNCONFIRMED");
        var result = new KeyboardRecordingBundle(Read(zip.GetEntry(names[0])!, FixtureObservation.MaximumBytes),
            Read(zip.GetEntry(names[1])!, Limit), Read(zip.GetEntry(names[2])!, 2 * 1024 * 1024), Read(zip.GetEntry(names[3])!, Limit));
        if ((long)result.Observation.Length + result.Video.Length + result.Traversal.Length + result.Sources.Length > Limit)
            throw new BrowserOperationException("WORKER_KEYBOARD_RECORDING_TOO_LARGE");
        result.Validate(job); return result;
    }
    private static byte[] Read(ZipArchiveEntry entry, int limit)
    {
        if (entry.Length is < 1 || entry.Length > limit) throw new BrowserOperationException("WORKER_KEYBOARD_RECORDING_TOO_LARGE");
        using var source = entry.Open(); using var output = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var count = source.Read(buffer); if (count == 0) break;
            if (output.Length + count > limit) throw new BrowserOperationException("WORKER_KEYBOARD_RECORDING_TOO_LARGE");
            output.Write(buffer, 0, count);
        }
        if (output.Length != entry.Length) throw new BrowserOperationException("WORKER_KEYBOARD_RECORDING_UNCONFIRMED");
        return output.ToArray();
    }
}
