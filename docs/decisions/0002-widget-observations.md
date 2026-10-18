# Widget observations and bounded destination verification

Date: October 17, 2026  
Status: Accepted for the second local increment

## Decision

Extend the existing Domain observations and reader session rather than adding projects. Keep DOM focus, active-descendant focus, checked/selected/expanded states, validation, descriptions, and modal state explicit. Chromium normalizes exposed AX data; a fixed read-only document.activeElement observation identifies the actual DOM focus owner. No application locator, injected focus, or arbitrary evaluation enters the reader contract.

The engine interprets active-descendant semantics and sends allowed keys to the DOM owner. The bundled website implements its own widget behavior. Modal focus containment and restoration are normal successful behavior for this fixture, not inferred keyboard traps.

Verify requested destinations with an explicit predicate, a monotonic TimeProvider deadline, and a poll budget. Input dispatch is awaited once; subsequent polls are read-only. A successful key API call is separate from destination observation. Timeout or interrupted observation preserves uncertainty. Only an observed disabled control produces the current route-scoped Blocked classification. Engine errors and policy denials have separate outcomes.

Keep an ordered, bounded, in-memory event timeline linked to command sequences. Record input attempts and completions, focus and widget changes, observed dialogs, verification, announcements, and final outcomes. Report truncation explicitly. Do not introduce persistence, recordings, live-region speech arbitration, or a generic workflow framework in this increment.

## Consequences and limits

Deterministic fake-time tests can prove deadline and cancellation behavior, while real Chromium tests validate the exposed widget observations. These are complementary gates. Neither establishes real assistive-technology compatibility.

The Chromium adapter coalesces identical repeated AX node records observed for synthetic list markers. Conflicting records with the same identity fail with AX_NODE_ID_CONFLICT, rather than choosing an arbitrary interpretation.

Predicate success establishes the observed state, not causality or stability across arbitrary future changes. Captures combine AX and DOM-focus reads and are not atomic browser transactions. Input values remain omitted, but accessible text can contain sensitive data; a full capture exclusion protocol remains required before credentials or recordings.

## References

- [WAI-ARIA modal dialog pattern](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/)
- [WAI-ARIA combobox pattern](https://www.w3.org/WAI/ARIA/apg/patterns/combobox/)
- [WAI-ARIA radio group pattern](https://www.w3.org/WAI/ARIA/apg/patterns/radio/)
