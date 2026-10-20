# Reader acceptance matrix

Review date: October 19, 2026. Baseline: reader v10, commit 10a99e9; 337 local Windows and Windows/Ubuntu CI tests passed, including 59 real Chromium cases. This is a reader review, not completion of platform milestones.

| Capability | Current scope | Evidence or limitation | Priority |
| --- | --- | --- | --- |
| Semantic navigation, focus and activation | Validated for trusted fixtures; bounded custom profile | Reader/session and real Chromium suites; activation still requires observed keyboard focus | Retain regression gates |
| Character, word and newline navigation | Validated within individual exposed text runs | TextNavigationTests; rendered lines and words spanning runs remain unsupported | Retain; do not reinterpret offsets |
| Paragraph reading across inline runs | Validated locally in reader v11 | reader-paragraphs-v11.md; 40 new cases, exact source fragments, ownership and content/size boundaries | Delivered; remote CI gate tracked separately |
| Multi-select listboxes | Planned next increment after v11 validation | No bounded profile contract for multiselectability, selected-option sets and focused keyboard toggles | Next reader feature |
| Trees and tabs | Bounded single-select trees and manual tabs | reader-composites-v9.md; multiselection, typeahead and asynchronous loading remain unsupported | Queue after paragraph reading |
| Native tables | Bounded read-only rectangular tables | reader-tables-v10.md; complex headers, spans, virtualization and grids remain unsupported | Queue broader support |
| Frames | Bounded trusted same-target child documents | reader-frames-v7.md; cross-target/out-of-process frames remain unsupported | Needs separate policy/coverage proof |
| Live regions | Bounded sampled observations | Snapshot changes do not implement speech scheduling or lossless delivery | Separate future reader increment |
| Editable text | Excluded from text capture | Capture privacy prerequisite remains unproven | Gate closed |
| Hosted/MCP/evidence workflow | Planned | Storage, capture, authorization, ownership and egress gates remain open | Later platform work |

Reader v11 implements the selected paragraph increment. Local Windows passed 377 tests, including 67 real Chromium cases, preserving all 337 v10 identities. The review baseline above remains the evidence available when prioritizing the increment; current remote receipts are tracked in GitHub Actions and the architecture Page.

After v11 CI passes, the next planned feature is bounded multi-select listbox support in trusted fixtures: observe whether multiple selection is supported, report observed selected options separately from focus/active descendant, and verify supported keyboard toggles through actual focus. Do not infer hidden selections, repair focus or expose editable values. Acceptance must include multiple selection and deselection, unchanged focus/selection during browse navigation, disabled options, stale/foreign ownership, real Chromium keyboard paths and Windows/Ubuntu CI. This is planned scope, not an implementation claim. Typeahead, asynchronous loading, broader tables and rendered text remain queued; do not start several increments concurrently.
