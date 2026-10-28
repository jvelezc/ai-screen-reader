using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Only coordinator-owned observations enter this producer path. Operator imports remain separate.</summary>
public sealed class WorkerEvidenceFinalizer(TraversalArchive archive)
{
    public async Task<WorkerEvidenceArchive> FinalizeAsync(FixtureJob job, Guid worker, byte[] observation,
        string digest, CancellationToken token, PassiveRecordingBundle? recording = null)
    {
        observation = observation.ToArray();
        if (recording is not null) recording = new(recording.Observation.ToArray(), recording.Video.ToArray(), recording.Capture.ToArray());
        ObservationStore.Validate(observation, job);
        if (worker == Guid.Empty || TraversalArchive.Hash(observation) != digest)
            throw new BrowserOperationException("WORKER_EVIDENCE_PROVENANCE_UNCONFIRMED");
        if (job.RecordObservation != (recording is not null))
            throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_UNCONFIRMED");
        if (recording is not null)
        {
            recording.Validate(job);
            if (!observation.AsSpan().SequenceEqual(recording.Observation))
                throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_UNCONFIRMED");
        }
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new WorkerEvidenceManifest(
            WorkerEvidenceManifest.CurrentSchema, job, worker, digest), ObservationStore.Json);
        TraversalArchiveSource Source(string path, byte[] bytes) =>
            new(path, bytes.Length, TraversalArchive.Hash(bytes), () => new MemoryStream(bytes, false));
        var files = new List<TraversalArchiveSource> { Source(TraversalArchive.SourceManifest, manifest), Source("observation.json", observation) };
        if (recording is not null)
        {
            files.Add(Source(PassiveObservationRecording.VideoFile, recording.Video));
            files.Add(Source(PassiveObservationRecording.CaptureFile, recording.Capture));
        }
        var receipt = await archive.FinalizeAsync(job.OrganizationId, job.JobId, TraversalArchive.Hash(manifest), files, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var result = new WorkerEvidenceArchive(job, worker, receipt) { Recording = job.RecordObservation ? "PassiveObservation" : "NotCollected" };
        result.Validate(job, worker);
        return result;
    }
}
