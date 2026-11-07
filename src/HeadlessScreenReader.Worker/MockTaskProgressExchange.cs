using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Durable grant intent. It records possible delivery, never a completed key or observed goal.</summary>
public sealed record MockTaskGrantIntent(MockTaskIntent Intent, string IntentSha256,
    DateTimeOffset PreparedAtUtc, bool InputMayHaveOccurred);

internal sealed class MockTaskProgressExchange(MockTaskStart start, string journalRoot,
    Func<CancellationToken, Task<bool>> authorize) : IWorkerProgressExchange
{
    private int _sequence;
    public int AuthorizedBoundaries => _sequence;

    public Task StartAsync(Stream input, CancellationToken token)
    {
        start.Validate();
        return ReaderWorkerWire.WriteAsync(input, JsonSerializer.SerializeToUtf8Bytes(start, ReaderWorkerWire.Json),
            MockTaskControl.MaximumFrameBytes, token);
    }

    public async Task ReadAsync(Stream progress, Stream acknowledgements, CancellationTokenSource stop)
    {
        try
        {
            while (await ReaderWorkerWire.ReadAsync(progress, MockTaskControl.MaximumFrameBytes, stop.Token).ConfigureAwait(false) is { } bytes)
            {
                var intent = JsonSerializer.Deserialize<MockTaskIntent>(bytes, ReaderWorkerWire.Json)
                    ?? throw new BrowserOperationException("MOCK_TASK_INTENT_INVALID");
                MockTaskControl.Validate(intent, start, _sequence + 1);
                if (!await authorize(stop.Token).ConfigureAwait(false)) throw new BrowserOperationException("MOCK_TASK_AUTHORITY_UNCONFIRMED");
                var digest = TraversalArchive.Hash(bytes);
                var record = new MockTaskGrantIntent(intent, digest, DateTimeOffset.UtcNow,
                    intent.Boundary.Action is "Tab" or "Enter" or "Space" or "Pointer");
                try
                {
                    Directory.CreateDirectory(journalRoot);
                    var folder = Path.Combine(journalRoot, start.ExecutionId.ToString("N"));
                    Directory.CreateDirectory(folder);
                    await using var saved = new FileStream(Path.Combine(folder, $"{intent.Sequence:D4}.json"),
                        FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
                    await JsonSerializer.SerializeAsync(saved, record, ReaderWorkerWire.Json, stop.Token).ConfigureAwait(false);
                    await saved.FlushAsync(stop.Token).ConfigureAwait(false);
                    saved.Flush(flushToDisk: true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { throw new BrowserOperationException("MOCK_TASK_INTENT_WRITE_UNCONFIRMED"); }
                stop.Token.ThrowIfCancellationRequested();
                if (!await authorize(stop.Token).ConfigureAwait(false)) throw new BrowserOperationException("MOCK_TASK_AUTHORITY_UNCONFIRMED");
                await ReaderWorkerWire.WriteAsync(acknowledgements, JsonSerializer.SerializeToUtf8Bytes(
                    new MockTaskAck(MockTaskControl.Schema, start.ExecutionId, intent.Sequence, digest), ReaderWorkerWire.Json),
                    MockTaskControl.MaximumFrameBytes, stop.Token).ConfigureAwait(false);
                _sequence = intent.Sequence;
            }
        }
        catch { await stop.CancelAsync().ConfigureAwait(false); throw; }
    }

    public void RequireComplete()
    {
        if (_sequence != MockTaskControl.Boundaries.Length) throw new BrowserOperationException("MOCK_TASK_AUTHORIZATION_INCOMPLETE");
    }
}
