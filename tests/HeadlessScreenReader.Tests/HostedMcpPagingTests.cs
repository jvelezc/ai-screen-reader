using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed partial class HostedMcpTests
{
    private static AccessibilitySnapshot Snapshot(int characters = 0) => new("document", [new("heading", "heading", new('x', characters))], []);
    private static ReaderStep Step(int characters = 0) => new(1, ReaderCommand.NextHeading, ReaderMode.Browse, ReaderOutcome.CursorMoved, new('x', characters), "heading", null, 0);
    private static ReaderNavigationTraceEntry Entry(long sequence, int characters = 0)
    {
        var source = new ReaderTraceState($"{sequence}:source", $"{sequence}:capture", "document", 0, ReaderMode.Browse, null, null, null);
        var result = source with { Id = $"{sequence}:result", CursorNodeId = "heading" };
        return new(sequence, (int)sequence, source.CaptureId, Snapshot(characters), new(0, 1), null, source, result,
            new($"{sequence}:edge", source.Id, result.Id, ReaderCommand.NextHeading, "document", ReaderOutcome.CursorMoved, VerificationStatus.NotRequested, 0, false), Step() with { Sequence = sequence });
    }
    private static object Trace(params ReaderNavigationTraceEntry[] entries) => new
    {
        sessionId = Session,
        readerProfile = ScreenReaderSession.Profile,
        schema = "hsr-reader-navigation-trace-v1",
        retention = "Transient",
        entries,
        nextAfterSequence = entries.LastOrDefault()?.Sequence ?? 0,
        hasMore = false,
        traceTruncated = false,
        truncationReason = (string?)null,
        retainedCommands = entries.Length,
        latestCommandSequence = entries.LastOrDefault()?.Sequence ?? 0,
        dispatchedInputs = 0,
        goalVerified = false
    };
    private static object Capture(AccessibilitySnapshot? snapshot = null) => new { sessionId = Session, readerProfile = ScreenReaderSession.Profile, snapshot = snapshot ?? Snapshot(), dispatchedInputs = 0, goalVerified = false };
    private static Func<System.Net.Http.HttpResponseMessage> Closed() => () => Json(new { sessionId = Session, state = "Closed" });

    [Fact]
    public async Task CachedPagesRecheckAuthorityAndCommandsInvalidateObservation()
    {
        var snapshot = Snapshot() with { Nodes = [new("a", "heading", "First"), new("b", "button", "Next")] };
        var handler = Queue(() => Json(Metadata(), 201), () => Json(Capture(snapshot)), () => Json(Metadata()),
            () => Json(new HostedReaderCommand(Session, ScreenReaderSession.Profile, "document", false, Step(), 0, false)), Closed());
        await using var client = new HostedReaderClient(Options(), handler); await client.OpenAsync();
        var first = Data(await client.SnapshotAsync(Session.ToString(), limit: 1)); var observation = first.GetProperty("observationId").GetString();
        var second = Data(await client.SnapshotAsync(Session.ToString(), 1, 1, observation));
        Assert.Equal("Next", Assert.Single(second.GetProperty("nodes").EnumerateArray()).GetProperty("name").GetString());
        Assert.False(second.GetProperty("hasMore").GetBoolean());
        Assert.Equal("CONTROL_READER_DOCUMENT_CHANGED", (await client.ExecuteAsync(Session.ToString(), "stale", "NextHeading")).ErrorCode);
        Assert.Equal("step", (await client.ExecuteAsync(Session.ToString(), "document", "NextHeading")).Status);
        Assert.Equal("OBSERVATION_CHANGED", (await client.SnapshotAsync(Session.ToString(), 1, 1, observation)).ErrorCode);
        Assert.Equal(new[] { "POST", "POST", "GET", "POST" }, handler.Requests.Select(r => r.Method));
    }

    [Fact]
    public async Task SnapshotBytePagingAndOversizedFirstNodeNeverSkipContent()
    {
        var snapshot = Snapshot() with { Nodes = [new("a", "heading", new('a', 70000)), new("b", "button", new('b', 70000))] };
        var handler = Queue(() => Json(Metadata(), 201), () => Json(Capture(snapshot)), () => Json(Metadata()), () => Json(Capture(Snapshot(140000))), Closed());
        await using var client = new HostedReaderClient(Options(), handler); await client.OpenAsync();
        var first = Data(await client.SnapshotAsync(Session.ToString()));
        Assert.Equal(1, first.GetProperty("returnedNodes").GetInt32()); Assert.True(first.GetProperty("hasMore").GetBoolean());
        var second = Data(await client.SnapshotAsync(Session.ToString(), 1, observationId: first.GetProperty("observationId").GetString()));
        Assert.Equal("b", Assert.Single(second.GetProperty("nodes").EnumerateArray()).GetProperty("id").GetString());
        Assert.False(second.GetProperty("hasMore").GetBoolean());
        var oversized = await client.SnapshotAsync(Session.ToString()); Assert.Equal("HOSTED_SNAPSHOT_ENTRY_TOO_LARGE", oversized.ErrorCode); Assert.Null(oversized.Data);
    }

    [Fact]
    public async Task RevokedAuthorityNeverServesCachedObservation()
    {
        var handler = Queue(() => Json(Metadata(), 201), () => Json(Capture()), () => Json(new { title = "CONTROL_WORKSPACE_NOT_FOUND" }, 404), Closed());
        await using var client = new HostedReaderClient(Options(), handler); await client.OpenAsync();
        var observation = Data(await client.SnapshotAsync(Session.ToString())).GetProperty("observationId").GetString();
        var denied = await client.SnapshotAsync(Session.ToString(), observationId: observation);
        Assert.Equal("CONTROL_WORKSPACE_NOT_FOUND", denied.ErrorCode); Assert.DoesNotContain("heading", denied.ToJson());
        Assert.Equal("HOSTED_SESSION_UNCONFIRMED", (await client.ExecuteAsync(Session.ToString(), "document", "NextHeading")).ErrorCode);
    }

    [Fact]
    public async Task TraceShrinksByEncodedBytesWithoutSkippingAtomicEntries()
    {
        var first = Entry(1, 70000); var second = Entry(2, 70000);
        var handler = Queue(() => Json(Metadata(), 201), () => Json(Trace(first, second)), () => Json(Trace(second)), Closed());
        await using var client = new HostedReaderClient(Options(), handler); await client.OpenAsync();
        var one = await client.TraceAsync(Session.ToString()); Assert.Equal("trace", one.Status);
        var page = Data(one); Assert.Single(page.GetProperty("entries").EnumerateArray());
        Assert.Equal(1, page.GetProperty("nextAfterSequence").GetInt64()); Assert.True(page.GetProperty("hasMore").GetBoolean());
        Assert.Equal(70000, page.GetProperty("entries")[0].GetProperty("snapshot").GetProperty("nodes")[0].GetProperty("name").GetString()!.Length);
        var two = Data(await client.TraceAsync(Session.ToString(), 1));
        Assert.Equal(2, Assert.Single(two.GetProperty("entries").EnumerateArray()).GetProperty("sequence").GetInt64()); Assert.False(two.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task OversizedTraceEntryFailsWithoutInventedContinuation()
    {
        var handler = Queue(() => Json(Metadata(), 201), () => Json(Trace(Entry(1, 140000))), Closed());
        await using var client = new HostedReaderClient(Options(), handler); await client.OpenAsync();
        var reply = await client.TraceAsync(Session.ToString());
        Assert.Equal("HOSTED_TRACE_ENTRY_TOO_LARGE", reply.ErrorCode); Assert.Null(reply.Data);
        Assert.DoesNotContain("nextAfterSequence", reply.ToJson());
    }

    [Theory]
    [InlineData("mechanism")]
    [InlineData("source")]
    [InlineData("capture")]
    [InlineData("sequence")]
    [InlineData("input")]
    [InlineData("missingFact")]
    [InlineData("retention")]
    [InlineData("cursor")]
    public async Task TraceRejectsContradictoryOrMissingProvenance(string failure)
    {
        var root = JsonSerializer.SerializeToNode(Trace(Entry(1)), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var entry = root["entries"]![0]!;
        switch (failure)
        {
            case "mechanism": entry["edge"]!["mechanism"] = "Keyboard"; break;
            case "source": entry["edge"]!["sourceStateId"] = "forged"; break;
            case "capture": entry["resultState"]!["captureId"] = "forged"; break;
            case "sequence": root["nextAfterSequence"] = 2; break;
            case "input": entry["step"]!["inputMayHaveOccurred"] = true; break;
            case "missingFact": entry["step"]!.AsObject().Remove("inputMayHaveOccurred"); break;
            case "retention": root["retention"] = "Durable"; break;
            case "cursor": entry["resultState"]!["cursorNodeId"] = "forged"; break;
        }
        var handler = Queue(() => Json(Metadata(), 201), () => Json(root), Closed());
        await using var client = new HostedReaderClient(Options(), handler); await client.OpenAsync();
        Assert.Equal("HOSTED_RESPONSE_INVALID", (await client.TraceAsync(Session.ToString())).ErrorCode);
    }

    [Theory]
    [InlineData("CommandLimit")]
    [InlineData("ByteLimit")]
    [InlineData("EntryTooLarge")]
    public async Task ExplicitTraceTruncationSurvivesMcpPaging(string reason)
    {
        var root = JsonSerializer.SerializeToNode(Trace(Entry(1)), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        root["traceTruncated"] = true; root["truncationReason"] = reason; root["latestCommandSequence"] = 80;
        var handler = Queue(() => Json(Metadata(), 201), () => Json(root), Closed());
        await using var client = new HostedReaderClient(Options(), handler); await client.OpenAsync();
        var page = Data(await client.TraceAsync(Session.ToString()));
        Assert.True(page.GetProperty("traceTruncated").GetBoolean()); Assert.Equal(reason, page.GetProperty("truncationReason").GetString());
        Assert.Equal(80, page.GetProperty("latestCommandSequence").GetInt64()); Assert.False(page.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task TimelineBytePagingRetainsEventsAndTruncationWithoutCapture()
    {
        ReaderEvent Event(long sequence) => new(sequence, 1, 0, ReaderEventKind.Announcement, ReaderCommand.NextHeading, "document", "heading", new('x', 70000));
        object Timeline(params ReaderEvent[] events) => new { sessionId = Session, readerProfile = ScreenReaderSession.Profile, events, nextAfterSequence = events[^1].Sequence, hasMore = false, timelineTruncated = true };
        var handler = Queue(() => Json(Metadata(), 201), () => Json(Timeline(Event(1), Event(2))), () => Json(Timeline(Event(2))), Closed());
        await using var client = new HostedReaderClient(Options(), handler); await client.OpenAsync();
        var first = Data(await client.TimelineAsync(Session.ToString()));
        Assert.Single(first.GetProperty("events").EnumerateArray()); Assert.Equal(1, first.GetProperty("nextAfterSequence").GetInt64());
        Assert.True(first.GetProperty("hasMore").GetBoolean()); Assert.True(first.GetProperty("timelineTruncated").GetBoolean());
        var second = Data(await client.TimelineAsync(Session.ToString(), 1)); Assert.Equal(2, second.GetProperty("nextAfterSequence").GetInt64());
        Assert.False(second.GetProperty("hasMore").GetBoolean()); Assert.All(handler.Requests.Skip(1), r => Assert.EndsWith("/timeline", r.Path));
    }

    [Fact]
    public async Task ReportedInputViolationPreservesFactsAndRequiresExplicitRecovery()
    {
        var reported = Step() with { BrowserKeystrokes = 1, InputMayHaveOccurred = true, Verification = VerificationStatus.TimedOut };
        var handler = Queue(() => Json(Metadata(), 201), () => Json(Capture()), () => Json(new HostedReaderCommand(Session, ScreenReaderSession.Profile, "document", false, reported, 1, false)), Closed());
        await using var client = new HostedReaderClient(Options(), handler); await client.OpenAsync(); await client.SnapshotAsync(Session.ToString());
        var result = await client.ExecuteAsync(Session.ToString(), "document", "NextHeading");
        Assert.Equal("HOSTED_READ_ONLY_VIOLATION", result.ErrorCode); Assert.True(Data(result).GetProperty("step").GetProperty("inputMayHaveOccurred").GetBoolean());
        Assert.Equal(1, Data(result).GetProperty("step").GetProperty("browserKeystrokes").GetInt32());
        Assert.Equal(1, Data(result).GetProperty("dispatchedInputs").GetInt32());
        Assert.Equal("HOSTED_SESSION_UNCONFIRMED", (await client.SnapshotAsync(Session.ToString())).ErrorCode);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task ContradictoryApiInputAndGoalTotalsRemainVisible(int inputs, bool goal)
    {
        var handler = Queue(() => Json(Metadata(), 201), () => Json(Capture()), () => Json(new HostedReaderCommand(Session, ScreenReaderSession.Profile, "document", false, Step(), inputs, goal)), Closed());
        await using var client = new HostedReaderClient(Options(), handler); await client.OpenAsync(); await client.SnapshotAsync(Session.ToString());
        var result = await client.ExecuteAsync(Session.ToString(), "document", "NextHeading");
        Assert.Equal("HOSTED_READ_ONLY_VIOLATION", result.ErrorCode);
        Assert.Equal(inputs, Data(result).GetProperty("dispatchedInputs").GetInt32()); Assert.Equal(goal, Data(result).GetProperty("goalVerified").GetBoolean());
        Assert.Equal(0, Data(result).GetProperty("step").GetProperty("browserKeystrokes").GetInt32());
        Assert.Equal("NotRequested", Data(result).GetProperty("step").GetProperty("verification").GetString());
    }
}
