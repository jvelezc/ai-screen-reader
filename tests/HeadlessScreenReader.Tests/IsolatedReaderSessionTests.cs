using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Worker;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class IsolatedReaderSessionTests
{
    private static ProcessStartInfo Start(string file, params string[] args)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment.Clear();
        foreach (var pair in IsolatedFixtureExecutor.ChildEnvironment()) start.Environment[pair.Key] = pair.Value;
        return start;
    }

    [Fact]
    public async Task ActualCliSessionReusesDocumentAndConfirmsExitBeforeIndependentRemoval()
    {
        var assembly = typeof(IsolatedReaderSessionTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "ReaderWorkerAssembly").Value!;
        IsolatedReaderSession? reader = null;
        var removed = 0;
        reader = new(Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", assembly, "--reader-fixture"),
            Guid.NewGuid(), () =>
            {
                Assert.True(reader!.NativeShutdownConfirmed);
                Assert.False(reader.ContainerAbsenceConfirmed);
                removed++;
                return Task.CompletedTask;
            });
        await using (reader)
        {
            await reader.OpenAsync(CancellationToken.None);
            var first = reader.InitialSnapshot;
            var second = await reader.ObserveAsync();
            var third = await reader.ObserveAsync();
            Assert.Equal(first.DocumentId, second.DocumentId);
            Assert.Equal(first.DocumentId, third.DocumentId);
            Assert.Contains(third.Nodes, n => n.Role == "heading" && n.Name == "Screen reader fixture");
            Assert.Equal(0, removed);
            await reader.DisposeAsync();
            Assert.True(reader.ContainerAbsenceConfirmed);
            Assert.Equal(1, removed);
            Assert.Equal("READER_CONTAINER_CLOSED", (await Assert.ThrowsAsync<BrowserOperationException>(() => reader.ObserveAsync())).Code);
        }
        Assert.Equal(1, removed);
    }

    private static async Task<string> FrameAsync(ReaderWorkerResponse response)
    {
        using var bytes = new MemoryStream();
        await ReaderWorkerWire.WriteResponseAsync(bytes, response);
        return Convert.ToBase64String(bytes.ToArray());
    }

    // A protocol-adversary process is distinct from real Chromium/container acceptance.
    private static ProcessStartInfo Adversary(string body) => Start("pwsh", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
        "$ErrorActionPreference='Stop'; $i=[Console]::OpenStandardInput(); $o=[Console]::OpenStandardOutput(); " +
        "function Read-Request { $b=[byte[]]::new(4); $i.ReadExactly($b,0,4); [Array]::Reverse($b); " +
        "$n=[BitConverter]::ToInt32($b,0); $p=[byte[]]::new($n); $i.ReadExactly($p,0,$n) }; " +
        "function Send-Frame([string]$s) { $b=[Convert]::FromBase64String($s); $o.Write($b,0,$b.Length); $o.Flush() }; " + body);

    [Theory]
    [InlineData("eof")]
    [InlineData("oversized")]
    [InlineData("diagnostics-overflow")]
    public async Task InvalidInitialOutputIsSanitizedAndStillJoinsRemoval(string scenario)
    {
        var body = scenario switch
        {
            "eof" => "Read-Request; exit 7",
            "oversized" => "Read-Request; $o.Write([byte[]]@(0,4,0,1),0,4); $o.Flush(); Read-Request",
            _ => "Read-Request; [Console]::Error.Write(('private-diagnostic-canary' * 1024)); Read-Request"
        };
        var removals = 0;
        await using var reader = new IsolatedReaderSession(Adversary(body), ReaderWorkerContractTests.Request().SessionId,
            () => { removals++; return Task.CompletedTask; });
        var fault = await Assert.ThrowsAsync<BrowserOperationException>(() => reader.OpenAsync(CancellationToken.None));
        Assert.Equal("READER_CONTAINER_OPEN_UNCONFIRMED", fault.Code);
        Assert.DoesNotContain("private-diagnostic-canary", fault.ToString());
        Assert.True(reader.ContainerAbsenceConfirmed);
        Assert.False(reader.NativeShutdownConfirmed);
        Assert.Equal(1, removals);
    }

    [Theory]
    [InlineData("other-document")]
    [InlineData("other-session")]
    [InlineData("old-sequence")]
    public async Task ChangedResponseBindingRetiresTheConnectionWithoutObservationSuccess(string scenario)
    {
        var request = ReaderWorkerContractTests.Request();
        var open = ReaderWorkerContractTests.OpenResponse(request);
        var observed = open with { Sequence = 2, State = ReaderWorkerState.Observed };
        observed = scenario switch
        {
            "other-document" => observed with { DocumentId = "other-document", Snapshot = observed.Snapshot! with { DocumentId = "other-document" } },
            "other-session" => observed with { SessionId = Guid.NewGuid() },
            _ => observed with { Sequence = 1 }
        };
        var body = $"Read-Request; Send-Frame '{await FrameAsync(open)}'; Read-Request; Send-Frame '{await FrameAsync(observed)}'; Read-Request";
        var removed = 0;
        await using var reader = new IsolatedReaderSession(Adversary(body), request.SessionId,
            () => { removed++; return Task.CompletedTask; });
        await reader.OpenAsync(CancellationToken.None);
        Assert.Equal("READER_CONTAINER_OBSERVATION_UNCONFIRMED",
            (await Assert.ThrowsAsync<BrowserOperationException>(() => reader.ObserveAsync())).Code);
        Assert.True(reader.ContainerAbsenceConfirmed);
        Assert.False(reader.NativeShutdownConfirmed);
        Assert.Equal(1, removed);
    }

    private static ReaderWorkerResponse ClosedResponse() => ReaderWorkerContractTests.OpenResponse(ReaderWorkerContractTests.Request()) with
    {
        Sequence = 2,
        State = ReaderWorkerState.Closed,
        Snapshot = null,
        Shutdown = FixtureWorkerTests.Receipt(FixtureWorkerTests.Job()).Shutdown
    };

    [Theory]
    [InlineData("extra-frame")]
    [InlineData("nonzero-exit")]
    [InlineData("diagnostics")]
    [InlineData("missing-ack")]
    public async Task CloseAcknowledgementRequiresCleanEofExitAndDiagnostics(string scenario)
    {
        var open = await FrameAsync(ReaderWorkerContractTests.OpenResponse(ReaderWorkerContractTests.Request()));
        var close = await FrameAsync(ClosedResponse());
        var tail = scenario switch
        {
            "extra-frame" => $"Send-Frame '{close}'; Send-Frame '{open}'; exit 0",
            "nonzero-exit" => $"Send-Frame '{close}'; exit 7",
            "diagnostics" => $"Send-Frame '{close}'; [Console]::Error.Write('private-diagnostic-canary'); exit 0",
            _ => "exit 0"
        };
        await using var reader = new IsolatedReaderSession(Adversary($"Read-Request; Send-Frame '{open}'; Read-Request; {tail}"),
            ReaderWorkerContractTests.Request().SessionId, () => Task.CompletedTask);
        await reader.OpenAsync(CancellationToken.None);
        Assert.Equal("READER_CONTAINER_CLOSE_UNCONFIRMED",
            (await Assert.ThrowsAsync<BrowserOperationException>(() => reader.DisposeAsync().AsTask())).Code);
        Assert.True(reader.ContainerAbsenceConfirmed);
        Assert.False(reader.NativeShutdownConfirmed);
    }

    [Fact]
    public async Task FailedRemovalRetainsOwnershipAndRetryDoesNotSendAnotherClose()
    {
        var open = await FrameAsync(ReaderWorkerContractTests.OpenResponse(ReaderWorkerContractTests.Request()));
        var close = await FrameAsync(ClosedResponse());
        var calls = 0;
        await using var reader = new IsolatedReaderSession(Adversary($"Read-Request; Send-Frame '{open}'; Read-Request; Send-Frame '{close}'; exit 0"),
            ReaderWorkerContractTests.Request().SessionId, () => ++calls == 1 ? Task.FromException(new IOException("private-removal-canary")) : Task.CompletedTask);
        await reader.OpenAsync(CancellationToken.None);
        var fault = await Assert.ThrowsAsync<BrowserOperationException>(() => reader.DisposeAsync().AsTask());
        Assert.Equal("READER_CONTAINER_CLEANUP_UNCONFIRMED", fault.Code);
        Assert.DoesNotContain("private-removal-canary", fault.ToString());
        Assert.True(reader.NativeShutdownConfirmed);
        Assert.False(reader.ContainerAbsenceConfirmed);
        await reader.DisposeAsync();
        Assert.True(reader.ContainerAbsenceConfirmed);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FailedOpenWithUnconfirmedRemovalRetainsOwnershipUntilExplicitRetry()
    {
        var calls = 0;
        await using var reader = new IsolatedReaderSession(Adversary("Read-Request; exit 7"),
            ReaderWorkerContractTests.Request().SessionId, () => ++calls == 1 ? Task.FromException(new IOException("private-removal-canary")) : Task.CompletedTask);
        Assert.Equal("READER_CONTAINER_CLEANUP_UNCONFIRMED",
            (await Assert.ThrowsAsync<BrowserOperationException>(() => reader.OpenAsync(CancellationToken.None))).Code);
        Assert.False(reader.ContainerAbsenceConfirmed);
        Assert.False(reader.NativeShutdownConfirmed);
        await reader.DisposeAsync();
        Assert.True(reader.ContainerAbsenceConfirmed);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ParentExpiryAllowsObservationImmediatelyBeforeTwoMinutesAndRetiresAtTheBoundary()
    {
        var request = ReaderWorkerContractTests.Request();
        var opened = ReaderWorkerContractTests.OpenResponse(request);
        var open = await FrameAsync(opened);
        var observed = await FrameAsync(opened with { Sequence = 2, State = ReaderWorkerState.Observed });
        var time = new FakeTimeProvider();
        await using var reader = new IsolatedReaderSession(Adversary($"Read-Request; Send-Frame '{open}'; Read-Request; Send-Frame '{observed}'; Read-Request"),
            request.SessionId, () => Task.CompletedTask, time);
        await reader.OpenAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(119999));
        Assert.False(reader.Expired);
        Assert.Equal("owned-document", (await reader.ObserveAsync()).DocumentId);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(reader.Expired);
        Assert.Equal("READER_CONTAINER_OBSERVATION_UNCONFIRMED",
            (await Assert.ThrowsAsync<BrowserOperationException>(() => reader.ObserveAsync())).Code);
        Assert.True(reader.ContainerAbsenceConfirmed);
        Assert.False(reader.NativeShutdownConfirmed);
    }

    [Fact]
    public async Task ParentCaptureBudgetAdmitsExactlyOneHundredAndThenOnlyClose()
    {
        var request = ReaderWorkerContractTests.Request();
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(ReaderWorkerContractTests.OpenResponse(request), ReaderWorkerWire.Json));
        var close = await FrameAsync(ClosedResponse() with { Sequence = 101 });
        var body = $"$r=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{payload}')) | ConvertFrom-Json; " +
            "for($s=1;$s -le 100;$s++){ Read-Request; $r.Sequence=$s; if($s -gt 1){$r.State='Observed'}; " +
            "$p=[Text.Encoding]::UTF8.GetBytes(($r|ConvertTo-Json -Depth 30 -Compress)); $h=[BitConverter]::GetBytes([int]$p.Length); " +
            "[Array]::Reverse($h); $o.Write($h,0,4); $o.Write($p,0,$p.Length); $o.Flush() }; " +
            $"Read-Request; Send-Frame '{close}'; exit 0";
        await using var reader = new IsolatedReaderSession(Adversary(body), request.SessionId, () => Task.CompletedTask);
        await reader.OpenAsync(CancellationToken.None);
        for (var capture = 2; capture <= 100; capture++) Assert.Equal("owned-document", (await reader.ObserveAsync()).DocumentId);
        Assert.Equal("READER_CONTAINER_BUDGET_EXHAUSTED",
            (await Assert.ThrowsAsync<BrowserOperationException>(() => reader.ObserveAsync())).Code);
        Assert.False(reader.ContainerAbsenceConfirmed);
        await reader.DisposeAsync();
        Assert.True(reader.NativeShutdownConfirmed);
        Assert.True(reader.ContainerAbsenceConfirmed);
    }

    [Fact]
    public async Task InFlightCancellationJoinsRemovalInsteadOfReturningAReusableLiveConnection()
    {
        var open = await FrameAsync(ReaderWorkerContractTests.OpenResponse(ReaderWorkerContractTests.Request()));
        var signal = Path.Combine(Path.GetTempPath(), "hsr-reader-cancel-" + Guid.NewGuid().ToString("N"));
        await using var reader = new IsolatedReaderSession(Adversary($"Read-Request; Send-Frame '{open}'; Read-Request; [IO.File]::WriteAllText('{signal.Replace("'", "''")}', 'observing'); Read-Request"),
            ReaderWorkerContractTests.Request().SessionId, () => Task.CompletedTask);
        try
        {
            await reader.OpenAsync(CancellationToken.None);
            using var cancel = new CancellationTokenSource();
            var observing = reader.ObserveAsync(cancel.Token);
            using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(signal)) await Task.Delay(10, bound.Token);
            cancel.Cancel();
            Assert.Equal("READER_CONTAINER_OBSERVATION_UNCONFIRMED", (await Assert.ThrowsAsync<BrowserOperationException>(() => observing)).Code);
            Assert.True(reader.ContainerAbsenceConfirmed);
            Assert.False(reader.NativeShutdownConfirmed);
        }
        finally { File.Delete(signal); }
    }
}
