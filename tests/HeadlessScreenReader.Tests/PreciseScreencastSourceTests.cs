using System.Collections.Concurrent;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using Microsoft.Playwright;

namespace HeadlessScreenReader.Tests;

public sealed class PreciseScreencastSourceTests
{
    [Fact]
    public async Task RawDoubleTimestampsSurviveAndStopWaitsForActualFrameAcknowledgements()
    {
        var folder = NewFolder();
        var session = new Session();
        var source = new PreciseScreencastSource(session, folder);
        try
        {
            var start = source.StartAsync();
            for (var i = 0; i < 3; i++) session.Emit(JsonSerializer.SerializeToElement(new
            {
                data = Convert.ToBase64String([1, 2, 3]),
                metadata = new { timestamp = 1_792_519_430.106268 + i * .04 },
                sessionId = i + 1
            }));
            await start.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(3, source.Frames.Length);
            Assert.Equal(3, source.Frames.Select(f => f.PresentationUnixMilliseconds).Distinct().Count());
            Assert.InRange(source.Frames[0].PresentationUnixMilliseconds, 1_792_519_430_106.267, 1_792_519_430_106.269);
            var stop = source.StopAsync();
            Assert.False(stop.IsCompleted);
            session.AllowAcks.SetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(3, session.Commands.Count(c => c == "Page.screencastFrameAck"));
            Assert.Contains("Page.stopScreencast", session.Commands);
            await Assert.ThrowsAsync<BrowserOperationException>(() => source.StartAsync());
            session.Emit(JsonSerializer.SerializeToElement(new { data = "late" }));
            Assert.Equal(3, Directory.GetFiles(folder).Length);
            foreach (var frame in source.Frames) Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(folder, frame.File)));
        }
        finally
        {
            session.AllowAcks.TrySetResult();
            try { await source.StopAsync(); } catch (BrowserOperationException) { }
            DeleteOwnedLeaves(folder);
        }
    }

    [Fact]
    public async Task InvalidProtocolPayload_FailsClosedWithoutPersistingOrExposingRawData()
    {
        var folder = NewFolder();
        var session = new Session();
        var source = new PreciseScreencastSource(session, folder);
        try
        {
            var start = source.StartAsync();
            session.Emit(JsonSerializer.SerializeToElement(new { data = "synthetic-private-payload" }));
            var error = await Assert.ThrowsAsync<BrowserOperationException>(() => start);
            Assert.Equal("PRECISE_CAPTURE_FRAME_INVALID", error.Code);
            Assert.DoesNotContain("synthetic-private-payload", error.ToString());
            await Assert.ThrowsAsync<BrowserOperationException>(() => source.StopAsync());
            Assert.Empty(source.Frames);
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally
        {
            session.AllowAcks.TrySetResult();
            try { await source.StopAsync(); } catch (BrowserOperationException) { }
            DeleteOwnedLeaves(folder);
        }
    }

    private static string NewFolder()
    {
        var folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hsr-source-test-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void DeleteOwnedLeaves(string folder)
    {
        foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
        Directory.Delete(folder);
    }

    private sealed class Session : ICDPSession
    {
        private readonly Events _events = new();
        public ConcurrentQueue<string> Commands { get; } = new();
        public TaskCompletionSource AllowAcks { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<ICDPSession> Close { add { } remove { } }
        public Task DetachAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ICDPSessionEvent Event(string name) => _events;
        public async Task<JsonElement?> SendAsync(string method, Dictionary<string, object>? args = null)
        {
            Commands.Enqueue(method);
            if (method == "Page.screencastFrameAck") await AllowAcks.Task;
            return null;
        }
        public void Emit(JsonElement payload) => _events.Emit(payload);
    }

    private sealed class Events : ICDPSessionEvent
    {
        public string EventName => "Page.screencastFrame";
        public event EventHandler<JsonElement?>? OnEvent;
        public void Emit(JsonElement payload) => OnEvent?.Invoke(this, payload);
    }
}
