# Managed approved-public reader runtime

November2,2026. Local Windows host with real rootless Podman Linux resources
passes the managed source and abrupt-parent-exit proofs. The focused suite
discovers/passes165, failed/skipped0. Strict build has0 warnings/errors, the full
Windows suite discovers/passes3519 with0 failed/skipped, and required format is
clean. All nine CI41506870871 jobs now pass, including3519 tests on each OS and
the actual Docker normal/crash/recovery proof. Private deployment41510425776
promotes fdb867c. Independent rehashing confirms all156 installed files,
4004213350 bytes, with0 missing/extra/mismatched files; manifest SHA-256 is
`3e4764f5059b1bf7312bd0f4ac7278dd4f99b47d7608ff89975999325998a6f9`.
The rootless image `sha256:f630c2bbe12f68fbf2eff4449d57d283f1cc4ba1cfc2d90c9c15b1e19aadfdf7`
matches independently, owned containers/networks are0 and PowerShell is installed. The last
original-account acceptance is9455013/HTTP1.17/private MCP21 through Edge12 and
UI00bc2bb. This does not expose hosted URL admission or publish a UI.

## Resulting behavior

A trusted host can open a credential-free approved HTTPS source using an immutable
image ID. Before native creation, the supervisor flushes an exact ownership intent
binding organization, session, source, runtime and image. The group has three
containers and two networks. Runtime child environments exclude application bearer
tokens and storage credentials. Source-bound Open/Observe/Close use the existing
strict public wire; there is no fixture digest, dispatched input or verified goal.

The journal lock covers startup recovery through owned retirement. Startup validates
the complete inventory before deleting anything. Cleanup checks every existing
resource's name, owner label and container image before removing immutable IDs,
then independently verifies all five names are absent. A durable acknowledgement
precedes intent retirement. Mismatched, malformed or unconfirmed ownership retains
the record and capacity. An unrelated owner cannot be deleted by this recovery.

At most two live reservations are permitted. Sessions retain the existing
two-minute/100-capture limits; maintenance retires expired ownership. Wrong-workspace
retirement and duplicate admission cannot affect a live reader. Native Closed,
joined client transport and all-resource absence are separate facts. Recovery of
an abandoned group does not establish a timely native Closed or a website goal.
The scoped organization contract is not original-account RLS acceptance.

## Actual runtime receipts

`Test-PublicReaderSupervisor.ps1`, final rund4efb8e562d941bcb49638dbcdc4619c,
uses image `sha256:e8bac1986124af784c046b3671b6b621b50d5dc1899dffa56e344e30663aa732`.
Normal run8898d87f9c9e413f868b476ade61978f opens
`https://screenreader.privensic.com/ci/v1/source`, captures9 nodes twice in the
same document, rejects foreign retirement and duplicate open, and confirms
native shutdown, one explicitly retained known Podman stdin-close warning, all
resource absence and zero intents. The earlier accepted normal run377b28bcdfcd4d238c71d3edc2e49a07 has zero warnings.
A clean restart after retirement recovers nothing.

The separate child opens the actual source and persists its intent. Before an
actual forced parent-process exit, independent native inspection confirms its
three containers and two networks exist and match the owner/image. A fresh
supervisor recovers one intent and independently confirms container/network
absence before retirement. A different owner's network survives recovery and is
then removed by exact ID. Pending intents end at0. No native-shutdown inference
is made from this crash recovery. This local host is Windows; Linux resources run
inside the Podman VM. Docker/Linux CI independently passes normal run
ac349a932f8a41829b1d05e9e1defd3d, proof5f11edd2d2894af89eba1cb8dfc78e79,
with image `sha256:554aef73f32109849aa38eb4914880cdf01ed887dc67a79710eec2e7817b0e7e`.
Both actual captures have9 nodes; native shutdown, transport join, owned absence,
unknown-group refusal and abrupt-exit recovery pass. Docker has0 stdin warnings.

Before killing that child, the proof adds an unexpected network under its owner
prefix. Cleanup refuses the complete group before deletion; independent inspection
confirms the same three container IDs and two original network IDs remain and the
intent still exists. The proof removes only that authored extra network by ID,
then performs the separate crash/recovery check. No refusal is counted as cleanup.

An earlier sentinel-creation check assumed every runtime prints a network ID;
Podman prints a name. That attempt is unaccepted. The proof now inspects the
created name, checks its owner and captures the immutable ID before proceeding.

## Reproduce

With the pinned SDK and a running supported Linux runtime:

```powershell
pwsh -NoProfile -File scripts/Build-IsolatedReader.ps1 -Runtime podman
pwsh -NoProfile -File scripts/Test-PublicReaderSupervisor.ps1 -Runtime podman
```

The existing isolated-prototype CI job runs this additional proof with Docker and
retains `public-reader-supervisor.json`. No credentials, migration, SDK/package
upgrade, privileged identity or public MCP/plugin publication is introduced.

| Requirement | Evidence |
| --- | --- |
| Persist actual source, tenant, session and exact native ownership | `PersistedIntentBindsPublicSourceTenantSessionAndFiveExactNames` |
| Do not discard unknown or changed ownership | `InvalidAcknowledgementCannotDiscardThePendingIntent`, `InterruptedIntentRetirementReconcilesTheExistingExactAcknowledgement` |
| Require pinned configuration and trusted launcher files | `InvalidRuntimeOrMutableImageCannotCreateAJournal`, `EachTrustedLauncherFileIsRequiredBeforeOwnershipIsCreated` |
| Exclusive ownership and pre-admission cancellation | `EmptySupervisorRetainsExclusiveOwnershipUntilDisposeAndRejectsUseAfterClose`, `CancelledAdmissionCreatesNoIntentAndInvalidIdentityIsRejected` |
| Scope denial and complete recovery validation | `ForeignTenantCannotRetireAnExistingDurableIntent`, `CompleteInventoryAndRuntimeAreValidatedBeforeAnyRecovery` |
| Real source-bound normal lifecycle and abrupt-exit recovery | `PublicReaderSupervisorProof.RunAsync` and `Test-PublicReaderSupervisor.ps1` actual receipts above |

Next: trusted tenant-scoped URL jobs, retained actual source results and independent
API/MCP access, consumed through the existing Edge gateway. Then finish the
[customer journey](../product-access.md) and publish its HTTPS UI. The planned
[mock sites](../test-site-scenarios.md) provide working/broken feature cases.
Product acceptance precedes complete documentation, then full refactoring.
