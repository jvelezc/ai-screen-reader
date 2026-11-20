using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;

namespace HeadlessScreenReader.Tests;

public sealed class PublicTaskLiveFrameTests
{
    [Fact]
    [Trait("Category", "Browser")]
    public async Task ActualChromiumScreencastProvidesABoundedDecodableLiveImage()
    {
        var folder = AttestationRoot();
        Directory.CreateDirectory(folder);
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.CapturePrivacy);
        await using var worker = new PublicNavigationBrowser(browser, PublicTaskTests.Report().Start.Job, folder);
        await worker.StartCaptureAsync(default);
        var captured = Assert.IsType<PublicTaskBrowserFrame>(worker.TakeLiveFrame());
        Assert.True(PublicTaskLiveFrame.TryJpegSize(captured.Data, out var width, out var height));
        var decoded = await CaptureProbeDecoder.InspectAsync(browser.DiagnosticPage, captured.Data, video: false);
        Assert.Equal(1, decoded.Frames);
        Assert.InRange(width, 1, 1280);
        Assert.InRange(height, 1, 720);
    }

    [Fact]
    [Trait("Category", "Browser")]
    public async Task ActualBrowserPointerObservationTracksTrustedInputOnly()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.CapturePrivacy);
        var page = browser.DiagnosticPage;
        await page.SetContentAsync("<a href='#target' onclick='event.preventDefault()' style='display:block;margin:100px;width:200px;height:40px'>Target</a>");
        var target = await page.Locator("a").EvaluateAsync<string>("a => a.href");
        var box = Assert.IsType<Microsoft.Playwright.LocatorBoundingBoxResult>(await page.Locator("a").BoundingBoxAsync());
        var viewport = await page.EvaluateAsync<int[]>("() => [innerWidth, innerHeight]");
        await browser.PublicTaskPointerAsync(0, target, default);
        var point = Assert.IsType<PublicTaskLivePointer>(browser.LivePointer(DateTimeOffset.UtcNow));
        Assert.True(point.Pressed);
        Assert.InRange(point.X * viewport[0] / 10000f, box.X - 1, box.X + box.Width);
        Assert.InRange(point.Y * viewport[1] / 10000f, box.Y - 1, box.Y + box.Height);
        await page.EvaluateAsync("() => document.querySelector('a').dispatchEvent(new PointerEvent('pointermove', { bubbles: true, pointerType: 'mouse', clientX: 2, clientY: 2 }))");
        Assert.Equal(point, browser.LivePointer(DateTimeOffset.UtcNow));
        Assert.Null(browser.LivePointer(DateTimeOffset.UtcNow.AddSeconds(4)));
    }

    [Fact]
    public async Task LiveFrameAndFinalArchiveUseDistinctBoundedMessages()
    {
        var start = PublicTaskTests.Report().Start;
        var frame = MakeFrame(start, 1);
        byte[] archive = [2, 9, 1, 5, 123];
        using var stream = new MemoryStream();
        await PublicTaskLiveWire.WriteFrameAsync(stream, frame, default);
        await PublicTaskLiveWire.WriteArchiveAsync(stream, archive, default);
        stream.Position = 0;
        var observations = new List<PublicTaskLiveFrame>();
        var result = await PublicTaskLiveWire.ReadArchiveAsync(stream, (bytes, _) =>
        {
            observations.Add(JsonSerializer.Deserialize<PublicTaskLiveFrame>(bytes, ReaderWorkerWire.Json)!);
            return Task.CompletedTask;
        }, default);
        Assert.Equal(archive, result);
        var observed = Assert.Single(observations);
        Assert.Equal(frame.Data, observed.Data);
        Assert.Equal(frame.ExecutionId, observed.ExecutionId);
        stream.Position = stream.Length;
        stream.WriteByte(1);
        stream.Position = 0;
        await Assert.ThrowsAsync<IOException>(() => PublicTaskLiveWire.ReadArchiveAsync(stream, (_, _) => Task.CompletedTask, default));
    }

    [Fact]
    public async Task LiveFrameCacheRejectsStaleForeignAndClosedAttemptFrames()
    {
        var root = AttestationRoot();
        var start = PublicTaskTests.Report().Start;
        var store = new PublicTaskLiveFrameStore(root);
        await WriteFence(root, start);
        using var sink = new PublicTaskLiveFrameSink(start, store.PathFor(start.ExecutionId));
        await sink.SetAttemptAsync(1, default);
        var frame = MakeFrame(start, 1);
        await Write(frame with { ExecutionId = Guid.NewGuid() });
        Assert.Null(await Current(1));
        await Write(frame with { CapturedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        Assert.Null(await Current(1));
        await Write(frame with { Sha256 = new('e', 64) });
        Assert.Null(await Current(1));
        await Write(frame with { Pointer = new(2000, 5000, true) });
        Assert.Null(await Current(1));
        await Write(frame with { LastKey = "F12" });
        Assert.Null(await Current(1));
        await Write(frame);
        Assert.Equal(frame.Sha256, Assert.IsType<PublicTaskLiveFrame>(await Current(1)).Sha256);
        Assert.Null(await Current(2));
        Assert.Null(await store.ReadAsync(start.Job with { RequestId = Guid.NewGuid() }, 1, DateTimeOffset.UtcNow, default));
        await sink.SetAttemptAsync(0, default);
        await Write(frame with { Sequence = 2 });
        Assert.Null(await Current(1));
        await sink.SetAttemptAsync(2, default);
        await Write(frame with { Sequence = 2 });
        Assert.Null(await Current(2));
        await Write(frame with { Sequence = 3, Attempt = 2 });
        Assert.Equal(3, Assert.IsType<PublicTaskLiveFrame>(await Current(2)).Sequence);

        Task Write(PublicTaskLiveFrame value) => sink.ObserveAsync(JsonSerializer.SerializeToUtf8Bytes(value, ReaderWorkerWire.Json), default);
        Task<PublicTaskLiveFrame?> Current(int attempt) => store.ReadAsync(start.Job, attempt, DateTimeOffset.UtcNow, default);
    }

    [Fact]
    public async Task LiveReadUsesExactRunningCreatorAndRechecksAccessAfterOpeningFrame()
    {
        using var rig = await PublicTaskHttpTests.Rig.Create();
        await using var server = await rig.Server();
        using var anonymous = rig.Http(server, false);
        using var authorized = rig.Http(server);
        var start = new PublicTaskStart(PublicTaskStart.CurrentSchema, rig.Status.Job, Guid.NewGuid(), Guid.NewGuid());
        var store = new PublicTaskLiveFrameStore(rig.Root);
        using var sink = new PublicTaskLiveFrameSink(start, store.PathFor(start.ExecutionId));
        await WriteFence(rig.Root, start);
        await sink.SetAttemptAsync(1, default);
        var frame = MakeFrame(start, 1);
        await sink.ObserveAsync(JsonSerializer.SerializeToUtf8Bytes(frame, ReaderWorkerWire.Json), default);
        var route = rig.Path + "/live-frame";
        using var unauthorized = await anonymous.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        var settled = rig.Status;
        using var finished = await authorized.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
        Assert.Equal("null", await finished.Content.ReadAsStringAsync());
        rig.Status = settled with
        {
            State = "leased",
            Phase = "KeyboardAttempt1",
            FinishedAt = null,
            ProducerId = null,
            ReportSha256 = null,
            Publication = null
        };
        using var active = await authorized.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.True(active.Headers.CacheControl?.NoStore);
        var live = Assert.IsType<PublicTaskLiveFrame>(await active.Content.ReadFromJsonAsync<PublicTaskLiveFrame>(PortalJson.Options));
        Assert.Equal((frame.ExecutionId, frame.Attempt, frame.Sha256), (live.ExecutionId, live.Attempt, live.Sha256));
        Assert.Equal(frame.Data, live.Data);
        var current = rig.Status;
        rig.Status = current with { CreatedBy = Guid.NewGuid() };
        using var foreignCreator = await authorized.GetAsync(route);
        Assert.Equal(HttpStatusCode.NotFound, foreignCreator.StatusCode);
        rig.Status = current;
        rig.DenyAfter = rig.Reads + 1;
        using var revoked = await authorized.GetAsync(route);
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.DoesNotContain(frame.Sha256, await revoked.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, rig.Creates);
    }

    private static string AttestationRoot() => Path.GetFullPath(Path.Combine("artifacts", "public-live-frame-tests", Guid.NewGuid().ToString("N")));

    private static async Task WriteFence(string root, PublicTaskStart start)
    {
        var directory = Path.Combine(root, ".public-task-runtime", "executions");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, $"{start.Job.OrganizationId:N}-{start.Job.JobId:N}.json"),
            JsonSerializer.SerializeToUtf8Bytes(start, ReaderWorkerWire.Json));
    }

    private static PublicTaskLiveFrame MakeFrame(PublicTaskStart start, int attempt)
    {
        // Minimal Synthetic JPEG envelope. Decodable Chromium pixels are exercised by the native worker proof.
        byte[] data = [0xff, 0xd8, 0xff, 0xc0, 0, 11, 8, 0, 1, 0, 1, 1, 1, 0x11, 0, 0xff, 0xd9];
        return new(PublicTaskLiveFrame.CurrentSchema, start.Job.OrganizationId, start.Job.ParentScanId, start.Job.JobId,
            start.ExecutionId, attempt, 1, DateTimeOffset.UtcNow, 1, 1, data, PublicTaskRules.Hash(data));
    }
}
