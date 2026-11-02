# Original-authority checks after native reader work

Status November2,2026: core5d8aec9/HTTP1.17/private MCP21 passes all9
[CI41432120262](https://github.com/jvelezc/ai-screen-reader/actions/runs/41432120262)
jobs,3348 per OS/database509/native71/operations174. Strict build/format pass.
[Private deployment41435030064](https://github.com/jvelezc/ai-screen-reader/actions/runs/41435030064)
is Ready with151 exact CI/installed files and matching rootless image/TLS/guards.
Existing Edge12 and UI00bc2bb pass821 per OS in CI41439156245; six existing lint
warnings remain. The original account's actual capture-time RLS loss and normal
native retirement are accepted below. Provider expiry and other evidence-load
lifecycles remain separate.

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

## Actual original-account capture-time acceptance

Only the isolated workspace6da73acb-5232-4348-8cb8-dab9d89f72fb is used under the
original SDK account. Reader27e20a25-747d-4b72-a610-63f6ec34cf4e first exposes31
authorized nodes, input0/goalNo. Exact rootless owner labels and two consumed
control/worker loopback samples establish held native work. After the exact test
membership is removed, manual release requires that same owner still paused:
postMutationHoldConfirmed=true. This causal ordering does not rely on comparing
clocks across hosts. Native20-second exchange deadline is unchanged.

The original notebook receives CONTROL_WORKSPACE_NOT_FOUND, support reference
a51fbba10de745968785d2d2fbf171fd; all reader panels/snapshots clear and the account
stays signed in. Independent normal removal is durably recorded at12:08:47.7047738Z,
before server expiry12:09:09Z. Native0, unchanged healthy/unfrozen services and all
four owned timers/one watcher inactive are separately verified. Only the exact
test membership is restored: test1/working1/sentinel0. The original account then
opens its working workspace and sees50 runs and one reader panel. No credential
entry or target input is needed for this read-only test.

Three missed pause-relative timing windows remain retained and inconclusive.
Only the request-coordinated case is accepted. Its source-hashed QA watcher and
independent backstop are removed from active state. Local reader expiry is not
provider-token expiry. Raw receipts and the actual screenshot remain private
under `.git/testagent/reader-authority-20261102/`.

## Remaining acceptance

Use the original signed-in application account and only its isolated test
workspace for remaining hosted access changes. Preserve the
working membership, foreign sentinel isolation and original provider identity.
Retain actual denial, cleared consumer results/media and exact native absence or
explicit unconfirmed ownership. Do not label a denial before observation as
revocation during native capture. Provider expiry, observation, graph, original
file pages and playback loading remain separate acceptance cases. The discovered
[archive error-path gap](archive-read-authority-v1.md) is being corrected. No human/AT,
arbitrary-site or legal-sufficiency completion follows from these checks.
