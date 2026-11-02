using System.Buffers.Binary;
using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Browser.Playwright;

/// <summary>Frames native PNG output without sampling. Pixel validation remains independent in Chromium.</summary>
internal static class PngFrameReader
{
    internal const int MaximumFrames = PassiveObservationRecording.MaximumDecodedVideoFrames;
    internal const int MaximumFrameBytes = 4 * 1024 * 1024;
    internal const int MaximumBytes = 16 * 1024 * 1024;
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    internal static async Task<ImmutableArray<byte[]>> ReadAsync(Stream input, CancellationToken token = default,
        int maxFrames = MaximumFrames, int maxFrameBytes = MaximumFrameBytes, int maxBytes = MaximumBytes)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (maxFrames is < 1 or > MaximumFrames) throw new ArgumentOutOfRangeException(nameof(maxFrames));
        if (maxFrameBytes is < 8 or > MaximumFrameBytes) throw new ArgumentOutOfRangeException(nameof(maxFrameBytes));
        if (maxBytes is < 8 or > MaximumBytes) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        var frames = ImmutableArray.CreateBuilder<byte[]>();
        var signature = new byte[8]; var header = new byte[8]; long total = 0;
        void Admit(long bytes)
        { if (bytes > maxFrameBytes || bytes > maxBytes - total) throw new BrowserOperationException("CAPTURE_FRAME_LIMIT"); }
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (await input.ReadAsync(signature.AsMemory(0, 1), token).ConfigureAwait(false) == 0) return frames.ToImmutable();
                if (frames.Count == maxFrames) throw new BrowserOperationException("CAPTURE_FRAME_LIMIT");
                Admit(8);
                await input.ReadExactlyAsync(signature.AsMemory(1), token).ConfigureAwait(false);
                if (!signature.AsSpan().SequenceEqual(Signature)) throw Invalid();
                using var frame = new MemoryStream(Math.Min(64, maxFrameBytes));
                frame.Write(signature); var first = true; var hasData = false;
                while (true)
                {
                    Admit(frame.Length + 8);
                    await input.ReadExactlyAsync(header, token).ConfigureAwait(false);
                    var length = BinaryPrimitives.ReadUInt32BigEndian(header);
                    Admit(frame.Length + 12 + length);
                    for (var i = 4; i < 8; i++) if (header[i] is not (>= 65 and <= 90) and not (>= 97 and <= 122)) throw Invalid();
                    var ihdr = header.AsSpan(4).SequenceEqual("IHDR"u8);
                    var idat = header.AsSpan(4).SequenceEqual("IDAT"u8);
                    var iend = header.AsSpan(4).SequenceEqual("IEND"u8);
                    if (first ? !ihdr || length != 13 : ihdr) throw Invalid();
                    if (iend && (length != 0 || !hasData)) throw Invalid();
                    var body = new byte[checked((int)length + 4)];
                    await input.ReadExactlyAsync(body, token).ConfigureAwait(false);
                    if (ihdr && (BinaryPrimitives.ReadUInt32BigEndian(body) is 0 or > 1280
                        || BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(4)) is 0 or > 720))
                        throw new BrowserOperationException("CAPTURE_DIMENSIONS_INVALID");
                    if (idat && length > 0) hasData = true;
                    frame.Write(header); frame.Write(body); first = false;
                    if (!iend) continue;
                    var bytes = frame.ToArray(); total += bytes.Length; frames.Add(bytes); break;
                }
            }
        }
        catch (EndOfStreamException) { throw Invalid(); }
    }

    private static BrowserOperationException Invalid() => new("CAPTURE_FRAME_INVALID");
}
