using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Only coordinator-owned observations enter this producer path. Operator imports remain separate.</summary>
public sealed class WorkerEvidenceFinalizer(TraversalArchive archive)
{
    public async Task<WorkerEvidenceArchive> FinalizeAsync(FixtureJob job, Guid worker, byte[] observation,
        string digest, CancellationToken token, PassiveRecordingBundle? recording = null, KeyboardRecordingBundle? keyboard = null)
    {
        observation = observation.ToArray();
        if (recording is not null) recording = new(recording.Observation.ToArray(), recording.Video.ToArray(), recording.Capture.ToArray());
        if (keyboard is not null) keyboard = new(keyboard.Observation.ToArray(), keyboard.Video.ToArray(), keyboard.Traversal.ToArray(), keyboard.Sources.ToArray());
        ObservationStore.Validate(observation, job);
        if (worker == Guid.Empty || TraversalArchive.Hash(observation) != digest)
            throw new BrowserOperationException("WORKER_EVIDENCE_PROVENANCE_UNCONFIRMED");
        if (job.RecordObservation != (recording is not null || keyboard is not null) || (job.TraversalRoute > 0) != (keyboard is not null)
            || recording is not null && keyboard is not null)
            throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_UNCONFIRMED");
        if (recording is not null)
        {
            recording.Validate(job);
            if (!observation.AsSpan().SequenceEqual(recording.Observation))
                throw new BrowserOperationException("WORKER_PASSIVE_RECORDING_UNCONFIRMED");
        }
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new WorkerEvidenceManifest(
            WorkerEvidenceManifest.CurrentSchema, job, worker, digest), ObservationStore.Json);
        if (keyboard is not null)
        {
            var route = keyboard.Validate(job);
            if (!observation.AsSpan().SequenceEqual(keyboard.Observation)) throw new BrowserOperationException("WORKER_KEYBOARD_BASELINE_CHANGED");
            manifest = JsonSerializer.SerializeToUtf8Bytes(new WorkerTraversalEvidenceManifest(WorkerTraversalEvidenceManifest.CurrentSchema,
                job, worker, digest, TraversalArchive.Hash(keyboard.Traversal), route.VideoSha256, route.SourceFramesSha256,
                route.Receipt.AttemptedKeystrokes, route.Receipt.CompletedKeystrokes, route.Receipt.Execution.ToString(),
                route.Receipt.Reachability.ToString(), route.Receipt.Barrier.ToString(), route.Receipt.StopReason.ToString()), ObservationStore.Json);
        }
        TraversalArchiveSource Source(string path, byte[] bytes) =>
            new(path, bytes.Length, TraversalArchive.Hash(bytes), () => new MemoryStream(bytes, false));
        var files = new List<TraversalArchiveSource> { Source(TraversalArchive.SourceManifest, manifest), Source("observation.json", observation) };
        if (recording is not null)
        {
            files.Add(Source(PassiveObservationRecording.VideoFile, recording.Video));
            files.Add(Source(PassiveObservationRecording.CaptureFile, recording.Capture));
        }
        if (keyboard is not null)
        {
            files.Add(Source(PassiveObservationRecording.VideoFile, keyboard.Video));
            files.Add(Source(AdmittedKeyboardRecording.RecordingFile, keyboard.Traversal));
            files.Add(Source(AdmittedKeyboardRecording.FramesFile, keyboard.Sources));
        }
        var receipt = await archive.FinalizeAsync(job.OrganizationId, job.JobId, TraversalArchive.Hash(manifest), files, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var result = new WorkerEvidenceArchive(job, worker, receipt)
        {
            Recording = keyboard is not null ? "KeyboardTraversal" : job.RecordObservation ? "PassiveObservation" : "NotCollected",
            Origin = keyboard is not null ? "QueueWorkerTraversal" : "QueueWorkerObservation"
        };
        result.Validate(job, worker);
        return result;
    }
}
