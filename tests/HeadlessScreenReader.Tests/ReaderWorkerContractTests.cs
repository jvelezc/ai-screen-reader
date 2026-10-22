using System.Collections.Immutable;
using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Tests;

public sealed class ReaderWorkerContractTests
{
    internal static ReaderWorkerRequest Request(int sequence = 1, ReaderWorkerOperation operation = ReaderWorkerOperation.Open) =>
        new(ReaderWorkerRequest.CurrentSchema, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), sequence, operation);
    internal static ReaderWorkerResponse OpenResponse(ReaderWorkerRequest request) => new(request.Schema, request.SessionId,
        request.Sequence, ReaderWorkerState.Open, ReaderWorkerResponse.ContentSha256, "owned-document",
        new("owned-document", [new("heading", "heading", "Accessibility fixture")], []) { CaptureEpoch = 1 }, null, 0, false);

    [Theory]
    [InlineData("schema")]
    [InlineData("empty-session")]
    [InlineData("zero-sequence")]
    [InlineData("negative-sequence")]
    [InlineData("over-budget")]
    [InlineData("observe-after-budget")]
    [InlineData("unknown-operation")]
    [InlineData("observe-first")]
    [InlineData("close-first")]
    [InlineData("open-later")]
    public void RequestRejectsInvalidIdentitySequenceAndOperation(string scenario)
    {
        var request = Request();
        request = scenario switch
        {
            "schema" => request with { Schema = "v0" },
            "empty-session" => request with { SessionId = Guid.Empty },
            "zero-sequence" => request with { Sequence = 0 },
            "negative-sequence" => request with { Sequence = -1 },
            "over-budget" => request with { Sequence = 102, Operation = ReaderWorkerOperation.Close },
            "observe-after-budget" => request with { Sequence = 101, Operation = ReaderWorkerOperation.Observe },
            "unknown-operation" => request with { Operation = (ReaderWorkerOperation)99 },
            "observe-first" => request with { Operation = ReaderWorkerOperation.Observe },
            "close-first" => request with { Operation = ReaderWorkerOperation.Close },
            _ => request with { Sequence = 2 }
        };
        Assert.Equal("READER_WORKER_REQUEST_INVALID", Assert.Throws<BrowserOperationException>(request.Validate).Code);
    }

    [Theory]
    [InlineData(1, ReaderWorkerOperation.Open)]
    [InlineData(2, ReaderWorkerOperation.Observe)]
    [InlineData(100, ReaderWorkerOperation.Observe)]
    [InlineData(101, ReaderWorkerOperation.Close)]
    public void RequestAcceptsSupportedOperationsAtSequenceBounds(int sequence, ReaderWorkerOperation operation) =>
        Request(sequence, operation).Validate();

    [Theory]
    [InlineData("schema")]
    [InlineData("session")]
    [InlineData("sequence")]
    [InlineData("state")]
    [InlineData("fixture")]
    [InlineData("empty-document")]
    [InlineData("changed-document")]
    [InlineData("snapshot-document")]
    [InlineData("epoch")]
    [InlineData("missing-snapshot")]
    [InlineData("empty-nodes")]
    [InlineData("too-many-nodes")]
    [InlineData("premature-shutdown")]
    [InlineData("input-claim")]
    [InlineData("goal-claim")]
    public void ResponseRejectsChangedBindingUnsupportedSemanticsAndInventedClaims(string scenario)
    {
        var request = Request(); var response = OpenResponse(request); var snapshot = response.Snapshot!;
        response = scenario switch
        {
            "schema" => response with { Schema = "other" },
            "session" => response with { SessionId = Guid.NewGuid() },
            "sequence" => response with { Sequence = 2 },
            "state" => response with { State = ReaderWorkerState.Observed },
            "fixture" => response with { FixtureSha256 = new string('a', 64) },
            "empty-document" => response with { DocumentId = " " },
            "changed-document" => response with { DocumentId = "other", Snapshot = snapshot with { DocumentId = "other" } },
            "snapshot-document" => response with { Snapshot = snapshot with { DocumentId = "other" } },
            "epoch" => response with { Snapshot = snapshot with { CaptureEpoch = 2 } },
            "missing-snapshot" => response with { Snapshot = null },
            "empty-nodes" => response with { Snapshot = snapshot with { Nodes = [] } },
            "too-many-nodes" => response with { Snapshot = snapshot with { Nodes = Enumerable.Repeat(snapshot.Nodes[0], 2049).ToImmutableArray() } },
            "premature-shutdown" => response with { Shutdown = FixtureWorkerTests.Receipt(FixtureWorkerTests.Job()).Shutdown },
            "input-claim" => response with { DispatchedInputs = 1 },
            _ => response with { GoalVerified = true }
        };
        Assert.Equal("READER_WORKER_RESPONSE_INVALID", Assert.Throws<BrowserOperationException>(
            () => response.Validate(request, "owned-document")).Code);
    }

    [Fact]
    public void ResponseAcceptsMaximumNodesAndObservedStateWithoutInputOrGoal()
    {
        var request = Request(2, ReaderWorkerOperation.Observe); var response = OpenResponse(request);
        response = response with
        {
            State = ReaderWorkerState.Observed,
            Snapshot = response.Snapshot! with { Nodes = Enumerable.Repeat(response.Snapshot.Nodes[0], 2048).ToImmutableArray() }
        };
        response.Validate(request, "owned-document");
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("snapshot")]
    [InlineData("not-closed")]
    [InlineData("active-call")]
    [InlineData("error")]
    [InlineData("collector")]
    [InlineData("collector-call")]
    [InlineData("context-call")]
    [InlineData("browser-call")]
    [InlineData("process")]
    [InlineData("process-call")]
    [InlineData("transport")]
    [InlineData("transport-disposed")]
    [InlineData("browser-deadline")]
    [InlineData("browser-rejected")]
    [InlineData("exit-deadline")]
    [InlineData("native-deadline")]
    [InlineData("negative-time")]
    [InlineData("missing-time")]
    [InlineData("unsettled-stop")]
    public void ClosedRequiresActualBoundedNativeShutdown(string scenario)
    {
        var request = Request(2, ReaderWorkerOperation.Close);
        var shutdown = FixtureWorkerTests.Receipt(FixtureWorkerTests.Job()).Shutdown;
        shutdown = scenario switch
        {
            "not-closed" => shutdown with { Phase = BrowserShutdownPhase.ClosingBrowser },
            "active-call" => shutdown with { ActiveCalls = 1 },
            "error" => shutdown with { ErrorCode = "CLOSE_UNCONFIRMED" },
            "collector" => shutdown with { CollectorStopAcknowledged = false },
            "collector-call" => shutdown with { CollectorCallSettled = false },
            "context-call" => shutdown with { ContextCallSettled = false },
            "browser-call" => shutdown with { BrowserCallSettled = false },
            "process" => shutdown with { ProcessExitConfirmed = false },
            "process-call" => shutdown with { ProcessCallSettled = false },
            "transport" => shutdown with { TransportCallSettled = false },
            "transport-disposed" => shutdown with { TransportDisposed = false },
            "browser-deadline" => shutdown with { BrowserDeadlineExceeded = true },
            "browser-rejected" => shutdown with { BrowserCallRejected = true },
            "exit-deadline" => shutdown with { ProcessExitConfirmationMilliseconds = 4000 },
            "native-deadline" => shutdown with { NativeElapsedMilliseconds = 5000 },
            "negative-time" => shutdown with { NativeElapsedMilliseconds = -1 },
            "missing-time" => shutdown with { ProcessExitConfirmationMilliseconds = null },
            "unsettled-stop" => shutdown with { OwnedProcessStopRequested = true, OwnedProcessStopCallSettled = false },
            _ => shutdown
        };
        var response = OpenResponse(request) with
        {
            State = ReaderWorkerState.Closed,
            Snapshot = scenario == "snapshot" ? OpenResponse(request).Snapshot : null,
            Shutdown = scenario == "missing" ? null : shutdown
        };
        if (scenario == "valid") response.Validate(request, "owned-document");
        else Assert.Equal("READER_WORKER_CLOSE_UNCONFIRMED", Assert.Throws<BrowserOperationException>(
            () => response.Validate(request, "owned-document")).Code);
    }

    [Fact]
    public void CloseAcceptsSettledEscalationImmediatelyBelowNativeDeadlines()
    {
        var request = Request(2, ReaderWorkerOperation.Close);
        var shutdown = FixtureWorkerTests.Receipt(FixtureWorkerTests.Job()).Shutdown with
        {
            ProcessExitConfirmationMilliseconds = 3999,
            NativeElapsedMilliseconds = 4999,
            OwnedProcessStopRequested = true,
            OwnedProcessStopCallSettled = true
        };
        var response = OpenResponse(request) with { State = ReaderWorkerState.Closed, Snapshot = null, Shutdown = shutdown };
        response.Validate(request, "owned-document");
    }
}
