# ADR 0005: explicit semantic sources and bounded idle observation

Status: accepted October 18, 2026. Profile: hsr-desktop-reader-v4.

The user scoped this increment to reader gaps, excluding capture privacy/MCP/hosting. The earlier comparison found that this Chromium AX source omits checkbox required state, and v3 had only exposed-peer counts, forward semantic navigation and command-triggered captures.

## Semantic sources

AX remains authoritative when it supplies required state. Where absent, a fixed read-only function on the exposed AX node's associated DOM object reads only standard semantic properties: native checkbox required, explicit aria-required, aria-posinset/aria-setsize, and native password type for protected-edit identification. Handles are released in finally blocks. There is no application selector, focus injection, locator activation, arbitrary script entry point, editable value read or guessed semantic label in the reader contract.

RequiredObservation and ProtectedObservation carry a Boolean and source. Null means unobserved; an explicit false remains distinguishable. ReaderStep exposes these records directly. AX names/descriptions/states otherwise remain source observations; outer name whitespace is normalized. Native required changes appear in widget-state events.

This narrowly supersedes ADR 0004's decision to leave native checkbox required unobserved. Direct standard semantic properties are an additional named source, separate from diagnostic feature inference. No CSS class, placeholder, validation message or attribute heuristic is used to invent a required state. Captures combine multiple read-only observations and are not atomic browser transactions; a stale/unavailable target produces an engine error.

LogicalSetPosition records author-declared position and size separately from ExposedPeers. Values must be positive integers, except size -1 means unknown. Partial declarations preserve the absent counterpart. Malformed/nonpositive/inconsistent declarations are marked invalid and not spoken. Declared size is not a verified inventory denominator. Structural peer counts remain limited to currently exposed controls.

These choices follow the [HTML required property](https://html.spec.whatwg.org/multipage/input.html#the-required-attribute) and [ARIA position/set-size semantics](https://www.w3.org/TR/wai-aria-1.2/#aria-posinset). They do not claim that an additional DOM observation was present in the AX response or independently validates author declarations.

## Navigation and idle observation

PreviousHeading/Link/Button/Landmark and DocumentStart/End are browse-only commands. Backward navigation from an unset cursor starts at the last match, does not wrap, and sends no input. Document boundaries refer to the first/last exposed nodes. Named/exposed form roles participate in landmark navigation. Focus-mode use of the new browse-only commands remains explicit Unsupported; existing H/K/B/D forwarding and activation policy are preserved.

ObserveChangesAsync is an explicitly started IAsyncEnumerable of typed batches. It captures while idle, serializes each capture with commands through the existing controller gate, releases the gate before yielding, and never sends input or verifies a goal. It uses the injected TimeProvider, bounds duration/interval/capture count, and shares the session command budget. Captures count completed captures; failed attempts still consume command budget.

Terminal states distinguish capture limit, duration, caller cancellation, command-budget exhaustion, browser failure and timeline truncation. A failed/interrupted capture closes its command record. Cancellation while waiting for the gate or next interval does not cancel another command's input. Consumers must finish/dispose active enumeration before disposing the session, consistent with the existing single-controller lifecycle.

This is a bounded sampled stream, not a lossless browser mutation feed or human speech scheduler. SamplingMayMissChanges is explicit. V3 live-root deduplication/busy handling remains in effect. No promise is made about intermediate updates between captures, spoken priority, timing or partial non-atomic speech.

## Validation boundary

Trusted Semantics and timed LiveRegions fixtures cover the new paths; Foundation and Widgets HTML remain unchanged. No production dependency/package/project was added. The original architecture plan under outputs/ is preserved. Credential entry, arbitrary targets and recordings remain unavailable. Historical NVDA worksheets remain scoped to their old profiles.
