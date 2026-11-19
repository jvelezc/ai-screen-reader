# Attorney screening and live worker view

The first case screening combines counsel’s selections with one task from the current review. The supported profile is a federal ADA Title III matter in the Ninth Circuit involving a physical public accommodation and prospective injunctive relief. Counsel confirms that the matter fits this scope and marks four criteria as unassessed, supported, contrary, or conflicting. The form accepts only status selections. Counsel keeps client facts and documents in the matter file.

The core independently verifies the selected task’s original evidence. It binds the result to the workspace, review request, task and original report fingerprint. A stopped, unconfirmed or unverifiable task leaves the barrier criterion unassessed. A verified successful keyboard attempt result weighs against a barrier for the task as tested. The report groups gaps, contrary evidence and conflicts and hands all-supported results back to counsel for review. It retains separate sources for counsel’s evidence selections and the recorded task. The report links each criterion to a primary authority. [Title III statutory text](https://www.ada.gov/law-and-regs/ada/) · [Robles v. Domino’s](https://cdn.ca9.uscourts.gov/datastore/opinions/2019/01/15/17-55504.pdf) · [Chapman v. Pier 1](https://cdn.ca9.uscourts.gov/datastore/opinions/2011/01/07/07-16326.pdf)

The screening is a current review computation. It starts no website input and saves no case or claimant details. It covers the supported profile and the recorded task only. Broader legal research, claim assessment for other jurisdictions, independent assistive-technology reproduction, and filing decisions remain separate. A change to the task’s original fingerprint requires refreshing the assessment.

From a queued or running public-link task, counsel can open **Watch the worker live**. During an active attempt, the view shows JPEG frames from the same isolated worker's existing screencast. It has no control channel. It labels keyboard and mouse attempts separately and retires pixels when the viewer closes, the attempt changes, the frame expires or access changes. The latest frame is temporary. The saved recording and verified original evidence remain available from the completed result.

Live images use bounded output messages distinct from the final archive. Input authorization stays on its existing channel. The host only accepts frames from the owned execution and active attempt. The original caller's current authority and exact attempt are checked before and after reading the temporary image. A live image expires after eight seconds. The viewer neither renews the lease nor turns a live image into an original evidence file.

The core serves the two caller-scoped operations through HTTP 1.30. The existing Supabase Edge Function adapts them for the browser. The UI publication gate checks the protected endpoints and authenticated gateway configuration before publishing the consumer. Hosted acceptance requires the exact installed release and an original-account test from request through live view, original playback and screening. Local checks and production rollout are tracked separately.

| Observer requirement | Verified local check |
| --- | --- |
| Actual Chromium supplies decodable JPEG bytes through the public task capture callback | `ActualChromiumScreencastProvidesABoundedDecodableLiveImage` |
| A worker frame cannot replace the final evidence archive | `LiveFrameAndFinalArchiveUseDistinctBoundedMessages` |
| Stale, foreign and closed-attempt frames are inaccessible | `LiveFrameCacheRejectsStaleForeignAndClosedAttemptFrames` |
| The exact running creator and current provider authority gate every frame read | `LiveReadUsesExactRunningCreatorAndRechecksAccessAfterOpeningFrame` |

The four focused Windows Release tests passed with .NET SDK 10.0.402. The disposable Linux public task proof passed the working, keyboard-blocked, pointer-unconfirmed, and alternative-content fixtures. Each fixture observed a JPEG from its own running execution, verified the prior saved original checks, and confirmed that live viewing ended with that execution. The consumer passed 1,354 tests, type checking, lint and production build. Its publication checks passed 19 focused Python tests. Deployment and signed-in browser acceptance remain separate checks.
