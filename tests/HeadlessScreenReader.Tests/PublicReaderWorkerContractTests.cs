using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class PublicReaderWorkerContractTests
{
    internal static PublicReaderSource Source() => new("hsr-approved-public-readonly-v1", "https://screenreader.privensic.com/ci/v1/source", "screenreader.privensic.com");
    internal static PublicReaderWorkerRequest Request(int sequence = 1, ReaderWorkerOperation operation = ReaderWorkerOperation.Open) =>
        new("hsr-public-reader-worker-v1", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), sequence, operation);
    [Theory]
    [InlineData(2048, true)]
    [InlineData(2049, false)]
    public void SourcePinsExactTargetLengthBoundary(int length, bool accepted)
    {
        var source = Source(); source = source with { TargetUrl = source.TargetUrl.PadRight(length, 'a') };
        var failure = Record.Exception(source.Validate);
        if (accepted) Assert.Null(failure);
        else Assert.Equal("PUBLIC_READER_SOURCE_INVALID", Assert.IsType<BrowserOperationException>(failure).Code);
    }
    internal static PublicReaderWorkerResponse Response(PublicReaderWorkerRequest? request = null)
    {
        request ??= Request();
        return new(request.Schema, request.SessionId, request.Sequence, ReaderWorkerState.Open, Source(), "public-document",
            new("public-document", [new("heading", "heading", "Untrusted public heading")], [])
            { CaptureEpoch = 1, DocumentUrl = Source().TargetUrl }, null, 0, false);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("http")]
    [InlineData("foreign-host")]
    [InlineData("userinfo")]
    [InlineData("ip")]
    [InlineData("fragment")]
    [InlineData("port")]
    [InlineData("whitespace")]
    [InlineData("too-long")]
    [InlineData("wildcard")]
    [InlineData("multiple-hosts")]
    [InlineData("empty-host")]
    public void SourceRejectsUnapprovedAndCredentialedTargets(string scenario)
    {
        var source = Source();
        source = scenario switch
        {
            "profile" => source with { Profile = "fixture" },
            "http" => source with { TargetUrl = "http://screenreader.privensic.com/ci/v1/source" },
            "foreign-host" => source with { TargetUrl = "https://other.example/source" },
            "userinfo" => source with { TargetUrl = "https://credential-canary@screenreader.privensic.com/" },
            "ip" => source with { TargetUrl = "https://89.117.17.81/", AllowedHost = "89.117.17.81" },
            "fragment" => source with { TargetUrl = source.TargetUrl + "#fragment" },
            "port" => source with { TargetUrl = "https://screenreader.privensic.com:444/" },
            "whitespace" => source with { TargetUrl = source.TargetUrl + " " },
            "too-long" => source with { TargetUrl = source.TargetUrl + new string('a', 2048) },
            "wildcard" => source with { AllowedHost = "*.privensic.com" },
            "multiple-hosts" => source with { AllowedHost = "screenreader.privensic.com,other.example" },
            _ => source with { AllowedHost = "" }
        };
        Assert.Equal("PUBLIC_READER_SOURCE_INVALID", Assert.Throws<BrowserOperationException>(source.Validate).Code);
    }

    [Theory]
    [InlineData("https://screenreader.privensic.com/ci/v1/source", true)]
    [InlineData("https://screenreader.privensic.com/ci/v1/destination", true)]
    [InlineData("https://screenreader.privensic.com/?q=read", true)]
    [InlineData("http://screenreader.privensic.com/", false)]
    [InlineData("https://sub.screenreader.privensic.com/", false)]
    [InlineData("https://screenreader.privensic.com.evil.example/", false)]
    [InlineData("https://127.0.0.1/", false)]
    [InlineData(null, false)]
    public void SourceAllowsOnlyItsDeclaredHttpsHost(string? url, bool allowed)
    {
        Source().Validate(); Assert.Equal(allowed, Source().AllowsDocument(url));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("identity")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("over")]
    [InlineData("observe-after-budget")]
    [InlineData("unknown")]
    [InlineData("observe-first")]
    [InlineData("close-first")]
    [InlineData("open-later")]
    public void RequestRejectsWrongBindingOrOperationBudget(string scenario)
    {
        var request = Request();
        request = scenario switch
        {
            "schema" => request with { Schema = "hsr-reader-worker-v1" },
            "identity" => request with { SessionId = Guid.Empty },
            "zero" => request with { Sequence = 0, Operation = ReaderWorkerOperation.Observe },
            "negative" => request with { Sequence = -1, Operation = ReaderWorkerOperation.Observe },
            "over" => request with { Sequence = 102, Operation = ReaderWorkerOperation.Close },
            "observe-after-budget" => request with { Sequence = 101, Operation = ReaderWorkerOperation.Observe },
            "unknown" => request with { Sequence = 2, Operation = (ReaderWorkerOperation)99 },
            "observe-first" => request with { Operation = ReaderWorkerOperation.Observe },
            "close-first" => request with { Operation = ReaderWorkerOperation.Close },
            _ => request with { Sequence = 2 }
        };
        Assert.Equal("PUBLIC_READER_WORKER_REQUEST_INVALID", Assert.Throws<BrowserOperationException>(request.Validate).Code);
    }

    [Theory]
    [InlineData(1, ReaderWorkerOperation.Open)]
    [InlineData(2, ReaderWorkerOperation.Observe)]
    [InlineData(100, ReaderWorkerOperation.Observe)]
    [InlineData(2, ReaderWorkerOperation.Close)]
    [InlineData(101, ReaderWorkerOperation.Close)]
    public void RequestAcceptsExactSupportedSequenceBounds(int sequence, ReaderWorkerOperation operation) => Request(sequence, operation).Validate();

    [Theory]
    [InlineData("schema")]
    [InlineData("session")]
    [InlineData("sequence")]
    [InlineData("state")]
    [InlineData("source")]
    [InlineData("empty-document")]
    [InlineData("different-document")]
    [InlineData("snapshot-document")]
    [InlineData("epoch")]
    [InlineData("missing-snapshot")]
    [InlineData("empty-nodes")]
    [InlineData("too-many-nodes")]
    [InlineData("shutdown")]
    [InlineData("input")]
    [InlineData("negative-input")]
    [InlineData("goal")]
    [InlineData("foreign-url")]
    [InlineData("http-url")]
    [InlineData("missing-url")]
    public void ResponseRejectsChangedSourceStateAndInventedClaims(string scenario)
    {
        var response = Response();
        response = scenario switch
        {
            "schema" => response with { Schema = "hsr-reader-worker-v1" },
            "session" => response with { SessionId = Guid.NewGuid() },
            "sequence" => response with { Sequence = 2 },
            "state" => response with { State = ReaderWorkerState.Observed },
            "source" => response with { Source = Source() with { TargetUrl = "https://screenreader.privensic.com/different" } },
            "empty-document" => response with { DocumentId = "", Snapshot = response.Snapshot! with { DocumentId = "" } },
            "different-document" => response with { DocumentId = "different", Snapshot = response.Snapshot! with { DocumentId = "different" } },
            "snapshot-document" => response with { Snapshot = response.Snapshot! with { DocumentId = "different" } },
            "epoch" => response with { Snapshot = response.Snapshot! with { CaptureEpoch = 2 } },
            "missing-snapshot" => response with { Snapshot = null },
            "empty-nodes" => response with { Snapshot = response.Snapshot! with { Nodes = [] } },
            "too-many-nodes" => response with { Snapshot = response.Snapshot! with { Nodes = Enumerable.Repeat(response.Snapshot.Nodes[0], 2049).ToImmutableArray() } },
            "shutdown" => response with { Shutdown = FixtureWorkerTests.Receipt(FixtureWorkerTests.Job()).Shutdown },
            "input" => response with { DispatchedInputs = 1 },
            "negative-input" => response with { DispatchedInputs = -1 },
            "goal" => response with { GoalVerified = true },
            "foreign-url" => response with { Snapshot = response.Snapshot! with { DocumentUrl = "https://other.example/" } },
            "http-url" => response with { Snapshot = response.Snapshot! with { DocumentUrl = "http://screenreader.privensic.com/" } },
            _ => response with { Snapshot = response.Snapshot! with { DocumentUrl = null } }
        };
        Assert.Equal("PUBLIC_READER_WORKER_RESPONSE_INVALID", Assert.Throws<BrowserOperationException>(() => response.Validate(Request(), Source(), scenario == "empty-document" ? null : "public-document")).Code);
    }

    [Fact]
    public void ObservedResponseAcceptsNodeBudgetAndRetainsUnverifiedOutcome()
    {
        var request = Request(2, ReaderWorkerOperation.Observe);
        var response = Response(request) with { State = ReaderWorkerState.Observed };
        response = response with { Snapshot = response.Snapshot! with { Nodes = Enumerable.Repeat(response.Snapshot.Nodes[0], 2048).ToImmutableArray() } };
        response.Validate(request, Source(), "public-document");
        Assert.Equal("https://screenreader.privensic.com/ci/v1/source", response.Snapshot.DocumentUrl);
        Assert.Equal(0, response.DispatchedInputs); Assert.False(response.GoalVerified);
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("missing")]
    [InlineData("phase")]
    [InlineData("active")]
    [InlineData("error")]
    [InlineData("context")]
    [InlineData("browser")]
    [InlineData("collector-call")]
    [InlineData("collector-stop")]
    [InlineData("transport")]
    [InlineData("transport-call")]
    [InlineData("process")]
    [InlineData("process-call")]
    [InlineData("exit-bound")]
    [InlineData("native-bound")]
    [InlineData("negative-time")]
    [InlineData("missing-time")]
    [InlineData("deadline")]
    [InlineData("rejected")]
    [InlineData("unsettled-stop")]
    public void CloseRequiresJoinedBoundedNativeShutdown(string scenario)
    {
        var request = Request(2, ReaderWorkerOperation.Close);
        var shutdown = FixtureWorkerTests.Receipt(FixtureWorkerTests.Job()).Shutdown;
        shutdown = scenario switch
        {
            "phase" => shutdown with { Phase = BrowserShutdownPhase.ClosingBrowser },
            "active" => shutdown with { ActiveCalls = 1 },
            "error" => shutdown with { ErrorCode = "CLOSE_UNCONFIRMED" },
            "context" => shutdown with { ContextCallSettled = false },
            "browser" => shutdown with { BrowserCallSettled = false },
            "collector-call" => shutdown with { CollectorCallSettled = false },
            "collector-stop" => shutdown with { CollectorStopAcknowledged = false },
            "transport" => shutdown with { TransportDisposed = false },
            "transport-call" => shutdown with { TransportCallSettled = false },
            "process" => shutdown with { ProcessExitConfirmed = false },
            "process-call" => shutdown with { ProcessCallSettled = false },
            "exit-bound" => shutdown with { ProcessExitConfirmationMilliseconds = 4000 },
            "native-bound" => shutdown with { NativeElapsedMilliseconds = 5000 },
            "negative-time" => shutdown with { NativeElapsedMilliseconds = -1 },
            "missing-time" => shutdown with { ProcessExitConfirmationMilliseconds = null },
            "deadline" => shutdown with { BrowserDeadlineExceeded = true },
            "rejected" => shutdown with { BrowserCallRejected = true },
            "unsettled-stop" => shutdown with { OwnedProcessStopRequested = true, OwnedProcessStopCallSettled = false },
            _ => shutdown
        };
        var response = Response(request) with { State = ReaderWorkerState.Closed, Snapshot = scenario == "snapshot" ? Response().Snapshot : null, Shutdown = scenario == "missing" ? null : shutdown };
        Assert.Equal("PUBLIC_READER_WORKER_CLOSE_UNCONFIRMED", Assert.Throws<BrowserOperationException>(() => response.Validate(request, Source(), "public-document")).Code);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3999, 4999)]
    public void CloseAcceptsSettledNativeTimesAtSupportedBounds(int processExitMilliseconds, int nativeMilliseconds)
    {
        var request = Request(101, ReaderWorkerOperation.Close);
        var shutdown = FixtureWorkerTests.Receipt(FixtureWorkerTests.Job()).Shutdown with
        { ProcessExitConfirmationMilliseconds = processExitMilliseconds, NativeElapsedMilliseconds = nativeMilliseconds, OwnedProcessStopRequested = true, OwnedProcessStopCallSettled = true };
        var response = Response(request) with { State = ReaderWorkerState.Closed, Snapshot = null, Shutdown = shutdown };
        response.Validate(request, Source(), "public-document");
        Assert.Null(response.Snapshot); Assert.Equal(BrowserShutdownPhase.Closed, response.Shutdown.Phase);
    }
}
