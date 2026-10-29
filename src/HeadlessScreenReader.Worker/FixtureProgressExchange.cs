using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>The child cannot advance until current authority and the retained checkpoint write are confirmed.</summary>
public sealed class FixtureProgressExchange(FixtureProgressStart start, CommandProgressJournal journal,
    Func<CancellationToken, Task<bool>> authorize)
{
    private readonly List<FixtureProgressMessage> _messages = [];
    public Task StartAsync(Stream input, CancellationToken token) => ReaderWorkerWire.WriteAsync(input,
        FixtureProgressProtocol.Encode(start), 4096, token);
    public async Task ReadAsync(Stream progress, Stream acknowledgements, CancellationTokenSource stop)
    {
        try
        {
            while (await ReaderWorkerWire.ReadAsync(progress, FixtureProgressProtocol.MaximumFrameBytes, stop.Token).ConfigureAwait(false) is { } bytes)
            {
                var message = JsonSerializer.Deserialize<FixtureProgressMessage>(bytes, ReaderWorkerWire.Json)
                    ?? throw new BrowserOperationException("WORKER_PROGRESS_INVALID");
                FixtureProgressProtocol.Validate(message, start, _messages.LastOrDefault());
                if (!await authorize(stop.Token).ConfigureAwait(false)) throw new BrowserOperationException("WORKER_PROGRESS_AUTHORITY_UNCONFIRMED");
                try { await journal.AppendAsync(message, stop.Token).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { throw new BrowserOperationException("WORKER_PROGRESS_WRITE_UNCONFIRMED"); }
                stop.Token.ThrowIfCancellationRequested();
                _messages.Add(message);
                await ReaderWorkerWire.WriteAsync(acknowledgements,
                    FixtureProgressProtocol.Encode(new FixtureProgressAck(FixtureProgressProtocol.Schema, start.ExecutionId, message.Sequence)),
                    4096, stop.Token).ConfigureAwait(false);
            }
        }
        catch { await stop.CancelAsync().ConfigureAwait(false); throw; }
    }
    public void Match(TraversalReceipt receipt)
    {
        FixtureProgressProtocol.Require(!receipt.Observations.IsDefaultOrEmpty && _messages.Count == 1 + 2 * receipt.Attempts.Length
            && FixtureProgressProtocol.Same(_messages[0].Checkpoint.Source, receipt.Observations[0]));
        for (var i = 0; i < receipt.Attempts.Length; i++)
        {
            var attempt = receipt.Attempts[i];
            var source = receipt.Observations.Single(o => o.Id == attempt.SourceObservationId);
            var result = attempt.ResultObservationId is null ? null : receipt.Observations.Single(o => o.Id == attempt.ResultObservationId);
            var intent = _messages[1 + 2 * i].Checkpoint; var observed = _messages[2 + 2 * i].Checkpoint;
            FixtureProgressProtocol.Require(FixtureProgressProtocol.Same(intent.Source, source) && intent.Instruction == attempt.Instruction
                && FixtureProgressProtocol.Same(observed.Attempt, attempt) && FixtureProgressProtocol.Same(observed.Result, result));
        }
    }
}
