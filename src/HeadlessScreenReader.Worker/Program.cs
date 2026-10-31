using System.Text.Json;
using System.Runtime.InteropServices;
using HeadlessScreenReader.Worker;
using HeadlessScreenReader.Domain;
using System.Security.Cryptography;

if (args.Length > 0 && args[0] == "--inspect-recorded-sources")
{
    if (args.Length != 5 || !Guid.TryParseExact(args[1], "D", out var organization) || organization == Guid.Empty
        || !Guid.TryParseExact(args[2], "D", out var job) || job == Guid.Empty
        || !long.TryParse(args[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var generation)
        || generation is < 1 or > 3 || !Guid.TryParseExact(args[4], "D", out var producer) || producer == Guid.Empty) return 2;
    try
    {
        var evidenceRoot = Environment.GetEnvironmentVariable("HSR_WORKER_EVIDENCE") ?? throw new InvalidOperationException();
        var sources = RecordedSourceJournal.Read(Path.Combine(evidenceRoot, ".runtime", "recorded-sources"), organization, job, generation, producer);
        Console.WriteLine(JsonSerializer.Serialize(sources, ObservationStore.Json)); return 0;
    }
    catch (Exception) { Console.Error.WriteLine("WORKER_SOURCE_INSPECTION_UNCONFIRMED"); return 3; }
}

if (args.Length > 0 && args[0] == "--inspect-command-progress")
{
    if (args.Length != 4 || !Guid.TryParse(args[1], out var organization) || !Guid.TryParse(args[2], out var job)
        || !long.TryParse(args[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var generation)) return 2;
    try
    {
        var evidenceRoot = Environment.GetEnvironmentVariable("HSR_WORKER_EVIDENCE") ?? throw new InvalidOperationException();
        var summary = CommandProgressJournal.Read(Path.Combine(evidenceRoot, ".runtime", "command-progress"), organization, job, generation);
        Console.WriteLine(JsonSerializer.Serialize(summary, ObservationStore.Json)); return 0;
    }
    catch (Exception) { Console.Error.WriteLine("WORKER_PROGRESS_INSPECTION_UNCONFIRMED"); return 3; }
}

var recordingRecovery = args.Length == 4 && args[0] == "--recover-recording";
Guid publicationOrganization = default, publicationJob = default; long publicationGeneration = default;
if (recordingRecovery && (!Guid.TryParseExact(args[1], "D", out publicationOrganization) || publicationOrganization == Guid.Empty
    || !Guid.TryParseExact(args[2], "D", out publicationJob) || publicationJob == Guid.Empty
    || !long.TryParse(args[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out publicationGeneration)
    || publicationGeneration is < 1 or > 3)) return 2;
if (!recordingRecovery && (args.Length != 1 || args[0] is not ("--once" or "--poll" or "--recover" or "--check-runtime" or "--check-reader-runtime" or "--recover-reader" or "--host-reader")))
{
    Console.Error.WriteLine("Usage: HeadlessScreenReader.Worker --once | --poll | --recover | --check-runtime | --check-reader-runtime | --recover-reader | --host-reader | --recover-recording <organization> <job> <generation>. Operator configuration is supplied through the private service environment.");
    return 2;
}
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
using var termination = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM,
    context => { context.Cancel = true; stop.Cancel(); });
try
{
    if (recordingRecovery)
    {
        var result = await RecordedPublicationCommand.RunAsync(publicationOrganization, publicationJob, publicationGeneration, stop.Token);
        Console.WriteLine(JsonSerializer.Serialize(result, ObservationStore.Json));
        return result.State is "Published" or "AlreadyCompleted" ? 0 : 3;
    }
    var image = Environment.GetEnvironmentVariable("HSR_WORKER_IMAGE") ?? throw new InvalidOperationException();
    var evidence = Environment.GetEnvironmentVariable("HSR_WORKER_EVIDENCE") ?? throw new InvalidOperationException();
    var runtime = Environment.GetEnvironmentVariable("HSR_WORKER_RUNTIME") ?? "podman";
    if (args[0] == "--host-reader")
    {
        // Private operator proof of the same broker used by --poll, without queue/database authority.
        await using var host = await ReaderHost.StartAsync(evidence, runtime, image, Environment.GetEnvironmentVariable("HSR_WORKER_SECCOMP"));
        Console.WriteLine("READER_HOST_READY");
        var input = new byte[1];
        if (await Console.OpenStandardInput().ReadAsync(input, stop.Token) != 0) throw new InvalidDataException();
        return 0; // EOF joins actual reader drain before this worker exits.
    }
    if (args[0] is "--check-reader-runtime" or "--recover-reader")
    {
        // Independent reader journal: this probe may run while the queue worker retains its lock.
        await using var supervisor = await IsolatedReaderSupervisor.StartAsync(evidence, runtime, image,
            Environment.GetEnvironmentVariable("HSR_WORKER_SECCOMP"));
        if (args[0] == "--recover-reader")
        {
            Console.WriteLine(JsonSerializer.Serialize(new { Schema = "hsr-reader-recovery-v1", State = "ReaderRecovered", supervisor.Recovered }));
            return 0;
        }
        var organization = Guid.NewGuid(); var secondOrganization = Guid.NewGuid();
        await using var reader = await supervisor.OpenAsync(organization, Guid.NewGuid(), stop.Token);
        await using var secondReader = await supervisor.OpenAsync(secondOrganization, Guid.NewGuid(), stop.Token);
        var duplicateDenied = false;
        var capacityDenied = false;
        try { await supervisor.OpenAsync(Guid.NewGuid(), reader.SessionId, stop.Token); }
        catch (BrowserOperationException fault) when (fault.Code == "READER_SESSION_ALREADY_OPEN") { duplicateDenied = true; }
        try { await supervisor.OpenAsync(Guid.NewGuid(), Guid.NewGuid(), stop.Token); }
        catch (BrowserOperationException fault) when (fault.Code == "READER_SUPERVISOR_CAPACITY_UNCONFIRMED") { capacityDenied = true; }
        if (!duplicateDenied || !capacityDenied) throw new InvalidDataException();
        var wrongOrganizationDenied = false;
        try { await supervisor.RetireAsync(Guid.NewGuid(), reader.SessionId); }
        catch (BrowserOperationException fault) when (fault.Code == "READER_SESSION_NOT_FOUND") { wrongOrganizationDenied = true; }
        if (!wrongOrganizationDenied || reader.ContainerAbsenceConfirmed) throw new InvalidDataException();
        var first = reader.InitialSnapshot;
        var second = await reader.ObserveAsync(stop.Token);
        var third = await reader.ObserveAsync(stop.Token);
        if (first.DocumentId != second.DocumentId || first.DocumentId != third.DocumentId)
            throw new InvalidDataException();
        var retired = await supervisor.RetireAsync(organization, reader.SessionId);
        var sibling = await secondReader.ObserveAsync(stop.Token);
        var siblingPreserved = sibling.DocumentId == secondReader.InitialSnapshot.DocumentId && !sibling.Nodes.IsDefaultOrEmpty;
        if (!retired.NativeShutdownConfirmed || !retired.ContainerAbsenceConfirmed || !siblingPreserved) throw new InvalidDataException();
        await supervisor.RetireAsync(secondOrganization, secondReader.SessionId);
        await using var replacement = await supervisor.OpenAsync(Guid.NewGuid(), Guid.NewGuid(), stop.Token);
        await replacement.DisposeAsync();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = "hsr-reader-runtime-readiness-v1",
            State = "ReaderRuntimeReady",
            Image = image,
            Captures = 3,
            SameDocument = true,
            ObservedNodes = third.Nodes.Length,
            reader.ContainerAbsenceConfirmed,
            reader.NativeShutdownConfirmed,
            supervisor.Recovered,
            DuplicateDenied = duplicateDenied,
            CapacityDenied = capacityDenied,
            CapacityReusableAfterConfirmedRemoval = replacement.ContainerAbsenceConfirmed && replacement.NativeShutdownConfirmed,
            ScopedRetirementConfirmed = retired.NativeShutdownConfirmed && retired.ContainerAbsenceConfirmed,
            WrongOrganizationDenied = wrongOrganizationDenied,
            SiblingDocumentPreserved = siblingPreserved,
            DatabaseAccessed = false,
            ObservationPersisted = false,
            DispatchedInputs = 0,
            GoalVerified = false
        }));
        return 0;
    }
    var journal = new OwnershipJournal(Path.Combine(evidence, ".runtime"));
    using var exclusive = journal.AcquireExclusive();
    var executor = new IsolatedFixtureExecutor(runtime, image, Environment.GetEnvironmentVariable("HSR_WORKER_SECCOMP"), journal);
    var recovered = await executor.RecoverAsync();
    if (args[0] == "--recover")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { State = "Recovered", Recovered = recovered })); return 0;
    }
    if (args[0] == "--check-runtime")
    {
        // Operator canary only: no admission, queue capability, database access or saved observation.
        // Deployment supplies a separate private readiness root, outside admitted evidence.
        var canary = new FixtureJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        var bytes = await executor.ExecuteAsync(canary, stop.Token);
        var observation = ObservationStore.Validate(bytes, canary);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Schema = "hsr-runtime-readiness-v1",
            State = "RuntimeReady",
            Image = image,
            ObservedNodes = observation.Snapshot.Nodes.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            DatabaseAccessed = false,
            ObservationPersisted = false,
            DispatchedInputs = observation.DispatchedInputs,
            GoalVerified = observation.GoalVerified,
            Recovered = recovered
        }));
        return 0;
    }
    var connection = Environment.GetEnvironmentVariable("HSR_WORKER_DATABASE") ?? throw new InvalidOperationException();
    var archiveConfiguration = S3EvidenceConfiguration.Optional(Environment.GetEnvironmentVariable("HSR_EVIDENCE_S3_ACCESS_KEY"),
        Environment.GetEnvironmentVariable("HSR_EVIDENCE_S3_SECRET"));
    using var archive = archiveConfiguration is null ? null : new S3ObservationArchive(archiveConfiguration);
    using var producerObjects = archiveConfiguration is null ? null : new S3TraversalObjectStore(archiveConfiguration);
    var producer = new WorkerEvidenceFinalizer(new TraversalArchive(producerObjects is null
        ? new FileTraversalObjectStore(Path.Combine(evidence, "archives")) : producerObjects));
    await using var leases = new PostgresWorkerLeases(connection);
    var coordinator = new ObservationCoordinator(leases, executor, new(evidence, archive, producer));
    // The trusted coordinator owns native execution. The separate read-only HTTP service uses the protected loopback broker.
    await using var readerHost = args[0] == "--poll" ? await ReaderHost.StartAsync(evidence, runtime, image,
        Environment.GetEnvironmentVariable("HSR_WORKER_SECCOMP")) : null;
    var worker = Guid.NewGuid();
    string? previousState = null;
    do
    {
        var result = await coordinator.RunOnceAsync(worker, stop.Token);
        if (args[0] == "--once" || result.State != "Empty" || previousState != result.State)
            Console.WriteLine(JsonSerializer.Serialize(result));
        previousState = result.State;
        // Unknown ownership/cleanup halts this process, never continues to another job.
        if (result.State == "Unconfirmed") return 3;
        if (args[0] == "--once") return result.State == "Blocked" ? 3 : 0;
        await Task.Delay(TimeSpan.FromSeconds(2), stop.Token);
    } while (!stop.IsCancellationRequested);
    return 0;
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
catch (Exception)
{
    Console.Error.WriteLine("WORKER_STOPPED_UNCONFIRMED");
    return 3;
}
