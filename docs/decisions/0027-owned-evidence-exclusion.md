# Scan-owned evidence exclusion

One local owner now composes a private reader, its browser adapter and a typed evidence store. Run the real Chromium demonstration:

~~~powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --owned-scan
~~~

The printed folder contains an append-only scan manifest, payload-free command intents and completions, allowed command results, lifecycle records and fresh resume baselines. Each command intent must be acknowledged before dispatch. Results retain reader outcomes, actual capture windows, snapshots and events separately from input requests, completed calls and uncertainty. A stored result does not mean the action or destination succeeded. The last acquired snapshot can precede interrupted input; it does not invent a post-input state.

Exclusion closes admission immediately, starts reader/browser exclusion and waits for whole commands and their actual writes. Cancelling a caller's wait cannot release a write or acknowledge a capture gap. Late raw results are discarded while completion metadata preserves input facts. A lease is returned only after the excluded lifecycle record is acknowledged. An error, stalled write or unconfirmed transition faults closed with a sanitized code. Failed pending files remain available for inspection; there is no automatic retry or overwrite.

Resume requires the owner's current, unconsumed lease. It preserves the browser context, advances the capture epoch and acknowledges the fresh baseline and permitted lifecycle record before admitting commands. The trusted caller must clear sensitive content before requesting resume. Old epochs, foreign leases, duplicate owners and exhausted budgets cannot dispatch new input. Shutdown waits for actual writes, including a baseline write left running after a timed-out resume, and closes the native context.

The concrete local store writes a new pending file, flushes and closes it, then renames without overwrite. Returned byte counts and SHA-256 hashes describe those serialized bytes. Typed writes reserve at most 4 MiB per file, 64 MiB per scan and 1,024 files; failed reservations are retained. Each owner permits at most 100 commands by default (configurable up to 500) and 64 capture epochs. This is process-level acknowledgement, not a power-loss or hosted durability guarantee.

The ownership registry prevents accidentally wrapping the same adapter twice inside this process. It is not a controller lease or tenant authorization. Previously returned evidence held by external consumers cannot be revoked. Existing independent fixture demos use their own fresh contexts; this owner does not retrofit every external writer. No general recordings, credential entry, human input channel or hosted privacy claim is enabled. See [validation](../validation/owned-evidence-exclusion-v1.md).

## Next: exclusive input ownership

Put every owned command behind one bounded, expiring controller lease and a monotonically fenced generation. Revoke admission before draining actual commands; a cancelled wait or expired lease must not grant a replacement controller while input may still be running. Reject foreign/stale tokens before dispatch, preserve input uncertainty and keep errors distinct from accessibility barriers. Prove contention, expiry, release, cancellation, late completion and shutdown with deterministic races and real Chromium. Keep the first increment local with one AI controller role; human takeover, private viewing and sensitive input remain later explicit protocols.
