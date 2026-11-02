# Archive read-error authorization boundary

Status November2,2026: source correction in progress. Six new real local HTTP
cases fail on baseline and pass after correction;136 related archive/progress/
hosted-consumer checks pass with0 failed/skipped. Strict Release build passes
with0 warnings/errors. Full Windows3354 and required format verification pass,
failed/skipped0. Remote CI, exact promotion and original-account hosted acceptance
are separate remaining checks. Installed core
remains5d8aec9/HTTP1.17, Edge12 and UI00bc2bb/821 per OS.

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

After green CI/exact private promotion, retry protected reads of the same producer
under the original account. Accept only observed current denial and clearing;
an unavailable error alone cannot establish known lost access. Keep actual file
page, playback-loading, local-journal graph timing and provider expiry distinct.
This does not complete website collection, human/AT, custody/legal acceptance or
the product. Product acceptance precedes complete documentation, then full
best-practice/complexity refactoring. Public MCP/plugins stay deferred.
