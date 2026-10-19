# Source tagged range observations

Date: October 18, 2026. Profile: hsr-desktop-reader-v8.

## Decision

Expose nullable numeric current/minimum/maximum observations only for noneditable slider, meter and progressbar roles. Numbers come from Chromium AX. Preserve the exposed units and bounds; do not compute a percentage, invent defaults or infer task completion. Missing current value remains unobserved, including native indeterminate progress. Range consistency validates finite numbers and the relationships between supplied numbers; it does not establish author conformance or require every field to be present.

AX can expose normalized defaults or repaired author declarations. These observations describe Chromium's exposure, not validation of the original HTML or ARIA. See the [Chromium AX protocol](https://chromedevtools.github.io/devtools-protocol/tot/Accessibility/) and [WAI-ARIA range semantics](https://www.w3.org/TR/wai-aria-1.2/#range).

Use exposed AX value text when available. The pinned Chromium build omits the custom fixture's declared aria-valuetext in its AX response. A fixed read-only semantic supplement therefore reads that attribute only into an already permitted range observation, tagged DeclaredAria independently from numeric ChromiumAccessibility data. No DOM value, text-content, label inference or diagnostic lookup supplies a range value. A spoken representation may prefer the observed value text; numeric verification still requires an actual consistent numeric value.

## Reader behavior

NextRange and PreviousRange move the browse cursor over exposed slider/meter/progressbar roles without input or wrap. They do not assert that a role has complete range data or is operable. Focus mode leaves raw keyboard operation under the existing allowlist; native sliders already use focus-mode routing. Activate does not invent Enter/Space semantics for range roles. Actual browser focus remains necessary for keyboard operation.

Results expose the selected node's Range. Node events carry historical range data, and WidgetStateChanged includes numeric, bound or value-text changes. Events remain sampled observations, not lossless delivery or speech scheduling. A RangeValue goal requires exact equality to an observed finite, consistent value, together with all other requested role/name/state/focus constraints. It proves only that predicate.

## Exclusions and consequences

Editable roles, spinbuttons, contenteditable range nodes and unrelated roles do not expose numeric values or value text through this path. The broader capture exclusion protocol remains unfinished; accessible names, ordinary text and permitted range text can still contain sensitive information. This is controlled-fixture minimization, not general PII redaction.

No new browser keys, editable-text operations, arbitrary targets, general slider automation policy, cross-target frames, capture collectors or hosted services are introduced. The independent reader profile changes to v8, so v7 benchmark worksheets are historical. No fresh NVDA comparison is claimed.
