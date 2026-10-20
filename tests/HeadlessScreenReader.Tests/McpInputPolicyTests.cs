using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;
using HeadlessScreenReader.ScreenReader;

namespace HeadlessScreenReader.Tests;

public sealed class McpInputPolicyTests
{
    [Theory]
    [InlineData("textbox")]
    [InlineData("searchbox")]
    [InlineData("spinbutton")]
    [InlineData("RootWebArea")]
    public async Task EditableOrUnsupportedFocus_IsPolicyDeniedWithoutDispatchOrBarrier(string role)
    {
        var inner = new PolicyBrowser(new AccessibleNode("field", role, "Field", IsFocused: true));
        using var reader = new ScreenReaderSession(new NoEditableEntryBrowser(inner));
        await reader.ExecuteAsync(ReaderCommand.EnterFocusMode);
        var step = await reader.ExecuteAsync(ReaderCommand.PressKey, "Space");
        Assert.Equal(ReaderOutcome.Denied, step.Outcome);
        Assert.Equal("EDITABLE_OR_UNSUPPORTED_INPUT_DENIED", step.ErrorCode);
        Assert.False(step.InputMayHaveOccurred);
        Assert.Equal(0, step.BrowserKeystrokes);
        Assert.Null(step.BarrierCode);
        Assert.Empty(inner.Keys);
        Assert.Contains(reader.Timeline, e => e.Kind == ReaderEventKind.InputDenied);
        Assert.DoesNotContain(reader.Timeline, e => e.Kind == ReaderEventKind.InputCompleted || e.Kind == ReaderEventKind.BrowserError);
    }

    [Theory]
    [InlineData("h")]
    [InlineData("k")]
    [InlineData("b")]
    [InlineData("d")]
    public async Task PrintableRoutingKeys_CannotBecomeTextEntry(string key)
    {
        var inner = new PolicyBrowser(new AccessibleNode("field", "textbox", "Field", IsFocused: true));
        var guard = new NoEditableEntryBrowser(inner);
        var exception = await Assert.ThrowsAsync<BrowserInputDeniedException>(() => guard.PressKeyAsync(key));
        Assert.Equal("PRINTABLE_INPUT_DENIED", exception.Code);
        Assert.Empty(inner.Keys);
    }

    [Theory]
    [InlineData("plaintext")]
    [InlineData("richtext")]
    public async Task EditableRoleOverride_CannotBypassInputGate(string kind)
    {
        var inner = new PolicyBrowser(new AccessibleNode("field", "button", "Field", IsFocused: true,
            Editability: new(kind, SemanticSource.ChromiumAccessibility)));
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => new NoEditableEntryBrowser(inner).PressKeyAsync("Enter"));
        Assert.Empty(inner.Keys);
    }

    [Theory]
    [InlineData("Tab")]
    [InlineData("Shift+Tab")]
    [InlineData("Escape")]
    public async Task TraversalKeys_CanLeaveEditableControlsWithoutTyping(string key)
    {
        var inner = new PolicyBrowser(new AccessibleNode("field", "textbox", "Field", IsFocused: true,
            Editability: new("plaintext", SemanticSource.ChromiumAccessibility)));
        await new NoEditableEntryBrowser(inner).PressKeyAsync(key);
        Assert.Equal(new[] { key }, inner.Keys);
    }

    [Fact]
    public async Task EditableActiveDescendant_IsRejectedEvenWithNoneditableOwner()
    {
        var inner = new PolicyBrowser(new AccessibleNode("owner", "listbox", "Owner", IsFocused: true, ActiveDescendantId: "field"),
            new("field", "option", "Field", Editability: new("plaintext", SemanticSource.ChromiumAccessibility)));
        await Assert.ThrowsAsync<BrowserInputDeniedException>(() => new NoEditableEntryBrowser(inner).PressKeyAsync("Space"));
        Assert.Empty(inner.Keys);
    }

    [Theory]
    [InlineData("button", "Enter", false)]
    [InlineData("checkbox", "Space", false)]
    [InlineData("link", "Space", false)]
    [InlineData("link", "Enter", true)]
    public async Task PublicPages_OnlyFocusedLinkEnterCanDispatchBeyondTraversal(string role, string key, bool allowed)
    {
        var inner = new PolicyBrowser(new AccessibleNode("focused", role, "Target", IsFocused: true));
        var guard = new NoEditableEntryBrowser(inner, linksOnly: true);
        if (allowed)
        {
            await guard.PressKeyAsync(key);
            Assert.Equal(new[] { key }, inner.Keys);
        }
        else
        {
            var denied = await Assert.ThrowsAsync<BrowserInputDeniedException>(() => guard.PressKeyAsync(key));
            Assert.Equal("PUBLIC_PAGE_READ_ONLY_INPUT_DENIED", denied.Code);
            Assert.Empty(inner.Keys);
        }
    }

    private sealed class PolicyBrowser(params AccessibleNode[] nodes) : IScreenReaderBrowser
    {
        public List<string> Keys { get; } = [];
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccessibilitySnapshot("doc", [.. nodes], []));
        public Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
        { Keys.Add(key); return Task.CompletedTask; }
    }
}

