# Interrupted recording recovery: first replay fence

October 28, 2026. **Recovery gate remains open.** This increment prevents silent
reexecution after a keyboard lease expires; it does not yet reconstruct an
interrupted original or prove a crash happened after input.

The new forward migration replaces only the queue claim function. Existing
worker-only grants and empty search_path are retained. Original worker,
generation and immutable attempt metadata survive; no replacement capability
or completed archive is published. Late completion is denied. The user sees a
stopped run and why collection cannot finish by waiting.

| Requirement | Evidence |
|---|---|
| Block expired keyboard replay without a new capability | `ReplayFenceBlocksExpiredKeyboardInsteadOfReexecutingKeys`, `ReplayFenceIssuesNoReplacementInputCapability` |
| Retain original attempt and reject stale publication | `ReplayFenceLeavesOriginalAttemptImmutable`, `ReplayFenceRejectsOriginalWorkersLateCompletion` |
| Keep safe input-free retries | `ReplayFencePreservesSafeZeroInputObservationRetry`, `ReplayFencePreservesSafeZeroInputPassiveRecordingRetry` |
| Preserve job/reason without browser allocation | `InterruptedKeyboardNonclaimRetainsJobAndReasonWithoutAllocatingInputOrLeakingCapability` |
| Stop suggesting pending collection or a barrier | `StoppedRecordingsCannotMasqueradeAsPendingOrAnAccessibilityBarrier` (two codes) |
| Consumer retains safe stopped-run outcome | `keeps execution and evidence separate: interrupted keyboard input`, `rejects unsafe stopped-run error text` |

Isolated PostgreSQL: 289 discovered, executed and passed; zero failed,
unconfirmed or skipped. Container removed, hosted project unchanged. This
includes 14 new fence cases.
It is a database-state proof, not an OS-crash proof.

Local focused .NET: 3 passed, zero failed or skipped. Local UI: 446 passed,
zero failed or skipped; typecheck/build pass and lint has zero errors and six
existing component warnings. UI commit dcdd29e and CI40740569149 pass the same
446 tests on Windows and Ubuntu. The first full Windows .NET run passed
2,399/2,400 with one CLI disconnect while native publishing was running
concurrently. The exact CLI test passes after rebuilding. The final sequential
Windows run discovers, executes and passes **2,400/2,400**, zero failed/skipped.
Strict Release build has zero warnings/errors and full format verification
passes. Commands: repository-pinned `dotnet build HeadlessScreenReader.slnx -c
Release --no-restore -warnaserror`, `dotnet test HeadlessScreenReader.slnx -c
Release --no-build`, and `dotnet format HeadlessScreenReader.slnx
--verify-no-changes --no-restore`.

Native validation on the local Windows host using Linux containers passes
46/46; zero failed, unconfirmed or skipped. Its database and image are removed.
The real restricted worker preserves the interrupted job/reason, creates no
replacement attempt/archive and leaves no native container or observation.
The initial setup-role failure and stale expected job total were corrected;
failed receipts are retained separately. This is simulated lease loss, not
crash-after-input acceptance on a Linux host.

Hosted migration **20261028180856** replaces the claim function with the exact
tested body. SECURITY DEFINER/empty search_path and worker-only execution are
unchanged; anonymous/application roles cannot execute it. The private evidence
bucket remains 26,214,400 bytes (25 MiB), without a MIME restriction.

Windows/Linux CI, actual Linux-host native validation, exact private deployment
and original-account visible stopped-run acceptance remain separate delivery
gates. Their post-commit receipts are maintained in the
[architecture Page](https://chatgpt.com/space/page_9a835b55f9808191a2accbc9900a3e25).
The local checks and hosted schema change alone do not establish those gates.

## Remaining recovery work

Retain durable per-command intent and last confirmed state. Recover interrupted
originals/publication without repeating uncertain input. Inject actual Linux
crash after input, upload-before-commit, storage outage, lease expiry,
cancellation and staging-disk exhaustion. Preserve completed receipts and
recover only independently verified owned containers and bounded capacity.
