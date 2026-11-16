using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Closed parent exchange. Original retention and input permission have separate durable acknowledgements.</summary>
internal sealed class SyntheticSearchTaskProgressExchange : IWorkerProgressExchange
{
    private readonly SyntheticSearchTaskStart _start;
    private readonly SyntheticSearchTaskGrants _grants;
    private readonly Func<CancellationToken, Task<bool>> _authorize;

    internal SyntheticSearchTaskProgressExchange(SyntheticSearchTaskStart start, SyntheticSearchTaskGrants grants,
        Func<CancellationToken, Task<bool>> authorize)
    {
        start.Validate();
        ArgumentNullException.ThrowIfNull(authorize);
        if (grants.Start != start) throw new BrowserOperationException("SEARCH_TASK_START_INVALID");
        _start = start; _grants = grants; _authorize = authorize;
    }

    public Task StartAsync(Stream input, CancellationToken token) => ReaderWorkerWire.WriteAsync(input,
        JsonSerializer.SerializeToUtf8Bytes(_start, ReaderWorkerWire.Json), SyntheticSearchTaskControl.MaximumFrameBytes, token);

    public async Task ReadAsync(Stream progress, Stream acknowledgements, CancellationTokenSource stop)
    {
        try
        {
            while (await ReaderWorkerWire.ReadAsync(progress, SyntheticSearchSourceProtocol.MaximumFrameBytes, stop.Token).ConfigureAwait(false) is { } bytes)
            {
                using var json = JsonDocument.Parse(bytes, new() { AllowDuplicateProperties = false });
                if (!json.RootElement.TryGetProperty("Schema", out var schema))
                    throw new BrowserOperationException("SEARCH_TASK_PROGRESS_INVALID");
                if (schema.GetString() == SyntheticSearchSourceProtocol.Schema)
                {
                    var batch = JsonSerializer.Deserialize<SyntheticSearchSourceBatch>(bytes, ReaderWorkerWire.Json)
                        ?? throw new BrowserOperationException("SEARCH_TASK_SOURCE_FRAMES_INVALID");
                    if (!bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(batch, ReaderWorkerWire.Json)))
                        throw new BrowserOperationException("SEARCH_TASK_PROGRESS_INVALID");
                    var ack = await _grants.RetainSourcesAsync(batch, _authorize, stop.Token).ConfigureAwait(false);
                    await ReaderWorkerWire.WriteAsync(acknowledgements, JsonSerializer.SerializeToUtf8Bytes(ack, ReaderWorkerWire.Json),
                        SyntheticSearchTaskControl.MaximumFrameBytes, stop.Token).ConfigureAwait(false);
                    continue;
                }
                if (schema.GetString() != SyntheticSearchTaskControl.Schema || bytes.Length > SyntheticSearchTaskControl.MaximumFrameBytes)
                    throw new BrowserOperationException("SEARCH_TASK_PROGRESS_INVALID");
                var intent = JsonSerializer.Deserialize<SyntheticSearchTaskIntent>(bytes, ReaderWorkerWire.Json)
                    ?? throw new BrowserOperationException("SEARCH_TASK_INTENT_INVALID");
                if (!bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(intent, ReaderWorkerWire.Json)))
                    throw new BrowserOperationException("SEARCH_TASK_PROGRESS_INVALID");
                if (intent.Input is { } input && !_grants.HasSources(intent.Boundary.Trial, input.DocumentId, input.CaptureEpoch))
                    throw new BrowserOperationException("SEARCH_TASK_ORIGINALS_REQUIRED_BEFORE_INPUT");
                var response = await _grants.PrepareAsync(intent, _authorize, stop.Token).ConfigureAwait(false);
                await ReaderWorkerWire.WriteAsync(acknowledgements, JsonSerializer.SerializeToUtf8Bytes(response, ReaderWorkerWire.Json),
                    SyntheticSearchTaskControl.MaximumFrameBytes, stop.Token).ConfigureAwait(false);
                if (response.Decision != SyntheticSearchInputDecision.Granted)
                    throw new BrowserOperationException("SEARCH_TASK_AUTHORITY_UNCONFIRMED");
            }
        }
        catch (OperationCanceledException) { _grants.Stop(); await stop.CancelAsync().ConfigureAwait(false); throw; }
        catch (BrowserOperationException) { _grants.Stop(); await stop.CancelAsync().ConfigureAwait(false); throw; }
        catch (Exception)
        {
            _grants.Stop(); await stop.CancelAsync().ConfigureAwait(false);
            throw new BrowserOperationException("SEARCH_TASK_PROGRESS_UNCONFIRMED");
        }
    }

    internal void RequireComplete() => _grants.RequireComplete();
}
