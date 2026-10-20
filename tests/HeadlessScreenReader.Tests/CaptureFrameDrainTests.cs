using HeadlessScreenReader.Browser.Playwright;

namespace HeadlessScreenReader.Tests;

public sealed class CaptureFrameDrainTests
{
    [Fact]
    public async Task Close_WaitsForActualWritesAndPermanentlyRejectsLateCallbacks()
    {
        var drain = new CaptureFrameDrain(100);
        var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = drain.WriteAsync(100, 8, sequence => { Assert.Equal(1, sequence); return write.Task; });
        var closed = drain.CloseAdmission();
        Assert.False(closed.IsCompleted);
        await drain.WriteAsync(101, 8, _ => throw new InvalidOperationException("Late callback wrote data."));
        write.SetResult();
        await pending;
        await closed.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new(1, 0, 8, null), drain.Status);
        await drain.WriteAsync(102, 8, _ => throw new InvalidOperationException("Closed segment reopened."));
        Assert.Equal(1, drain.Status.Accepted);
    }

    [Fact]
    public async Task PreviousEpochPresentation_IsDroppedAndBoundaryTimestampIsAdmitted()
    {
        var drain = new CaptureFrameDrain(100);
        var written = 0;
        await drain.WriteAsync(99.99, 8, _ => throw new InvalidOperationException("Stale frame persisted."));
        await drain.WriteAsync(100, 8, _ => { written++; return Task.CompletedTask; });
        Assert.Equal(1, written);
        Assert.Equal(new(1, 1, 8, null), drain.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactFrameAndByteBounds_AreAllowedAndNextWriteFailsClosed(bool byteBound)
    {
        var drain = new CaptureFrameDrain(0, maxFrames: byteBound ? 3 : 2, maxBytes: byteBound ? 16 : 24);
        var writes = 0;
        Task Sink(int sequence) { writes++; Assert.Equal(writes, sequence); return Task.CompletedTask; }
        await drain.WriteAsync(1, 8, Sink);
        await drain.WriteAsync(2, 8, Sink);
        await drain.WriteAsync(3, 8, Sink);
        await drain.WriteAsync(4, 1, Sink);
        Assert.Equal(2, writes);
        Assert.Equal(new(2, 0, 16, "CAPTURE_FRAME_LIMIT"), drain.Status);
        await drain.CloseAdmission();
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public async Task InvalidFrame_IsNeverWrittenAndFaultsSegment(double timestamp, int bytes)
    {
        var drain = new CaptureFrameDrain(0);
        await drain.WriteAsync(timestamp, bytes, _ => throw new InvalidOperationException("Invalid data written."));
        Assert.Equal(new(0, 0, 0, "CAPTURE_FRAME_INVALID"), drain.Status);
        await drain.WriteAsync(2, 8, _ => throw new InvalidOperationException("Faulted segment reopened."));
    }

    [Fact]
    public async Task SinkFailure_IsSanitizedAndDrainStillSettles()
    {
        var drain = new CaptureFrameDrain(0);
        await drain.WriteAsync(1, 8, _ => throw new IOException("synthetic-private-payload"));
        await drain.CloseAdmission().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("CAPTURE_FRAME_WRITE_FAILED", drain.Status.ErrorCode);
        Assert.DoesNotContain("synthetic-private-payload", System.Text.Json.JsonSerializer.Serialize(drain.Status));
        await drain.WriteAsync(2, 8, _ => throw new InvalidOperationException("Failed sink reused."));
        Assert.Equal(1, drain.Status.Accepted);
    }
}
