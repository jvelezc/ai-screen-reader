using System.Text.Json;
using System.Runtime.InteropServices;
using HeadlessScreenReader.Worker;

if (args.Length != 1 || args[0] is not ("--once" or "--poll" or "--recover"))
{
    Console.Error.WriteLine("Usage: HeadlessScreenReader.Worker --once | --poll | --recover. Operator configuration is supplied through the private service environment.");
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
    var connection = Environment.GetEnvironmentVariable("HSR_WORKER_DATABASE") ?? throw new InvalidOperationException();
    await using var leases = new PostgresWorkerLeases(connection);
    var coordinator = new ObservationCoordinator(leases, executor, new(evidence));
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
