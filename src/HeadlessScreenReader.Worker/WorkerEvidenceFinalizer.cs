using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Only coordinator-owned observations enter this producer path. Operator imports remain separate.</summary>
public sealed class WorkerEvidenceFinalizer(TraversalArchive archive)
{
    public async Task<WorkerEvidenceArchive> FinalizeAsync(FixtureJob job, Guid worker, byte[] observation,
        string digest, CancellationToken token)
    {
        observation = observation.ToArray();
        ObservationStore.Validate(observation, job);
        if (worker == Guid.Empty || TraversalArchive.Hash(observation) != digest)
            throw new BrowserOperationException("WORKER_EVIDENCE_PROVENANCE_UNCONFIRMED");
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new WorkerEvidenceManifest(
            WorkerEvidenceManifest.CurrentSchema, job, worker, digest), ObservationStore.Json);
        TraversalArchiveSource Source(string path, byte[] bytes) =>
            new(path, bytes.Length, TraversalArchive.Hash(bytes), () => new MemoryStream(bytes, false));
        var receipt = await archive.FinalizeAsync(job.OrganizationId, job.JobId, TraversalArchive.Hash(manifest),
            [Source(TraversalArchive.SourceManifest, manifest), Source("observation.json", observation)], token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var result = new WorkerEvidenceArchive(job, worker, receipt);
        result.Validate(job, worker);
        return result;
    }
}
