---
version: 1
slug: "src-headlessscreenreader-browser-playwright-privateviewer"
primary_target: "src/HeadlessScreenReader.Browser.Playwright/PrivateViewer/view.html"
related_targets: ["src/HeadlessScreenReader.Browser.Playwright/PrivateViewer/view.css", "src/HeadlessScreenReader.Browser.Playwright/PrivateViewer/view.js", "src/HeadlessScreenReader.Browser.Playwright/ChromiumPrivateViewer.cs"]
---

# Private fixture viewer extension

Mode: Operate. This is an ordinary inherited UI extension for a scoped, local, read-only fixture viewer. The incumbent traversal evidence surface supplies the visual authority. No new identity, global `DESIGN.md`, or design sidecar is required by this scoped documentation pass.

## Direction contract

THESIS: Inspect fresh permitted fixture pixels beside retained reader observations while keeping their capture times and evidentiary meanings explicit.

OWN-WORLD: Inherit navy text, muted secondary text, white and cool-gray flat surfaces, system UI type, native controls, and visible focus from the existing evidence viewer.

STORY: Read the viewing state, inspect the pixel and retained-semantic bindings, pause or resume viewing, refresh a permitted observation, or end viewing.

FIRST VIEWPORT: Purpose and viewing controls precede a full-width state band. Pixels and retained observations share two desktop columns and stack on mobile.

FORM: A fixed fixture display with native Pause/Resume, Refresh, and End controls. Its controls affect viewing only.

FINISH: The supplied fresh finish review received all five sections and returned **ship**, with no material fixes. This record captures the finished source and scoped visual evidence; it does not expand that verdict into platform or accessibility acceptance.

## Finished-source evidence (2026-10-21)

- Checked `PRODUCT.md`, the incumbent surface record `.impeccable/surfaces/src-headlessscreenreader-cli-traversalreport-cs.md`, and `src/HeadlessScreenReader.Cli/Viewer/viewer.css` for product and visual authority. Checked the four target files above for the implemented viewer. Additionally inspected `ChromiumFixtureBrowser.PrivatePixels.cs` and `Domain/ViewerPresentation.cs` for pixel provenance and display bounds.
- The viewer uses navy (`#16243a`), muted text (`#42526b`), white, and cool gray (`#f1f5fa`). Thin cool-gray dividers and tonal state/image areas provide structure without shadows. System UI text uses `16px/1.5`; desktop title and section headings use `2rem` and `1.15rem`. Six-pixel button corners and restrained blue hover/focus treatments extend the incumbent evidence viewer rather than defining a new shared token system.
- The bounded desktop workspace has a maximum width of 1120px, 32px outer padding, a full-width status band, and two columns with a 40px gap. The pixel column has a 320px minimum and the observation column can shrink. At 760px and below, the workspace stacks, outer padding becomes `24px 16px`, the title reduces to `1.7rem`, and End loses its desktop trailing alignment so controls wrap in reading order. Images scale to their available width; observation text wraps without changing its content order.
- Native buttons retain a minimum height of 44px. A skip link targets the observation section, headings label the pixel and observation sections, and visible focus outlines support keyboard location. The state heading has a polite live region. Source inspection establishes these mechanisms; screenshots alone do not establish keyboard operation or real assistive-technology acceptance. Button background motion is enabled only when reduced motion is not requested.
- Rechecked the subsequent `view.js` status delta: heading and message text are assigned only when their values differ, avoiding identical live-region mutations during automatic refresh. Inspected the real Chromium `MutationObserver` assertion in `PrivateClientTests.cs` for zero heading mutations on repeated Viewing. The implementation run's passing result is supplied separately; the documenter did not rerun it. The finish reviewer rechecked this delta and returned **ship**; the prior scoped visual approval holds with unchanged screenshots and layout.
- The page distinguishes **Fresh fixture pixels** from **Retained reader observations** in both headings and explanatory copy. Epoch, retained observation ordinal, and pixel baseline are labeled together without asserting an atomic capture. Pixels explicitly do not verify a goal. The retained-semantic display is bounded to 128 nodes with bounded role/name text, reports truncation, preserves focused/disabled labels, and explicitly labels prior simulated assistance when present.
- Initial loading waits for a permitted observation. Viewing displays pixels and semantics; Pause clears displayed content, changes its button to Resume, and disables Refresh. Resume and Refresh clear prior display while loading. Excluded, unavailable, and ended render paths clear content; a failed refresh offers Refresh to retry. End disables every viewing control. A missing viewing host clears content and instructs the user to close the window. Revision matching rejects stale render results. These are source-defined states; the two reviewed screenshots show Viewing only.
- The viewer is a separate fixed display surface. Its source blocks network requests, disables downloads and service workers, and uses a restrictive content security policy with hashed embedded script/style and blob-only images. It exposes viewing intents without a target keyboard, pointer, clipboard, URL-entry, or credential-entry UI. The footer states that excluded or ended content clears when observed and that already received bytes cannot be recalled. This source review does not certify arbitrary-site policy or external egress isolation.
- Visually inspected `.impeccable/review/private-client/desktop.png` and `.impeccable/review/private-client/mobile.png`. The captures show the inherited palette, explicit Viewing state and bindings, desktop columns, mobile stacking and wrapped controls, readable bounded observations, and the footer boundary statement. The mobile capture is narrow and preserves the sequence from controls through state, pixels, observations, and footer.
- Image provenance: the displayed PNG comes from an actual in-memory Playwright screenshot of the opted-in fixed synthetic fixture, with capture admission and document/epoch checks around acquisition. It is observation evidence, not a generated illustration or a goal-verification claim. Review screenshots are QA artifacts. No generated shipping raster or recording was introduced, and no image detector was rerun during this documentation pass.

## Review boundary

The finish review and this surface record cover the private viewer's implemented UI and supplied desktop/mobile captures. Documentation changed only this new surface record. Production code, `PRODUCT.md`, global design/configuration, screenshots, and the existing architecture plan under `outputs/` were preserved. No build, test suite, Linux run, remote CI, deployment, human review session, or real assistive-technology validation was performed by the documenter; those acceptance results remain separately reported.
