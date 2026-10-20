using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

public sealed record HeadedSessionReceipt(string ScanId, bool Ready, string Stage, string? ErrorCode,
    HeadedDisplayObservation? Display, HeadedKernelObservation? Kernel, HeadedProcessObservation? Before, HeadedProcessObservation? After,
    bool SameDocument, bool GoalVerified, bool BrowserCloseConfirmed, ControllerStatus? Controller, string Report)
{
    public string Schema => "hsr-headed-session-proof-v1";
    public string CaseName => "HeadedChromium_TwoControllerGenerationsPreserveProcessContextAndCleanOwnedDisplay";
}

public static class HeadedSessionDemo
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var store = new OwnedEvidenceFileStore(Path.Combine("artifacts", "headed-session"));
        var stage = "display"; string? error = null;
        HeadedDisplayObservation? display = null;
        HeadedKernelObservation? kernel = null;
        HeadedProcessObservation? before = null, after = null;
        ControlledScanSession? control = null;
        var sameDocument = false; var verified = false; var closed = false;
        try
        {
            display = HeadedDisplayObservation.ReadCurrent();
            kernel = HeadedKernelObservation.ReadCurrent();
            if (!kernel.Restricted) throw new InvalidOperationException("HEADED_KERNEL_UNCONFIRMED");
            await TraversalArtifactWriter.WriteNewAsync("display-number.txt", display.Number.ToString(CultureInfo.InvariantCulture) + "\n");
            await TraversalArtifactWriter.WriteNewAsync("display-pid.txt", display.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n");
            stage = "launch";
            var browser = await ChromiumFixtureBrowser.LaunchAsync(headed: true, fixture: BundledFixture.Traversal, cancellationToken: cancellationToken);
            try { control = new(browser, store); }
            catch { await browser.DisposeAsync(); throw; }
            stage = "identity-before";
            before = HeadedProcessObservation.Read(await browser.CaptureFixtureProcessIdentityAsync(cancellationToken));
            stage = "first-controller";
            var first = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            var start = await control.ExecuteAsync(first, 1, ReaderCommand.ReadCurrent, cancellationToken: cancellationToken);
            await control.ExecuteAsync(first, 1, ReaderCommand.Tab, cancellationToken: cancellationToken);
            stage = "drain";
            await control.ReleaseAsync(first, cancellationToken);
            stage = "replacement";
            var second = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            stage = "identity-after";
            after = HeadedProcessObservation.Read(await browser.CaptureFixtureProcessIdentityAsync(cancellationToken));
            stage = "goal";
            var finish = await control.ExecuteAsync(second, 2, ReaderCommand.Activate, cancellationToken: cancellationToken,
                expectation: new("heading", "Checkout preview", MustBeFocused: true));
            sameDocument = start.Evidence?.Snapshot?.DocumentId is { } doc && finish.Evidence?.Snapshot?.DocumentId == doc;
            verified = finish.Evidence?.Step.Verification == VerificationStatus.Satisfied
                && finish.Completion.CompletedInputCalls == 1 && finish.Completion.Intent.ControllerGeneration == 2;
            if (!sameDocument || !verified || before != after || before.HeadlessSwitchObserved || before.NoSandboxSwitchObserved)
                throw new InvalidOperationException("HEADED_PROOF_UNCONFIRMED");
            stage = "close";
        }
        catch (OperationCanceledException) { error = "HEADED_PROOF_CANCELLED"; }
        catch (BrowserOperationException exception) { error = exception.Code; }
        catch (InvalidOperationException exception) when (exception.Message is "HEADED_PROOF_REQUIRES_LINUX" or "HEADED_DISPLAY_INVALID"
            or "HEADED_AUTHORITY_UNCONFIRMED" or "HEADED_PROCESS_UNCONFIRMED" or "HEADED_KERNEL_UNCONFIRMED"
            or "HEADED_PROCESS_ARGUMENTS_UNCONFIRMED" or "HEADED_PROOF_UNCONFIRMED")
        { error = exception.Message; }
        catch { error = "HEADED_PROOF_UNCONFIRMED"; }
        finally
        {
            if (control is not null)
                try { await control.DisposeAsync(); }
                catch { error ??= "HEADED_CLOSE_UNCONFIRMED"; }
        }
        if (before is not null) closed = await ConfirmProcessExitAsync(before.Identity.BrowserProcessId);
        var status = control?.Status;
        var ready = error is null && display is { PrivateAuthority: true, TcpDisabled: true } && kernel is { Restricted: true }
            && before == after && sameDocument && verified && closed
            && status is
            {
                Phase: ControllerPhase.Closed, Generation: 2, ActiveCommands: 0, FaultCode: null,
                Owned: { WritesInFlight: 0, ActiveOperations: 0, FaultCode: null }
            };
        if (!ready) error ??= "HEADED_PROOF_UNCONFIRMED";
        var receipt = new HeadedSessionReceipt(store.ScanId, ready, ready ? "completed" : stage, error,
            display, kernel, before, after, sameDocument, verified, closed, status, Path.Combine(store.Folder, "index.md"));
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(store.Folder, "receipt.json"), JsonSerializer.Serialize(receipt, TraversalDemo.JsonOptions));
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(store.Folder, "index.md"),
            "# Headed Linux session proof\n\nSee receipt.json for actual Xvfb, browser process/context/target identity, "
            + "controller replacement, same-document and fresh goal evidence. Display cleanup is acknowledged separately by the outer Xvfb runner.\n\n"
            + "This fixture proof enables no VNC, human control, credential entry or arbitrary website navigation.\n");
        Console.WriteLine(JsonSerializer.Serialize(receipt, TraversalDemo.JsonOptions));
        return ready ? 0 : 3;
    }

    private static async Task<bool> ConfirmProcessExitAsync(long pid)
    {
        if (pid is < 1 or > int.MaxValue) return false;
        for (var i = 0; i < 100; i++)
        {
            try { using var process = Process.GetProcessById((int)pid); if (process.HasExited) return true; }
            catch (ArgumentException) { return true; }
            await Task.Delay(20);
        }
        return false;
    }
}
