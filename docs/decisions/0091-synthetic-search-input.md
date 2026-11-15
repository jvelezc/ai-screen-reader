# 0091: A separate fixed-query search input capability

The next public journey needs genuine text entry. Existing listbox typeahead and printable routing shortcuts cannot safely supply a general editable-input channel. The first search increment therefore authorizes only the nonpersonal literal `sample course` on two new trusted embedded fixtures: `MockSearchWorkingV1` and `MockSearchKeyboardBlockedV1`.

Domain declares `ISyntheticSearchBrowser` separately from `IScreenReaderBrowser`. Existing browser implementers and signatures stay unchanged; an adapter without the new capability remains unsupported. `EnterSearchText` is appended to the command enum, preserving existing numeric values. The base reader profile remains v13; the opt-in result declares `hsr-synthetic-search-v1`. `ReaderStep.SearchInput` is omitted when absent, preserving the exact shape of existing command responses.

The reader uses its existing whole-command gate and capture admission. Each native character binds the original document, capture epoch and focused search node. The browser independently refreshes that scope, requires the exact embedded source, native input type search, writable state and the expected already-entered prefix. Existing text cannot be cleared or overwritten. A changed scope or value stops the command; uncertain input is never repeated. After all thirteen characters, an independent native value check establishes entry only. Submission and destination verification require separate actions and observations.

The MCP fixture wrapper forwards only this declared capability. Public-page wrappers and the native public browser deny it; all other fixtures deny it as well. `reader_status` and the tool description identify the fixed query, exact sources and absence of public entry/submission authority. No new credentials, general text, public navigation policy, queue, hosted task admission or recording channel is introduced.

Actual Chromium checks compare native keyboard activation with a fresh mouse activation on both sources. The deliberately broken control accepts a click but has no Enter/Space handler. These authored expectations establish reproducible synthetic behavior, not a finding about a public website or a sufficient evidence package. Recorded search attempts, same-task graph/video, public search authorization and the separate consumer connection remain subsequent gates.

The bounded traversal runner forwards only the explicit native search capability and retains the text command as one graph edge with thirteen ordered input attempt/completion pairs. Enter and Space plans are authored separately. Graphs preserve actual state observations and missing recordings; they do not supply a video or imply that a hidden editable value was exposed to the reader. Hosted read-only schemas and their closed command allowlist exclude this fixture extension.

Validation and delivery state: [synthetic search input](../validation/synthetic-search-input-v1.md).
