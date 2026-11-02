# Worker-permission reporting v1

November 1, 2026. Product: Privensic Screen Reader.

## Purpose and done condition

After permission expires, an original caller must see the actual raw `leased`
execution plus an explicit `Expired` snapshot and safe-recovery guidance before
queue reconciliation. The worker must not resend retained input or allocate a
replacement attempt. Subsequent fenced recovery must preserve the original
producer/generation/prefix and block unconfirmed input. A distinct normal run
must still complete with its original graph, media and private S3 evidence.

The earlier [actual expiry receipt](hosted-lease-expiry-v1.md) accepted fencing
and catalogued the missing consumer status. [Decision0082](../decisions/0082-worker-permission-reporting.md)
describes the new independent API/private MCP contract; this is not a legal or
accessibility conclusion.

## Accepted implementation and checks

Migration history reconciliation: the CLI-created source originally used
`20261102031402`; Supabase MCP recorded its actual applied version as
`20261102032301`. The repository filename now matches that observed version.
Its SQL bytes are unchanged, published Git history remains intact and no SQL is
reexecuted. All earlier migration names/versions match the hosted history.

- HTTP1.15 snapshot and shared Domain validation are privately delivered at734c761.
- Private MCP returns `AwaitFencedRecovery`/`canReplay:false` for Expired/Missing.
- Lovable consumer compatibility and an explicit recovery notice are implemented
  in the separate UI repository; existing Edge11 remains a transport.
- The complete local pinned Linux PostgreSQL suite discovers/executes/passes483
  cases, fails/skips0 and removes its owned container. This includes22 new
  permission checks: exact scope, original membership/revocation, database clock,
  no renewal/private capability disclosure, expired/missing permission and denial.
  The outer ad-hoc PowerShell wrapper captured125 from the expected final
  missing-container inspection; the script's independent `run.json` is Ready,
  completed,483/483 and ContainerRemoved:true. CI invocation remains separate.
- The exact new migration is installed through Supabase MCP. Independent catalog
  readback confirms its seven-column result, empty search path, authenticated
  execution and no anon/worker execution. No job or lease is changed by this RPC.
- Strict local Release build passes with0warnings/errors. Full Windows discovers
  and passes3,212 tests, fails/skips0;116 MCP/actual Chromium transport tests also
  pass after the help-text change. Format verification passes. The two earlier
  polling-fixture failures are repaired and independently pass; their first full
  run remains recorded as3,210 passed/2failed. UIb9df07c CI41383238426 passes758
  tests and production build on Windows/Linux.

## Exact delivery and original-account acceptance

All9 [CI41384855222](https://github.com/jvelezc/ai-screen-reader/actions/runs/41384855222)
jobs pass at core734c7614b5412cf7332074e68f565dbc56755f6a. Windows/Ubuntu each
discover/execute/pass3,212, fail/skip0, including67 new permission cases; real
native71, database483 and operations172 receipts are Ready. The first isolated
prototype startup failed before MCP initialized with ISOLATION_VERIFICATION_FAILED/
ClientTransportClosedException; its receipt is retained. One explicit same-source
rerun passes namespace/TLS/private-target denial, actual stdio browse/focus/key
navigation, destination, session close and owned cleanup. No limit or policy was
weakened; the first startup's precise cause remains unconfirmed.

[Deployment41387029664](https://github.com/jvelezc/ai-screen-reader/actions/runs/41387029664)
is Ready (run17e0be7f0aa84422930f5c9af2f0fcfe, previousc6e367b). Every151 CI and
installed file matches manifestb5534ea46e7c8184da5910f708d2187f23503d73405cc264a0d33955542c5210.
Actual rootless imagesha256:478aedfe9f972be251d86aebcb5082b5a538d5e7febda5c341dc7c6383e33698,
default TLS, HTTP1.15 permission/cancellation schemas and anonymous guards pass.
The1,621,823,581-byte archive hashes toc1463c2ad8ddcdbb0fdd536dd8b7599d8c0a8b3347aa5cba8064bf7c3abd1593.

The original account's new route4 scan144078a4-d7e7-4c77-980c-ea89e7978cf7/job
773c2fd5-9336-401c-a878-d88ec0608f6f retains producerf1b8931b-cd16-4315-93bc-1ba8a5cce1f4,
gen1/one attempt. Its exact lease expired at2026-11-02T04:18:31.991867Z while
only its owned coordinator/container were held. The guard observes the confirmed
expiry signal and releases after2.77194s. The worker exits Unconfirmed; native0
is independently verified, with raw leased execution and no result/archive.

Before recovery, the original signed-in notebook → existing Edge11 → core reads
`leased`/Pending, `workerPermission:Expired`, checkedAt2026-11-02T04:19:04.543449Z,
safe-recovery guidance and no-repeat warning. Actual input/goal remain Unconfirmed.
Freshly authorized progress still reads2 records/1intent/0results, lastTab/Unknown.
The snapshot does not grant replay or label an engine failure an accessibility barrier.

Explicit ordered stop/recovery/start blocks the SAME generation/producer with
WORKER_INPUT_OUTCOME_UNCONFIRMED, one attempt/zero leases and null result/archive.
All35 sources/470,934bytes and39-file/479,130-byte retained inventory match before
and after recovery, SHA-256:1b9ad22e9b30fdb9ee06338afc20c5f055455c26f7de1be7a5f528f4d51f9b6f.
The2-record progress digest remains684e2bf5f48352bc9c2cd353599eb994b99c40248a433d5136ab4e72746d3a95.
All32 owned guard checks settle. The blocked original report remains NotCollected.

A distinct normal original-account route1 scan3afcc463-e1a8-4f88-bc37-d9df107c89a5/
job1a2bd109-6fc8-4199-9202-330490aa3fa4 completes under producerb9871677-c264-4d73-ae86-c42770529100,
gen1/one attempt. All5 S3 originals match the1,109,825-byte archive and native files;
59 sources/80 decoded frames,2 actual keys/Reached,3nodes/2edges and closed capture
are independently checked. Index04936f8eec53c5803a68e0f8ce058283f79ac0b9951bad322a101c994432a699;
manifest1dd003954bff05fed6f28e02e755713a2f0c7d0c6b9871a25e671e91445c7c46.
The original notebook verifies5 originals, selects a2/Observed/Satisfied with
86.57021ms uncertainty and Covered1.464072–1.985363s, then plays toEnded3.20s/errornull.
Both services are healthy/unfrozen, Restart=no, native0. Browser reconnection
restores the same user's saved sign-in without requesting credentials.

## Completed delivery gates

1. Finish full Windows tests/format, assertion and behavioral-gap review; record
   actual discovered/passed/failed/skipped counts and named regression cases.
2. Commit core/UI to main; require both CI platforms and all nine core jobs.
   Verify actual database receipt, release artifact and151 installed hashes.
3. Repeat one bounded original-account expiry on that exact release. Read
   raw leased/Expired before recovery through notebook → existing Edge → core.
   Preserve every failed measurement and all originals; never harvest a browser
   token or substitute an administrator/test identity for consumer acceptance.
4. Independently verify retained prefix, actual native absence, original
   generation/producer and ordered recovery. Observe the blocked report, then a
   distinct successful recording with5 matched originals and real playback.

Provider-authenticated stdio MCP, provider-token expiry, storage/network failure,
approved external collection, real assistive technology and operational product
gates remain separate. Public MCP/plugin publication remains deferred.
