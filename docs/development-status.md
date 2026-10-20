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

## Next: capture exclusion feasibility

Prove the original milestone 0 privacy gate before enabling recording or credential entry. Use synthetic canaries only. Demonstrate acknowledged stop of every sensitive collector, the same browser context surviving suspension, explicit observation/recording gaps, and a fresh post-resumption baseline. Failure to stop a collector must prevent sensitive input; failed restart must remain visible. Inspect every persisted artifact and staging file for canary leakage.

After that gate passes, add original permitted recording segments to the current traversal receipt. Measure correspondence between at least 20 fixture action markers and the original frames; retain uncertainty against the proposal's 250 ms target. Do not call milestone 2 complete before those acceptance tests pass.

## Run the completed traversal slice

```powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release --no-build -- --traversal
```

Open the index path printed in the JSON summary. Four routes run in fresh browsers and produce separate receipts. Earlier batches are preserved. The report is passive evidence; selecting a link never sends browser input.

This is authored route execution rather than an autonomous frontier search. Graph version `execution-trace-v2` has one node per retained observation, labeled reader-command/browser-key edges and separate unresolved attempts. SVG files show the directed trace and link to the equivalent observation/event evidence; states are not merged. No video, annotation, takeover or hosted durability is claimed.

## Later: features and reasonable action paths

The [feature-path concept](decisions/0018-feature-path-traces.md) ties future objectives to candidate command/keystroke paths. Preserve the successful and unsuccessful traces needed to assess discoverability, repetitions, backtracking, timing and interaction burden. A bounded search that finds no combination remains Unknown; a large or complex graph alone does not establish unreasonable effort. Feature search and the burden rubric remain future work, and do not displace the capture exclusion gate.
