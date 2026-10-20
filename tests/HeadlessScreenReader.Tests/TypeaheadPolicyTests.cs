using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Mcp;

namespace HeadlessScreenReader.Tests;

public sealed class TypeaheadPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopedInput_AuthorizesOnlyActualOwnedActiveOrRovingFocus(bool roving)
    {
        var browser = new TypeaheadBrowser();
        if (roving)
        {
            browser.Replace("list", n => n with { IsFocused = false, ActiveDescendantId = null });
            browser.Replace("a", n => n with { IsFocused = true });
        }
        var input = new ListboxKeyInput("p", "doc", "list", "doc", "frame");
        Assert.True(input.IsAuthorizedBy(browser.Snapshot));
        Assert.False((input with { SnapshotDocumentId = "stale" }).IsAuthorizedBy(browser.Snapshot));
        Assert.False((input with { ListboxNodeId = "foreign" }).IsAuthorizedBy(browser.Snapshot));
        Assert.False((input with { DocumentId = "stale-child" }).IsAuthorizedBy(browser.Snapshot));
        Assert.False((input with { FrameId = "foreign-frame" }).IsAuthorizedBy(browser.Snapshot));
        Assert.False((input with { Key = "pe" }).IsAuthorizedBy(browser.Snapshot));
        Assert.False(BrowserKeyPolicy.Allows("p"));
        Assert.False(BrowserKeyPolicy.Allows("pe"));
    }

    [Theory]
    [InlineData("missing-active")]
    [InlineData("foreign-active")]
    [InlineData("frame")]
    [InlineData("document")]
    [InlineData("missing-parent")]
    [InlineData("cycle")]
    [InlineData("editable-option")]
    [InlineData("editable-owner")]
    [InlineData("editable-ancestor")]
    [InlineData("textbox-focus")]
    [InlineData("combobox-focus")]
    [InlineData("missing-identity")]
    public async Task UnsupportedOwnershipOrEditableFocus_CannotReachTypedInput(string caseId)
    {
        var browser = new TypeaheadBrowser();
        switch (caseId)
        {
            case "missing-active": browser.Replace("list", n => n with { ActiveDescendantId = null }); break;
            case "foreign-active": browser.Replace("a", n => n with { ParentId = "root" }); break;
            case "frame": browser.Replace("a", n => n with { FrameId = "foreign" }); break;
            case "document": browser.Replace("a", n => n with { DocumentId = "foreign" }); break;
            case "missing-parent": browser.Replace("list", n => n with { ParentId = "absent" }); break;
            case "cycle": browser.Replace("root", n => n with { Role = "generic", ParentId = "list" }); break;
            case "editable-option": browser.Replace("a", n => n with { Editability = new("plaintext", SemanticSource.ChromiumAccessibility) }); break;
            case "editable-owner": browser.Replace("list", n => n with { Editability = new("richtext", SemanticSource.ChromiumAccessibility) }); break;
            case "editable-ancestor": browser.Replace("root", n => n with { Editability = new("richtext", SemanticSource.ChromiumAccessibility) }); break;
            case "textbox-focus": browser.Replace("list", n => n with { Role = "textbox" }); break;
            case "combobox-focus": browser.Replace("list", n => n with { Role = "combobox" }); break;
            case "missing-identity": browser.Replace("a", n => n with { DocumentId = null }); break;
        }
        Assert.Null(ListboxTypeaheadPolicy.FocusContext(browser.Snapshot));
        var exception = await Assert.ThrowsAsync<BrowserInputDeniedException>(() => new NoEditableEntryBrowser(browser)
            .PressListboxKeyAsync(new("p", "doc", "list", "doc", "frame")));
        Assert.Equal("TYPEAHEAD_SCOPE_CHANGED", exception.Code);
        Assert.Empty(browser.Keys);
        Assert.Empty(browser.RawKeys);
    }

    [Fact]
    public async Task PublicReadOnlyPolicy_DeniesTypedListboxInputEvenWithValidFocus()
    {
        var browser = new TypeaheadBrowser();
        var exception = await Assert.ThrowsAsync<BrowserInputDeniedException>(() => new NoEditableEntryBrowser(browser, linksOnly: true)
            .PressListboxKeyAsync(new("p", "doc", "list", "doc", "frame")));
        Assert.Equal("PUBLIC_PAGE_READ_ONLY_INPUT_DENIED", exception.Code);
        Assert.Empty(browser.Keys);
        Assert.Equal(0, browser.Captures);
    }

    [Fact]
    public async Task BrowserContract_DefaultsToDeniedForAdaptersWithoutTypedInput()
    {
        IScreenReaderBrowser browser = new UnsupportedBrowser();
        var exception = await Assert.ThrowsAsync<BrowserInputDeniedException>(() => browser.PressListboxKeyAsync(new("p", "doc", "list", "doc", "frame")));
        Assert.Equal("TYPEAHEAD_NOT_SUPPORTED", exception.Code);
    }

    private sealed class UnsupportedBrowser : IScreenReaderBrowser
    {
        public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task PressKeyAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
    }
}
