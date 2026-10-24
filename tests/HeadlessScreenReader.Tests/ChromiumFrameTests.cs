using System.Text.Json;
using HeadlessScreenReader.Browser.Playwright;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

[Trait("Category", "Browser")]
public sealed class ChromiumFrameTests
{
    [Fact]
    public async Task Frames_Chromium_NestedOrderIdentityAndExcludedCoverageAreExplicit()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Frames);
        var snapshot = await browser.CaptureAsync();
        Assert.Equal(5, snapshot.Frames.Length);
        Assert.Equal(3, snapshot.Frames.Count(frame => frame.Status == FrameObservationStatus.Observed));
        Assert.Single(snapshot.Frames, frame => frame.Status == FrameObservationStatus.Excluded && frame.ReasonCode == "FRAME_OWNER_NOT_EXPOSED");
        Assert.Single(snapshot.Frames, frame => frame.Status == FrameObservationStatus.Unsupported && frame.ReasonCode == "FRAME_DOCUMENT_UNSUPPORTED");
        Assert.Equal(snapshot.Nodes.Length, snapshot.Nodes.Select(node => node.Id).Distinct().Count());
        Assert.All(snapshot.Nodes, node => Assert.Contains(snapshot.Frames,
            frame => frame.FrameId == node.FrameId && frame.DocumentId == node.DocumentId && frame.Status == FrameObservationStatus.Observed));
        foreach (var child in snapshot.Frames.Where(frame => frame.ParentFrameId is not null && frame.Status == FrameObservationStatus.Observed))
            Assert.Equal(child.OwnerNodeId, Assert.Single(snapshot.Nodes, node => node.Id == child.RootNodeId).ParentId);
        var json = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("frame-secret-do-not-read", json, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-frame-do-not-read", json, StringComparison.Ordinal);
        Assert.DoesNotContain("unsupported-frame-do-not-read", json, StringComparison.Ordinal);
        using var reader = new ScreenReaderSession(browser);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        foreach (var expected in new[] { "Frame fixture, heading level 1", "Child heading, heading level 2", "Nested heading, heading level 3", "After child, heading level 2" })
            Assert.Equal(expected, (await reader.ExecuteAsync(ReaderCommand.NextHeading)).Announcement);
        Assert.Equal(ReaderOutcome.NoMatch, (await reader.ExecuteAsync(ReaderCommand.NextHeading)).Outcome);
        Assert.Equal("Nested heading, heading level 3", (await reader.ExecuteAsync(ReaderCommand.PreviousHeading)).Announcement);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task Frames_Chromium_ChildFocusActivationAndActiveDescendantKeepOwners()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Frames);
        using var reader = new ScreenReaderSession(browser, maxCommands: 100);
        await TabToAsync(reader, browser, "Child action");
        var focused = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.NotNull(focused.Frame?.ParentFrameId);
        Assert.Equal(focused.CursorNodeId, focused.FocusedNodeId);
        Assert.Contains(focused.Context!.Ancestors, node => node.Role == "Iframe" && node.Name == "Preferences frame");
        var activated = await reader.ExecuteAsync(ReaderCommand.Activate, expectation: new("StaticText", "Child complete"));
        Assert.Equal(VerificationStatus.Satisfied, activated.Verification);
        Assert.Equal(1, activated.BrowserKeystrokes);
        Assert.Equal(focused.Frame, activated.Frame);
        var live = Assert.Single(reader.Timeline, item => item.Kind == ReaderEventKind.LiveRegionChanged && item.Detail == "Child complete");
        Assert.Equal(focused.Frame, live.Frame);
        await TabToAsync(reader, browser, "Child region");
        var south = await reader.ExecuteAsync(ReaderCommand.PressKey, "ArrowDown");
        Assert.Equal("Child South, option", south.Announcement);
        Assert.NotEqual(south.FocusedNodeId, south.CursorNodeId);
        Assert.Equal(south.ActiveDescendantNodeId, south.CursorNodeId);
        Assert.Equal(focused.Frame, south.Frame);
        Assert.Equal(2, south.Context?.ExposedPeers?.Count);
        await TabToAsync(reader, browser, "Nested action");
        var nested = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(focused.Frame?.FrameId, nested.Frame?.ParentFrameId);
        Assert.NotEqual(focused.Frame?.DocumentId, nested.Frame?.DocumentId);
        Assert.Equal(nested.FocusedNodeId, nested.CursorNodeId);
    }

    [Fact]
    public async Task Frames_Chromium_RadioPeersDoNotCrossDocumentBoundary()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Frames);
        using var reader = new ScreenReaderSession(browser, maxCommands: 100);
        await TabToAsync(reader, browser, "Ungrouped child choice");
        var child = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Null(child.Context?.ExposedPeers);
        await TabToAsync(reader, browser, "Child first");
        var grouped = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(1, grouped.Context?.ExposedPeers?.Position);
        Assert.Equal(2, grouped.Context?.ExposedPeers?.Count);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task Frames_Chromium_BudgetsRetainCoverageWithoutPretendingChildWasRead(bool depthLimit, int observed)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Frames,
            frameOptions: depthLimit ? new(MaxDepth: observed - 1) : new(MaxFrames: observed));
        var snapshot = await browser.CaptureAsync();
        Assert.Equal(5, snapshot.Frames.Length);
        Assert.Equal(observed, snapshot.Frames.Count(frame => frame.Status == FrameObservationStatus.Observed));
        Assert.Contains(snapshot.Frames, frame => frame.Status == FrameObservationStatus.LimitExceeded && frame.ReasonCode == "FRAME_CAPTURE_LIMIT");
        Assert.Equal(observed == 2, snapshot.Nodes.Any(node => node.Role == "heading" && node.Name == "Child heading"));
        Assert.DoesNotContain(snapshot.Nodes, node => node.Role == "heading" && node.Name == "Nested heading");
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(ReaderCommand.ReadCurrent);
        Assert.Equal(snapshot.Frames.ToArray(), step.FrameCoverage.ToArray());
        Assert.Null(step.BarrierCode);
        Assert.False(new ObservationExpectation("heading", "Nested heading").IsSatisfiedBy(snapshot));
    }

    [Fact]
    public async Task FrameCapture_Chromium_MaximumValidBounds_AreReported()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(frameOptions: new(32, 8));
        Assert.Equal(new FrameCaptureOptions(32, 8), browser.Environment.FrameLimits);
        Assert.Equal(FrameObservationStatus.Observed, Assert.Single((await browser.CaptureAsync()).Frames).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Frames_Chromium_ReplacementOrRemovalInvalidatesChildTextWithoutResettingMain(bool remove)
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Frames);
        using var reader = new ScreenReaderSession(browser, maxCommands: 100);
        var initial = await browser.CaptureAsync();
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        await reader.ExecuteAsync(ReaderCommand.NextHeading);
        await reader.ExecuteAsync(ReaderCommand.NextHeading);
        var text = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal("Child", text.Announcement);
        Assert.NotEqual(initial.DocumentId, text.TextRange?.DocumentId);
        var oldFrame = text.Frame!;
        await ExternalTabToAsync(browser, remove ? "Remove child" : "Replace child");
        await browser.PressKeyAsync("Enter");
        AccessibilitySnapshot? settled = null;
        for (var i = 0; i < 10; i++)
        {
            try
            {
                var candidate = await browser.CaptureAsync();
                if (remove ? candidate.Frames.All(frame => frame.FrameId != oldFrame.FrameId)
                    : candidate.Nodes.Any(node => node.Name == "Replacement heading")) { settled = candidate; break; }
            }
            catch (BrowserOperationException exception) when (exception.Code == "FRAME_TREE_CHANGED") { }
        }
        Assert.NotNull(settled);
        Assert.Equal(initial.DocumentId, settled!.DocumentId);
        var invalidated = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal(ReaderOutcome.Unknown, invalidated.Outcome);
        Assert.Equal(remove ? TextCursorResetReason.NodeRemoved : TextCursorResetReason.DocumentChanged, invalidated.TextCursorReset);
        Assert.Null(invalidated.TextRange);
        Assert.DoesNotContain(settled.Nodes, node => node.Id == text.CursorNodeId);
        var stillInvalid = await reader.ExecuteAsync(ReaderCommand.ReadCurrentText);
        Assert.Equal(ReaderOutcome.Unknown, stillInvalid.Outcome);
        Assert.Equal(invalidated.TextCursorReset, stillInvalid.TextCursorReset);
        Assert.Null(stillInvalid.TextRange);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        var anchored = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal("Frame", anchored.Announcement);
        Assert.Equal(initial.DocumentId, anchored.TextRange?.DocumentId);
        Assert.Equal(0, anchored.TextRange?.Start); Assert.Null(anchored.TextCursorReset);
        Assert.Contains(reader.Timeline, item => item.Kind == ReaderEventKind.FrameChanged &&
            (remove ? item.Frame?.ReasonCode == "FRAME_DETACHED" : item.Frame?.FrameId == oldFrame.FrameId && item.Frame.DocumentId != oldFrame.DocumentId));
        Assert.DoesNotContain(reader.Timeline, item => item.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public async Task Frames_Chromium_ExplicitTextAnchorsKeepCurrentParentAndChildProvenance()
    {
        await using var browser = await ChromiumFixtureBrowser.LaunchAsync(fixture: BundledFixture.Frames);
        using var reader = new ScreenReaderSession(browser);
        var snapshot = await browser.CaptureAsync();
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        var parent = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal("Frame", parent.Announcement); Assert.Equal(snapshot.DocumentId, parent.TextRange?.DocumentId);
        Assert.Equal(snapshot.Frames.Single(frame => frame.ParentFrameId is null).FrameId, parent.TextRange?.FrameId);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        await reader.ExecuteAsync(ReaderCommand.NextHeading);
        await reader.ExecuteAsync(ReaderCommand.NextHeading);
        var child = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal("Child", child.Announcement); Assert.Equal(0, child.TextRange?.Start);
        Assert.NotEqual(parent.TextRange?.DocumentId, child.TextRange?.DocumentId);
        Assert.NotEqual(parent.TextRange?.FrameId, child.TextRange?.FrameId);
        Assert.Equal(child.TextRange, (await reader.ExecuteAsync(ReaderCommand.ReadCurrentText)).TextRange);
        var next = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal("heading", next.Announcement); Assert.Equal(6, next.TextRange?.Start);
        Assert.Equal(child.TextRange?.DocumentId, next.TextRange?.DocumentId);
        Assert.Equal(child.TextRange?.FrameId, next.TextRange?.FrameId);
        await reader.ExecuteAsync(ReaderCommand.DocumentStart);
        var returned = await reader.ExecuteAsync(ReaderCommand.NextWord);
        Assert.Equal(parent.TextRange, returned.TextRange); Assert.Null(returned.TextCursorReset);
        Assert.DoesNotContain(reader.Timeline, item => item.Kind is ReaderEventKind.InputAttempted or ReaderEventKind.GoalVerified);
    }

    private static async Task TabToAsync(ScreenReaderSession reader, IScreenReaderBrowser browser, string name)
    {
        for (var i = 0; i < 20; i++)
        {
            await reader.ExecuteAsync(ReaderCommand.Tab);
            if ((await browser.CaptureAsync()).FocusedNode?.Name == name) return;
        }
        Assert.Fail($"Focus did not reach {name}.");
    }

    private static async Task ExternalTabToAsync(IScreenReaderBrowser browser, string name)
    {
        for (var i = 0; i < 20; i++)
        {
            await browser.PressKeyAsync("Tab");
            if ((await browser.CaptureAsync()).FocusedNode?.Name == name) return;
        }
        Assert.Fail($"External focus did not reach {name}.");
    }
}
