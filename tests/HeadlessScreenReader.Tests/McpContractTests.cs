using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class McpContractTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("../../secret")]
    [InlineData("0")]
    [InlineData("foundation")]
    public async Task FixtureOpen_RejectsUrlsPathsNumbersAndUnknownNames(string fixture)
    {
        await using var workspace = new ReaderWorkspace(TimeProvider.System);
        var reply = await workspace.OpenFixtureAsync(fixture, false);
        Assert.Equal("FIXTURE_UNSUPPORTED", reply.ErrorCode);
        Assert.Null(reply.SessionId);
        Assert.Equal("error", reply.Status);
    }

    [Theory]
    [InlineData(-1, 80)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task SnapshotBounds_RejectInvalidPagesBeforeOpeningBrowser(int offset, int limit)
    {
        await using var workspace = new ReaderWorkspace(TimeProvider.System);
        Assert.Equal("PAGE_BOUNDS_INVALID", (await workspace.SnapshotAsync("absent", offset, limit)).ErrorCode);
    }

    [Fact]
    public async Task Workspace_RejectsUnboundedObservationAndUnknownCommandAndMissingSession()
    {
        await using var workspace = new ReaderWorkspace(TimeProvider.System);
        Assert.Equal("OBSERVATION_BOUNDS_INVALID", (await workspace.ObserveAsync("absent", 2001)).ErrorCode);
        Assert.Equal("OBSERVATION_BOUNDS_INVALID", (await workspace.ObserveAsync("absent", 500, 11)).ErrorCode);
        Assert.Equal("COMMAND_UNSUPPORTED", (await workspace.ExecuteAsync("absent", "doc", "0")).ErrorCode);
        Assert.Equal("DOCUMENT_REQUIRED", (await workspace.ExecuteAsync("absent", "", "Activate")).ErrorCode);
        Assert.Equal("SESSION_NOT_FOUND", (await workspace.SnapshotAsync("absent")).ErrorCode);
        Assert.Equal("SESSION_NOT_FOUND", (await workspace.CloseAsync("absent")).ErrorCode);
    }

    [Theory]
    [InlineData(ReaderCommand.Activate)]
    [InlineData(ReaderCommand.Tab)]
    [InlineData(ReaderCommand.PressKey)]
    public async Task DocumentPrecondition_RejectsChangedDocumentBeforeInput(ReaderCommand command)
    {
        var browser = new DocumentBrowser();
        using var reader = new ScreenReaderSession(browser);
        var step = await reader.ExecuteAsync(command, key: command == ReaderCommand.PressKey ? "Enter" : null,
            expectedDocumentId: "old");
        Assert.Equal(ReaderOutcome.Denied, step.Outcome);
        Assert.Equal("DOCUMENT_CHANGED", step.ErrorCode);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.False(step.InputMayHaveOccurred);
        Assert.Equal(0, browser.Keys);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputAttempted);
    }

    [Fact]
    public void OversizedReply_PreservesInputUncertaintyWithinResponseBudget()
    {
        var step = new ReaderStep(1, ReaderCommand.Activate, ReaderMode.Focus, ReaderOutcome.Unknown,
            new string('x', 140_000), "button", "button", 1, InputMayHaveOccurred: true,
            Verification: VerificationStatus.TimedOut);
        var json = new ToolReply("step", "session", ScreenReaderSession.Profile, step).ToJson();
        Assert.True(Encoding.UTF8.GetByteCount(json) < 131_072);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("error", root.GetProperty("status").GetString());
        Assert.Equal("RESPONSE_TOO_LARGE", root.GetProperty("errorCode").GetString());
        Assert.True(root.GetProperty("data").GetProperty("inputMayHaveOccurred").GetBoolean());
        Assert.Equal(1, root.GetProperty("data").GetProperty("browserKeystrokes").GetInt32());
        Assert.Equal("TimedOut", root.GetProperty("data").GetProperty("verification").GetString());
    }

    [Fact]
    public async Task ToolErrors_SetProtocolErrorAndStructuredContent()
    {
        await using var workspace = new ReaderWorkspace(TimeProvider.System);
        var tools = new ReaderTools(workspace);
        var result = await tools.OpenFixture("unsupported");
        Assert.True(result.IsError);
        Assert.Equal("FIXTURE_UNSUPPORTED", result.StructuredContent!.Value.GetProperty("errorCode").GetString());
        Assert.False(tools.Status().IsError);
    }

    private sealed class DocumentBrowser : IScreenReaderBrowser
    {
        public int Keys { get; private set; }
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccessibilitySnapshot("new", ImmutableArray.Create(
                new AccessibleNode("button", "button", "Open", IsFocused: true)), []));
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        { Keys++; return Task.CompletedTask; }
    }
}
