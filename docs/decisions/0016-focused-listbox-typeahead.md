# 0016: Bounded focused listbox prefix input

Reader profile hsr-desktop-reader-v13 adds the explicit Typeahead command. Its key argument is a complete prefix of 1..16 ASCII letters or digits, normalized to lowercase invariantly. Spaces, modifiers, punctuation, Unicode letters and longer strings are denied before capture/input. BrowserKeyPolicy's ordinary key allowlist remains unchanged.

Each request waits 1100ms without input before starting a fresh prefix. The dedicated trusted fixture resets its buffer after 1000ms idle. This is a bounded fixture/profile timing contract; other widgets can use different timing. The reset wait is cancellable and uses the session's existing TimeProvider. The reader does not edit the widget buffer or change focus to prepare input.

Typing requires focus mode, actual noneditable listbox/option focus, an observed locally owned effective option, known document/frame identities, complete noneditable ancestry and enabled owner/current option. Active-descendant and roving-option models are supported. The snapshot document, listbox, child document and frame are pinned before the idle wait and checked again before every character. A specialized Domain ListboxKeyInput contract defaults to denial for unsupported adapters. The Chromium adapter and MCP input gate independently revalidate each request before the physical key.

Every character has separate InputAttempted/InputCompleted evidence and contributes to BrowserKeystrokes. Changed scope/focus, cancellation, policy denial and browser failure stop further dispatch. Partial completion retains the completed count and input uncertainty. A policy denial is not an accessibility barrier. Failure to observe a supported focused prefix match yields Unknown; this does not prove there is no matching hidden or virtual option.

TypeaheadObservation reports the normalized requested prefix, completed characters and immediately observed local effective option/name-prefix match. It does not establish the widget's internal buffer, selection or goal completion. ListboxSelection remains separately source-tagged. A widget can preserve selection or change it as focus moves; the reader reports its observation. An optional explicit goal is verified separately and may observe later state.

The [WAI-ARIA listbox pattern](https://www.w3.org/WAI/ARIA/apg/patterns/listbox/) recommends character/prefix navigation and distinguishes DOM focus, active descendant and selected state. Character bounds, idle timing and supported fixture scope are custom choices, not universal ARIA requirements or an NVDA-equivalence claim.

Public pages continue to permit traversal and focused-link Enter only. Both the MCP guard and public Chromium adapter reject typed-listbox input. Editable entry, credentials and recording remain unavailable. Tree typeahead, asynchronous loading, native select-multiple modifiers and arbitrary public prefix typing remain outside this increment.
