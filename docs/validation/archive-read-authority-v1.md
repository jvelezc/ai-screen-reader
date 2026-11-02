# Archive read-error authorization boundary

Status November2,2026: delivered at9455013/HTTP1.17, existing Edge12 and UI00bc2bb.
Six real HTTP cases fail on baseline and pass after correction;136 related checks
pass. Strict Release build has0 warnings/errors; Windows/Ubuntu CI each discover
and pass3354, failed/skipped0, with clean required format. All nine
[CI41454031773 jobs](https://github.com/jvelezc/ai-screen-reader/actions/runs/41454031773)
pass, including native71/database509/operations174. Private
[deployment41456833152](https://github.com/jvelezc/ai-screen-reader/actions/runs/41456833152)
is Ready; all151 installed files, the independently downloaded CI manifest and
rootless image match. Trusted default TLS and anonymous/query/route guards pass.

## Problem and resulting behavior

The archive reader already rechecks the original workspace after successful
storage work. A mapped missing/invalid/unavailable storage failure previously
returned before that check, potentially masking authority lost during the read.
Now the mapped failure is retained, current original-caller authority and
cancellation are checked, and only then is the failure or result returned.
Known denial wins over stale storage outcomes. Current-authority storage failures
retain their existing404/409/503 codes. No data, repair write or implicit replay
is introduced; HTTP1.17 shapes and generic Problem contract are unchanged.

| Requirement | Exact test | Cases |
| --- | --- | ---: |
| Failed manifest/file work cannot mask membership loss | `FailedArchiveReadCannotMaskAuthorityLostDuringItsWork` |6|
| Successful reads still suppress data after membership loss | `MembershipLossDuringReadSuppressesAlreadyReadBytes` |2 existing|
| Current-authority storage failures remain explicit without repair | `MissingCorruptAndUnavailableOriginalsAreErrorsWithoutRepair` |6 existing|

The6 new cases cross manifest/file with missing/invalid/unavailable storage.
The existing injected store hides only the original workspace inside the first
attempt, then throws its chosen domain failure. Assertions require404, exact
safe problem title/detail/status, support reference, problem media, absent file
hash/data,1 attempted read and unchanged writes. All6 genuinely fail on old code;
all6 pass after the minimal correction. No expected assertion is weakened.

## Hosted observation and limits

The original account admits one sanitized Checkout preview recording in isolated
workspace6da73acb-5232-4348-8cb8-dab9d89f72fb: requestdccc4ef3-5911-4fa6-ac58-2517b0eb0b8a,
scana7da5a5d-1699-44f2-b027-5a44bfb11e91. It is observed/Available; five originals
verify and the actual video has duration4.08/errornull. Retained progress has5
checkpoints,2 intents/results; last-commandSatisfied is separate from overall
NotAssessed. Reading or reauthorizing evidence sends no fixture input.

A scoped S3-reader hold records7 blocked packets/420bytes and remains active
after exact test-membership removal, then is removed. Authentication addresses
are disjoint and the worker/storage objects are untouched. The UI returns
CONTROL_ARCHIVE_UNAVAILABLE; video/recorded graph clear but the broader workspace
and retained path remain. This is NOT accepted live revocation proof: the10s
connect deadline may have settled before mutation. The source gap is independently
proved by the6 HTTP regressions. Strict restoration returns test1/working1/
sentinel0. Keep rejected dry runs, peer-notation diagnostics, timing/failure and
cleanup receipts under the private `.git/testagent/` directories.

### Accepted original-account cases after promotion

The same recording is reused without replaying input or admitting another scan.

| Operation | Actual observation and limit |
| --- | --- |
| Cached saved-path refresh | Revoke the isolated membership after loading graph/video/path; Refresh saved path returns CONTROL_WORKSPACE_NOT_FOUND and clears workspace, graph, media and reader views. This is current-denial clearing, not local-journal mid-I/O. |
| Failed storage reauthorization | Actual S3 packets are blocked before and after membership removal. The UI returns CONTROL_WORKSPACE_NOT_FOUND and clears graph/media while the hold is still active; only then does manual release confirm9 packets/6111 bytes and an unchanged control-process epoch. This reaches the manifest read within video/graph reauthorization. |
| Initial original-file operation | A fresh authorized inventory lists5 originals. Download traversal.json begins at0/140553 bytes; the established S3 connection remains blocked through exact revocation and the UI's denial. The archive inputs/download buttons and protected views clear; Open workspace receives focus and Sign out remains. Manual release after denial again confirms the still-active hold9/6111 and unchanged process epoch. This proves the initial file-endpoint/page operation; it does not prove a later page or successful delivery of file bytes. |

Accepted reauthorization revocation/restoration:14:10:19.408426Z /
14:10:50.975771Z. Accepted file revocation/restoration:14:40:25.263204Z /
14:40:58.563349Z. Both use disjoint Auth/S3 addresses, an exact control-cgroup
S3-only hold, request-relative expiry and an independent backstop. Product HTTP
deadlines, worker, storage originals and Auth traffic are unchanged. The hold is
independently confirmed active after the UI outcome, before manual release.
Only the isolated original-user membership is restored: test1/working1/sentinel0.
Working50/saved sign-in are recovered. Native0, all eight owned timer/watcher
units inactive, no hold table, unchanged healthy/unfrozen control/observation
processes and exact release/image are independently checked after cleanup.

An earlier28s storage window and a cold initial-file attempt remain unaccepted;
the latter returns CONTROL_ARCHIVE_UNAVAILABLE. Keep those and the earlier
pre-correction outage receipts. No failed or missed window is relabeled passed.

Local-journal mid-I/O, later original-file pages and real provider expiry remain
separate scopes. A normal original-account provider/Edge expiry check has a200
baseline; its post-expiry browser readback awaits the required Chrome extension
update. It uses its own memory-only, non-refreshing session and does not replace
the notebook's saved session. No provider/core expiry result is inferred from RLS.
This does not complete website collection, human/AT, custody/legal acceptance or
the product. Product acceptance precedes complete documentation, then full
best-practice/complexity refactoring. Public MCP/plugins stay deferred.
