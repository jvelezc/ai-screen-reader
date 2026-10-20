using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

public static class ControllerDemo
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var store = new OwnedEvidenceFileStore(Path.Combine("artifacts", "controller"));
        var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal, cancellationToken: cancellationToken);
        var controller = new ControlledScanSession(browser, store);
        var ready = false;
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
        }
        finally { await controller.DisposeAsync(); }
        var status = controller.Status;
        ready &= status.Phase == ControllerPhase.Closed && status.ActiveCommands == 0 && status.FaultCode is null
            && status.Owned is { ActiveOperations: 0, WritesInFlight: 0, FaultCode: null };
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(store.Folder, "controller.json"),
            JsonSerializer.Serialize(status, TraversalDemo.JsonOptions));
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(store.Folder, "index.md"),
            "# Exclusive local AI controller\n\nTwo sequential controller generations share the same browser context. "
            + "The first tabs to checkout, release drains actual work, and the second activates with a fresh baseline and verifies the destination. "
            + "Contending and stale authority is rejected. Inspect the typed command and lifecycle files plus controller.json.\n\n"
            + "This local demonstration enables no human controller, credentials, remote viewer or public replay. Bearer tokens are absent from evidence.\n");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ControllerOwnershipReady = ready,
            status.ScanId,
            Phase = status.Phase.ToString(),
            status.Generation,
            Epoch = status.Owned.Capture.Epoch,
            status.ActiveCommands,
            status.Owned.WritesInFlight,
            status.FaultCode,
            Report = Path.Combine(store.Folder, "index.md")
        }));
        return ready ? 0 : 3;
    }
}
