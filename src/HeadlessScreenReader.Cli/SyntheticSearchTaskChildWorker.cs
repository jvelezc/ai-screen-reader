using System.Collections.Immutable;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

internal static class SyntheticSearchTaskChildWorker
{
    internal static async Task<int> RunAsync(Stream input, Stream output, Stream progress, CancellationToken token = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        stop.CancelAfter(TimeSpan.FromSeconds(150));
        string? folder = null;
        try
        {
            var bytes = await ReaderWorkerWire.ReadAsync(input, SyntheticSearchTaskControl.MaximumFrameBytes, stop.Token).ConfigureAwait(false)
                ?? throw new BrowserOperationException("SEARCH_TASK_START_INVALID");
            var start = JsonSerializer.Deserialize<SyntheticSearchTaskStart>(bytes, ReaderWorkerWire.Json)
                ?? throw new BrowserOperationException("SEARCH_TASK_START_INVALID");
            start.Validate();
            if (!bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(start, ReaderWorkerWire.Json)))
                throw new BrowserOperationException("SEARCH_TASK_START_INVALID");
            var control = new SyntheticSearchTaskChildControl(start, input, progress, stop);
            folder = Path.Combine(Path.GetTempPath(), "hsr-search-worker-" + Guid.NewGuid().ToString("N"));
            var result = await ControlledSearchRecordings.RecordAsync(folder, start, control.BoundaryAsync, control.NativeAsync,
                control.SourcesAsync, stop.Token).ConfigureAwait(false);
            if (control.PreparedResponses != 85) throw new BrowserOperationException("SEARCH_TASK_GRANTS_INCOMPLETE");
            var archive = await File.ReadAllBytesAsync(result.Archive, stop.Token).ConfigureAwait(false);
            var bundle = new SyntheticSearchTaskBundle(new(SyntheticSearchTaskCollectionReceipt.CurrentSchema, start,
                result.ManifestSha256, result.ArchiveSha256, control.PreparedResponses), archive);
            var encoded = bundle.Encode(start);
            await output.WriteAsync(encoded, stop.Token).ConfigureAwait(false);
            await output.FlushAsync(stop.Token).ConfigureAwait(false);
            return 0;
        }
        catch (Exception) { return 3; }
        finally
        {
            if (folder is not null)
            {
                var resolved = Path.GetFullPath(folder);
                if (Path.GetDirectoryName(resolved) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                    || !Path.GetFileName(resolved).StartsWith("hsr-search-worker-", StringComparison.Ordinal))
                    throw new IOException("SEARCH_TASK_TEMP_SCOPE_INVALID");
                if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
                if (File.Exists(resolved + ".zip")) File.Delete(resolved + ".zip");
            }
        }
    }
}

internal sealed class SyntheticSearchTaskChildControl(SyntheticSearchTaskStart start, Stream input, Stream progress, CancellationTokenSource stop)
{
    private int _sequence;
    private int _sourceSequence;
    private int _trial;
    public int PreparedResponses => _sequence;

    internal async Task BoundaryAsync(int trial, string action, CancellationToken token)
    {
        var boundary = Next();
        if (boundary.Trial != trial || boundary.Action != action || boundary.Action == "Native")
            throw new BrowserOperationException("SEARCH_TASK_BOUNDARY_INVALID");
        await GrantAsync(boundary, null, token).ConfigureAwait(false);
        if (action == "Open") _trial = trial;
    }

    internal async Task<SyntheticSearchInputDecision> NativeAsync(int trial, SyntheticSearchInputIntent intent, CancellationToken token)
    {
        var boundary = Next();
        if (trial != _trial || boundary.Trial != trial || boundary.Action != "Native")
            throw new BrowserOperationException("SEARCH_TASK_BOUNDARY_INVALID");
        await GrantAsync(boundary, intent, token).ConfigureAwait(false);
        return SyntheticSearchInputDecision.Granted;
    }

    private SyntheticSearchTaskBoundary Next()
    {
        if (_sequence >= SyntheticSearchTaskControl.Boundaries.Length || stop.IsCancellationRequested)
            throw new BrowserOperationException("SEARCH_TASK_GRANTS_STOPPED");
        return SyntheticSearchTaskControl.Boundaries[_sequence];
    }

    private async Task GrantAsync(SyntheticSearchTaskBoundary boundary, SyntheticSearchInputIntent? inputIntent, CancellationToken token)
    {
        try
        {
            var intent = new SyntheticSearchTaskIntent(SyntheticSearchTaskControl.Schema, start, _sequence + 1, boundary, inputIntent);
            SyntheticSearchTaskControl.Validate(intent, start, _sequence + 1);
            var payload = JsonSerializer.SerializeToUtf8Bytes(intent, ReaderWorkerWire.Json);
            var response = await ExchangeAsync<SyntheticSearchTaskAck>(payload, SyntheticSearchTaskControl.MaximumFrameBytes, token).ConfigureAwait(false);
            if (response.Schema != SyntheticSearchTaskControl.Schema || response.ExecutionId != start.ExecutionId || response.Sequence != intent.Sequence
                || response.IntentSha256 != TraversalArchive.Hash(payload) || response.Decision != SyntheticSearchInputDecision.Granted)
                throw new BrowserOperationException("SEARCH_TASK_GRANT_UNCONFIRMED");
            _sequence = intent.Sequence;
        }
        catch { await stop.CancelAsync().ConfigureAwait(false); throw; }
    }

    internal async Task SourcesAsync(int trial, string document, long epoch, ImmutableArray<CompletedCaptureSource> originals, CancellationToken token)
    {
        try
        {
            if (trial != _trial || stop.IsCancellationRequested) throw new BrowserOperationException("SEARCH_TASK_SOURCE_SCOPE_INVALID");
            var frames = ImmutableArray.CreateBuilder<FixtureSourceFrame>(); var size = 0;
            async Task Flush()
            {
                if (frames.Count == 0) return;
                var batch = new SyntheticSearchSourceBatch(SyntheticSearchSourceProtocol.Schema, start.ExecutionId, trial,
                    _sourceSequence + 1, document, epoch, frames.ToImmutable());
                SyntheticSearchSourceProtocol.Validate(batch, start.ExecutionId, _sourceSequence + 1);
                var payload = JsonSerializer.SerializeToUtf8Bytes(batch, ReaderWorkerWire.Json);
                var ack = await ExchangeAsync<SyntheticSearchSourceAck>(payload, SyntheticSearchSourceProtocol.MaximumFrameBytes, token).ConfigureAwait(false);
                if (ack.Schema != SyntheticSearchSourceProtocol.Schema || ack.ExecutionId != start.ExecutionId || ack.Trial != trial
                    || ack.Sequence != batch.Sequence || ack.BatchSha256 != TraversalArchive.Hash(payload))
                    throw new BrowserOperationException("SEARCH_TASK_SOURCE_ACK_UNCONFIRMED");
                _sourceSequence = batch.Sequence;
                frames.Clear(); size = 0;
            }
            foreach (var original in originals)
            {
                if (frames.Count == 64 || original.Content.Length > 512 * 1024 - size) await Flush().ConfigureAwait(false);
                frames.Add(new(original.Frame.Sequence, original.Frame.File, original.Frame.PresentationUnixMilliseconds, original.Frame.Sha256, original.Content));
                size += original.Content.Length;
            }
            await Flush().ConfigureAwait(false);
        }
        catch { await stop.CancelAsync().ConfigureAwait(false); throw; }
    }

    private async Task<T> ExchangeAsync<T>(byte[] payload, int maximum, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, stop.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await ReaderWorkerWire.WriteAsync(progress, payload, maximum, deadline.Token).ConfigureAwait(false);
        var bytes = await ReaderWorkerWire.ReadAsync(input, SyntheticSearchTaskControl.MaximumFrameBytes, deadline.Token).ConfigureAwait(false)
            ?? throw new BrowserOperationException("SEARCH_TASK_ACK_MISSING");
        deadline.Token.ThrowIfCancellationRequested();
        var ack = JsonSerializer.Deserialize<T>(bytes, ReaderWorkerWire.Json);
        if (ack is null || !bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(ack, ReaderWorkerWire.Json)))
            throw new BrowserOperationException("SEARCH_TASK_ACK_INVALID");
        deadline.Token.ThrowIfCancellationRequested();
        return ack;
    }
}
