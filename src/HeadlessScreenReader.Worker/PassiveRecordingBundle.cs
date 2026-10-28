using System.IO.Compression;
using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Exact bounded stdout transport. No extraction, paths, host mounts or container-provided executable content.</summary>
public sealed record PassiveRecordingBundle(byte[] Observation, byte[] Video, byte[] Capture)
{
    public PassiveObservationRecording Validate(FixtureJob job)
    {
        var observation = ObservationStore.Validate(Observation, job);
        if (!job.RecordObservation || !Video.AsSpan().StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 })
            || Capture.Length is < 1 or > 256 * 1024)
            throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_UNCONFIRMED");
        var capture = JsonSerializer.Deserialize<PassiveObservationRecording>(Capture, ObservationStore.Json)
            ?? throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_UNCONFIRMED");
        capture.Validate(observation, TraversalArchive.Hash(Observation), TraversalArchive.Hash(Video), Video.Length);
        return capture;
    }

    public byte[] Encode(FixtureJob job)
    {
        Validate(job);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, bytes) in Files())
            {
                using var file = zip.CreateEntry(path, CompressionLevel.NoCompression).Open();
                file.Write(bytes);
            }
        if (output.Length > PassiveObservationRecording.MaximumBundleBytes)
            throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_TOO_LARGE");
        return output.ToArray();
    }

    private IEnumerable<(string Path, byte[] Bytes)> Files() =>
        [("observation.json", Observation), (PassiveObservationRecording.VideoFile, Video), (PassiveObservationRecording.CaptureFile, Capture)];

    public static PassiveRecordingBundle Decode(byte[] bytes, FixtureJob job)
    {
        if (!job.RecordObservation || bytes.Length is < 1 or > PassiveObservationRecording.MaximumBundleBytes)
            throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_UNCONFIRMED");
        using var input = new MemoryStream(bytes, false); using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        string[] paths = ["observation.json", PassiveObservationRecording.VideoFile, PassiveObservationRecording.CaptureFile];
        if (zip.Entries.Count != 3 || !zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).SequenceEqual(paths.Order(StringComparer.Ordinal)))
            throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_UNCONFIRMED");
        byte[] Read(string path, int limit)
        {
            var entry = zip.GetEntry(path)!;
            if (entry.Length is < 1 || entry.Length > limit)
                throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_TOO_LARGE");
            using var source = entry.Open(); using var result = new MemoryStream(); var buffer = new byte[8192];
            while (true)
            {
                var count = source.Read(buffer);
                if (count == 0) break;
                if (result.Length + count > limit) throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_TOO_LARGE");
                result.Write(buffer, 0, count);
            }
            if (result.Length != entry.Length) throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_UNCONFIRMED");
            return result.ToArray();
        }
        var bundle = new PassiveRecordingBundle(Read("observation.json", FixtureObservation.MaximumBytes),
            Read(PassiveObservationRecording.VideoFile, PassiveObservationRecording.MaximumBundleBytes), Read(PassiveObservationRecording.CaptureFile, 256 * 1024));
        if ((long)bundle.Observation.Length + bundle.Video.Length + bundle.Capture.Length > PassiveObservationRecording.MaximumBundleBytes)
            throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_TOO_LARGE");
        bundle.Validate(job);
        return bundle;
    }
}
