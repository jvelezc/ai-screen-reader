# Original development goals and next step

Updated October 20, 2026. The original proposal under `outputs/` remains unchanged. This status document tracks implementation against its acceptance gates.

The user has deferred public MCP hosting, Claude setup and official plugin publication. Continue the independent screen reader and evidence-backed workflow.

| Original milestone | Status | Delivered | Remaining gate |
| --- | --- | --- | --- |
| 0: core assumptions | Partial | Real Chromium semantics, separate cursor/focus, bounded keyboard input, same-context browser/reader exclusion, owned local persistence, headed Xvfb continuity, controlled video canaries and combined synthetic excluded-input handoff and scoped loopback semantic and pixel viewing | Accessible private viewing client, authenticated remote provider and handshake |
| 1: local screen reader | Substantial local implementation | Reader profile v13, fixtures, bounded events/observations, policy checks and CI | Capture/privacy feasibility remains a prerequisite to editable entry and recording; broader supported-control scope is explicit |
| 2: evidence-backed workflow | In progress | Bounded routes, append-only JSON, node/edge reports, controlled recording canaries, 20-marker timing proof and original route media with capture/action/event intervals and passive local viewer; bounded diagnostic provenance, explicit fresh-run replay and owned evidence writes | Broader workflow acceptance and diagnostics/search coverage |
| 3: intervention and isolation | Partial | Exact public-host policy, independent Linux egress fence and exclusive local controller leases, headed Xvfb continuity and explicitly simulated excluded input and scoped loopback semantic and pixel viewing | Accessible private viewing client, actual human acceptance and hosted capture/persistence fencing |
| 4: durable hosted execution | Planned | Local append-only receipts are a starting contract | Supabase authorization, durable attempt storage, pgmq idempotency/fencing, private S3 finalization and failure/recovery tests |
| 5: hosted pilot | Deferred | Existing local stdio adapter | Public HTTPS endpoint, remote authorization, operations, client acceptance and hosted report |
| 6: measured extensions | Later | Bounded reader improvements through v13 | Broader validated widgets and compatible-run comparisons; public plugins remain deferred |

## Next: pass the Windows shutdown gate, then build the private viewing client

The immediate gate is the [owned browser shutdown repair](validation/browser-shutdown-v1.md). CI39539944407 passed all five Linux jobs, including Ubuntu1,171/1,171 and all demos, but Windows passed1,169/1,171 with two native shutdown deadlines. The repair now stops its privately owned transport after two seconds without root-exit confirmation, then still requires actual process-exit confirmation within the unchanged four-second native wait and joins every actual callback. The five-second outer ownership deadline remains unchanged. Local checks and replacement six-job CI must pass before the accessible viewing client begins. The original native cause remains unmeasured; no late cleanup is counted as success.

The [capture admission protocol](decisions/0019-capture-admission.md) fences observations and both keyboard paths, drains actual operations after caller cancellation, requires AX stop acknowledgement, preserves the same context, and resets the reader baseline across epochs. Failed or stalled transitions remain closed. The [controlled collector probe](decisions/0020-capture-collector-feasibility.md) adds separately opted-in synthetic recording segments with pending-write drain, decoded video/JPEG canaries, staging inspection, artifact hashes and explicit failure stages. Default sessions start no recording collector.

The [20-marker synchronization probe](decisions/0021-recording-synchronization.md) now uses raw Chromium JSON timestamps, independent visual frame identities and clock calibration. All twenty actual reader actions passed the unchanged 250 ms conservative target locally. The SDK single-precision timestamp is bypassed. Run `dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --recording-sync` to inspect its original video, event timeline, hashes and measured errors. Platform/CI evidence is tracked separately in [validation](validation/recording-synchronization-v1.md).

The [recorded traversal slice](decisions/0022-traversal-recording-evidence.md) now attaches original permitted video and calibrated capture/action/event intervals to four fresh input-free synthetic routes. Run `dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --traversal-recording` and open its printed index. Source/result observations, actual keys, goal verification and video timing remain separate. Missing/partial media never changes a barrier assessment.

The [passive local viewer](decisions/0023-passive-evidence-viewer.md) now supports node/action/event selection, original-video seeking and related timeline highlights, native keyboard operation and explicit unavailable/partial states. Selection sends no live input. The [reader-owned exclusion fence](decisions/0024-reader-capture-exclusion.md) now drains complete commands and idle sampling, blocks late results/timeline retention, rejects stale iterators and preserves same-context resume with a fresh baseline. The [bounded diagnostic inventory](decisions/0025-diagnostic-feature-provenance.md) now keeps declared hypotheses separate from exposed names, binds document/epoch/node evidence to authored traces, and preserves missing/stale cases. Run the fixed four-route demo with --features. The [controlled replay workflow](decisions/0026-eligible-fresh-run-replay.md) now compares fresh source equivalence and current environment/policy, rechecks before first dispatch and retains an independent new result. Run --replay for the fixed successful shop route. The [owned local pipeline](decisions/0027-owned-evidence-exclusion.md) now writes intent before dispatch, drains actual commands and file writes before exclusion acknowledgement, rejects stale epochs and requires a persisted fresh baseline before resume. Run --owned-scan for the real Chromium/files demo. The [exclusive local controller](decisions/0028-exclusive-local-controller.md) now checks expiring authority at command admission and final native dispatch, drains actual work before replacement and persists controller generation without bearer tokens. Run --controller to observe two generations sharing one real Chromium context. The [headed Linux proof](decisions/0029-headed-linux-session.md) now retains actual browser/context/target/document identity across controller replacement on a private Xvfb display, verifies native keys and fresh goal, and confirms browser/display/container/image cleanup. Run scripts/Test-HeadedSession.ps1 with Podman or Docker and open its returned folder. The [synthetic excluded-input handoff](decisions/0030-synthetic-excluded-input-handoff.md) now combines actual protected-field input, explicitly simulated assistance, actual input/write drain, fresh baseline and same-context continuity with canary absence across owned JSON and every original/staging media frame. Run --synthetic-handoff, or add -Scenario synthetic-handoff to the headed harness. Local Windows validation passed 982/982; local Linux/Podman headed acceptance passed separately. The [private read-only semantic viewer](decisions/0031-private-read-only-viewer.md) now binds a process-provisioned identity to one owned scan/session, exchanges a bootstrap once and enforces monotonic expiry and actual delivery drain/revocation through a loopback-only HTTP transport. Viewing uses the last acknowledged observation, sends no input or new capture, and joins actual capture exclusion. Run --private-viewer for the fixed Chromium/HTTP proof, which closes after acceptance and prints no credentials. Local Windows now passes 1,186/1,186, with all 1,171 prior cases retained. The [owned shutdown repair](decisions/0032-owned-browser-shutdown.md) adds actual-task joining, context escalation and explicit failure receipts; the earlier Windows failures remain catalogued. CI39528421432 passed all five jobs with 1,085/1,085 on each operating system. The [private pixel relay](decisions/0033-private-pixel-viewer.md) now delivers scoped fresh fixture PNGs through the same actual revocation/capture drain. Its fixed proof decodes green/blue delivered pixels and a separately excluded memory-only red control, rejects input/clipboard at the actual HTTP boundary, and confirms owned cleanup. Run --private-pixels or use -Scenario private-pixels with the headed harness. Windows passed 1,186/1,186 and local Linux/Podman acceptance passed; replacement six-job CI is pending after Windows shutdown failures. The shutdown repair shares the same four-second native wait budget, reserves cleanup time with an early owned transport stop, independently confirms root-process exit, joins actual callbacks and preserves the five-second ownership deadline. SDK temp-directory erasure outside the disposable container is not verified; credentials remain gated. **[NEXT: accessible private viewing client](decisions/0033-private-pixel-viewer.md#next-accessible-private-viewing-client).** Prove the client authority/origin boundary, visible excluded/expired/revoked states, keyboard operation and cleanup before human control. Browser UI, remote identity-provider acceptance, real credentials, human acceptance and hosted durability remain separate work.

```mermaid
flowchart LR
    Reader[Reader v13] --> Trace[Authored node/edge traces]
    Trace --> Privacy[Controlled capture exclusion]
    Privacy --> Timing[20-marker synchronization]
    Timing --> Recording[Original route recordings and frame links]
    Recording --> Viewer[Passive local playback viewer]
    Viewer --> ReaderGate[Reader-owned exclusion fence]
    ReaderGate --> Features[Bounded diagnostic provenance]
    Features --> Replay[Eligible fresh-run replay]
    Replay --> Owned[Owned local evidence exclusion]
    Owned --> Controller[Exclusive local AI controller]
    Controller --> Headed[Headed Linux session continuity]
    Headed --> Handoff[Synthetic excluded-input handoff]
    Handoff --> PrivateViewer[Scoped private semantic viewing]
    PrivateViewer --> Pixels[Private read-only pixel relay]
    Pixels --> Gate[NEXT: pass Windows shutdown CI]
    Gate --> Next[Accessible private viewing client]
    Next --> ViewerControl[Human takeover acceptance]
    ViewerControl --> Durable[Durable hosted execution]
    classDef done fill:#dcfce7,stroke:#166534,color:#14532d
    classDef next fill:#dbeafe,stroke:#1d4ed8,color:#1e3a8a
    classDef planned fill:#f3f4f6,stroke:#6b7280,color:#374151
    class Reader,Trace,Privacy,Timing,Recording,Viewer,ReaderGate,Features,Replay,Owned,Controller,Headed,Handoff,PrivateViewer,Pixels done
    class Gate next
    class Next,ViewerControl,Durable planned
    click Gate "validation/browser-shutdown-v1.md"
    click Next "decisions/0033-private-pixel-viewer.md#next-accessible-private-viewing-client"
```

Green means an implemented, locally validated bounded capability; blue is the next increment; gray is planned. These are not full milestone-completion labels. CI, deployment and assistive-technology gates remain separate.

## Run the completed traversal slice

```powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release --no-build -- --traversal
```

Open the index path printed in the JSON summary. Four routes run in fresh browsers and produce separate receipts. Earlier batches are preserved. The report is passive evidence; selecting a link never sends browser input.

This is authored route execution rather than an autonomous frontier search. Graph version `execution-trace-v2` has one node per retained observation, labeled reader-command/browser-key edges and separate unresolved attempts. SVG files show the directed trace and link to the equivalent observation/event evidence; states are not merged. The ordinary `--traversal` remains unrecorded; `--traversal-recording` uses the input-free sibling fixture and adds an offline HTML viewer and original video links. Annotation, takeover and hosted durability remain open.

## Later: features and reasonable action paths

The [feature-path concept](decisions/0018-feature-path-traces.md) ties future objectives to candidate command/keystroke paths. Preserve the successful and unsuccessful traces needed to assess discoverability, repetitions, backtracking, timing and interaction burden. A bounded search that finds no combination remains Unknown; a large or complex graph alone does not establish unreasonable effort. Feature search and the burden rubric remain future work, and do not displace the capture exclusion gate.
