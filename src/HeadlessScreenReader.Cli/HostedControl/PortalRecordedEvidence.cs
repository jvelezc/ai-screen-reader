using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli.HostedControl;

internal sealed record PortalRecordedEvidenceResult(EvidenceScanRecord? Record, EvidenceSourceProof Proof,
    ImmutableArray<EvidenceArtifactProof> Artifacts);

/// <summary>Original-caller metadata and complete producer originals precede any recorded-fact projection.</summary>
internal sealed class PortalRecordedEvidence(IPortalProjectClient project, TraversalArchive? archive)
{
    public async Task<PortalRecordedEvidenceResult> ReadAsync(PortalIdentity actor, Guid organization, Guid scanId,
        Func<Task> authorize, CancellationToken token)
    {
        if (scanId == Guid.Empty) throw new PortalFault(400, "CONTROL_EVIDENCE_SOURCE_INVALID");
        var scan = await project.ScanAsync(actor, organization, scanId, token).ConfigureAwait(false);
        var job = await project.JobAsync(actor, organization, scanId, token).ConfigureAwait(false);
        if (scan is null || scan.Id != scanId || scan.OrganizationId != organization || job is null || job.OrganizationId != organization || job.ScanId != scanId)
            throw new PortalFault(404, "CONTROL_SCAN_NOT_FOUND");
        await authorize().ConfigureAwait(false);
        PortalRecordedEvidenceResult Missing(string state, string issue) => new(null,
            EvidenceSourceProof.Missing with { State = state, Issues = [issue] }, []);
        if (job.State != "completed") return Missing("Pending", "The saved run has no completed recording yet. Open its status and review it again after collection finishes.");
        if (job.TraversalRoute == 0 || job.WorkerArchive is not { Recording: "KeyboardTraversal" } producer)
            return Missing("Unsupported", "This run did not retain a keyboard journey. Use an admitted recorded keyboard route.");
        var descriptor = new FixtureJob(organization, scanId, job.JobId, job.Generation)
        { RecordObservation = job.RecordObservation, TraversalRoute = job.TraversalRoute };
        try { producer.Validate(descriptor); }
        catch (BrowserOperationException) { throw new PortalFault(503, "CONTROL_EVIDENCE_IDENTITY_UNCONFIRMED"); }
        if (archive is null) return Missing("Unavailable", "The original recording store is unavailable. Its historical receipt cannot replace the original files.");
        try
        {
            var identity = producer.Receipt.Identity;
            var index = await archive.ReadIndexAsync(identity, token).ConfigureAwait(false);
            string[] names = ["evidence-manifest.json", "observation.json", "original.webm", "source-frames.zip", "traversal.json"];
            if (index.SourceManifestSha256 != producer.Receipt.SourceManifestSha256 || index.Files.Length != 5
                || index.Files.Sum(f => f.Bytes) != producer.Receipt.Bytes || !index.Files.Select(f => f.Path).SequenceEqual(names))
                throw new TraversalArchiveException(TraversalArchiveFailure.Invalid);
            async Task<byte[]> Read(string name, long limit)
            {
                var file = index.Files.Single(f => f.Path == name);
                var bytes = await archive.ReadOriginalAsync(identity, name, file.Sha256, limit, token).ConfigureAwait(false);
                await authorize().ConfigureAwait(false);
                return bytes;
            }
            var baselineBytes = await Read("observation.json", FixtureObservation.MaximumBytes).ConfigureAwait(false);
            var traversal = await Read("traversal.json", 2 * 1024 * 1024).ConfigureAwait(false);
            var video = await Read("original.webm", PassiveObservationRecording.MaximumBundleBytes).ConfigureAwait(false);
            var frames = await Read("source-frames.zip", PassiveObservationRecording.MaximumBundleBytes).ConfigureAwait(false);
            var manifest = await Read("evidence-manifest.json", 4096).ConfigureAwait(false);
            var bundle = new KeyboardRecordingBundle(baselineBytes, video, traversal, frames);
            var recording = bundle.Validate(descriptor);
            var baseline = ObservationStore.Validate(baselineBytes, descriptor);
            var expected = new WorkerTraversalEvidenceManifest(WorkerTraversalEvidenceManifest.CurrentSchema, descriptor, producer.WorkerId,
                TraversalArchive.Hash(baselineBytes), TraversalArchive.Hash(traversal), recording.VideoSha256, recording.SourceFramesSha256,
                recording.Receipt.AttemptedKeystrokes, recording.Receipt.CompletedKeystrokes, recording.Receipt.Execution.ToString(),
                recording.Receipt.Reachability.ToString(), recording.Receipt.Barrier.ToString(), recording.Receipt.StopReason.ToString());
            using var originalManifest = JsonDocument.Parse(manifest, new() { AllowDuplicateProperties = false });
            if (!JsonElement.DeepEquals(originalManifest.RootElement, JsonSerializer.SerializeToElement(expected, ObservationStore.Json)))
                throw new TraversalArchiveException(TraversalArchiveFailure.Invalid);
            var current = await project.JobAsync(actor, organization, scanId, token).ConfigureAwait(false);
            await authorize().ConfigureAwait(false);
            if (current != job) throw new PortalFault(409, "CONTROL_EVIDENCE_SOURCE_CHANGED");
            var record = RecordedEvidenceProjection.Create(recording, TraversalArchive.Hash(baselineBytes), TraversalArchive.Hash(traversal));
            EvidenceReadiness.Validate(record);
            var proofs = record.Artifacts.Select(a => new EvidenceArtifactProof(a.Id,
                a.Id == "AX" && baseline.RecordedSnapshots.IsDefaultOrEmpty ? "BaselineOnly" : "HashVerified", a.Sha256)).ToImmutableArray();
            return new(record, RecordedEvidenceProjection.Compare(record, record, producer, recording), proofs);
        }
        catch (TraversalArchiveException error)
        {
            return Missing(error.Failure.ToString(), "An original recording file is missing, changed or unavailable. Restore the exact originals or collect a new test; do not rely on file names or fingerprints alone.");
        }
        catch (Exception error) when (error is BrowserOperationException or JsonException or InvalidDataException or ArgumentException)
        {
            return Missing("Invalid", "The original recording does not match its admitted job, worker, recorded states or input events. Retain the originals and collect a new test.");
        }
    }
}
