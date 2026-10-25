# Windows driver-only shutdown witness

CI40240631581 on documentation-only792c26f passed eight jobs but failed one
Windows test:2,003/2,004, zero skipped. The failing test expected an injected
stalled SDK callback to close successfully after driver disposal regardless of
elapsed time. Actual transport disposal took5,428.7336ms; native process-exit
confirmation was5,661.8909ms. The four-second production budget was exceeded.

Production correctly reported `CHROMIUM_BROWSER_CLOSE_UNCONFIRMED`, `Faulted`,
`BrowserDeadlineExceeded=true`, while retaining `ProcessExitConfirmed=true` and
joined actual calls. The accepted API release remained deployed; failed CI did
not promote792c26f. This was an invalid success-only test expectation, not an
authorization, queue, evidence or accessibility failure.

The repaired real Chromium test
`ActualOwnedDriverStop_ConfirmsExitAndPreservesTheOriginalDeadlineOutcome` retains
the real context, driver, retained process handle and kernel-exit witness. Only
the close callback is fault-injected. It requires one actual joined transport
stop, actual root exit, settled calls and original escalation facts. Observed
exit before4,000ms must be `Closed` with no error; exit at/after4,000ms must retain
the exact exception/code, `Faulted` and the deadline flag. Unexpected errors and
missing/active native work fail. The original eight-second test guard remains.
The sibling retained-root capability test still requires a timely successful
close and verifies another private browser remains alive.

| Requirement | Evidence |
| --- | --- |
| Actual exit and bounded success are separate facts | `ActualOwnedDriverStop_ConfirmsExitAndPreservesTheOriginalDeadlineOutcome` |
| Real scoped root stop remains timely and preserves a sibling | `ActualOwnedRootCapability_ConfirmsExitAndLeavesAnotherPrivateBrowserAlive` |
| Driver cleanup cannot extend the original deadline | `DriverCleanup_ReservesItsMeasuredCostWithoutExtendingTheActualExitDeadline`:3,500/3,749ms succeed;3,750/5,400ms cleanup costs fail after the250ms grace |

The deterministic fake-clock cases enforce exact3,999/4,000ms boundaries and the
observed slow-SDK partition independently of CI load. These complement, rather
than replace, the real Chromium path. No production deadline was changed, no
test was skipped or retried, and failure was not converted into a successful
native cleanup receipt. Focused local Windows checks pass8/8. Full local Windows
passes2,005/2,005, zero failed/skipped, with a strict nonincremental Release build
(zero warnings/errors) and full format verification. Replacement CI and exact
deployment remain separate gates; their accepted source receipts belong in the
workflow results and the canonical progress Page.

Replacement sourcebeea0b9 passed all nine jobs in
[CI40242081847](https://github.com/jvelezc/ai-screen-reader/actions/runs/40242081847):
Windows/Ubuntu2,005/2,005 each, native Linux30 and operator144. Both downloaded
TRX files contain the real Chromium repair and all four deterministic cleanup
partitions as passed, with zero failed/skipped. Exact
[deployment40243214756](https://github.com/jvelezc/ai-screen-reader/actions/runs/40243214756)
and fresh host checks passed, image
`sha256:3d51219fe07246bbaf076dc035e34ab07ca928419e0f432105dedc9583b075cf`.
Trusted TLS/IP identity, private broker/read-only API, release/operator hashes
and unchanged unrelated Caddy configuration were independently rechecked.
