# Original-authority checks after native reader work

Status November2,2026:39 new deterministic/wire cases and256 relevant checks
pass locally, failed/skipped0. Strict Release build passes with0 warnings/errors.
The full Windows solution discovers and passes3348, failed/skipped0; required
format verification is clean. UI typecheck/build and820 tests pass, with its same
six existing lint warnings. No CI, private
deployment or actual hosted-native acceptance of this correction is claimed yet.
The installed accepted release remains bf2a8bd/HTTP1.17, Edge12 and UI6eaf97f.

## Behavior

Previously the controller checked the original caller's workspace access before
native initialization, capture or reader commands. Losing membership or provider
access while that work was outstanding could leave a successful response after
access changed. Each operation now rechecks the same original bearer after the
joined native work and before publishing data or trace. Failed or canceled work
still joins actual cleanup and never replays an input.

Known401/403/404 followed by failed native cleanup keeps that denial status with
`CONTROL_READER_ACCESS_CHANGED_CLOSE_UNCONFIRMED`. The consumer's existing access
reset clears protected results and controls; the server retains quarantined
ownership and never acknowledges retirement. Provider503 does not establish lost
membership. Native policy403 is not a provider denial. A failed close in either
case remains503 `CONTROL_READER_CLOSE_UNCONFIRMED`.

There is no HTTP shape/version change from1.17, no new public endpoint or private
MCP tool, and no Domain, browser, schema, grant or credential/capture change.
This security correction precedes the dedicated documentation and full
refactoring phases, which remain after product acceptance.

## Deterministic and wire evidence

| Requirement | Exact test | Cases |
| --- | --- | ---: |
| Membership hidden during joined native work cannot publish a result | `MembershipLostDuringJoinedNativeWorkCannotPublishItsResult` |3|
| Expiry, denial and provider outage cannot become successful native results | `ProviderExpiryDenialOrOutageAfterNativeWorkCannotBecomeAResult` |9|
| Known access loss survives failed cleanup without retirement acknowledgment | `KnownAccessLossWithFailedCleanupClearsAccessWithoutAcknowledgingRetirement` |6|
| Outage plus failed cleanup cannot invent known membership loss | `ProviderOutageAndFailedCleanupCannotInventConfirmedAccessLoss` |3|
| Actual close remains joined before capacity can be released | `PostWorkDenialJoinsActualCloseBeforeReleasingCapacity` |3|
| Native policy errors are not authorization facts | `NativePolicyFailureAndFailedCleanupCannotBeRelabeledAsMembershipLoss` |3|
| Real HTTP denial contains safe problems and no captured/private data | `ActualHttpPostWorkDenialIsSafeProblemWithoutCapturedData` |12|

The first3 cases fail on the original code, then pass with the post-work check.
Six additional cases fail because cleanup masks401/404 as503, then all24 pass
with the explicit combined diagnosis. The expanded39 cases plus217 existing
reader/HTTP/schema/isolated-reader checks pass256. Nine existing exact
authorization-count cases now explicitly require the pre/post checks while
retaining their denial codes and0 capture/close assertions. Twelve actual local
HTTP cases check status, media, no-store, safe support reference, absent captured
data/private canaries and the401 Bearer challenge. Native seams are deterministic
and cannot substitute for actual deployed acceptance.

Raw red/green/focused logs, TRX and assertion review are retained under the
private `.git/testagent/reader-authority-20261102/` directory. UI regression checks
exercise the unchanged reset behavior for combined401/403/404 observation
failures; this is consumer compatibility, not a new deployed frontend feature.

## Remaining acceptance

After full verification and green CI, install and verify the exact151-file
artifact and runtime image. Use the original signed-in application account and
only its isolated test workspace for real hosted access changes. Preserve the
working membership, foreign sentinel isolation and original provider identity.
Retain actual denial, cleared consumer results/media and exact native absence or
explicit unconfirmed ownership. Do not label a denial before observation as
revocation during native capture. Provider expiry, observation, graph, original
file pages and playback loading remain separate acceptance cases. No human/AT,
arbitrary-site or legal-sufficiency completion follows from these checks.
