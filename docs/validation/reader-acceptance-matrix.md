# Reader acceptance matrix

Review date: October 19, 2026. Baseline: reader v10, commit 10a99e9; 337 local Windows and Windows/Ubuntu CI tests passed, including 59 real Chromium cases. This is a reader review, not completion of platform milestones.

| Capability | Current scope | Evidence or limitation | Priority |
| --- | --- | --- | --- |
| Semantic navigation, focus and activation | Validated for trusted fixtures; bounded custom profile | Reader/session and real Chromium suites; activation still requires observed keyboard focus | Retain regression gates |
| Character, word and newline navigation | Validated within individual exposed text runs | TextNavigationTests; rendered lines and words spanning runs remain unsupported | Retain; do not reinterpret offsets |
| Paragraph reading across inline runs | Validated in reader v11 locally and Windows/Ubuntu CI | reader-paragraphs-v11.md; 40 new cases, exact source fragments, ownership and content/size boundaries | Retain regression gates |
| Multi-select listboxes | Implemented in reader v12 for trusted ARIA fixtures | reader-multiselect-v12.md; source-tagged exposed selected sets, scoped browse and focused Space toggles | Delivered; exact remote receipts tracked separately |
| Listbox keyboard typeahead | Planned next increment after v12 validation | Bounded printable-key routing to actual listbox focus; selection must stay separate from focus | Next reader feature |
| Trees and tabs | Bounded single-select trees and manual tabs | reader-composites-v9.md; tree multiselection, typeahead and asynchronous loading remain unsupported | Queue broader support |
| Native tables | Bounded read-only rectangular tables | reader-tables-v10.md; complex headers, spans, virtualization and grids remain unsupported | Queue broader support |
| Frames | Bounded trusted same-target child documents | reader-frames-v7.md; cross-target/out-of-process frames remain unsupported | Needs separate policy/coverage proof |
| Live regions | Bounded sampled observations | Snapshot changes do not implement speech scheduling or lossless delivery | Separate future reader increment |
| Editable text | Excluded from text capture | Capture privacy prerequisite remains unproven | Gate closed |
| Hosted/MCP/evidence workflow | Planned | Storage, capture, authorization, ownership and egress gates remain open | Later platform work |

Reader v12 implements the selected multi-select increment. Local Windows passed 431 tests, including 74 real Chromium cases, preserving all 377 v11 identities. The review baseline above remains the evidence available when prioritizing the earlier increments; current remote receipts are tracked in GitHub Actions and the architecture Page.

After v12 CI passes, the next planned feature is bounded listbox keyboard typeahead in trusted fixtures: send a permitted search prefix only through actual supported listbox focus, observe the resulting focus/active descendant, and verify that selection changes only when the widget exposes them. Specify supported characters, case handling, prefix length, timeout/reset behavior and bounded outcomes before implementation. Acceptance must cover repeated prefixes, multiple and absent matches, disabled options, unsupported/foreign/stale focus, no focus repair or editable entry, real Chromium paths, CLI and Windows/Ubuntu CI. This is planned scope, not an implementation claim. Asynchronous loading, tree typeahead, broader tables and rendered text remain queued; do not start several increments concurrently.
