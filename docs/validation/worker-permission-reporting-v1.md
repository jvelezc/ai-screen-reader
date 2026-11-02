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

## Current implementation and checks

- HTTP1.15 snapshot and shared Domain validation are implemented locally.
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
  tests and production build on Windows/Linux. Core CI, exact private deployment
  and new original-account acceptance are pending.

## Delivery gates

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
