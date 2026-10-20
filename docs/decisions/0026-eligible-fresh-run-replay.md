# Eligible fresh-run replay

Run one recorded authored checkout preview followed by an explicit request on a fresh trusted browser:

~~~powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --replay
~~~

The printed index links to the historical route, eligibility receipt and independent new traversal. The original receipt is hashed before and after publication. A replay request receives a new request ID and, if attempted, a new traversal run ID. Historical and new goal verification remain separate.

This first policy supports only the unrecorded bundled Traversal fixture's successful checkout-preview route: reader Tab, then Activate. It requires the current reader profile, matching browser/driver/fixture hash, the same headless frame policy, successful observed historical destination and complete unambiguous Tab/Enter evidence. Blocked, cancelled, policy-denied, truncated, recorded, incompatible and uncertain historical routes are ineligible. Trusted host-supplied environment metadata is evidence within this local composition; it is not an authorization capability for arbitrary websites.

The source contract supports one fully observed main document and the fresh capture epoch. It normalizes document, frame and node IDs while preserving node order, exposed semantics, states, parent ownership, active descendants, focus and limitations. A new DOM instance may match; sharing a URL does not establish equivalence. Missing identities, unsupported frame coverage, malformed ownership and excessive source data cannot produce a source proof.

The engine compares a fresh observation before starting a new traversal and rechecks before the first native key. A changed or unconfirmed starting state prevents dispatch and retains its reason. Preflight and rejected observations are kept with URL payloads omitted so a rejection is inspectable. The engine executes reader commands under current browser policy and current focus checks; it does not dispatch historical key records.

Every attempt verifies its destination afresh. Equivalent observed starts cannot guarantee equivalent website behavior: the new result may remain Unknown even after historical success. Current policy denials, browser errors, input uncertainty, cancellation and timeout remain distinct from observed barriers. Captures and key waits have five-second bounds; the authored attempt has a separate fifteen-second limit. Timeout does not prove that an already attempted key failed to occur.

Replay is a separate CLI workflow. Passive reports and video selection still send no live input. No public replay tool, general recording, credential entry, autonomous search or reasonable-effort classification is enabled. See [validation](../validation/eligible-fresh-run-replay-v1.md).

## Next: scan-owned persistence exclusion

Coordinate reader exclusion with the evidence writer for one owned scan. Close admissions to new reader work and owned writes, drain actual pending writes before acknowledging a capture gap, reject stale epoch payloads and retain explicit pending/failure states. Keep the same browser context and require a fresh baseline on resume. Prove the boundary with real files and synthetic canaries alongside the existing browser/reader/media gates. Previously issued immutable results held by arbitrary external consumers cannot be revoked. Sensitive entry and human control must remain disabled until the owned capture/persistence boundary and later ownership protocol are proven.
