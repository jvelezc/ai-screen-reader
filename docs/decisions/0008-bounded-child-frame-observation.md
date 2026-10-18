# Bounded child frame observation

Date: October 18, 2026. Status: accepted. Profile: hsr-desktop-reader-v7.

## Scope and evidence

The trusted fixture adapter now captures same-target about:srcdoc/about:blank child documents, including nested frames. The public harness still accepts only embedded BundledFixture values and aborts context network requests. Other document kinds, cross-target execution and arbitrary websites are outside this increment.

A pinned Chromium probe established that the default AX tree omits child document content, child trees can be requested separately, and frame owners are available through CDP. The implementation uses Page.getFrameTree, DOM.getFrameOwner and per-frame Accessibility.getFullAXTree. These are documented in the [Chromium DevTools Protocol](https://chromedevtools.github.io/devtools-protocol/); the actual pinned-browser experiment and Chromium acceptance tests establish this implementation's observed behavior. Playwright exposes [Chromium CDP sessions](https://playwright.dev/dotnet/docs/api/class-browsercontext#browser-context-new-cdp-session).

## Identity and reading order

Domain adds FrameObservation with frame/parent/owner/document/root identity, status and safe reason code. Snapshot.Frames and ReaderStep.FrameCoverage expose the inventory returned by the page target. Empty coverage from a provider means no frame observation was supplied; it does not prove a frame-free website. Each normalized node carries its FrameId and DocumentId.

Document identity combines browser session, CDP frame ID, loader generation and AX document root. Node IDs are scoped to that document. A child navigation invalidates its node identities without changing the main document's identity or unrelated text selection.

Each supported child root is attached to its actually exposed iframe owner and inserted immediately after that owner in reading order. No owner name or containment relationship is inferred from diagnostic DOM text. An owner excluded by AX visibility or editable-content normalization does not expose child contents. RootWebArea boundaries stop peer-group lookup so a child radio cannot inherit an outer document's group/count. The outer iframe remains available as container context.

NextFrame/PreviousFrame select exposed iframe owners in browse mode, without wrap or input. The selected owner's Frame identifies the document containing that element; its child coverage record identifies the child through OwnerNodeId. Ordinary heading/link/text navigation follows the merged exposed order. Text ranges identify their own document and frame. Child generation changes invalidate child text even if a provider reuses node IDs; detachment produces NodeRemoved and an explicit historical FrameChanged event.

## Focus and updates

The adapter resolves each supported document's actual focus owner with a fixed read-only document.activeElement/hasFocus observation, excluding parent iframe elements from competing with a child control. It reads no editable value or DOM text and does not set focus. Root-level AX focus remains a fallback document observation. Active descendants are resolved within the owning document separately from the actual focus owner. Multiple observed non-root focus owners cause FRAME_FOCUS_AMBIGUOUS rather than a guessed destination.

Child live-region content has a document-specific initial baseline. Replacing a child does not announce its initial status as an update; changes in the unchanged main document remain observable. These are snapshot observations, not speech scheduling or lossless events.

## Limits and failure states

FrameCaptureOptions defaults to eight captured documents including main and three child levels. Valid configured bounds are 1..32 documents and 0..8 child levels, and actual limits are included in the browser environment manifest. Hidden/unsupported documents do not consume a document-capture attempt. The discovered target frame tree remains in coverage even when captures are limited.

| Status | Meaning |
| --- | --- |
| Observed | A supported document was captured and attached to an observed owner. |
| Excluded | Its owner was not exposed, or an ancestor was excluded. |
| Unsupported | The child document kind is outside this adapter's supported scope. |
| Unavailable | Owner/document capture was unavailable; supporting child nodes are absent. |
| LimitExceeded | The capture count/depth bound or an ancestor's bound prevented observation. |

Descendants of an unobserved frame inherit that status with ANCESTOR_FRAME_NOT_OBSERVED. Count/depth restrictions can take precedence over checking document kind. Coverage does not enumerate internals of unsupported cross-target subtrees and is not a whole-site completeness claim.

The frame tree is read before and after capture. A changed tree/generation produces FRAME_TREE_CHANGED; a caller may capture again, but input is never retried automatically. Main-document capture failure remains BrowserError. A child capture failure retains its coverage record as Unavailable. Neither unavailable coverage, a limit nor an engine error is an accessibility barrier. Captures are sequential and bounded by the existing five-second capture deadline; they are not an atomic cross-document snapshot.

No new project, package, host, arbitrary-target API, input broker, recording or credential-entry capability was introduced. v7 requires fresh reference evidence rather than accepting older profile worksheets.
