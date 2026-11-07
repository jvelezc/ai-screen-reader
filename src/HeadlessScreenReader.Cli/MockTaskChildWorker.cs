using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

/// <summary>Credential-free embedded collector. Progress is framed, stdout contains only a bounded bundle.</summary>
public static class MockTaskChildWorker
{
    public static async Task<int> RunAsync(Stream input, Stream output, Stream progress, CancellationToken token = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        stop.CancelAfter(TimeSpan.FromSeconds(90));
        string? folder = null;
        try
        {
            var bytes = await ReaderWorkerWire.ReadAsync(input, MockTaskControl.MaximumFrameBytes, stop.Token).ConfigureAwait(false)
                ?? throw new BrowserOperationException("MOCK_TASK_START_INVALID");
            var start = JsonSerializer.Deserialize<MockTaskStart>(bytes, ReaderWorkerWire.Json)
                ?? throw new BrowserOperationException("MOCK_TASK_START_INVALID");
            start.Validate();
            var task = MockSiteTasks.Fixtures.Select(MockSiteTasks.Describe).Single(t => t.Id == start.Job.Source.Id);
            if (task.SourceSha256 != start.Job.Source.Sha256) throw new BrowserOperationException("MOCK_TASK_SOURCE_INVALID");
            var sequence = 0;
            async Task Authorize(MockTaskBoundary boundary, CancellationToken cancellation)
            {
                try
                {
                    var intent = new MockTaskIntent(MockTaskControl.Schema, start, sequence + 1, boundary);
                    MockTaskControl.Validate(intent, start, sequence + 1);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    deadline.CancelAfter(TimeSpan.FromSeconds(5));
                    var payload = JsonSerializer.SerializeToUtf8Bytes(intent, ReaderWorkerWire.Json);
                    await ReaderWorkerWire.WriteAsync(progress, payload, MockTaskControl.MaximumFrameBytes, deadline.Token).ConfigureAwait(false);
                    var acknowledgement = await ReaderWorkerWire.ReadAsync(input, MockTaskControl.MaximumFrameBytes, deadline.Token).ConfigureAwait(false)
                        ?? throw new BrowserOperationException("MOCK_TASK_ACK_MISSING");
                    var ack = JsonSerializer.Deserialize<MockTaskAck>(acknowledgement, ReaderWorkerWire.Json);
                    if (ack is null || ack.Schema != MockTaskControl.Schema || ack.ExecutionId != start.ExecutionId
                        || ack.Sequence != intent.Sequence || ack.IntentSha256 != TraversalArchive.Hash(payload))
                        throw new BrowserOperationException("MOCK_TASK_ACK_INVALID");
                    sequence++;
                }
                catch { await stop.CancelAsync().ConfigureAwait(false); throw; }
            }
            folder = Path.Combine(Path.GetTempPath(), "hsr-mock-worker-" + Guid.NewGuid().ToString("N"));
            var exported = await MockTaskRecordings.RecordAuthorizedAsync(folder, task.Fixture, start, Authorize, stop.Token).ConfigureAwait(false);
            stop.Token.ThrowIfCancellationRequested();
            if (sequence != MockTaskControl.Boundaries.Length || exported.Observation == "IncompleteOrMixedResults")
                throw new BrowserOperationException("MOCK_TASK_COLLECTION_INCOMPLETE");
            var archive = await File.ReadAllBytesAsync(exported.Archive, stop.Token).ConfigureAwait(false);
            var receipt = new MockTaskCollectionReceipt(MockTaskCollectionReceipt.Schema, start, exported.ManifestSha256,
                TraversalArchive.Hash(archive), sequence);
            var bundle = new MockTaskCollectionBundle(receipt, archive).Encode(start);
            await output.WriteAsync(bundle, stop.Token).ConfigureAwait(false);
            await output.FlushAsync(stop.Token).ConfigureAwait(false);
            return 0;
        }
        catch (Exception)
        {
            // No raw exception/page data and no fabricated progress or successful result.
            return 3;
        }
        finally
        {
            if (folder is not null)
            {
                var resolved = Path.GetFullPath(folder);
                if (Path.GetDirectoryName(resolved) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                    || !Path.GetFileName(resolved).StartsWith("hsr-mock-worker-", StringComparison.Ordinal))
                    throw new IOException("MOCK_TASK_TEMP_SCOPE_INVALID");
                if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
                if (File.Exists(resolved + ".zip")) File.Delete(resolved + ".zip");
            }
        }
    }
}
