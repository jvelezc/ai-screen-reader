---
version: 1
slug: "src-headlessscreenreader-cli-traversalreport-cs"
primary_target: "src/HeadlessScreenReader.Cli/TraversalReport.cs"
related_targets: ["src/HeadlessScreenReader.Cli/TraversalEvidenceViewer.cs"]
---

# Traversal evidence report extension

Mode: Operate / Read. Use the user's existing architecture acceptance criteria and explicit instruction to proceed. Extend the incumbent report, SVG trace and semantic colors; no new visual identity or conceptual choice is needed. No standing build-path preference is set.

## Direction contract

THESIS: Inspect what the reader actually observed and did beside its original recording. The existing source/result trace remains authoritative; selection is passive.

OWN-WORLD: Inherit the trace's navy text, white/cool-gray surfaces, green verified goals and amber observed disabled controls. Use system UI type, compact native controls and clearly labeled evidence tables.

STORY: Read the route outcome, select an observation or action, inspect the actual source/result and verification, then seek the corresponding video and timeline interval.

FIRST VIEWPORT: Route name/outcome and report links first; a readable trace list and one original-video player share the workspace on desktop and stack on mobile. Selected evidence and transcript remain reachable by keyboard.

FORM: A scoped extension of the existing traversal report, with native video controls and equivalent tables. No concept roll or image comp applies to this fixed report task.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

The extension preserves existing visual authority; missing global design documentation is not a redesign request. Review only this artifact and its functional contract. No marketing claims, stock imagery, decorative motion or live-browser controls.

## Finished-source evidence (2026-10-20)

This is an ordinary extension under `reference/new-work.md`'s finish rule. The incumbent Markdown/SVG system remains the authority; no global `DESIGN.md`, design sidecar, identity, or production source was changed by this documentation pass. The finish reviewer supplies the separate disposition; these observations do not certify it.

- Checked `src/HeadlessScreenReader.Cli/TraversalReport.cs`, `src/HeadlessScreenReader.Cli/TraversalTraceDiagram.cs`, `src/HeadlessScreenReader.Cli/TraversalEvidenceViewer.cs`, `src/HeadlessScreenReader.Cli/Viewer/viewer.css`, and `src/HeadlessScreenReader.Cli/Viewer/viewer.js`. The Markdown addition links to the viewer; the SVG retains its existing state/edge structure and evidence-report links.
- The viewer inherits SVG navy text (`#16243a`), secondary text (`#42526b`), white (`#ffffff`), observed gray (`#f1f5fa`), verified green (`#e7f5ed`), and disabled-control amber (`#fff1dc`). Blue links/focus/selection support local interaction. Explicit state labels carry the meaning alongside color. The SVG uses Arial; HTML uses `system-ui, sans-serif` at `16px/1.5`, with compact heading sizes. These existing medium-specific type choices are preserved rather than declared a new shared token system.
- Desktop uses a bounded two-column workspace, a sticky evidence/player column, and a video capped at 480px. At 760px the workspace stacks, sticky positioning ends, and buttons have a 44px minimum height. Dense evidence tables retain a 620px minimum width inside horizontally scrollable wrappers. Flat surfaces, thin borders, and small rounded buttons extend the incumbent report's restrained diagram language.
- Selection uses native buttons with `aria-pressed`, native disclosure summaries, a skip link, visible focus outlines, and links to selected evidence. Native video controls own playback; selection pauses then seeks without playing or issuing browser commands. Status regions describe selection timing and media availability. Source inspection supports these mechanisms; screenshots do not establish keyboard operation or real assistive-technology acceptance.
- Timing is shown only from calibrated Covered/Partial cues with finite positive intervals. Acquisition intervals remain distinct from atomic frames, overlapping events do not imply causation, and Unknown/unavailable positions are not inferred. Retained uncertainty, source/result identities, input attempts, completed calls, observed outcomes, and goal verification remain explicit and separate.
- Media loading, ready/paused, seeking, playing, ended, load failure, insufficient clip coverage, and seek failure have source-defined status messages. A missing video is hidden after error while timing and structured-evidence guidance remain. No finalized media displays recording stage/code; the 4 MiB display limit links to readable and structured evidence. Engine/policy status is separate from destination/barrier status.
- Visually inspected `.impeccable/review/desktop.png`, `mobile.png`, `tiny.png`, `desktop-viewport.png`, `mobile-viewport.png`, `tiny-viewport.png`, `disabled.png`, and the refreshed `missing.png`. They show the inherited palette, desktop columns/mobile stack, verified/disabled states, and retained evidence when video fails. The refreshed missing-media capture includes the current execution/completeness header. No global design drift was established in this scoped comparison.
- The original route video is evidence media. Review screenshots are QA artifacts; neither is a newly generated shipping raster. No detector was rerun during documentation.
