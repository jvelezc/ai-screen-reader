using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

internal sealed record ControllerDemoResult(bool Ready, string Stage, string? ExecutionErrorCode, string? CleanupErrorCode, ControllerStatus Status)
{
    public string? ErrorCode => ExecutionErrorCode ?? CleanupErrorCode ?? (Ready ? null : "CONTROL_DEMO_ACCEPTANCE_UNCONFIRMED");
}

public static class ControllerDemo
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var store = new OwnedEvidenceFileStore(Path.Combine("artifacts", "controller"));
        var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal, cancellationToken: cancellationToken);
        var result = await RunCoreAsync(browser, store, cancellationToken);
        var status = result.Status;
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(store.Folder, "controller.json"), JsonSerializer.Serialize(status, TraversalDemo.JsonOptions));
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(store.Folder, "controller-demo.json"), CreateMetadata(result));
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(store.Folder, "index.md"),
            "# Exclusive local AI controller\n\nTwo sequential controller generations share the same browser context. "
            + "The first tabs to checkout, release drains actual work, and the second activates with a fresh baseline and verifies the destination. "
            + "Contending and stale authority is rejected. Inspect the typed command/lifecycle files, controller.json and controller-demo.json; execution and cleanup failures remain separate.\n\n"
            + "This local demonstration enables no human controller, credentials, remote viewer or public replay. Bearer tokens are absent from evidence.\n");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ControllerOwnershipReady = result.Ready,
            result.Stage,
            result.ErrorCode,
            result.ExecutionErrorCode,
            result.CleanupErrorCode,
            status.ScanId,
            Phase = status.Phase.ToString(),
            status.Generation,
            Epoch = status.Owned.Capture.Epoch,
            status.ActiveCommands,
            status.Owned.WritesInFlight,
            status.FaultCode,
            status.Shutdown,
            Report = Path.Combine(store.Folder, "index.md")
        }, TraversalDemo.JsonOptions));
        return result.Ready ? 0 : 3;
    }
    // Existing Domain interfaces form the internal deterministic seam. Production still creates real Chromium/files above.
    internal static async Task<ControllerDemoResult> RunCoreAsync(IScreenReaderBrowser browser, IOwnedEvidenceStore store, CancellationToken cancellationToken = default)
    {
        var controller = new ControlledScanSession(browser, store);
        var ready = false;
        var stage = "execute"; string? executionError = null, cleanupError = null;
        try
        {
            var first = await controller.AcquireAsync(TimeSpan.FromMinutes(1));
            var initial = await controller.ExecuteAsync(first, 1, ReaderCommand.ReadCurrent, cancellationToken: cancellationToken);
            var tab = await controller.ExecuteAsync(first, 1, ReaderCommand.Tab, cancellationToken: cancellationToken);
            var contentionDenied = false;
            try { await controller.AcquireAsync(TimeSpan.FromMinutes(1)); }
            catch (BrowserOperationException exception) when (exception.Code == "CONTROL_BUSY") { contentionDenied = true; }
            await controller.ReleaseAsync(first, cancellationToken);
            var second = await controller.AcquireAsync(TimeSpan.FromMinutes(1));
            var staleDenied = false;
            try { await controller.ExecuteAsync(first, 2, ReaderCommand.Tab, cancellationToken: cancellationToken); }
            catch (BrowserOperationException exception) when (exception.Code == "CONTROL_LEASE_INVALID") { staleDenied = true; }
            var result = await controller.ExecuteAsync(second, 2, ReaderCommand.Activate, cancellationToken: cancellationToken,
                expectation: new("heading", "Checkout preview", MustBeFocused: true));
            ready = contentionDenied && staleDenied && first.Generation == 1 && second.Generation == 2
                && tab.Completion.CompletedInputCalls == 1 && result.Completion.CompletedInputCalls == 1
                && result.Evidence!.Step.Verification == VerificationStatus.Satisfied
                && result.Evidence.Snapshot!.DocumentId == initial.Evidence!.Snapshot!.DocumentId
                && result.Completion.Intent.ControllerGeneration == 2;
            await controller.ReleaseAsync(second, cancellationToken);
            stage = "close";
        }
        catch (OperationCanceledException) { executionError = "CONTROL_DEMO_CANCELLED"; }
        catch { executionError = "CONTROL_DEMO_EXECUTION_UNCONFIRMED"; }
        finally
        {
            try { await controller.DisposeAsync(); }
            catch { cleanupError = "CONTROL_CLOSE_UNCONFIRMED"; }
        }
        var status = controller.Status;
        ready &= executionError is null && cleanupError is null && status.Phase == ControllerPhase.Closed && status.ActiveCommands == 0 && status.FaultCode is null
            && status.Owned is { ActiveOperations: 0, WritesInFlight: 0, FaultCode: null };
        return new(ready, ready ? "completed" : stage, executionError, cleanupError, status);
    }
    internal static string CreateMetadata(ControllerDemoResult result) => JsonSerializer.Serialize(new
    {
        Schema = "hsr-controller-demo-v1",
        result.Ready,
        result.Stage,
        result.ErrorCode,
        result.ExecutionErrorCode,
        result.CleanupErrorCode,
        StatusArtifact = "controller.json",
        result.Status.Shutdown
    }, TraversalDemo.JsonOptions);
}
