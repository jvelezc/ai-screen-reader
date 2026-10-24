# Text cursor ownership validation

The [ownership decision](../decisions/0050-text-cursor-ownership.md) prevents stale text ranges from surviving a frame or node-document change with the same node ID and text.

| Requirement | Evidence |
| --- | --- |
| Changed frame, independently changed node document, root/child transitions and legacy-to-child ownership invalidate both current-text reads and word movement; reset stays unknown until explicit re-anchor | `ReusedTextNodeCannotRetainOffsetsAcrossChangedOwnership` (ten cases) |
| Stable explicit and legacy root ownership preserve exact offsets across unrelated child changes | `StableTextOwnershipKeepsOffsetsWhenAnotherFrameChanges` (two cases) |
| Hosted commands and trace retain the new observation and unknown/reset outcome without an old text range, input or goal claim | `HostedTraceRetainsOwnershipResetWithoutInventingTextContinuity` |
| Actual Chromium parent/child text anchors retain separate current documents/frames and word offsets | `Frames_Chromium_ExplicitTextAnchorsKeepCurrentParentAndChildProvenance` |
| Actual Chromium child replacement/removal keeps the text reset sticky, then explicit parent re-anchoring recovers | `Frames_Chromium_ReplacementOrRemovalInvalidatesChildTextWithoutResettingMain` (two existing cases strengthened) |

Before the production repair, the new targeted suite discovered13 cases:11 failed at the stale-range outcome assertions and two stable-ownership cases passed. After the repair, the combined ownership/text/frame suite passes **69/69** locally on Windows, zero failures/skips. No failure deadline, expected outcome or browser assertion was relaxed.

Full local Windows passes **1,886/1,886**, zero failures/skips, including14 new cases. The strict nonincremental Release build passes with zero warnings/errors and architecture dependency checks; full solution format verification passes. Native Linux CI, exact private deployment and independent VPS readback are recorded on the architecture Page after their gates finish. VPS Foundation-fixture canaries establish delivery and native isolation; they do not reproduce cross-frame mutations or establish real application identity. Actual Chromium frame behavior is covered by the browser tests above.

The previously accepted trace release25b2ecb passed all nine jobs in [CI40080149650](https://github.com/jvelezc/ai-screen-reader/actions/runs/40080149650), Windows/Ubuntu1,872 each, native Linux30 and operator122. [Private deployment40081051473](https://github.com/jvelezc/ai-screen-reader/actions/runs/40081051473) and independent VPS checks accepted that exact artifact. These are baseline receipts, not current-repair acceptance.
