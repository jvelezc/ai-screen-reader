using System.Collections.Immutable;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Cli;

public static class FixtureProgressWorker
{
    public static async Task<int> RunAsync(Stream input, Stream output, Stream progress)
    {
        FixtureProgressStart? failureScope = null;
        var sequence = 0;
        var sourceSequence = 0;
        try
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var start = await FixtureProgressProtocol.ReadAsync<FixtureProgressStart>(input, 4096, stop.Token);
            FixtureProgressProtocol.Validate(start);
            failureScope = start;
            async Task Sources(ImmutableArray<CompletedCaptureSource> originals, CancellationToken token)
            {
                var frames = ImmutableArray.CreateBuilder<FixtureSourceFrame>(); var size = 0;
                async Task Flush()
                {
                    if (frames.Count == 0) return;
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    var batch = new FixtureSourceBatch(FixtureSourceProtocol.Schema, start.ExecutionId, ++sourceSequence, frames.ToImmutable());
                    FixtureSourceProtocol.Validate(batch, start, sourceSequence);
                    var encoded = FixtureProgressProtocol.Encode(batch);
                    await ReaderWorkerWire.WriteAsync(progress, encoded, FixtureProgressProtocol.MaximumFrameBytes, timeout.Token);
                    var ack = await FixtureProgressProtocol.ReadAsync<FixtureSourceAck>(input, 4096, timeout.Token);
                    FixtureSourceProtocol.Validate(ack, start.ExecutionId, sourceSequence, TraversalArchive.Hash(encoded));
                    token.ThrowIfCancellationRequested(); frames.Clear(); size = 0;
                }
                foreach (var original in originals)
                {
                    if (frames.Count == 64 || original.Content.Length > FixtureSourceProtocol.MaximumBatchBytes - size)
                        await Flush();
                    var frame = original.Frame;
                    frames.Add(new(frame.Sequence, frame.File, frame.PresentationUnixMilliseconds, frame.Sha256, original.Content));
                    size += original.Content.Length;
                }
                await Flush();
            }
            var bytes = await KeyboardFixtureWorker.RecordAsync(start.Job, stop.Token, async (checkpoint, token) =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var value = new FixtureProgressMessage(FixtureProgressProtocol.Schema, start.ExecutionId, ++sequence, checkpoint);
                await ReaderWorkerWire.WriteAsync(progress, FixtureProgressProtocol.Encode(value), FixtureProgressProtocol.MaximumFrameBytes, timeout.Token);
                var ack = await FixtureProgressProtocol.ReadAsync<FixtureProgressAck>(input, 4096, timeout.Token);
                FixtureProgressProtocol.Validate(ack, start.ExecutionId, sequence);
                token.ThrowIfCancellationRequested();
            }, start.RetainSourceFrames ? Sources : null);
            await output.WriteAsync(bytes, stop.Token); return 0;
        }
        catch (Exception failure)
        {
            if (failureScope is not null)
            {
                var diagnostic = FixtureRecordingFailureProtocol.FromException(failureScope, sequence, sourceSequence, failure);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try
                {
                    await ReaderWorkerWire.WriteAsync(progress, FixtureProgressProtocol.Encode(diagnostic),
                        FixtureRecordingFailureProtocol.MaximumBytes, timeout.Token);
                }
                catch (Exception) { /* A failed diagnostic write cannot produce a recording or disclose raw exception data. */ }
            }
            return 3;
        }
    }
}
