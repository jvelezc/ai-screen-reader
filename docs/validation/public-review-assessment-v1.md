# Source-bound review evidence assessment

## Purpose and scope

A review needs one evidence answer tied to its current source and saved task set. This additive read uses the existing eight-condition assessment for each independent task. It preserves original observations and separates complete barrier evidence, incomplete records and successful tested tasks. Conditions from different tasks never combine into one barrier.

Source contract1.28 adds `GET /api/workspaces/{organization}/website-scans/{scan}/tasks/sets/{request}/assessment` and private MCP `website_review_assessment`. The existing set remains metadata with `isAssessment=false`; its route and admission behavior do not change. This new assessment is read-only and works without a ready worker. It performs no admission, website input, original-file write or automatic retry. No hosted schema migration is required.

## Decision contract

| Outcome | Required support |
| --- | --- |
| WebsiteEvidenceReady | At least one exact task independently satisfies all eight conditions after report and supporting-original verification. Other pending or unavailable tasks remain explicit; their conditions are not borrowed. |
| NoBlockingBarrierInTestedTasks | Every member of a nonempty set has independently verified originals and the existing successful-task outcome. Covers only those declared tasks. |
| InsufficientEvidence | Empty sets, incomplete evidence, pending/stopped tasks, unavailable originals or completed tasks not yet read prevent a clean-set conclusion when no independent barrier qualifies. |

The response pins the exact set/request, organization, parent review/original fingerprint, ordered child IDs, expected report fingerprints and each verified execution and file inventory. Closed per-member availability is VerifiedOriginals, Pending, Stopped, OriginalsUnavailable or OriginalsNotRead. An unavailable member carries no finding or claimed verified files. A task completing during the read stays OriginalsNotRead until another explicit read.

Only the captured page's supported public service links are in scope. This read establishes neither whole-site conformance nor external NVDA equivalence, a person's disability/injury/standing or a legal case. Public MCP hosting and plugin publication remain deferred.

## Authority and diagnostics

The service reads the current set, verifies completed reports and all supporting files through the existing result path, rechecks each verified task and reads the set/current authority again before responding. Changed identity or lost access denies the read. Integrity failure produces explicit missing evidence rather than a barrier. Cancellation remains cancellation; no replacement task is created.

Request diagnostics retain exact job/set/request, expected report SHA, verified execution, closed report/supporting-original verification stage and expected original name/SHA/length. A partial200 response preserves each missing member's safe failure detail in server diagnostics. Existing single-task503 keeps its public code. No token, account address, URL/query, raw exception or original content is logged; diagnostic-sink failure cannot change the assessment or repeat reads.

## Validation and delivery

Source-only focused Domain/API/private consumer67discovered/executed/passed,0failed/skipped, SDK10.0.402. Includes actual HTTP/private stdio composition, forged/missing truth fields, cross-task condition isolation, empty/pending/stopped/late-completed records, changed/foreign scope, final current-access loss, report/supporting-original corruption and safe diagnostics. Contract fixtures are not actual browser recordings; existing Chromium/native acceptance remains required. Canonical local Windows Release now passes4,740discovered/executed/passed,0failed/skipped, and canonical format verification passes. Complete Windows/Linux CI, exact artifact promotion, existing Edge/UI consumption and original-account browser acceptance remain separate gates pending for this addition.

Named regression checks include TrueConditionsFromDifferentTasksCannotBeCombinedIntoABarrier; ATaskFinishingDuringTheReadRemainsExplicitlyUnverified; FinalCurrentAccessLossDropsAllPreviouslyVerifiedEvidence; UnavailableOriginalDiagnosticsRetainExactFailureWithoutDisclosingItInTheResponse; ActualPrivateStdioReadsTheCurrentReviewWithoutAdmissionAndDeniesRevokedAccess; IndependentConsumerRejectsMissingTruthAndForgedReviewFindings.

The [completion plan](../completion-plan.md) remains the full product gate list. This summary does not close broader public search/forms, measured event/video seeking, hosted credential exclusion, fault/operations or real user/AT acceptance.
