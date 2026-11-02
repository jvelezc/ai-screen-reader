# Approved-public read-only worker protocol

November2,2026. Strict build passes with0 warnings/errors, full Windows3464 pass
with0 failed/skipped, all110 focused final cases pass and required format is
clean. The actual Linux VM proof passes. CI is a separate required gate before
the next increment. This is a worker base, not hosted public-target
admission, a website goal assessment, original-account acceptance or a public
MCP/plugin release. Hosted HTTP1.17/private MCP21/Edge12/UI remain unchanged.

The Foundation worker binds an embedded fixture digest. A new
`hsr-public-reader-worker-v1` wire binds the declared credential-free HTTPS URL,
one exact DNS host and `hsr-approved-public-readonly-v1` profile instead. Actual
observed URLs must remain in that approved HTTPS host policy. Public observations
cannot cross-validate as Foundation frames or acquire a fabricated fixture hash.
Open/Observe/Close carry an exact session/sequence/document, at most100 captures
and2048 nodes, bounded strict JSON frames and an existing two-minute lifetime.
Dispatched input stays0 and goal verification staysfalse.

The CLI proves the real namespace/proxy/fence isolation before launching
sandboxed Chromium. It renews a scoped bounded lease and joins native browser
shutdown before Closed. The host explicitly bounds console reads even when a
native pipe ignores cancellation. The launcher copies raw byte streams, joins
transfers and independently removes owned containers/networks. Domain and Worker
retain their dependency constraints; CLI composes the existing Egress verifier.

## Actual native proof

Local Windows launcher → rootless Podman Linux VM → the real approved source:
`https://screenreader.privensic.com/ci/v1/source`. Run65914abd2729400b8968a3f38c4f6023
captures9 actual nodes twice in the same document, with Open/Observed/Closed,
source URL binding, the real root name and no manufactured heading. Native close
and launcher exit are confirmed; parent checks find no owned container/network.
The one known Podman stdin-already-closed warning is retained explicitly only
after valid native Closed, zero exit and exact stdout EOF. Other diagnostics,
extra frames/bytes, malformed data and incomplete shutdown still fail acceptance.
The Docker/Linux CI path must independently pass; local VM proof is not CI proof.

With stdin held open and zero request bytes, a separate native idle check ends
unsuccessfully after131.423s including provisioning/relay cleanup. It observes
the worker's denial and the launcher's unsettled-input denial, no response bytes,
and independent container/network absence. The product's existing two-minute
worker deadline is unchanged. This is deadline/cleanup evidence, not page capture.

Earlier implicit-handle, command-resolution, early EOF, trailing-output and
unknown-diagnostic attempts remain failed/unaccepted. Their ownership checks,
cleanup and private receipts are retained. No failure is counted as a successful
worker proof or an accessibility barrier.

## Reproduce the supported normal path

Use the repository-pinned SDK and a running Podman Linux machine, or Docker on
Linux with the existing sandbox configuration:

```powershell
pwsh -NoProfile -File scripts/Build-IsolatedReader.ps1 -Runtime podman
pwsh -NoProfile -File scripts/Test-PublicReaderWorker.ps1 -Runtime podman
```

The existing isolated-prototype CI job additionally runs this proof with Docker
and retains `public-reader-worker.json`, alongside the existing real public MCP
and isolation receipts. No new package, project, target credential, recording,
database migration, UI proxy or broadened permission is introduced.

| Requirement | Exact tests or actual proof |
| --- | --- |
| Explicit approved source and exact boundary | `SourceRejectsUnapprovedAndCredentialedTargets`, `SourceAllowsOnlyItsDeclaredHttpsHost`, `SourcePinsExactTargetLengthBoundary` |
| Bound identities, operation budgets and zero input/goal | `RequestRejectsWrongBindingOrOperationBudget`, `RequestAcceptsExactSupportedSequenceBounds`, `ResponseRejectsChangedSourceStateAndInventedClaims`, `ObservedResponseAcceptsNodeBudgetAndRetainsUnverifiedOutcome` |
| Joined native closure and exact time bounds | `CloseRequiresJoinedBoundedNativeShutdown`, `CloseAcceptsSettledNativeTimesAtSupportedBounds`; actual native proof |
| Strict bounded wire and legacy separation | `RoundTripBindsPublicSourceWithoutFixtureDigest`, `RequestRejectsUnconfirmedJsonFields`, `RequestAcceptsExactFrameBoundButRejectsNextByteBeforePayload`, `PartialFramesAndEmptyStreamCannotBecomeResponses`, `FoundationAndPublicFramesCannotCrossValidate` |
| Safe configuration and diagnostics | `BadConfigurationCannotReadInputOrRevealValues`, `ProofRejectsUnsafeRuntimeOrMissingLauncherWithoutClaimingAcceptance`, `OnlyNamedRuntimeEofWarningIsRetained`, `UnknownOrUnboundedDiagnosticsCannotBecomeAcceptance` |
| Actual public source and owner removal | `PublicReaderWorkerProof` plus `Test-PublicReaderWorker.ps1`; independent idle receipt |

Next: complete CI, build the [mock-site catalogue](../test-site-scenarios.md), then
connect approved public collection to trusted hosted admission/ownership and the
independent API/MCP. Preserve original caller scope and source provenance through
the existing Edge consumer. Do not enable arbitrary public URLs in the notebook
or turn observations into unsupported task/barrier/evidence-sufficiency claims.
