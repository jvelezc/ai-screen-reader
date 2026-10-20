using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Exploration;

namespace HeadlessScreenReader.Cli;

public static class OwnedScanDemo
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var store = new OwnedEvidenceFileStore(Path.Combine("artifacts", "owned-scan"));
        var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Traversal, cancellationToken: cancellationToken);
        var owner = new OwnedScanSession(browser, store);
        var ready = false;
        try
        {
            var first = await owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent, cancellationToken: cancellationToken);
            var tab = await owner.ExecuteAndPersistAsync(1, ReaderCommand.Tab, cancellationToken: cancellationToken);
            var lease = await owner.ExcludeCaptureAsync();
            var closed = false;
            try { await owner.ExecuteAndPersistAsync(1, ReaderCommand.ReadCurrent, cancellationToken: cancellationToken); }
            catch (BrowserOperationException exception) when (exception.Code == "OWNED_SCAN_EXCLUDED") { closed = true; }
            var baseline = await owner.ResumeCaptureAsync(lease);
            var current = await owner.ExecuteAndPersistAsync(2, ReaderCommand.ReadCurrent, cancellationToken: cancellationToken);
            ready = first.Completion.State == OwnedCompletionState.ResultStored && tab.Completion.CompletedInputCalls == 1
                && closed && baseline.CaptureEpoch == 2 && current.Completion.State == OwnedCompletionState.ResultStored
                && baseline.DocumentId == first.Evidence!.Snapshot!.DocumentId
                && current.Evidence!.Events.Any(e => e.Kind == ReaderEventKind.CaptureBaselineReset)
                && owner.Status is { ActiveOperations: 0, WritesInFlight: 0, FaultCode: null };
        }
        finally { await owner.DisposeAsync(); }
        var status = owner.Status;
        ready &= status.Capture.Phase == CapturePhase.Closed && status.ActiveOperations == 0 && status.WritesInFlight == 0
            && status.FaultCode is null && !Directory.EnumerateFiles(store.Folder, "*.pending").Any();
        await TraversalArtifactWriter.WriteNewAsync(Path.Combine(store.Folder, "index.md"),
            "# Owned scan evidence\n\nEach command has a payload-free intent, an allowed result and a completion receipt. "
            + "Lifecycle and resume-baseline files show the exclusion boundary. Inspect the JSON files beside this report.\n\n"
            + $"Final state: {status.Capture.Phase}. Epoch: {status.Capture.Epoch}. Active operations: {status.ActiveOperations}. Writes in flight: {status.WritesInFlight}.\n\n"
            + "No credential entry, human input, general recording, external-consumer revocation or hosted durability is enabled.\n");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            OwnedPersistenceReady = ready,
            status.ScanId,
            Phase = status.Capture.Phase.ToString(),
            status.Capture.Epoch,
            status.ActiveOperations,
            status.WritesInFlight,
            status.FaultCode,
            Report = Path.Combine(store.Folder, "index.md")
        }));
        return ready ? 0 : 3;
    }
}
