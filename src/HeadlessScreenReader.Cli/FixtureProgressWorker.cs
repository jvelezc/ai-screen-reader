using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

public static class FixtureProgressWorker
{
    public static async Task<int> RunAsync(Stream input, Stream output, Stream progress)
    {
        try
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var start = await FixtureProgressProtocol.ReadAsync<FixtureProgressStart>(input, 4096, stop.Token);
            FixtureProgressProtocol.Validate(start);
            var sequence = 0;
            var bytes = await KeyboardFixtureWorker.RecordAsync(start.Job, stop.Token, async (checkpoint, token) =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var value = new FixtureProgressMessage(FixtureProgressProtocol.Schema, start.ExecutionId, ++sequence, checkpoint);
                await ReaderWorkerWire.WriteAsync(progress, FixtureProgressProtocol.Encode(value), FixtureProgressProtocol.MaximumFrameBytes, timeout.Token);
                var ack = await FixtureProgressProtocol.ReadAsync<FixtureProgressAck>(input, 4096, timeout.Token);
                FixtureProgressProtocol.Validate(ack, start.ExecutionId, sequence);
                token.ThrowIfCancellationRequested();
            });
            await output.WriteAsync(bytes, stop.Token); return 0;
        }
        catch (Exception) { return 3; }
    }
}
