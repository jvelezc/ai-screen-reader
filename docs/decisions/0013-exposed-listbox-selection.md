# 0013: Bounded exposed multi-select listboxes

Status: accepted for reader profile hsr-desktop-reader-v12.

Observe Chromium AX multiselectable and selected properties with explicit provenance. Preserve the older Selected scalar for existing consumers; the new selection context only treats it as known when it agrees with the source-tagged Selection observation. Missing or conflicting data remains unknown.

ListboxSelectionContext contains only exposed options whose nearest listbox is established by an acyclic observed ancestor chain with matching, known document and frame identities. Groups are supported; root boundaries, nested options, missing ancestors and foreign identities cannot establish ownership. The context always declares ExposedOptionsOnly. Its counts and exact-set expectations do not establish the complete logical selection of virtual or hidden options.

Next/PreviousListbox traverses exposed listboxes. Next/PreviousOption stays within the current known listbox. These commands require browse mode, do not wrap, dispatch no input and never alter focus or selection. ReadCurrent and resulting steps/events carry the current exposed selection context. Existing single-select announcements and combobox Enter activation retain their behavior.

Activate dispatches one Space only for an observed multi-select option with known selected state and actual effective focus. Supported focus arrangements are a focused listbox with that locally owned active descendant, or the focused option itself. Disabled options/owners block input. Missing/stale/foreign ownership and unknown selection/mode are unsupported. Activation never repairs focus. Success means keyboard dispatch and fresh observation; an optional identity-scoped ExposedSelectionExpectation verifies the exact exposed set. Failed verification returns Unknown and never retries input. Explicit raw keys retain the preexisting focus-mode allowlist contract.

The trusted fixture demonstrates active-descendant and roving-focus ARIA listboxes. It is not a claim about native select-multiple modifier behavior, arbitrary custom widgets, typeahead, asynchronous loading, cross-target documents, speech equivalence or accessibility conformance. The browser contract, dependency direction and closed arbitrary-site/privacy gates remain unchanged.
