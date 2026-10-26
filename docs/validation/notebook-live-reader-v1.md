# Original-user notebook live reader acceptance

Accepted October 25, 2026, with UI `e473aea2a82887fe3d4e4910cd968baf5de71c2f`
and private core release `95d6b9fc581bfa86e87143259d030b9cfb2b71b7`.
The browser used the confirmed application's existing user session at port6087.
The actual path was notebook → existing-project `hsr-core` Edge v1 → core
HTTPS API → isolated Chromium. No administrator, test identity, direct-core
browser fetch or privileged credential replaced the original caller.

## Observed lifecycle

Session `49cb8f1f-d06e-41c8-9615-e26687b6273d` opened the read-only Foundation
fixture, separate from saved synthetic-shop reports. Before observation, input
and objective fields were **Unobserved**. The fresh document exposed31 nodes.
ReadCurrent returned Observed; NextHeading returned "Screen reader fixture,
heading level1"; NextButton returned "Open details, button". The reader cursor
moved while browser focus retained its original node. All commands reported
browser keystrokes0 and verification NotRequested; the objective remained false.

The event page returned14 retained events, with command IDs1–3 and actual
capture/announcement results. The graph returned three atomic source/edge/result
entries. Entries2 and3 retained `refreshedFromStateId` links to the preceding
result, with no intervening action inferred. Each entry retained its own capture
window and current document. Both pages reported the end of retained history
and no retention truncation. This walkthrough exercised one actual page per
endpoint; synthetic cases separately cover continuation and corrupt pages.

Close returned the explicit core acknowledgement and cleared reader evidence.
Focus returned to Open Foundation reader; Shift+Tab and Tab traversed the
preceding report control and returned to Open Foundation reader. A separate SSH
inspection observed the exact owned container `7a701a7940a9` while the session
was open. After close, the exact session-label inventory was empty and
`podman container exists 7a701a7940a9` returned1. API acknowledgement and this
independent native-absence evidence are separate facts.

## Narrow-screen and expiry checks

A second original-user session, `4e06f99e-2e7e-41d0-976e-3102d90d8b04`,
was observed and navigated at an explicit320×900 CSS-pixel viewport. Its actual
NextHeading graph retained source/result states, ReaderCursor edge and capture
identity. Document client/scroll widths were both305 pixels, with the remaining
15 pixels occupied by the vertical scrollbar; no tested main button, code,
preformatted block or definition value crossed the viewport. Screenshot
inspection showed stacked graph states and wrapped long IDs. The original
viewport was restored. Keyboard activation and explicit close passed; separate
inspection confirmed owned container `cf767eb15f32` absent after close.

Two earlier sessions expired while the walkthrough was paused. A subsequent
actual request returned CONTROL_READER_NOT_FOUND, displayed a support reference,
and cleared reader and workspace views together. Reconnecting the workspace
required an explicit action, followed by an explicit fresh reader open. This is
session-expiry evidence, not provider membership revocation or token-expiry proof.

## Verification scopes and next step

- Local Windows UI:182 discovered/passed,0 failed/pending; types, lint and build pass.
- UI CI40272688892: Windows and Ubuntu182/182 each, types/lint/build pass.
- Core CI40273178754: all nine jobs pass; Windows/Ubuntu2,047 each, native Linux30,
  operator152. Private deployment40274565259 succeeded separately.
- Actual original-user browser: the bounded Foundation lifecycle, expiry clearing,
  keyboard operation,320px reflow and independently checked native close pass.
- Actual human/NVDA operation, real revoked/expired provider membership, published
  Lovable origin/email callbacks, broader recovery and hosted recording remain open.

**Next implementation:** durable tenant-scoped traversal/media finalization and
core API/MCP retrieval, followed by the notebook's original-evidence graph view
through Edge. Transient reader graph entries are not archived media, keyboard
paths, verified objectives, accessibility judgments or custody attestations.
Public MCP, Claude onboarding and official plugins remain deferred.
