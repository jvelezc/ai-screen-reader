using System.Collections.Immutable;
using HeadlessScreenReader.Cli.HostedControl;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;
using Microsoft.Extensions.Time.Testing;

namespace HeadlessScreenReader.Tests;

public sealed class TextCursorOwnershipTests
{
    [Theory]
    [InlineData("frame", ReaderCommand.ReadCurrentText)]
    [InlineData("frame", ReaderCommand.NextWord)]
    [InlineData("node-document", ReaderCommand.ReadCurrentText)]
    [InlineData("node-document", ReaderCommand.NextWord)]
    [InlineData("child-to-root", ReaderCommand.ReadCurrentText)]
    [InlineData("child-to-root", ReaderCommand.NextWord)]
    [InlineData("root-to-child", ReaderCommand.ReadCurrentText)]
    [InlineData("root-to-child", ReaderCommand.NextWord)]
    [InlineData("legacy-to-child", ReaderCommand.ReadCurrentText)]
    [InlineData("legacy-to-child", ReaderCommand.NextWord)]
    public async Task ReusedTextNodeCannotRetainOffsetsAcrossChangedOwnership(string change, ReaderCommand command)
    {
        var initialFrame = change is "root-to-child" ? "main" : change is "legacy-to-child" ? null : "child";
        var initialDocument = initialFrame is "child" ? "child-document" : initialFrame is null ? null : "root-document";
        var browser = new TextBrowser(Snapshot(initialFrame, initialDocument));
        using var reader = new ScreenReaderSession(browser);
        var selected = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal("Same", selected.Announcement); Assert.Equal(0, selected.TextRange?.Start);
        browser.Snapshot = ChangeOwner(change);
        var reset = await reader.ExecuteAsync(command);
        Assert.Equal(ReaderOutcome.Unknown, reset.Outcome);
        Assert.Equal(TextCursorResetReason.DocumentChanged, reset.TextCursorReset);
        Assert.Null(reset.TextRange); Assert.Null(reset.BarrierCode);
        var stillReset = await reader.ExecuteAsync(ReaderCommand.ReadCurrentText);
        Assert.Equal(ReaderOutcome.Unknown, stillReset.Outcome);
        Assert.Equal(TextCursorResetReason.DocumentChanged, stillReset.TextCursorReset); Assert.Null(stillReset.TextRange);

        // Restore coherent current frame metadata before explicitly anchoring to its new owner.
        if (change == "node-document") browser.Snapshot = Snapshot("child", "replacement-document", "replacement-document");
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        var anchored = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal(ReaderOutcome.CursorMoved, anchored.Outcome); Assert.Equal("Same", anchored.Announcement);
        Assert.Equal(0, anchored.TextRange?.Start); Assert.Equal(4, anchored.TextRange?.Length);
        var current = browser.Snapshot.Nodes.Single(node => node.Id == "text");
        Assert.Equal(current.DocumentId, anchored.TextRange?.DocumentId); Assert.Equal(current.FrameId, anchored.TextRange?.FrameId);
        Assert.Null(anchored.TextCursorReset); Assert.Empty(browser.Keys);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind is ReaderEventKind.InputAttempted or ReaderEventKind.GoalVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StableTextOwnershipKeepsOffsetsWhenAnotherFrameChanges(bool legacy)
    {
        var browser = new TextBrowser(Snapshot(legacy ? null : "main", legacy ? null : "root-document"));
        using var reader = new ScreenReaderSession(browser);
        var first = await reader.ExecuteAsync(ReaderCommand.NextWord);
        browser.Snapshot = browser.Snapshot with
        {
            Frames = browser.Snapshot.Frames.Select(frame => frame.FrameId == "child"
            ? frame with { DocumentId = "replacement-document" } : frame).ToImmutableArray()
        };
        var current = await reader.ExecuteAsync(ReaderCommand.ReadCurrentText);
        Assert.Equal(first.TextRange, current.TextRange); Assert.Equal(ReaderOutcome.Observed, current.Outcome);
        var next = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal("words", next.Announcement); Assert.Equal(5, next.TextRange?.Start);
        Assert.Equal("root-document", next.TextRange?.DocumentId); Assert.Null(next.TextCursorReset); Assert.Empty(browser.Keys);
    }

    [Fact]
    public async Task HostedTraceRetainsOwnershipResetWithoutInventingTextContinuity()
    {
        var browser = new PortalTextBrowser(Snapshot("child", "child-document"));
        await using var sessions = new PortalReaderSessions(new ReaderProjectStub(), new FakeTimeProvider(), (_, _) => browser);
        var actor = PortalReaderSessionTests.Actor; var organization = PortalReaderSessionTests.Organization;
        var opened = await sessions.OpenAsync(actor, organization, default);
        await sessions.ObserveAsync(actor, organization, opened.SessionId, default);
        var initial = await sessions.CommandAsync(actor, organization, opened.SessionId, new("NextWord", "root-document"), default);
        Assert.Equal("child", initial.Step.TextRange?.FrameId);
        browser.Snapshot = ChangeOwner("frame");
        var changed = await sessions.CommandAsync(actor, organization, opened.SessionId, new("ReadCurrentText", "root-document"), default);
        Assert.Equal(ReaderOutcome.Unknown, changed.Step.Outcome); Assert.Null(changed.Step.TextRange);
        Assert.Equal(TextCursorResetReason.DocumentChanged, changed.Step.TextCursorReset);
        var trace = await sessions.TraceAsync(actor, organization, opened.SessionId, new(0, 20), default);
        Assert.Equal(2, trace.Entries.Length);
        Assert.Equal("child", trace.Entries[0].Step.TextRange?.FrameId);
        var reset = trace.Entries[1];
        Assert.Equal(trace.Entries[0].ResultState.Id, reset.RefreshedFromStateId);
        Assert.Equal("other", reset.Snapshot.Nodes.Single(node => node.Id == "text").FrameId);
        Assert.Equal(ReaderOutcome.Unknown, reset.Edge.Outcome); Assert.Equal(changed.Step, reset.Step);
        Assert.Null(reset.Step.TextRange); Assert.Equal(TextCursorResetReason.DocumentChanged, reset.Step.TextCursorReset);
        Assert.Equal(0, reset.Edge.BrowserKeystrokes); Assert.False(reset.Edge.InputMayHaveOccurred);
        Assert.False(trace.GoalVerified); Assert.False(trace.TraceTruncated);
    }

    private static AccessibilitySnapshot ChangeOwner(string change) => change switch
    {
        "frame" => Snapshot("other", "child-document"),
        "node-document" => Snapshot("child", "replacement-document"),
        "child-to-root" => Snapshot("main", "root-document"),
        _ => Snapshot("child", "child-document")
    };

    private static AccessibilitySnapshot Snapshot(string? frame, string? document, string childDocument = "child-document") =>
        new("root-document", [new("root", "RootWebArea", "", FrameId: "main", DocumentId: "root-document"),
            new("text", "StaticText", "Same words", ParentId: "root", Text: new("Same words", SemanticSource.ChromiumAccessibility),
                FrameId: frame, DocumentId: document)], [])
        {
            Frames = [new("main", null, null, "root-document", "root", FrameObservationStatus.Observed),
            new("child", "main", "owner", childDocument, "child-root", FrameObservationStatus.Observed),
            new("other", "main", "other-owner", "child-document", "other-root", FrameObservationStatus.Observed)]
        };

    private sealed class TextBrowser(AccessibilitySnapshot snapshot) : IScreenReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = snapshot;
        public List<string> Keys { get; } = [];
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default) { Keys.Add(key); return Task.CompletedTask; }
    }

    private sealed class PortalTextBrowser(AccessibilitySnapshot snapshot) : IPortalReaderBrowser
    {
        public AccessibilitySnapshot Snapshot { get; set; } = snapshot;
        public Task<AccessibilitySnapshot> CaptureAsync() => Task.FromResult(Snapshot);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
