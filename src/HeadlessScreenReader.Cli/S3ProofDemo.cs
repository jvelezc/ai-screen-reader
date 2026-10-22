using System.Globalization;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

internal static class S3ProofDemo
{
    internal static async Task<int> RunAsync(string portText, string ownerText)
    {
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535 || !Guid.TryParseExact(ownerText, "N", out var owner) || owner == Guid.Empty)
            return 2;
        try
        {
            if (Environment.GetEnvironmentVariable("HSR_WORKER_DATABASE") is not null)
                throw new InvalidOperationException("S3_PROOF_DATABASE_ENVIRONMENT_DENIED");
            var image = Environment.GetEnvironmentVariable("HSR_S3_PROOF_IMAGE") ?? throw new InvalidOperationException();
            var runtime = Environment.GetEnvironmentVariable("HSR_S3_PROOF_RUNTIME") ?? throw new InvalidOperationException();
            var root = Environment.GetEnvironmentVariable("HSR_S3_PROOF_EVIDENCE") ?? throw new InvalidOperationException();
            root = Path.GetFullPath(root);
            if (Directory.Exists(root)) throw new InvalidOperationException("S3_PROOF_EVIDENCE_ALREADY_EXISTS");
            var journal = new OwnershipJournal(Path.Combine(root, ".runtime"));
            using var exclusive = journal.AcquireExclusive();
            var executor = new IsolatedFixtureExecutor(runtime, image, Environment.GetEnvironmentVariable("HSR_S3_PROOF_SECCOMP"), journal);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var job = new FixtureJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
            var bytes = await executor.ExecuteAsync(job, timeout.Token).ConfigureAwait(false);
            var cases = await S3LocalProof.RunAsync(port, owner, root, job, bytes, timeout.Token,
                Environment.GetEnvironmentVariable("HSR_S3_PROOF_HOST") ?? "127.0.0.1").ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Schema = "hsr-local-s3-proof-v1",
                Ready = cases.Length == 10,
                Expected = 10,
                Passed = cases.Length,
                Cases = cases,
                HostedProjectChanged = false,
                AppJwtValidated = false,
                DispatchedInputs = 0,
                GoalVerified = false
            }));
            return cases.Length == 10 ? 0 : 3;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("LOCAL_S3_EVIDENCE_PROOF_UNCONFIRMED");
            return 3;
        }
    }
}
