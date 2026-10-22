using System.Diagnostics;
using System.IO.Pipelines;
using System.Reflection;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Cli;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class ChromiumReaderWorkerTests
{
    [Fact]
    public async Task PersistentWorkerObservesIndependentDomChangesInTheSameActualContextThenCloses()
    {
        await using var worker = new Harness();
        var first = await worker.ExchangeAsync(ReaderWorkerContractTests.Request());
        var browser = await worker.Browser.Task;
        await browser.DiagnosticPage.EvaluateAsync("""
            () => { window.workerContinuity = 'same-context'; const h = document.createElement('h2');
                    h.id = 'worker-continuity'; h.textContent = 'First observed marker'; document.body.append(h); }
            """);
        var second = await worker.ExchangeAsync(ReaderWorkerContractTests.Request(2, ReaderWorkerOperation.Observe));
        Assert.Contains(second.Snapshot!.Nodes, n => n.Role == "heading" && n.Name == "First observed marker");
        await browser.DiagnosticPage.EvaluateAsync("() => document.getElementById('worker-continuity').textContent = 'Second observed marker'");
        var third = await worker.ExchangeAsync(ReaderWorkerContractTests.Request(3, ReaderWorkerOperation.Observe));
        Assert.Contains(third.Snapshot!.Nodes, n => n.Role == "heading" && n.Name == "Second observed marker");
        Assert.DoesNotContain(third.Snapshot.Nodes, n => n.Name == "First observed marker");
        Assert.Equal("same-context", await browser.DiagnosticPage.EvaluateAsync<string>("() => window.workerContinuity"));
        Assert.Equal(first.DocumentId, second.DocumentId); Assert.Equal(first.DocumentId, third.DocumentId);
        Assert.Equal(1, third.Snapshot.CaptureEpoch);
        var close = ReaderWorkerContractTests.Request(4, ReaderWorkerOperation.Close);
        var closed = await worker.ExchangeAsync(close);
        closed.Validate(close, first.DocumentId);
        Assert.Equal(ReaderWorkerState.Closed, closed.State); Assert.Null(closed.Snapshot);
        Assert.True(closed.Shutdown!.ProcessExitConfirmed);
        Assert.Equal(0, await worker.Run); Assert.Equal("", worker.Diagnostics.ToString());
    }

    [Fact]
    public async Task ActualCliClosureKeepsTwoObservationsAndAcknowledgesNativeExitBeforeProcessExit()
    {
        var assembly = typeof(ChromiumReaderWorkerTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "ReaderWorkerAssembly").Value!;
        Assert.True(File.Exists(assembly), "Launch the CLI application's own complete build output.");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(assembly)!
        };
        start.ArgumentList.Add(assembly); start.ArgumentList.Add("--reader-fixture");
        start.Environment.Clear();
        foreach (var pair in IsolatedFixtureExecutor.ChildEnvironment()) start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            ReaderWorkerResponse? previous = null;
            for (var sequence = 1; sequence <= 4; sequence++)
            {
                var request = ReaderWorkerContractTests.Request(sequence, sequence == 1 ? ReaderWorkerOperation.Open
                    : sequence == 4 ? ReaderWorkerOperation.Close : ReaderWorkerOperation.Observe);
                await SendAsync(process.StandardInput.BaseStream, request, timeout.Token);
                var response = await ReaderWorkerWire.ReadResponseAsync(process.StandardOutput.BaseStream, request, previous?.DocumentId, timeout.Token);
                if (sequence != 4)
                {
                    Assert.Contains(response.Snapshot!.Nodes, n => n.Role == "heading" && n.Name == "Screen reader fixture");
                    Assert.DoesNotContain("fixture-secret-do-not-log", JsonSerializer.Serialize(response, ReaderWorkerWire.Json));
                }
                else
                {
                    Assert.True(response.Shutdown!.ProcessExitConfirmed);
                    Assert.True(response.Shutdown.TransportCallSettled);
                    Assert.Equal(ReaderWorkerState.Closed, response.State);
                }
                previous = response;
            }
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode); Assert.Equal("", await error);
            Assert.Null(await ReaderWorkerWire.ReadAsync(process.StandardOutput.BaseStream, 262144, timeout.Token));
        }
        finally
        {
            process.StandardInput.Close();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [Fact]
    public async Task InvalidFirstRequestIsRejectedBeforeNativeLaunch()
    {
        using var input = new MemoryStream(); using var output = new MemoryStream(); using var errors = new StringWriter();
        await SendAsync(input, ReaderWorkerContractTests.Request() with { Operation = ReaderWorkerOperation.Observe }); input.Position = 0;
        var launched = false;
        Assert.Equal(3, await FixtureReaderWorker.RunAsync(input, output, errors, open: _ =>
        {
            launched = true; throw new InvalidOperationException("private-engine-canary");
        }));
        Assert.False(launched); Assert.Empty(output.ToArray());
        Assert.Equal("READER_WORKER_UNCONFIRMED", errors.ToString().Trim());
    }

    [Theory]
    [InlineData("foreign-session")]
    [InlineData("duplicate-sequence")]
    [InlineData("skipped-sequence")]
    [InlineData("reopen")]
    [InlineData("extended-request")]
    [InlineData("truncated-request")]
    public async Task InvalidFollowingRequestDisposesActualBrowserWithoutFalseClose(string scenario)
    {
        await using var worker = new Harness(); await worker.ExchangeAsync(ReaderWorkerContractTests.Request());
        var request = ReaderWorkerContractTests.Request(2, ReaderWorkerOperation.Observe);
        request = scenario switch
        {
            "foreign-session" => request with { SessionId = Guid.NewGuid() },
            "duplicate-sequence" => request with { Sequence = 1 },
            "skipped-sequence" => request with { Sequence = 3 },
            "reopen" => request with { Operation = ReaderWorkerOperation.Open },
            _ => request
        };
        if (scenario == "truncated-request") await worker.Input.WriteAsync(new byte[] { 0, 0 });
        else if (scenario == "extended-request")
        {
            var json = JsonSerializer.Serialize(request, ReaderWorkerWire.Json);
            await ReaderWorkerWire.WriteAsync(worker.Input, Encoding.UTF8.GetBytes(json[..^1] + ",\"Token\":\"private-token-canary\"}"), 1024);
        }
        else await SendAsync(worker.Input, request);
        await worker.Input.DisposeAsync();
        Assert.Equal(3, await worker.Run);
        Assert.Null(await ReaderWorkerWire.ReadAsync(worker.Output, 262144));
        Assert.Equal("READER_WORKER_UNCONFIRMED", worker.Diagnostics.ToString().Trim());
        AssertActualShutdown(await worker.Browser.Task);
    }

    [Theory]
    [InlineData("eof")]
    [InlineData("canceled")]
    [InlineData("expired")]
    public async Task DisconnectedCanceledOrExpiredReaderJoinsActualNativeCleanup(string scenario)
    {
        var clock = new FakeTimeProvider();
        await using var worker = new Harness(clock);
        await worker.ExchangeAsync(ReaderWorkerContractTests.Request());
        if (scenario == "eof") await worker.Input.DisposeAsync();
        else if (scenario == "canceled") worker.Stop.Cancel();
        else
        {
            clock.Advance(TimeSpan.FromMinutes(2) - TimeSpan.FromMilliseconds(1));
            Assert.False(worker.Run.IsCompleted);
            var observation = await worker.ExchangeAsync(ReaderWorkerContractTests.Request(2, ReaderWorkerOperation.Observe));
            Assert.Equal(ReaderWorkerState.Observed, observation.State);
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }
        Assert.Equal(3, await worker.Run);
        Assert.Null(await ReaderWorkerWire.ReadAsync(worker.Output, 262144));
        AssertActualShutdown(await worker.Browser.Task);
        Assert.Equal("READER_WORKER_UNCONFIRMED", worker.Diagnostics.ToString().Trim());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureBudgetAllowsExactlyOneHundredThenOnlyConfirmedClose(bool extraCapture)
    {
        await using var worker = new Harness();
        string? document = null;
        for (var sequence = 1; sequence <= 100; sequence++)
        {
            var request = ReaderWorkerContractTests.Request(sequence,
                sequence == 1 ? ReaderWorkerOperation.Open : ReaderWorkerOperation.Observe);
            var response = await worker.ExchangeAsync(request);
            response.Validate(request, document); document = response.DocumentId;
        }
        var last = ReaderWorkerContractTests.Request(101, extraCapture ? ReaderWorkerOperation.Observe : ReaderWorkerOperation.Close);
        await SendAsync(worker.Input, last);
        if (extraCapture)
        {
            Assert.Equal(3, await worker.Run);
            Assert.Null(await ReaderWorkerWire.ReadAsync(worker.Output, 262144));
        }
        else
        {
            var response = await ReceiveAsync(worker.Output, worker.Stop.Token);
            response.Validate(last, document);
            Assert.Equal(ReaderWorkerState.Closed, response.State);
            Assert.Equal(0, await worker.Run);
        }
        AssertActualShutdown(await worker.Browser.Task);
    }

    [Fact]
    public async Task FailedOutputJoinsActualBrowserShutdownAndRedactsEngineMessages()
    {
        using var input = new MemoryStream(); using var output = new BrokenOutput(); using var errors = new StringWriter();
        await SendAsync(input, ReaderWorkerContractTests.Request()); input.Position = 0;
        ChromiumFixtureBrowser? actual = null;
        Assert.Equal(3, await FixtureReaderWorker.RunAsync(input, output, errors,
            open: async token => actual = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Foundation, cancellationToken: token)));
        Assert.NotNull(actual); AssertActualShutdown(actual);
        Assert.Equal("READER_WORKER_UNCONFIRMED", errors.ToString().Trim());
    }

    private static void AssertActualShutdown(ChromiumFixtureBrowser browser)
    {
        Assert.Equal(BrowserShutdownPhase.Closed, browser.ShutdownStatus.Phase);
        Assert.True(browser.ShutdownStatus.ProcessExitConfirmed);
        Assert.True(browser.ShutdownStatus.TransportCallSettled);
        Assert.Equal(0, browser.ShutdownStatus.ActiveCalls);
        Assert.Null(browser.ShutdownStatus.ErrorCode);
    }

    private static Task SendAsync(Stream input, ReaderWorkerRequest request, CancellationToken token = default) =>
        ReaderWorkerWire.WriteAsync(input, JsonSerializer.SerializeToUtf8Bytes(request, ReaderWorkerWire.Json), 1024, token);
    private static async Task<ReaderWorkerResponse> ReceiveAsync(Stream output, CancellationToken token)
    {
        var bytes = await ReaderWorkerWire.ReadAsync(output, 262144, token);
        Assert.NotNull(bytes);
        var response = JsonSerializer.Deserialize<ReaderWorkerResponse>(bytes, ReaderWorkerWire.Json);
        Assert.NotNull(response);
        return response;
    }

    private sealed class Harness : IAsyncDisposable
    {
        public CancellationTokenSource Stop { get; } = new(TimeSpan.FromSeconds(60));
        public TaskCompletionSource<ChromiumFixtureBrowser> Browser { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public StringWriter Diagnostics { get; } = new();
        public Stream Input { get; }
        public Stream Output { get; }
        public Task<int> Run { get; }
        public Harness(TimeProvider? time = null)
        {
            var input = new Pipe(); var output = new Pipe();
            Input = input.Writer.AsStream(); Output = output.Reader.AsStream();
            Run = ExecuteAsync(input.Reader.AsStream(), output.Writer.AsStream(), time);
        }
        private async Task<int> ExecuteAsync(Stream input, Stream output, TimeProvider? time)
        {
            await using (input)
            await using (output)
                return await FixtureReaderWorker.RunAsync(input, output, Diagnostics, Stop.Token, time, async token =>
                {
                    var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Foundation, cancellationToken: token);
                    Browser.SetResult(browser); return browser;
                });
        }
        public async Task<ReaderWorkerResponse> ExchangeAsync(ReaderWorkerRequest request)
        {
            await SendAsync(Input, request, Stop.Token);
            return await ReceiveAsync(Output, Stop.Token);
        }
        public async ValueTask DisposeAsync()
        {
            Stop.Cancel(); await Input.DisposeAsync();
            await Run; await Output.DisposeAsync(); Stop.Dispose(); Diagnostics.Dispose();
        }
    }

    private sealed class BrokenOutput : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("private-output-canary"));
    }
}
