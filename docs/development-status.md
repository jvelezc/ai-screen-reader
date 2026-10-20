# Original development goals and next step

Updated October 20, 2026. The original proposal under `outputs/` remains unchanged. This status document tracks implementation against its acceptance gates.

The user has deferred public MCP hosting, Claude setup and official plugin publication. Continue the independent screen reader and evidence-backed workflow.

| Original milestone | Status | Delivered | Remaining gate |
| --- | --- | --- | --- |
| 0: core assumptions | Partial | Real Chromium semantics, separate cursor/focus, bounded supported keyboard activation, optional reference tooling | Capture-exclusion canaries, same-context suspension/resumption, Xvfb takeover and authenticated remote handshake |
| 1: local screen reader | Substantial local implementation | Reader profile v13, fixtures, bounded events/observations, policy checks and CI | Capture/privacy feasibility remains a prerequisite to editable entry and recording; broader supported-control scope is explicit |
| 2: evidence-backed workflow | In progress | Authored bounded routes, independent synthetic alternatives, immutable JSON receipts, observation graph, linked Markdown reports and key-count metrics | Capture privacy first; then original recording segments and measured synchronization, diagnostic feature provenance, local evidence viewing and separate policy-eligible replay results |
| 3: intervention and isolation | Partial | Exact public-host policy and independent Linux egress fence | Capture exclusion, headed Xvfb/private viewer and exclusive input ownership with expiring controller leases |
| 4: durable hosted execution | Planned | Local append-only receipts are a starting contract | Supabase authorization, durable attempt storage, pgmq idempotency/fencing, private S3 finalization and failure/recovery tests |
| 5: hosted pilot | Deferred | Existing local stdio adapter | Public HTTPS endpoint, remote authorization, operations, client acceptance and hosted report |
| 6: measured extensions | Later | Bounded reader improvements through v13 | Broader validated widgets and compatible-run comparisons; public plugins remain deferred |

## Next: precise recording synchronization

The [capture admission protocol](decisions/0019-capture-admission.md) fences observations and both keyboard paths, drains actual operations after caller cancellation, requires AX stop acknowledgement, preserves the same context, and resets the reader baseline across epochs. Failed or stalled transitions remain closed. The [controlled collector probe](decisions/0020-capture-collector-feasibility.md) adds separately opted-in synthetic recording segments with pending-write drain, decoded video/JPEG canaries, staging inspection, artifact hashes and explicit failure stages. Default sessions start no recording collector.

Next, resolve precise recording timestamps. The pinned SDK exposes frame times as single-precision Unix milliseconds, with 131,072 ms spacing at the current epoch; the receipt retains that uncertainty. It cannot prove the proposal's 250 ms synchronization target. Obtain a precise timestamp source and measure at least 20 fixture action markers in original frames before adding permitted recording segments to traversal receipts.

Do not call milestone 2 complete before that acceptance passes. General recordings, complete reader-command/persistence exclusion, credential entry, a human takeover lease and a private viewer remain separate work. Run the current synthetic privacy probe with `dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --capture-probe` and open its printed report path.

## Run the completed traversal slice

```powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release --no-build -- --traversal
```

Open the index path printed in the JSON summary. Four routes run in fresh browsers and produce separate receipts. Earlier batches are preserved. The report is passive evidence; selecting a link never sends browser input.

This is authored route execution rather than an autonomous frontier search. Graph version `execution-trace-v2` has one node per retained observation, labeled reader-command/browser-key edges and separate unresolved attempts. SVG files show the directed trace and link to the equivalent observation/event evidence; states are not merged. No video, annotation, takeover or hosted durability is claimed.

## Later: features and reasonable action paths

The [feature-path concept](decisions/0018-feature-path-traces.md) ties future objectives to candidate command/keystroke paths. Preserve the successful and unsuccessful traces needed to assess discoverability, repetitions, backtracking, timing and interaction burden. A bounded search that finds no combination remains Unknown; a large or complex graph alone does not establish unreasonable effort. Feature search and the burden rubric remain future work, and do not displace the capture exclusion gate.
