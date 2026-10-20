using System.Globalization;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

public sealed record SyntheticHandoffReceipt(string ScanId, bool Ready, string Stage, string? ErrorCode,
    bool Headed, bool SameDocument, bool InputChannelDrainedBeforeResume, bool BrowserCloseConfirmed,
    FixtureProcessIdentity? Before, FixtureProcessIdentity? After, HeadedDisplayObservation? Display, HeadedKernelObservation? Kernel,
    SyntheticHandoffMediaAudit? Media, ControllerStatus? Controller, string Report)
{
    public string Schema => "hsr-synthetic-handoff-proof-v1";
    public string CaseName => "SyntheticHandoff_ActualInputIsExcludedFromOwnedFilesAndOriginalMedia";
    public ControllerRole AssistanceOrigin => ControllerRole.SimulatedAssistance;
    public bool HumanAcceptancePerformed => false;
    public bool IndependentWholePathGoalClaimed => false;
}

public static class SyntheticHandoffDemo
{
    public static async Task<int> RunAsync(bool headed = false, CancellationToken cancellationToken = default)
    {
        var folder = Path.GetFullPath(Path.Combine("artifacts", "synthetic-handoff", Guid.NewGuid().ToString("N")));
        var store = new OwnedEvidenceFileStore(Path.Combine(folder, "owned"));
        var stage = "display"; string? error = null;
        ChromiumSyntheticHandoffProbe? probe = null; ControlledScanSession? control = null;
        HeadedDisplayObservation? display = null; HeadedKernelObservation? kernel = null;
        FixtureProcessIdentity? before = null, after = null;
        var sameDocument = false; var drained = false; var complete = false;
        SyntheticHandoffMediaAudit? media = null;
        try
        {
            if (headed)
            {
                display = HeadedDisplayObservation.ReadCurrent(); kernel = HeadedKernelObservation.ReadCurrent();
                if (!kernel.Restricted) throw new BrowserOperationException("SYNTHETIC_HEADED_KERNEL_UNCONFIRMED");
                await TraversalArtifactWriter.WriteNewAsync("display-number.txt", display.Number.ToString(CultureInfo.InvariantCulture) + "\n");
                await TraversalArtifactWriter.WriteNewAsync("display-pid.txt", display.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n");
            }
            stage = "launch";
            probe = await ChromiumFixtureBrowser.LaunchSyntheticHandoffProbeAsync(Path.Combine(folder, "media"), headed, cancellationToken);
            control = new(probe.Browser, store);
            before = await probe.Browser.CaptureFixtureProcessIdentityAsync(cancellationToken);
            if (headed && HeadedProcessObservation.Read(before) is { HeadlessSwitchObserved: true } or { NoSandboxSwitchObserved: true })
                throw new BrowserOperationException("SYNTHETIC_HEADED_FLAGS_UNCONFIRMED");
            stage = "automation-before";
            var ai = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            var start = await control.ExecuteAsync(ai, 1, ReaderCommand.Tab, cancellationToken: cancellationToken);
            if (start.Evidence?.Snapshot?.FocusedNode?.Name != "Synthetic canary field" || start.Completion.CompletedInputCalls != 1)
                throw new BrowserOperationException("SYNTHETIC_INITIAL_FOCUS_UNCONFIRMED");
            stage = "exclude";
            await control.ReleaseAsync(ai, cancellationToken);
            stage = "simulation";
            var simulated = await control.AcquireSimulatedAssistanceAsync(TimeSpan.FromMinutes(1));
            var typed = await control.ExecuteSimulatedAssistanceAsync(simulated, SyntheticInputAction.EnterCanary, cancellationToken);
            if (typed.Completion.Input is not { CompletedInputCalls: 1, InputUncertain: false, CanaryCleared: false, ErrorCode: null })
                throw new BrowserOperationException("SYNTHETIC_INPUT_UNCONFIRMED");
            stage = "positive-control";
            if (!await probe.ObserveInMemoryPositiveControlAsync()) throw new BrowserOperationException("SYNTHETIC_POSITIVE_CONTROL_MISSING");
            stage = "clear";
            var cleared = await control.ExecuteSimulatedAssistanceAsync(simulated, SyntheticInputAction.ClearCanary, cancellationToken);
            if (cleared.Completion.Input is not { CompletedInputCalls: 2, InputUncertain: false, CanaryCleared: true, ErrorCode: null })
                throw new BrowserOperationException("SYNTHETIC_CLEAR_UNCONFIRMED");
            await control.ReleaseAsync(simulated, cancellationToken);
            drained = probe.Browser.SyntheticStatus is { AuthorityActive: false, ActiveOperations: 0, CanaryMayRemain: false };
            stage = "resume";
            var resumed = await control.AcquireAsync(TimeSpan.FromMinutes(1));
            var finish = await control.ExecuteAsync(resumed, 2, ReaderCommand.ReadCurrent, cancellationToken: cancellationToken);
            after = await probe.Browser.CaptureFixtureProcessIdentityAsync(cancellationToken);
            sameDocument = start.Evidence.Snapshot.DocumentId == finish.Evidence?.Snapshot?.DocumentId;
            complete = before == after && sameDocument && drained && finish.Completion.Intent.PriorSimulatedAssistance
                && finish.Evidence!.Events.Any(e => e.Kind == ReaderEventKind.CaptureBaselineReset);
            stage = "close";
        }
        catch (OperationCanceledException) { error = "SYNTHETIC_HANDOFF_CANCELLED"; }
        catch (BrowserOperationException exception) { error = exception.Code; }
        catch { error = "SYNTHETIC_HANDOFF_UNCONFIRMED"; }
        finally
        {
            try
            {
                if (control is not null) await control.DisposeAsync();
                else if (probe is not null) await probe.DisposeAsync();
            }
            catch { error ??= "SYNTHETIC_CLOSE_UNCONFIRMED"; }
        }
        var closed = before is not null && await HeadedSessionDemo.ConfirmProcessExitAsync(before.BrowserProcessId);
        if (error is null && probe is not null)
        {
            stage = "inspect";
            try { media = await probe.InspectClosedEvidenceAsync(folder, cancellationToken); }
            catch (OperationCanceledException) { error = "SYNTHETIC_AUDIT_CANCELLED"; }
            catch { error = "SYNTHETIC_AUDIT_UNCONFIRMED"; }
        }
        var status = control?.Status;
        var ready = error is null && complete && closed && media is { Verified: true }
            && status is
            {
                Phase: ControllerPhase.Closed, FaultCode: null, ActiveCommands: 0,
                Owned: { ActiveOperations: 0, WritesInFlight: 0, SimulatedAssistanceIntroduced: true, FaultCode: null }
            };
        if (!ready) error ??= media?.ErrorCode ?? "SYNTHETIC_HANDOFF_UNCONFIRMED";
        var receipt = new SyntheticHandoffReceipt(store.ScanId, ready, ready ? "completed" : stage, error, headed, sameDocument,
            drained, closed, before, after, display, kernel, media, status, Path.Combine(folder, "index.md"));
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, "receipt.json"), JsonSerializer.Serialize(receipt, TraversalDemo.JsonOptions));
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(folder, "index.md"),
            "# Synthetic excluded-input handoff\n\nThis fixed fixture uses explicitly simulated assistance, never real human acceptance. "
            + "The input intent and completion contain counts, generation and origin; no canary text or bearer tokens. "
            + "Inspect receipt.json, owned/ and the hashed original media files under media/.\n\n"
            + "The positive-control screenshot stayed in memory. Capture remained excluded during typing/clearing; actual input drained before "
            + "fresh-baseline resume in the same context. Every original and staging video/JPEG was inspected for the red marker and the batch was scanned for canary text.\n\n"
            + "No real credentials, general text entry, private viewer or independent whole-path goal claim is enabled.\n");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            SyntheticHandoffReady = ready,
            receipt.ScanId,
            receipt.CaseName,
            receipt.Stage,
            receipt.ErrorCode,
            receipt.AssistanceOrigin,
            receipt.HumanAcceptancePerformed,
            receipt.IndependentWholePathGoalClaimed,
            receipt.SameDocument,
            receipt.InputChannelDrainedBeforeResume,
            receipt.BrowserCloseConfirmed,
            MediaArtifacts = media?.Artifacts.Length ?? 0,
            DecodedFrames = media?.Artifacts.Sum(a => a.Pixels.Frames) ?? 0,
            Report = receipt.Report
        }, TraversalDemo.JsonOptions));
        // The headed wrapper consumes one full proof envelope, matching the ordinary headed experiment.
        if (headed) await File.WriteAllTextAsync("run-proof.json", JsonSerializer.Serialize(receipt, TraversalDemo.JsonOptions), cancellationToken);
        return ready ? 0 : 3;
    }
}
