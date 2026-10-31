namespace HeadlessScreenReader.Worker;

internal static class RecordedPublicationCommand
{
    internal static async Task<RecordedPublicationResult> RunAsync(Guid organization, Guid job, long generation, CancellationToken token)
    {
        var evidence = Environment.GetEnvironmentVariable("HSR_WORKER_EVIDENCE") ?? throw new InvalidOperationException();
        var connection = Environment.GetEnvironmentVariable("HSR_WORKER_DATABASE") ?? throw new InvalidOperationException();
        var native = new OwnershipJournal(Path.Combine(evidence, ".runtime"));
        using var exclusive = native.AcquireExclusive();
        var output = RecordedOutputJournal.ReadCandidate(Path.Combine(evidence, ".runtime", "recorded-output"), organization, job, generation, token);
        var configuration = S3EvidenceConfiguration.Optional(Environment.GetEnvironmentVariable("HSR_EVIDENCE_S3_ACCESS_KEY"),
            Environment.GetEnvironmentVariable("HSR_EVIDENCE_S3_SECRET"));
        using var archive = configuration is null ? null : new S3ObservationArchive(configuration);
        using var producerObjects = configuration is null ? null : new S3TraversalObjectStore(configuration);
        var producer = new WorkerEvidenceFinalizer(new TraversalArchive(producerObjects is null
            ? new FileTraversalObjectStore(Path.Combine(evidence, "archives")) : producerObjects));
        await using var publications = new PostgresRecordedPublications(connection);
        var recovery = new RecordedPublicationRecovery(publications, new(evidence, archive, producer), TimeProvider.System);
        return await recovery.RunAsync(output, token).ConfigureAwait(false);
    }
}
