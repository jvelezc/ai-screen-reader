using System.Text.Json;
using System.Runtime.InteropServices;
using HeadlessScreenReader.Worker;
using HeadlessScreenReader.Domain;
using System.Security.Cryptography;

if (args.Length != 1 || args[0] is not ("--once" or "--poll" or "--recover" or "--check-runtime"))
{
    Console.Error.WriteLine("Usage: HeadlessScreenReader.Worker --once | --poll | --recover | --check-runtime. Operator configuration is supplied through the private service environment.");
    return 2;
}
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
using var termination = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM,
    context => { context.Cancel = true; stop.Cancel(); });
try
{
    var image = Environment.GetEnvironmentVariable("HSR_WORKER_IMAGE") ?? throw new InvalidOperationException();
    var evidence = Environment.GetEnvironmentVariable("HSR_WORKER_EVIDENCE") ?? throw new InvalidOperationException();
    var runtime = Environment.GetEnvironmentVariable("HSR_WORKER_RUNTIME") ?? "podman";
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
    await using var leases = new PostgresWorkerLeases(connection);
    var coordinator = new ObservationCoordinator(leases, executor, new(evidence, archive));
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
