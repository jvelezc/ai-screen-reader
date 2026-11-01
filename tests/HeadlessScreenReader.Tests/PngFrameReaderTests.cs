using System.Buffers.Binary;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class PngFrameReaderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4096)]
    public async Task ConcatenatedFramesPreserveEveryByteAndOrderAcrossShortReads(int readSize)
    {
        var first = Frame(1); var second = Frame(2);
        using var input = new ShortReads([.. first, .. second], readSize);
        var frames = await PngFrameReader.ReadAsync(input);
        Assert.Equal(2, frames.Length); Assert.Equal(first, frames[0]); Assert.Equal(second, frames[1]);
        Assert.Equal(input.Length, input.Position);
    }

    [Fact]
    public async Task EmptyOutputStaysEmptyForTheNativeExitCodeCheck()
    { using var input = new MemoryStream(); Assert.Empty(await PngFrameReader.ReadAsync(input)); }

    [Theory]
    [InlineData(10000, true)]
    [InlineData(10001, false)]
    public async Task NativeFrameCountMatchesTheExistingArchiveBoundAndRejectsTheNextFrame(int count, bool accepted)
    {
        var frame = Frame(1); using var input = new MemoryStream();
        for (var i = 0; i < count; i++) input.Write(frame);
        input.Position = 0;
        if (accepted)
        {
            var frames = await PngFrameReader.ReadAsync(input);
            Assert.Equal(count, frames.Length); Assert.Equal(frame, frames[0]); Assert.Equal(frame, frames[^1]);
        }
        else Assert.Equal("CAPTURE_FRAME_LIMIT", (await Assert.ThrowsAsync<BrowserOperationException>(() => PngFrameReader.ReadAsync(input))).Code);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task PerFrameAdmissionIncludesTheSignatureChunkHeadersAndCrc(int delta, bool accepted)
    {
        var frame = Frame(1); using var input = new MemoryStream(frame);
        if (accepted) Assert.Equal(frame, Assert.Single(await PngFrameReader.ReadAsync(input, maxFrameBytes: frame.Length + delta)));
        else Assert.Equal("CAPTURE_FRAME_LIMIT", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => PngFrameReader.ReadAsync(input, maxFrameBytes: frame.Length + delta))).Code);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task AggregateAdmissionCannotReturnAnAlreadyReadSuccessfulPrefix(int delta, bool accepted)
    {
        var frame = Frame(1); using var input = new MemoryStream([.. frame, .. frame]);
        if (accepted)
        {
            var frames = await PngFrameReader.ReadAsync(input, maxBytes: 2 * frame.Length + delta);
            Assert.Equal(2, frames.Length); Assert.Equal(frame, frames[1]);
        }
        else Assert.Equal("CAPTURE_FRAME_LIMIT", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => PngFrameReader.ReadAsync(input, maxBytes: 2 * frame.Length + delta))).Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(57)]
    public async Task TruncatedFrameNeverReturnsEarlierCompleteFrames(int prefix)
    {
        var frame = Frame(1); using var input = new MemoryStream([.. frame, .. frame.AsSpan(0, prefix)]);
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => PngFrameReader.ReadAsync(input));
        Assert.Equal("CAPTURE_FRAME_INVALID", error.Code); Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("first-chunk")]
    [InlineData("header-length")]
    [InlineData("repeated-header")]
    [InlineData("missing-data")]
    [InlineData("iend-data")]
    [InlineData("chunk-type")]
    public async Task MalformedNativeFramingIsSanitizedWithoutReturningAFrame(string kind)
    {
        var frame = Frame(1);
        switch (kind)
        {
            case "signature": frame[0] = 0; break;
            case "first-chunk": "IDAT"u8.CopyTo(frame.AsSpan(12)); break;
            case "header-length": BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8), 12); break;
            case "repeated-header": frame = [.. frame.AsSpan(0, 33), .. frame.AsSpan(8, 25), .. frame.AsSpan(33)]; break;
            case "missing-data": frame = [.. frame.AsSpan(0, 33), .. frame.AsSpan(46)]; break;
            case "iend-data": BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(46), 1); break;
            case "chunk-type": frame[37] = 0; break;
        }
        using var input = new MemoryStream(frame);
        var error = await Assert.ThrowsAsync<BrowserOperationException>(() => PngFrameReader.ReadAsync(input));
        Assert.Equal("CAPTURE_FRAME_INVALID", error.Code); Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1281, 1)]
    [InlineData(1, 721)]
    public async Task OversizedOrEmptyDimensionsAreRejectedBeforeBrowserPixelDecoding(uint width, uint height)
    {
        var frame = Frame(1); BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(20), height);
        using var input = new MemoryStream(frame);
        Assert.Equal("CAPTURE_DIMENSIONS_INVALID", (await Assert.ThrowsAsync<BrowserOperationException>(() => PngFrameReader.ReadAsync(input))).Code);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1280, 720)]
    public async Task ValidDimensionBoundariesRetainTheExactNativeBytes(uint width, uint height)
    {
        var frame = Frame(1); BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(20), height);
        using var input = new MemoryStream(frame);
        Assert.Equal(frame, Assert.Single(await PngFrameReader.ReadAsync(input)));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task AStricterFrameLimitIsEnforcedWithoutChangingTheProductionDefault(int count, bool accepted)
    {
        var frame = Frame(1); using var input = new MemoryStream();
        for (var i = 0; i < count; i++) input.Write(frame);
        input.Position = 0;
        if (accepted) Assert.Equal(frame, Assert.Single(await PngFrameReader.ReadAsync(input, maxFrames: 1)));
        else Assert.Equal("CAPTURE_FRAME_LIMIT", (await Assert.ThrowsAsync<BrowserOperationException>(
            () => PngFrameReader.ReadAsync(input, maxFrames: 1))).Code);
    }

    [Fact]
    public async Task DeclaredOversizedChunkIsDeniedBeforeAllocatingOrReadingItsPayload()
    {
        var frame = Frame(1); BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8), uint.MaxValue);
        using var input = new MemoryStream(frame);
        Assert.Equal("CAPTURE_FRAME_LIMIT", (await Assert.ThrowsAsync<BrowserOperationException>(() => PngFrameReader.ReadAsync(input))).Code);
        Assert.Equal(16, input.Position);
    }

    [Fact]
    public async Task AncillaryChunksAreRetainedWithoutTreatingFramingAsPixelVerification()
    {
        var frame = Frame(1); using var chunk = new MemoryStream(); Chunk(chunk, "tEXt"u8.ToArray(), [1, 2, 3]);
        byte[] original = [.. frame.AsSpan(0, 46), .. chunk.ToArray(), .. frame.AsSpan(46)];
        using var input = new MemoryStream(original);
        Assert.Equal(original, Assert.Single(await PngFrameReader.ReadAsync(input)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeOrDuringAFrameCannotReturnAPartialResult(bool during)
    {
        using var stop = new CancellationTokenSource();
        using var input = new ShortReads(Frame(1), 1, during ? stop : null);
        if (!during) stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PngFrameReader.ReadAsync(input, stop.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public async Task CancellationAtOrInsideTheNextFrameCannotReturnTheCompletedPrefix(int offset)
    {
        var frame = Frame(1); using var stop = new CancellationTokenSource();
        using var input = new ShortReads([.. frame, .. frame], 1, stop, frame.Length + offset);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PngFrameReader.ReadAsync(input, stop.Token));
        Assert.True(input.Position >= frame.Length);
    }

    [Theory]
    [InlineData(0, 64, 128)]
    [InlineData(10001, 64, 128)]
    [InlineData(1, 7, 128)]
    [InlineData(1, 4194305, 128)]
    [InlineData(1, 64, 7)]
    [InlineData(1, 64, 16777217)]
    public async Task InternalBudgetOverridesCanOnlyTightenProductionLimits(int count, int frame, int total)
    {
        using var input = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => PngFrameReader.ReadAsync(input,
            maxFrames: count, maxFrameBytes: frame, maxBytes: total));
        Assert.Equal(0, input.Position);
    }

    // Framing fixtures deliberately do not claim valid pixel or CRC evidence.
    // Actual native PNG decoding and first/last pixel controls are exercised by ChromiumCaptureProbeTests.
    private static byte[] Frame(byte marker)
    {
        using var bytes = new MemoryStream(); bytes.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, 1);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 1); header[8] = 8; header[9] = 6;
        Chunk(bytes, "IHDR"u8.ToArray(), header); Chunk(bytes, "IDAT"u8.ToArray(), [marker]); Chunk(bytes, "IEND"u8.ToArray(), []);
        return bytes.ToArray();
    }

    private static void Chunk(Stream output, byte[] type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length); output.Write(type); output.Write(data); output.Write(new byte[4]);
    }

    private sealed class ShortReads(byte[] bytes, int readSize, CancellationTokenSource? cancel = null, int cancelAfter = 9) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position >= cancelAfter) cancel?.Cancel();
            return base.ReadAsync(buffer[..Math.Min(readSize, buffer.Length)], token);
        }
    }
}
