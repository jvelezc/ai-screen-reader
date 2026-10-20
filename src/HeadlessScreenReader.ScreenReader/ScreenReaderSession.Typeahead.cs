using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.ScreenReader;

public sealed partial class ScreenReaderSession
{
    private async Task<ReaderStep> TypeaheadAsync(string prefix, CancellationToken token)
    {
        if (_mode != ReaderMode.Focus)
            return Result(ReaderOutcome.Unsupported, "Typeahead requires focus mode and actual supported listbox focus.");
        var context = ListboxTypeaheadPolicy.FocusContext(_snapshot!);
        if (context is null)
            return Result(ReaderOutcome.Unsupported, "Noneditable listbox focus and local option ownership must be observed.");
        var input = new ListboxKeyInput(prefix[..1], _snapshot!.DocumentId, context.ListboxNodeId, context.DocumentId, context.FrameId);
        if (!input.IsAuthorizedBy(_snapshot))
            return Result(ReaderOutcome.Blocked, "Observed listbox or focused option is disabled.")
                with
            { BarrierCode = "OBSERVED_DISABLED_CONTROL" };
        // A new request starts after the trusted fixture's 1000ms idle buffer reset.
        // No key, focus repair or injected widget state is used to clear its buffer.
        await Task.Delay(TimeSpan.FromMilliseconds(ListboxTypeaheadPolicy.ResetIdleMilliseconds), _time, token).ConfigureAwait(false);
        await RefreshAsync(token).ConfigureAwait(false);
        foreach (var character in prefix)
        {
            input = input with { Key = character.ToString() };
            if (!input.IsAuthorizedBy(_snapshot!))
                return Result(_completedKeystrokes == 0 ? ReaderOutcome.Denied : ReaderOutcome.Unknown,
                    "Typeahead scope or focus changed; remaining characters were not dispatched.",
                    errorCode: "TYPEAHEAD_SCOPE_CHANGED", inputMayHaveOccurred: _completedKeystrokes > 0)
                    with
                { Typeahead = new(prefix, _completedKeystrokes, context.ListboxNodeId, null, null) };
            token.ThrowIfCancellationRequested();
            _inputAttempted = true;
            _text.Clear();
            Record(ReaderEventKind.InputAttempted, input.Key, _snapshot!.FocusedNode?.Id);
            await _browser.PressListboxKeyAsync(input, token).ConfigureAwait(false);
            _completedKeystrokes++;
            Record(ReaderEventKind.InputCompleted, input.Key, _snapshot.FocusedNode?.Id);
            await RefreshAsync(token).ConfigureAwait(false);
            _cursorId = _snapshot!.EffectiveFocusedNode?.Id;
        }
        var sameScope = input.IsAuthorizedBy(_snapshot!);
        var focused = sameScope ? _snapshot!.EffectiveFocusedNode : null;
        bool? matches = focused is null ? null : focused.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        return Result(matches == true ? ReaderOutcome.Observed : ReaderOutcome.Unknown,
            matches == true ? Describe(focused) : "Typed prefix did not establish a matching focused option; result unknown.",
            inputMayHaveOccurred: matches != true)
            with
        { Typeahead = new(prefix, _completedKeystrokes, context.ListboxNodeId, focused?.Id, matches) };
    }
}
