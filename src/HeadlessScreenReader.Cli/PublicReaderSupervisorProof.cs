using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

internal static class PublicReaderSupervisorProof
{
    internal static async Task<int> RecoveryDriverAsync(string runtime, string root, string scripts, string image, bool crashChild, TextWriter output)
    {
        try
        {
            await using var supervisor = await IsolatedPublicReaderSupervisor.StartAsync(root, scripts, runtime, image).ConfigureAwait(false);
            if (crashChild)
            {
                var source = new PublicReaderSource(PublicReaderSource.CurrentProfile, "https://screenreader.privensic.com/ci/v1/source", "screenreader.privensic.com");
                var reader = await supervisor.OpenAsync(Guid.NewGuid(), Guid.NewGuid(), source).ConfigureAwait(false);
                if (!reader.InitialSnapshot.Nodes.Any(node => node.Role == "RootWebArea" && node.Name == "Reader CI source v1"))
                    throw new InvalidDataException();
                var intent = new PublicReaderOwnershipJournal(supervisor.JournalRoot).ReadPending().Single();
                await output.WriteLineAsync(JsonSerializer.Serialize(new { Ready = true, intent.Owner, NativeObserved = true })).ConfigureAwait(false);
                await output.FlushAsync().ConfigureAwait(false);
                await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            }
            else
            {
                if (supervisor.Recovered != 1 || new PublicReaderOwnershipJournal(supervisor.JournalRoot).ReadPending().Count != 0)
                    throw new InvalidDataException();
                await output.WriteLineAsync(JsonSerializer.Serialize(new { Ready = true, supervisor.Recovered, PendingIntents = 0 })).ConfigureAwait(false);
            }
            return 0;
        }
        catch
        {
            await output.WriteLineAsync("{\"Ready\":false,\"Code\":\"PUBLIC_READER_RECOVERY_PROOF_UNCONFIRMED\"}").ConfigureAwait(false);
            return 3;
        }
    }

    internal static async Task<int> RunAsync(string runtime, string repository, TextWriter output)
    {
        var phase = "Configuration";
        IsolatedPublicReaderSupervisor? supervisor = null;
        var run = Guid.NewGuid().ToString("N");
        try
        {
            if (runtime is not ("podman" or "docker")) throw new ArgumentException();
            var root = Path.GetFullPath(repository);
            var folder = Path.Combine(root, "artifacts", "public-reader-supervisor", run);
            var start = new ProcessStartInfo(runtime) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "image", "inspect", "localhost/headless-screen-reader-prototype:local", "--format", "{{.Id}}" }) start.ArgumentList.Add(arg);
            using var imageRead = Process.Start(start) ?? throw new IOException();
            using var imageDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var imageText = imageRead.StandardOutput.ReadToEndAsync(imageDeadline.Token);
            var imageErrors = imageRead.StandardError.ReadToEndAsync(imageDeadline.Token);
            await Task.WhenAll(imageText, imageErrors, imageRead.WaitForExitAsync(imageDeadline.Token)).ConfigureAwait(false);
            var image = (await imageText.ConfigureAwait(false)).Trim();
            if (Regex.IsMatch(image, "\\A[a-f0-9]{64}\\z", RegexOptions.NonBacktracking)) image = "sha256:" + image;
            if (imageRead.ExitCode != 0 || (await imageErrors.ConfigureAwait(false)).Length != 0
                || !Regex.IsMatch(image, "\\Asha256:[a-f0-9]{64}\\z", RegexOptions.NonBacktracking)) throw new IOException();
            phase = "Start";
            supervisor = await IsolatedPublicReaderSupervisor.StartAsync(folder, Path.Combine(root, "scripts"), runtime, image).ConfigureAwait(false);
            var organization = Guid.NewGuid(); var session = Guid.NewGuid();
            var source = new PublicReaderSource(PublicReaderSource.CurrentProfile, "https://screenreader.privensic.com/ci/v1/source", "screenreader.privensic.com");
            phase = "Open";
            var reader = await supervisor.OpenAsync(organization, session, source).ConfigureAwait(false);
            var journal = new PublicReaderOwnershipJournal(supervisor.JournalRoot);
            var intent = journal.ReadPending().Single();
            if (intent.OrganizationId != organization || intent.SessionId != session || intent.Source != source || intent.Image != image
                || !reader.InitialSnapshot.Nodes.Any(node => node.Role == "RootWebArea" && node.Name == "Reader CI source v1")
                || reader.InitialSnapshot.DocumentUrl != source.TargetUrl) throw new InvalidDataException();
            phase = "Scope";
            try { await supervisor.RetireAsync(Guid.NewGuid(), session).ConfigureAwait(false); throw new InvalidDataException(); }
            catch (BrowserOperationException error) when (error.Code == "PUBLIC_READER_SCOPE_DENIED") { }
            try { await supervisor.OpenAsync(organization, session, source).ConfigureAwait(false); throw new InvalidDataException(); }
            catch (BrowserOperationException error) when (error.Code == "PUBLIC_READER_SESSION_ALREADY_OPEN") { }
            phase = "Observe";
            var observed = await reader.ObserveAsync().ConfigureAwait(false);
            if (observed.DocumentId != reader.InitialSnapshot.DocumentId || observed.DocumentUrl != source.TargetUrl || observed.Nodes.IsDefaultOrEmpty
                || observed.Nodes.Any(node => node.Role == "heading")) throw new InvalidDataException();
            phase = "Retire";
            var retirement = await supervisor.RetireAsync(organization, session).ConfigureAwait(false);
            if (retirement is not { ResourcesAbsenceConfirmed: true, NativeShutdownConfirmed: true } || journal.ReadPending().Count != 0
                || !reader.ResourcesAbsenceConfirmed || !reader.NativeShutdownConfirmed) throw new InvalidDataException();
            var removalPath = Path.Combine(journal.RemovalsDirectory, intent.Owner.ToString("N") + ".json");
            var removal = JsonSerializer.Deserialize<PublicReaderRemoval>(await File.ReadAllBytesAsync(removalPath).ConfigureAwait(false), ObservationStore.Json);
            if (removal is null || removal.Intent != intent || !removal.ContainersAbsenceConfirmed || !removal.NetworksAbsenceConfirmed)
                throw new InvalidDataException();
            phase = "Restart";
            await supervisor.DisposeAsync().ConfigureAwait(false); supervisor = null;
            await using var restarted = await IsolatedPublicReaderSupervisor.StartAsync(folder, Path.Combine(root, "scripts"), runtime, image).ConfigureAwait(false);
            if (restarted.Recovered != 0 || new PublicReaderOwnershipJournal(restarted.JournalRoot).ReadPending().Count != 0)
                throw new InvalidDataException();
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                Schema = "hsr-public-reader-supervisor-proof-v1",
                Ready = true,
                Run = run,
                Image = image,
                Source = source.TargetUrl,
                Captures = 2,
                FirstNodes = reader.InitialSnapshot.Nodes.Length,
                SecondNodes = observed.Nodes.Length,
                SameDocument = true,
                ForeignRetirementDenied = true,
                DuplicateOpenDenied = true,
                NativeShutdownConfirmed = true,
                ContainersAbsent = true,
                NetworksAbsent = true,
                PendingIntents = 0,
                RestartAfterRetirementConfirmed = true,
                RuntimeStdinAlreadyClosedWarnings = reader.RuntimeStdinAlreadyClosedWarnings,
                Scope = "Actual managed public runtime only; no hosted admission, original-user RLS, recorded goals or crash-recovery inference"
            })).ConfigureAwait(false);
            return 0;
        }
        catch (Exception error)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(new
            {
                Schema = "hsr-public-reader-supervisor-proof-v1",
                Ready = false,
                Run = run,
                Phase = phase,
                Code = error is BrowserOperationException fault ? fault.Code : "PUBLIC_READER_SUPERVISOR_PROOF_UNCONFIRMED"
            })).ConfigureAwait(false);
            return 3;
        }
        finally
        {
            if (supervisor is not null) try { await supervisor.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
        }
    }
}
