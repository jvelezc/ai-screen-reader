using System.Text.Json;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Worker;

/// <summary>Tags live images and the final archive on stdout. Input grants stay on their original separate lane.</summary>
public static class PublicTaskLiveWire
{
    private const byte Frame = 1, Archive = 2;

    public static Task WriteFrameAsync(Stream output, PublicTaskLiveFrame frame, CancellationToken token) =>
        WriteAsync(output, Frame, JsonSerializer.SerializeToUtf8Bytes(frame, ReaderWorkerWire.Json), PublicTaskLiveFrame.MaximumEnvelopeBytes, token);

    public static Task WriteArchiveAsync(Stream output, byte[] archive, CancellationToken token) =>
        WriteAsync(output, Archive, archive, PublicTaskReport.MaximumArchiveBytes, token);

    internal static async Task<byte[]> ReadArchiveAsync(Stream input, Func<byte[], CancellationToken, Task> observe, CancellationToken token)
    {
        var tag = new byte[1];
        while (await input.ReadAsync(tag, token).ConfigureAwait(false) == 1)
        {
            var limit = tag[0] switch
            {
                Frame => PublicTaskLiveFrame.MaximumEnvelopeBytes,
                Archive => PublicTaskReport.MaximumArchiveBytes,
                _ => throw new IOException("PUBLIC_TASK_OUTPUT_INVALID")
            };
            var bytes = await ReaderWorkerWire.ReadAsync(input, limit, token).ConfigureAwait(false)
                ?? throw new IOException("PUBLIC_TASK_OUTPUT_INCOMPLETE");
            if (tag[0] == Frame)
            {
                await observe(bytes, token).ConfigureAwait(false);
                continue;
            }
            if (await input.ReadAsync(tag, token).ConfigureAwait(false) != 0) throw new IOException("PUBLIC_TASK_OUTPUT_INVALID");
            return bytes;
        }
        throw new IOException("PUBLIC_TASK_OUTPUT_INCOMPLETE");
    }

    private static async Task WriteAsync(Stream output, byte tag, byte[] bytes, int maximum, CancellationToken token)
    {
        await output.WriteAsync(new byte[] { tag }, token).ConfigureAwait(false);
        await ReaderWorkerWire.WriteAsync(output, bytes, maximum, token).ConfigureAwait(false);
    }
}
