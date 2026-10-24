using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class PortalReaderTraceTests
{
    private static PortalIdentity Actor => PortalReaderSessionTests.Actor;
    private static Guid Organization => PortalReaderSessionTests.Organization;
    private static ReaderStep Step(long sequence) => new(sequence, ReaderCommand.NextHeading, ReaderMode.Browse,
        ReaderOutcome.CursorMoved, "First, heading", "h1", null, 0);
    private static AccessibilitySnapshot Snapshot(int text = 0) => new("document",
        [new("h1", "heading", "First", HeadingLevel: 1), new("h2", "heading", text == 0 ? "Second" : new string('x', text), HeadingLevel: 2)], []);

    [Fact]
    public async Task TraceLinksActualCapturesCursorStatesAndRefreshWithoutInventingInput()
    {
        var clock = new FakeTimeProvider(); var browser = new MutableBrowser(clock);
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), clock, (_, _) => browser);
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        var empty = await sessions.TraceAsync(Actor, Organization, opened.SessionId, new(0, 20), default);
        Assert.Empty(empty.Entries); Assert.Equal(0, empty.LatestCommandSequence); Assert.False(empty.TraceTruncated);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        var first = await sessions.CommandAsync(Actor, Organization, opened.SessionId, new("NextHeading", "document"), default);
        browser.Snapshot = Snapshot() with { Nodes = [new("h1", "heading", "First", HeadingLevel: 1), new("h2", "heading", "Mutated second", HeadingLevel: 2)] };
        var second = await sessions.CommandAsync(Actor, Organization, opened.SessionId, new("NextHeading", "document"), default);
        var page = await sessions.TraceAsync(Actor, Organization, opened.SessionId, new(0, 1), default);
        var a = Assert.Single(page.Entries);
        Assert.Equal(1, a.Sequence); Assert.Equal(2, a.CaptureSequence); Assert.Null(a.RefreshedFromStateId);
        Assert.Null(a.SourceState.CursorNodeId); Assert.Equal("h1", a.ResultState.CursorNodeId);
        Assert.Equal(new EvidenceInterval(5, 10), a.CaptureWindow);
        Assert.Equal(a.SourceState.Id, a.Edge.SourceStateId); Assert.Equal(a.ResultState.Id, a.Edge.ResultStateId);
        Assert.Equal(a.CaptureId, a.SourceState.CaptureId); Assert.Equal(a.CaptureId, a.ResultState.CaptureId);
        Assert.Equal("ReaderCursor", a.Edge.Mechanism); Assert.Equal(first.Step, a.Step);
        Assert.True(page.HasMore); Assert.Equal(1, page.NextAfterSequence);
        var rest = await sessions.TraceAsync(Actor, Organization, opened.SessionId, new(1, 20), default);
        var b = Assert.Single(rest.Entries);
        Assert.Equal(a.ResultState.Id, b.RefreshedFromStateId); Assert.Equal("h1", b.SourceState.CursorNodeId);
        Assert.Equal("h2", b.ResultState.CursorNodeId); Assert.Equal(second.Step, b.Step);
        Assert.Equal("Mutated second", b.Snapshot.Nodes[1].Name); Assert.Equal("Second", a.Snapshot.Nodes[1].Name);
        Assert.Equal(3, b.CaptureSequence); Assert.Equal(new EvidenceInterval(10, 15), b.CaptureWindow);
        Assert.Equal(0, b.Edge.BrowserKeystrokes); Assert.False(b.Edge.InputMayHaveOccurred);
        Assert.Equal(VerificationStatus.NotRequested, b.Edge.Verification);
        Assert.False(rest.GoalVerified); Assert.Equal(0, rest.DispatchedInputs);
        Assert.False(rest.HasMore); Assert.Equal(2, rest.RetainedCommands); Assert.Equal(2, rest.LatestCommandSequence);
        Assert.Equal(3, browser.Captures);
    }

    [Theory]
    [InlineData("document")]
    [InlineData("epoch")]
    [InlineData("removed")]
    public async Task RefreshResetsAStaleSourceCursorAndPreservesDocumentDenial(string change)
    {
        var clock = new FakeTimeProvider(); var browser = new MutableBrowser(clock);
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), clock, (_, _) => browser);
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        await sessions.CommandAsync(Actor, Organization, opened.SessionId, new("NextHeading", "document"), default);
        browser.Snapshot = change switch
        {
            "document" => browser.Snapshot with { DocumentId = "changed-document" },
            "epoch" => browser.Snapshot with { CaptureEpoch = 1 },
            _ => browser.Snapshot with { Nodes = [new("h2", "heading", "Second", HeadingLevel: 2)] }
        };
        var command = await sessions.CommandAsync(Actor, Organization, opened.SessionId, new("NextHeading", "document"), default);
        var entry = Assert.Single((await sessions.TraceAsync(Actor, Organization, opened.SessionId, new(1, 20), default)).Entries);
        Assert.Null(entry.SourceState.CursorNodeId);
        Assert.Equal(browser.Snapshot.DocumentId, entry.SourceState.DocumentId);
        Assert.Equal(browser.Snapshot.CaptureEpoch, entry.SourceState.CaptureEpoch);
        if (change == "document")
        {
            Assert.Equal(ReaderOutcome.Denied, entry.Edge.Outcome); Assert.True(command.RequiresObservation);
            Assert.Equal("document", entry.Edge.ExpectedDocumentId); Assert.Equal("changed-document", entry.ResultState.DocumentId);
            Assert.Null(entry.ResultState.CursorNodeId);
        }
        else Assert.Equal(ReaderOutcome.CursorMoved, entry.Edge.Outcome);
    }

    [Fact]
    public void RetentionKeepsAnExactPrefixAndMarksTheFirstOmittedCommand()
    {
        var trace = new PortalReaderTrace(); var session = Guid.NewGuid();
        for (var sequence = 1; sequence <= 64; sequence++) trace.Append(session, sequence, Snapshot(), new(0, 1), null, Step(sequence), "document");
        var full = trace.Page(session, new(60, 20));
        Assert.False(full.TraceTruncated); Assert.Null(full.TruncationReason); Assert.Equal(64, full.RetainedCommands);
        Assert.Equal(new long[] { 61, 62, 63, 64 }, full.Entries.Select(item => item.Sequence));
        trace.Append(session, 65, Snapshot(), new(1, 2), null, Step(65), "document");
        trace.Append(session, 66, Snapshot(), new(2, 3), null, Step(66), "document");
        var truncated = trace.Page(session, new(64, 20));
        Assert.Empty(truncated.Entries); Assert.False(truncated.HasMore); Assert.True(truncated.TraceTruncated);
        Assert.Equal("CommandLimit", truncated.TruncationReason); Assert.Equal(64, truncated.RetainedCommands);
        Assert.Equal(66, truncated.LatestCommandSequence); Assert.Equal(64, truncated.NextAfterSequence);
        trace.Clear();
        var cleared = trace.Page(session, new(0, 20));
        Assert.Empty(cleared.Entries); Assert.False(cleared.TraceTruncated); Assert.Equal(0, cleared.LatestCommandSequence);
    }

    [Fact]
    public void ByteRetentionAndOversizedEntriesCannotSilentlyResumeAfterAGap()
    {
        var session = Guid.NewGuid(); var trace = new PortalReaderTrace();
        for (var sequence = 1; sequence <= 64; sequence++) trace.Append(session, sequence, Snapshot(200000), new(0, 1), null, Step(sequence), "document");
        var bounded = trace.Page(session, new(0, 20));
        Assert.True(bounded.TraceTruncated); Assert.Equal("ByteLimit", bounded.TruncationReason);
        Assert.Equal(41, bounded.RetainedCommands); Assert.Equal(64, bounded.LatestCommandSequence);
        var retained = bounded.RetainedCommands;
        trace.Append(session, 65, Snapshot(), new(0, 1), null, Step(65), "document");
        Assert.Equal(retained, trace.Page(session, new(0, 20)).RetainedCommands);
        var oversized = new PortalReaderTrace();
        oversized.Append(session, 1, Snapshot(262144), new(0, 1), null, Step(1), "document");
        oversized.Append(session, 2, Snapshot(), new(0, 1), null, Step(2), "document");
        var missing = oversized.Page(session, new(0, 20));
        Assert.Empty(missing.Entries); Assert.True(missing.TraceTruncated); Assert.Equal("EntryTooLarge", missing.TruncationReason);
        Assert.Equal(2, missing.LatestCommandSequence);
    }

    [Fact]
    public void BytePagingNeverSeparatesAnEdgeFromItsSnapshotAndStateNodes()
    {
        var trace = new PortalReaderTrace(); var session = Guid.NewGuid();
        for (var sequence = 1; sequence <= 3; sequence++) trace.Append(session, sequence, Snapshot(150000), new(0, 1), null, Step(sequence), "document");
        var page = trace.Page(session, new(0, 20));
        Assert.True(page.HasMore); Assert.Equal(1, Assert.Single(page.Entries).Sequence);
        for (var sequence = 1; sequence <= 3; sequence++)
        {
            page = trace.Page(session, new(sequence - 1, 20));
            var item = Assert.Single(page.Entries);
            Assert.Equal(sequence, item.Sequence); Assert.Equal(sequence, page.NextAfterSequence);
            Assert.Equal(sequence < 3, page.HasMore);
            Assert.Equal(item.SourceState.Id, item.Edge.SourceStateId); Assert.Equal(item.ResultState.Id, item.Edge.ResultStateId);
            Assert.Equal(150000, item.Snapshot.Nodes[1].Name.Length);
            Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(page, PortalJson.Options).Length, 1, 262144);
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void EntryByteCeilingAcceptsTheBoundaryAndRejectsTheNextByte(int extra, bool retained)
    {
        var session = Guid.Parse("11111111-1111-4111-8111-111111111111"); var calibration = new PortalReaderTrace();
        calibration.Append(session, 1, Snapshot(1), new(0, 1), null, Step(1), "document");
        var seed = Assert.Single(calibration.Page(session, new(0, 1)).Entries);
        var baseBytes = JsonSerializer.SerializeToUtf8Bytes(seed, PortalJson.Options).Length;
        // Calibrate an ASCII test input to the published byte ceiling, independently of the retention decision.
        var nameLength = 1 + 260096 - baseBytes + extra;
        var trace = new PortalReaderTrace();
        trace.Append(session, 1, Snapshot(nameLength), new(0, 1), null, Step(1), "document");
        var page = trace.Page(session, new(0, 1));
        Assert.Equal(retained ? 1 : 0, page.RetainedCommands);
        Assert.Equal(!retained, page.TraceTruncated);
        Assert.Equal(retained ? null : "EntryTooLarge", page.TruncationReason);
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(page, PortalJson.Options).Length, 1, 262144);
    }

    [Fact]
    public async Task QuarantinedCleanupCannotExposePriorTraceAndRetryConfirmsRetirement()
    {
        var project = new ReaderProjectStub(); var browser = new ReaderBrowserStub { FailClose = true };
        await using var sessions = new PortalReaderSessions(project, new FakeTimeProvider(), (_, _) => browser);
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        await sessions.CommandAsync(Actor, Organization, opened.SessionId, new("NextHeading", "document"), default);
        Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<PortalFault>(() => sessions.CloseAsync(Actor, Organization, opened.SessionId, default))).Code);
        Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<PortalFault>(() => sessions.TraceAsync(Actor, Organization, opened.SessionId, new(0, 20), default))).Code);
        browser.FailClose = false;
        await sessions.CloseAsync(Actor, Organization, opened.SessionId, default);
        Assert.Equal(404, (await Assert.ThrowsAsync<PortalFault>(() => sessions.TraceAsync(Actor, Organization, opened.SessionId, new(0, 20), default))).Status);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 21)]
    public void InvalidTracePagesAreRejected(long after, int limit) =>
        Assert.Equal("CONTROL_READER_PAGE_INVALID", Assert.Throws<PortalFault>(() => new PortalReaderTrace().Page(Guid.NewGuid(), new(after, limit))).Code);

    [Theory]
    [InlineData("subject")]
    [InlineData("workspace")]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("closed")]
    public async Task TraceAuthorizationAndLifetimeCannotExposeRetainedStates(string reason)
    {
        var project = new ReaderProjectStub(); var clock = new FakeTimeProvider(); var browser = new MutableBrowser(clock);
        await using var sessions = new PortalReaderSessions(project, clock, (_, _) => browser);
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        await sessions.CommandAsync(Actor, Organization, opened.SessionId, new("NextHeading", "document"), default);
        var actor = reason == "subject" ? new PortalIdentity(Guid.NewGuid(), "other@example.invalid", "other") : Actor;
        var organization = reason == "workspace" ? Guid.NewGuid() : Organization;
        project.Hidden = reason == "revoked";
        if (reason == "expired") clock.Advance(PortalReaderSessions.Lifetime);
        if (reason == "closed") await sessions.CloseAsync(Actor, Organization, opened.SessionId, default);
        Assert.Equal(404, (await Assert.ThrowsAsync<PortalFault>(() => sessions.TraceAsync(actor, organization, opened.SessionId, new(0, 20), default))).Status);
        Assert.Equal(2, browser.Captures);
    }

    [Theory]
    [InlineData("{\"afterSequence\":\"0\",\"limit\":1}")]
    [InlineData("{\"afterSequence\":0,\"limit\":\"1\"}")]
    [InlineData("{\"limit\":1}")]
    [InlineData("{\"afterSequence\":0,\"limit\":1,\"userId\":\"injected\"}")]
    public async Task HttpTraceRejectsMalformedRequestsBeforeAccessingTheRegistry(string json)
    {
        var project = new ReaderProjectStub(); var readers = new PortalReaderSessions(project, new FakeTimeProvider(), (_, _) => new ReaderBrowserStub());
        await using var server = await PortalReaderHttpTests.Start(project, readers);
        using var http = PortalReaderHttpTests.Client(server);
        using var result = await http.PostAsync(PortalReaderHttpTests.Collection + "/" + Guid.NewGuid() + "/trace", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode); Assert.Equal(0, project.WorkspaceCalls);
    }

    [Fact]
    public async Task RealChromiumTraceHasMatchingObservedNodesAndSeparateRefreshLinks()
    {
        await using var server = await PortalReaderHttpTests.Start(new ReaderProjectStub());
        using var http = PortalReaderHttpTests.Client(server);
        using var opened = await http.PostAsJsonAsync(PortalReaderHttpTests.Collection, new { });
        var resource = opened.Headers.Location!;
        using var captured = await http.PostAsJsonAsync(resource + "/observations", new { });
        var snapshot = (await captured.Content.ReadFromJsonAsync<PortalReaderSnapshotResponse>())!.Snapshot;
        foreach (var command in new[] { "NextHeading", "NextButton" })
        {
            using var response = await http.PostAsJsonAsync(resource + "/commands", new PortalReaderCommandRequest(command, snapshot.DocumentId));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var trace = await http.PostAsJsonAsync(resource + "/trace", new PortalReaderTraceRequest(0, 20));
        Assert.Equal(HttpStatusCode.OK, trace.StatusCode);
        var result = (await trace.Content.ReadFromJsonAsync<PortalReaderTraceResponse>())!;
        Assert.Equal(2, result.Entries.Length); Assert.False(result.TraceTruncated);
        var first = result.Entries[0]; var second = result.Entries[1];
        Assert.Equal("Screen reader fixture", first.Snapshot.Nodes.Single(node => node.Id == first.ResultState.CursorNodeId).Name);
        Assert.Equal("Open details", second.Snapshot.Nodes.Single(node => node.Id == second.ResultState.CursorNodeId).Name);
        Assert.Equal(first.ResultState.Id, second.RefreshedFromStateId);
        Assert.Equal(first.ResultState.CursorNodeId, second.SourceState.CursorNodeId);
        Assert.Equal(first.SourceState.FocusedNodeId, second.ResultState.FocusedNodeId);
        Assert.All(result.Entries, item => { Assert.Equal("ReaderCursor", item.Edge.Mechanism); Assert.Equal(0, item.Edge.BrowserKeystrokes); Assert.Equal(VerificationStatus.NotRequested, item.Edge.Verification); });
        Assert.Equal("Transient", result.Retention); Assert.False(result.GoalVerified);
        Assert.DoesNotContain("fixture-secret-do-not-log", await trace.Content.ReadAsStringAsync());
        using var closed = await http.DeleteAsync(resource); Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
    }

    private sealed class MutableBrowser(FakeTimeProvider clock) : IPortalReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = PortalReaderTraceTests.Snapshot();
        public int Captures { get; private set; }
        public Task<AccessibilitySnapshot> CaptureAsync() { Captures++; clock.Advance(TimeSpan.FromMilliseconds(5)); return Task.FromResult(Snapshot); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
