using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Mcp;

/// <summary>Prototype input gate. This does not establish a capture-exclusion protocol.</summary>
public sealed class NoEditableEntryBrowser(IScreenReaderBrowser inner, bool linksOnly = false) : IScreenReaderBrowser
{
    public Task<AccessibilitySnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        inner.CaptureAsync(cancellationToken);

    public async Task PressListboxKeyAsync(ListboxKeyInput input, CancellationToken cancellationToken = default)
    {
        if (linksOnly) throw new BrowserInputDeniedException("PUBLIC_PAGE_READ_ONLY_INPUT_DENIED");
        if (!input.IsAuthorizedBy(await inner.CaptureAsync(cancellationToken).ConfigureAwait(false)))
            throw new BrowserInputDeniedException("TYPEAHEAD_SCOPE_CHANGED");
        await inner.PressListboxKeyAsync(input, cancellationToken).ConfigureAwait(false);
    }

    public async Task PressKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (key is "Tab" or "Shift+Tab" or "Escape")
        {
            await inner.PressKeyAsync(key, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (key is not ("Enter" or "Space" or "ArrowUp" or "ArrowDown" or "ArrowLeft" or "ArrowRight" or "Home" or "End"))
            throw new BrowserInputDeniedException("PRINTABLE_INPUT_DENIED");
        var snapshot = await inner.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var owner = snapshot.FocusedNode;
        var effective = snapshot.EffectiveFocusedNode;
        if (owner?.Editability is not null || effective?.Editability is not null
            || owner?.Role is not ("button" or "link" or "menuitem" or "checkbox" or "radio" or "switch"
                or "slider" or "tree" or "treeitem" or "tab" or "tablist" or "listbox" or "option" or "combobox"))
            throw new BrowserInputDeniedException("EDITABLE_OR_UNSUPPORTED_INPUT_DENIED");
        if (linksOnly && (key != "Enter" || owner.Role != "link" || effective?.Id != owner.Id))
            throw new BrowserInputDeniedException("PUBLIC_PAGE_READ_ONLY_INPUT_DENIED");
        await inner.PressKeyAsync(key, cancellationToken).ConfigureAwait(false);
    }
}
