using System.Diagnostics;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

internal static class PublicReaderWorkerProof
{
    internal static async Task<int> RunAsync(string runtime, string repository, TextWriter output)
    {
        Process? child = null;
        var run = Guid.NewGuid().ToString("N");
        var phase = "Configure";
        int? launcherExitCode = null, stderrBytes = null, trailingBytes = null;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(110));
        try
        {
            if (runtime is not ("podman" or "docker")) throw new ArgumentException();
            var script = Path.Combine(Path.GetFullPath(repository), "scripts", "Start-IsolatedPublicReader.ps1");
            if (!File.Exists(script)) throw new ArgumentException();
            var source = new PublicReaderSource(PublicReaderSource.CurrentProfile,
                "https://screenreader.privensic.com/ci/v1/source", "screenreader.privensic.com");
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.Environment.Clear();
            foreach (var pair in IsolatedFixtureExecutor.ChildEnvironment()) start.Environment[pair.Key] = pair.Value;
            // PowerShell native-command resolution on Windows needs an executable
            // suffix; retain no additional host variables or credentials.
            if (OperatingSystem.IsWindows()) start.Environment["PATHEXT"] = ".EXE";
            start.Environment["HSR_ACCEPTANCE_RUN_ID"] = run;
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script,
                "-Runtime", runtime, "-AllowedHost", source.AllowedHost, "-TargetUrl", source.TargetUrl }) start.ArgumentList.Add(arg);
            phase = "Launch";
            child = Process.Start(start) ?? throw new IOException();
            var errors = DrainAsync(child.StandardError.BaseStream, stop);
            var session = Guid.NewGuid();
            var request = new PublicReaderWorkerRequest(PublicReaderWorkerRequest.CurrentSchema, session, 1, ReaderWorkerOperation.Open);
            phase = "Open";
            await PublicReaderWorkerWire.WriteRequestAsync(child.StandardInput.BaseStream, request, stop.Token).ConfigureAwait(false);
            var first = await PublicReaderWorkerWire.ReadResponseAsync(child.StandardOutput.BaseStream, request, source, token: stop.Token).ConfigureAwait(false);
            if (!first.Snapshot!.Nodes.Any(node => node.Role == "RootWebArea" && node.Name == "Reader CI source v1")
                || first.Snapshot.Nodes.Any(node => node.Role == "heading")) throw new InvalidDataException();
            phase = "Observe";
            request = request with { Sequence = 2, Operation = ReaderWorkerOperation.Observe };
            await PublicReaderWorkerWire.WriteRequestAsync(child.StandardInput.BaseStream, request, stop.Token).ConfigureAwait(false);
            var second = await PublicReaderWorkerWire.ReadResponseAsync(child.StandardOutput.BaseStream, request, source, first.DocumentId, stop.Token).ConfigureAwait(false);
            if (!second.Snapshot!.Nodes.Any(node => node.Role == "RootWebArea" && node.Name == "Reader CI source v1")
                || second.Snapshot.Nodes.Any(node => node.Role == "heading"))
                throw new InvalidDataException();
            phase = "Close";
            request = request with { Sequence = 3, Operation = ReaderWorkerOperation.Close };
            await PublicReaderWorkerWire.WriteRequestAsync(child.StandardInput.BaseStream, request, stop.Token).ConfigureAwait(false);
            var closed = await PublicReaderWorkerWire.ReadResponseAsync(child.StandardOutput.BaseStream, request, source, first.DocumentId, stop.Token).ConfigureAwait(false);
            child.StandardInput.Close();
            phase = "Exit";
            await child.WaitForExitAsync(stop.Token).ConfigureAwait(false);
            var trailing = new byte[1];
            launcherExitCode = child.ExitCode;
            trailingBytes = await child.StandardOutput.BaseStream.ReadAsync(trailing, stop.Token).ConfigureAwait(false);
            var diagnostics = await errors.ConfigureAwait(false); stderrBytes = diagnostics.Length;
            if (launcherExitCode != 0 || trailingBytes != 0) throw new InvalidDataException();
            var stdinWarnings = PublicReaderDiagnostics.ClassifyAfterConfirmedClose(diagnostics, runtime);
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                Schema = "hsr-approved-public-worker-proof-v1",
                Ready = true,
                AcceptanceRun = run,
                Source = source.TargetUrl,
                Profile = source.Profile,
                Captures = 2,
                FirstNodes = first.Snapshot.Nodes.Length,
                SecondNodes = second.Snapshot.Nodes.Length,
                SameDocument = first.DocumentId == second.DocumentId,
                ObserveResponseConfirmed = true,
                MissingHeadingPreserved = true,
                DispatchedInputs = 0,
                GoalVerified = false,
                NativeCloseConfirmed = closed.Shutdown!.ProcessExitConfirmed,
                LauncherExitConfirmed = true,
                RuntimeStdinAlreadyClosedWarnings = stdinWarnings,
                Scope = "Public worker protocol only; not hosted tenant admission or original-user acceptance"
            })).ConfigureAwait(false);
            return 0;
        }
        catch (Exception)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                Schema = "hsr-approved-public-worker-proof-v1",
                Ready = false,
                AcceptanceRun = run,
                Phase = phase,
                LauncherExitCode = launcherExitCode,
                StderrBytes = stderrBytes,
                TrailingBytes = trailingBytes,
                Code = "PUBLIC_READER_WORKER_PROOF_UNCONFIRMED"
            })).ConfigureAwait(false);
            return 3;
        }
        finally
        {
            if (child is not null)
            {
                try
                {
                    child.StandardInput.Close();
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await child.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                }
                catch (Exception) { }
                try { if (!child.HasExited) child.Kill(entireProcessTree: true); } catch (Exception) { }
                child.Dispose();
            }
        }
    }

    private static async Task<byte[]> DrainAsync(Stream stream, CancellationTokenSource stop)
    {
        var buffer = new byte[1024]; using var bytes = new MemoryStream();
        while (true)
        {
            var count = await stream.ReadAsync(buffer, stop.Token).ConfigureAwait(false);
            if (count == 0) return bytes.ToArray();
            if (bytes.Length + count > 16384) { stop.Cancel(); throw new InvalidDataException(); }
            bytes.Write(buffer, 0, count);
        }
    }
}
