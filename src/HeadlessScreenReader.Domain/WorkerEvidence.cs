namespace HeadlessScreenReader.Domain;

/// <summary>The database attempt identity is (organization, job, generation), not an invented run UUID.</summary>
public sealed record WorkerEvidenceManifest(string Schema, FixtureJob Job, Guid WorkerId, string ObservationSha256)
{
    public const string CurrentSchema = "hsr-worker-observation-package-v1";
    public string Origin => "QueueWorkerObservation";
    public string Scope => "ControlledSyntheticFixtureObservation";
    public string Recording => Job.RecordObservation ? "PassiveObservation" : "NotCollected";
    public int DispatchedInputs => 0;
    public bool GoalVerified => false;
}

/// <summary>A receipt atomically bound to a completed admitted attempt; readback is historical, not current availability.</summary>
public sealed record WorkerEvidenceArchive(FixtureJob Job, Guid WorkerId, TraversalArchiveReceipt Receipt)
{
    public string Origin { get; init; } = "QueueWorkerObservation";
    public string Recording { get; init; } = "NotCollected";
    public void Validate(FixtureJob expected, Guid? worker = null)
    {
        expected.Validate();
        static bool Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
        var maximum = expected.RecordObservation ? PassiveObservationRecording.MaximumBundleBytes + 4096 : FixtureObservation.MaximumBytes + 4096;
        if (Origin != "QueueWorkerObservation" || Job != expected || WorkerId == Guid.Empty || worker is not null && WorkerId != worker
            || Recording != (expected.RecordObservation ? "PassiveObservation" : "NotCollected")
            || Receipt is not
            {
                Identity: { } identity, Bytes: > 0,
                ObjectReadbackVerified: true, IndexReadbackVerified: true
            }
            || Receipt.Files != (expected.RecordObservation ? 4 : 2) || Receipt.Bytes > maximum
            || identity.OrganizationId != expected.OrganizationId || identity.ArchiveId != expected.JobId
            || !Digest(identity.IndexSha256) || !Digest(Receipt.SourceManifestSha256))
            throw new BrowserOperationException("WORKER_EVIDENCE_PROVENANCE_UNCONFIRMED");
    }
}
