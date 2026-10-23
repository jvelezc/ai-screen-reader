using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class PortalReaderNavigationTests
{
    private static PortalIdentity Actor => PortalReaderSessionTests.Actor;
    private static Guid Organization => PortalReaderSessionTests.Organization;
    private static PortalReaderCommandRequest Next(string document = "document") => new("NextHeading", document);

    [Fact]
    public async Task CursorProgressionUsesFreshSemanticsAndPaginatesOrderedCommandEvents()
    {
        var browser = new MutableBrowser(); var project = new ReaderProjectStub();
        await using var sessions = new PortalReaderSessions(project, new FakeTimeProvider(), (_, _) => browser);
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        var empty = await sessions.TimelineAsync(Actor, Organization, opened.SessionId, new(0, 1), default);
        Assert.Empty(empty.Events); Assert.False(empty.HasMore); Assert.False(empty.TimelineTruncated);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        var first = await sessions.CommandAsync(Actor, Organization, opened.SessionId, Next(), default);
        Assert.Equal("h1", first.Step.CursorNodeId); Assert.Contains("First", first.Step.Announcement);
        browser.Snapshot = browser.Snapshot with { Nodes = [new("h1", "heading", "First", HeadingLevel: 1), new("h2", "heading", "Updated second", HeadingLevel: 2)] };
        var second = await sessions.CommandAsync(Actor, Organization, opened.SessionId, Next(), default);
        Assert.Equal("h2", second.Step.CursorNodeId); Assert.Contains("Updated second", second.Step.Announcement);
        Assert.Equal(ReaderOutcome.CursorMoved, second.Step.Outcome);
        Assert.Equal(3, browser.Captures); Assert.False(second.RequiresObservation);
        Assert.Equal(0, second.DispatchedInputs); Assert.False(second.GoalVerified);
        Assert.Equal(0, second.Step.BrowserKeystrokes); Assert.False(second.Step.InputMayHaveOccurred);
        Assert.Equal(VerificationStatus.NotRequested, second.Step.Verification);
        var page = await sessions.TimelineAsync(Actor, Organization, opened.SessionId, new(0, 1), default);
        Assert.Equal(ReaderEventKind.CommandStarted, Assert.Single(page.Events).Kind);
        Assert.Equal(1, page.NextAfterSequence); Assert.True(page.HasMore);
        var rest = await sessions.TimelineAsync(Actor, Organization, opened.SessionId, new(page.NextAfterSequence, 100), default);
        Assert.False(rest.HasMore); Assert.False(rest.TimelineTruncated);
        Assert.Equal(ReaderEventKind.CommandCompleted, rest.Events[^1].Kind);
        Assert.Contains(rest.Events, item => item.Kind == ReaderEventKind.Announcement && item.NodeId == "h2" && item.CommandSequence == 2);
        Assert.All(rest.Events, item => Assert.True(item.Sequence > page.NextAfterSequence));
        Assert.Equal(3, browser.Captures); // Paging does not spend capture budget.
    }

    [Fact]
    public async Task DocumentFencesRequireObservationAndChangedDocumentMustBeAcknowledgedAgain()
    {
        var browser = new MutableBrowser();
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), (_, _) => browser);
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        Assert.Equal("CONTROL_READER_OBSERVATION_REQUIRED", (await Assert.ThrowsAsync<PortalFault>(() => sessions.CommandAsync(Actor, Organization, opened.SessionId, Next(), default))).Code);
        Assert.Equal(0, browser.Captures);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        Assert.Equal("CONTROL_READER_DOCUMENT_CHANGED", (await Assert.ThrowsAsync<PortalFault>(() => sessions.CommandAsync(Actor, Organization, opened.SessionId, Next("stale"), default))).Code);
        Assert.Equal(1, browser.Captures);
        browser.Snapshot = browser.Snapshot with { DocumentId = "new-document" };
        var changed = await sessions.CommandAsync(Actor, Organization, opened.SessionId, Next(), default);
        Assert.True(changed.RequiresObservation); Assert.Equal("new-document", changed.DocumentId);
        Assert.Equal(ReaderOutcome.Denied, changed.Step.Outcome); Assert.Equal("DOCUMENT_CHANGED", changed.Step.ErrorCode);
        Assert.Null(changed.Step.CursorNodeId);
        Assert.Equal("CONTROL_READER_OBSERVATION_REQUIRED", (await Assert.ThrowsAsync<PortalFault>(() => sessions.CommandAsync(Actor, Organization, opened.SessionId, Next("new-document"), default))).Code);
        Assert.Equal(2, browser.Captures);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        Assert.Equal("h1", (await sessions.CommandAsync(Actor, Organization, opened.SessionId, Next("new-document"), default)).Step.CursorNodeId);
    }

    [Theory]
    [InlineData("Tab")]
    [InlineData("Activate")]
    [InlineData("EnterFocusMode")]
    [InlineData("PressKey")]
    [InlineData("Escape")]
    [InlineData("ObserveChanges")]
    [InlineData("Typeahead")]
    [InlineData("1")]
    [InlineData("nextheading")]
    [InlineData(null)]
    public async Task UnreviewedOrInputCommandsCannotCaptureOrChangeReaderState(string? command)
    {
        var browser = new MutableBrowser();
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), (_, _) => browser);
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        var fault = await Assert.ThrowsAsync<PortalFault>(() => sessions.CommandAsync(Actor, Organization, opened.SessionId, new(command!, "document"), default));
        Assert.Equal(400, fault.Status); Assert.Equal("CONTROL_READER_COMMAND_INVALID", fault.Code);
        Assert.Equal(1, browser.Captures);
        Assert.Empty((await sessions.TimelineAsync(Actor, Organization, opened.SessionId, new(0, 100), default)).Events);
    }

    [Fact]
    public async Task CommandsShareTheExactCaptureQuotaAndHistoryTruncationRemainsExplicit()
    {
        var browser = new MutableBrowser();
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), (_, _) => browser);
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        for (var index = 0; index < 99; index++) await sessions.CommandAsync(Actor, Organization, opened.SessionId, new("ReadCurrent", "document"), default);
        Assert.Equal(100, browser.Captures);
        Assert.Equal("CONTROL_READER_BUDGET_EXHAUSTED", (await Assert.ThrowsAsync<PortalFault>(() => sessions.CommandAsync(Actor, Organization, opened.SessionId, Next(), default))).Code);
        Assert.Equal("CONTROL_READER_BUDGET_EXHAUSTED", (await Assert.ThrowsAsync<PortalFault>(() => sessions.ObserveAsync(Actor, Organization, opened.SessionId, default))).Code);
        var after = 0L; var total = 0; PortalReaderTimelineResponse page;
        do
        {
            page = await sessions.TimelineAsync(Actor, Organization, opened.SessionId, new(after, 100), default);
            Assert.True(page.TimelineTruncated); Assert.InRange(page.Events.Length, 1, 100);
            total += page.Events.Length; after = page.NextAfterSequence;
        } while (page.HasMore);
        Assert.Equal(256, total); Assert.Equal(256, after);
        Assert.Equal(100, browser.Captures); Assert.Equal(0, browser.Closes);
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("workspace")]
    [InlineData("revoked")]
    public async Task NavigationAndTimelineRecheckScopeBeforeRevealingOrCapturing(string denial)
    {
        var project = new ReaderProjectStub(); var browser = new MutableBrowser();
        await using var sessions = new PortalReaderSessions(project, new FakeTimeProvider(), (_, _) => browser);
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        await sessions.CommandAsync(Actor, Organization, opened.SessionId, Next(), default);
        var actor = denial == "subject" ? new PortalIdentity(Guid.NewGuid(), "other@example.invalid", "reader-second") : Actor;
        var organization = denial == "workspace" ? Guid.NewGuid() : Organization;
        project.Hidden = denial == "revoked";
        Assert.Equal(404, (await Assert.ThrowsAsync<PortalFault>(() => sessions.CommandAsync(actor, organization, opened.SessionId, Next(), default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<PortalFault>(() => sessions.TimelineAsync(actor, organization, opened.SessionId, new(0, 100), default))).Status);
        Assert.Equal(2, browser.Captures); Assert.Equal(0, browser.Closes);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("expire")]
    [InlineData("failure")]
    [InlineData("oversized")]
    [InlineData("reply-too-large")]
    public async Task FailedCommandsJoinCaptureAndRetireBeforeReturning(string cause)
    {
        var time = new FakeTimeProvider(); var browser = new MutableBrowser();
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), time, (_, _) => browser);
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        browser.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource();
        var work = sessions.CommandAsync(Actor, Organization, opened.SessionId, Next(), cancel.Token);
        await browser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cause == "cancel") cancel.Cancel();
        if (cause == "expire") time.Advance(PortalReaderSessions.Lifetime);
        browser.FailCapture = cause == "failure";
        if (cause == "oversized") browser.Snapshot = browser.Snapshot with { Nodes = [new("huge", "heading", new string('x', 262144))] };
        if (cause == "reply-too-large") browser.Snapshot = browser.Snapshot with
        {
            Nodes = [new("huge", "heading", "Range heading",
            Range: new(1, 0, 10, SemanticSource.DeclaredAria, new(new string('x', 150000), SemanticSource.DeclaredAria)))]
        };
        Assert.False(work.IsCompleted); Assert.Equal(0, browser.Closes);
        browser.Release.SetResult();
        if (cause == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        else
        {
            var fault = await Assert.ThrowsAsync<PortalFault>(() => work);
            Assert.Equal(cause == "expire" ? 404 : 503, fault.Status);
            if (cause == "reply-too-large") Assert.Equal("CONTROL_READER_COMMAND_TOO_LARGE", fault.Code);
        }
        Assert.Equal(1, browser.Closes);
        Assert.Equal(404, (await Assert.ThrowsAsync<PortalFault>(() => sessions.TimelineAsync(Actor, Organization, opened.SessionId, new(0, 100), default))).Status);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task TimelineRejectsInvalidPageBounds(long after, int limit)
    {
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), (_, _) => new MutableBrowser());
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        Assert.Equal("CONTROL_READER_PAGE_INVALID", (await Assert.ThrowsAsync<PortalFault>(() => sessions.TimelineAsync(Actor, Organization, opened.SessionId, new(after, limit), default))).Code);
    }

    [Fact]
    public async Task LargeHistoryIsPagedByBytesWithoutDroppingTheNextEvent()
    {
        var browser = new MutableBrowser { Snapshot = new("document", [new("large", "heading", new string('x', 100000), HeadingLevel: 1)], []) };
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), (_, _) => browser);
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        await sessions.CommandAsync(Actor, Organization, opened.SessionId, Next(), default);
        for (var index = 0; index < 3; index++) await sessions.CommandAsync(Actor, Organization, opened.SessionId, new("ReadCurrent", "document"), default);
        var first = await sessions.TimelineAsync(Actor, Organization, opened.SessionId, new(0, 100), default);
        Assert.True(first.HasMore);
        var all = first.Events.ToList(); var cursor = first.NextAfterSequence;
        while (true)
        {
            var page = await sessions.TimelineAsync(Actor, Organization, opened.SessionId, new(cursor, 100), default);
            Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(page, PortalJson.Options).Length, 1, 262144);
            Assert.Equal(cursor + 1, page.Events[0].Sequence);
            all.AddRange(page.Events); cursor = page.NextAfterSequence;
            if (!page.HasMore) break;
        }
        Assert.Equal(16, all.Count);
        Assert.Equal(4, all.Count(item => item.Kind == ReaderEventKind.Announcement && item.NodeId == "large"));
        Assert.Equal(16, cursor); Assert.Equal(5, browser.Captures);
    }

    [Fact]
    public async Task FailedCommandCleanupQuarantinesBothNavigationAndHistoryUntilConfirmedClose()
    {
        var browser = new MutableBrowser();
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), (_, _) => browser);
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        browser.FailCapture = true; browser.FailClose = true;
        Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<PortalFault>(() => sessions.CommandAsync(Actor, Organization, opened.SessionId, Next(), default))).Code);
        Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<PortalFault>(() => sessions.TimelineAsync(Actor, Organization, opened.SessionId, new(0, 100), default))).Code);
        Assert.Equal("CONTROL_READER_CLOSE_UNCONFIRMED", (await Assert.ThrowsAsync<PortalFault>(() => sessions.CommandAsync(Actor, Organization, opened.SessionId, Next(), default))).Code);
        Assert.Equal(2, browser.Captures); Assert.Equal(1, browser.Closes);
        browser.FailClose = false;
        Assert.Equal("Closed", (await sessions.CloseAsync(Actor, Organization, opened.SessionId, default)).State);
        Assert.Equal(2, browser.Closes);
    }

    [Fact]
    public async Task AnOversizedEventFailsAtItsCursorWithoutSkippingOrRetiringTheSession()
    {
        var browser = new MutableBrowser();
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), (_, _) => browser);
        var opened = await sessions.OpenAsync(Actor, Organization, default);
        await sessions.ObserveAsync(Actor, Organization, opened.SessionId, default);
        await sessions.CommandAsync(Actor, Organization, opened.SessionId, Next(), default);
        browser.Snapshot = browser.Snapshot with
        {
            Nodes = [new("h1", "heading", "First", HeadingLevel: 1),
            new("live", "status", "", LiveRegion: new("polite", true, false, new string('x', 150000)))]
        };
        await sessions.CommandAsync(Actor, Organization, opened.SessionId, new("ReadCurrent", "document"), default);
        var prefix = await sessions.TimelineAsync(Actor, Organization, opened.SessionId, new(0, 100), default);
        Assert.True(prefix.HasMore); Assert.Equal(6, prefix.NextAfterSequence);
        Assert.DoesNotContain(prefix.Events, item => item.Kind == ReaderEventKind.LiveRegionChanged);
        var fault = await Assert.ThrowsAsync<PortalFault>(() => sessions.TimelineAsync(Actor, Organization, opened.SessionId, new(prefix.NextAfterSequence, 100), default));
        Assert.Equal(503, fault.Status); Assert.Equal("CONTROL_READER_TIMELINE_TOO_LARGE", fault.Code);
        Assert.Equal(0, browser.Closes); Assert.Equal(3, browser.Captures);
        Assert.Equal("Open", (await sessions.ReadAsync(Actor, Organization, opened.SessionId, default)).State);
    }

    [Theory]
    [InlineData("{\"command\":\"NextHeading\"}")]
    [InlineData("{\"command\":\"NextHeading\",\"expectedDocumentId\":\"document\",\"key\":\"Enter\"}")]
    [InlineData("{\"command\":1,\"expectedDocumentId\":\"document\"}")]
    public async Task HttpCommandRejectsMalformedOrExtraInputFields(string json)
    {
        var project = new ReaderProjectStub(); var browser = new MutableBrowser();
        var readers = new PortalReaderSessions(project, new FakeTimeProvider(), (_, _) => browser);
        await using var server = await PortalReaderHttpTests.Start(project, readers);
        using var http = PortalReaderHttpTests.Client(server);
        using var created = await http.PostAsJsonAsync(PortalReaderHttpTests.Collection, new { });
        using var response = await http.PostAsync(created.Headers.Location + "/commands", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, browser.Captures);
    }

    [Theory]
    [InlineData("{\"afterSequence\":\"0\",\"limit\":1}")]
    [InlineData("{\"afterSequence\":0,\"limit\":\"1\"}")]
    [InlineData("{\"limit\":1}")]
    public async Task HttpTimelineRequiresThePublishedIntegerFields(string json)
    {
        var project = new ReaderProjectStub();
        var readers = new PortalReaderSessions(project, new FakeTimeProvider(), (_, _) => new MutableBrowser());
        await using var server = await PortalReaderHttpTests.Start(project, readers);
        using var http = PortalReaderHttpTests.Client(server);
        using var created = await http.PostAsJsonAsync(PortalReaderHttpTests.Collection, new { });
        using var response = await http.PostAsync(created.Headers.Location + "/timeline", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RealChromiumHttpNavigationRetainsCursorAndReportsMatchingEventsWithoutInput()
    {
        await using var server = await PortalReaderHttpTests.Start(new ReaderProjectStub());
        using var http = PortalReaderHttpTests.Client(server);
        using var created = await http.PostAsJsonAsync(PortalReaderHttpTests.Collection, new { });
        using var captured = await http.PostAsJsonAsync(created.Headers.Location + "/observations", new { });
        var snapshot = (await captured.Content.ReadFromJsonAsync<PortalReaderSnapshotResponse>())!.Snapshot;
        using var first = await http.PostAsJsonAsync(created.Headers.Location + "/commands", Next(snapshot.DocumentId));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var heading = (await first.Content.ReadFromJsonAsync<PortalReaderCommandResponse>())!;
        Assert.Equal("Screen reader fixture", snapshot.Nodes.Single(node => node.Id == heading.Step.CursorNodeId).Name);
        using var next = await http.PostAsJsonAsync(created.Headers.Location + "/commands", new PortalReaderCommandRequest("NextButton", snapshot.DocumentId));
        var button = (await next.Content.ReadFromJsonAsync<PortalReaderCommandResponse>())!;
        Assert.Equal(ReaderOutcome.CursorMoved, button.Step.Outcome);
        Assert.Equal("Open details", snapshot.Nodes.Single(node => node.Id == button.Step.CursorNodeId).Name);
        Assert.Equal(heading.Step.FocusedNodeId, button.Step.FocusedNodeId);
        Assert.Equal(0, button.Step.BrowserKeystrokes); Assert.False(button.Step.InputMayHaveOccurred);
        using var events = await http.PostAsJsonAsync(created.Headers.Location + "/timeline", new PortalReaderTimelineRequest(0, 100));
        Assert.Equal(HttpStatusCode.OK, events.StatusCode);
        var timeline = (await events.Content.ReadFromJsonAsync<PortalReaderTimelineResponse>())!;
        Assert.Contains(timeline.Events, item => item.Kind == ReaderEventKind.Announcement && item.NodeId == button.Step.CursorNodeId && item.DocumentId == snapshot.DocumentId);
        Assert.DoesNotContain(timeline.Events, item => item.Kind is ReaderEventKind.InputAttempted or ReaderEventKind.InputCompleted or ReaderEventKind.GoalVerified);
        Assert.DoesNotContain("fixture-secret-do-not-log", await events.Content.ReadAsStringAsync());
        using var closed = await http.DeleteAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
    }

    private sealed class MutableBrowser : IPortalReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = new("document", [new("h1", "heading", "First", HeadingLevel: 1), new("h2", "heading", "Second", HeadingLevel: 2)], []);
        public int Captures { get; private set; }
        public int Closes { get; private set; }
        public bool FailCapture { get; set; }
        public bool FailClose { get; set; }
        public TaskCompletionSource? Release { get; set; }
        public TaskCompletionSource Entered { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AccessibilitySnapshot> CaptureAsync()
        {
            Captures++; Entered.TrySetResult();
            if (Release is not null) await Release.Task;
            if (FailCapture) throw new PortalFault(503, "CONTROL_READER_CAPTURE_UNCONFIRMED");
            return Snapshot;
        }
        public ValueTask DisposeAsync()
        {
            Closes++;
            if (FailClose) throw new IOException("private-close-canary");
            return ValueTask.CompletedTask;
        }
    }
}
