# Original development goals and next step

Updated October 21, 2026. The original proposal under `outputs/` remains unchanged. This status document tracks implementation against its acceptance gates.

The user has deferred public MCP hosting, Claude setup and official plugin publication. Continue the independent screen reader and evidence-backed workflow.

| Original milestone | Status | Delivered | Remaining gate |
| --- | --- | --- | --- |
| 0: core assumptions | Partial | Real Chromium semantics, separate cursor/focus, bounded keyboard input, same-context browser/reader exclusion, owned local persistence, headed Xvfb continuity, controlled video canaries and combined synthetic excluded-input handoff and scoped loopback semantic and pixel viewing | Authenticated remote provider and handshake; human/AT viewing acceptance |
| 1: local screen reader | Substantial local implementation | Reader profile v13, fixtures, bounded events/observations, policy checks and CI | Capture/privacy feasibility remains a prerequisite to editable entry and recording; broader supported-control scope is explicit |
| 2: evidence-backed workflow | In progress | Bounded routes, append-only JSON, node/edge reports, controlled recording canaries, 20-marker timing proof and original route media with capture/action/event intervals and passive local viewer; bounded diagnostic provenance, explicit fresh-run replay and owned evidence writes | Broader workflow acceptance and diagnostics/search coverage |
| 3: intervention and isolation | Partial | Exact public-host policy, independent Linux egress fence and exclusive local controller leases, headed Xvfb continuity and explicitly simulated excluded input and scoped loopback semantic and pixel viewing | Actual human/AT acceptance and hosted identity/capture/persistence fencing |
| 4: durable hosted execution | In progress | Admission migration installed after eight-job CI and50 PostgreSQL/pgmq cases; worker lease/capability/attempt protocol under local validation | Live JWT acceptance, native coordinator and browser execution, worker process recovery, private S3 finalization and CI deployment |
| 5: hosted pilot | Deferred | Existing local stdio adapter | Public HTTPS endpoint, remote authorization, operations, client acceptance and hosted report |
| 6: measured extensions | Later | Bounded reader improvements through v13 | Broader validated widgets and compatible-run comparisons; public plugins remain deferred |

## Next: leased fixture execution and recovery

The [private fixture viewing client](decisions/0034-private-fixture-viewing-client.md) now displays scoped pixels and retained semantics in a separate fixed Chromium window, with native Pause/Resume, Refresh and End controls. Its focused44-case suite passed. CI39655895713 caught a Windows root-exit delay; the repair adds a Windows-only stop through the already-held owned-root handle while preserving actual exit before4,000ms, callback drain and the5,000ms ownership deadline. Local Windows passed1,247/1,247, focused shutdown checks60/60, Release build/formatting and a separate Linux/Podman headed proof. Replacement [CI39660487347](https://github.com/jvelezc/ai-screen-reader/actions/runs/39660487347) at b83a784 passed all seven jobs, with Windows and Ubuntu each1,247/1,247, zero failures/skips. Four independent Linux headed scenarios and public isolation passed separately. The next implementation is tenant-authorized durable scan jobs, atomic pgmq enqueue, fenced attempts and recovery. Public MCP/plugin publication remains deferred.

The selected database target is Supabase **gpnijaxrgtwzwtrqsbcu**. Access is working through the saved Windows credential; no further CLI setup is needed. Admission is installed as20261021201121 after inspection of the empty project and accepted CI. Readback confirms four RLS-enabled tables, restricted grants and zero app users/scans/messages. The selected worker host is the existing shared Contabo VPS. Read-only SSH inspection confirmed Podman, active Caddy and existing applications; its services are unchanged. Deployment must use separate resources and an unused port. Live JWT, native execution and full hosted durability remain unaccepted. The user did not see the temporary viewer window; human/AT acceptance remains unperformed before human target-input authority.

The [tenant admission slice](decisions/0035-tenant-scan-admission.md) passed50/50 local and Linux CI database cases in [accepted CI39665891030](https://github.com/jvelezc/ai-screen-reader/actions/runs/39665891030), with1,247 tests on each Windows/Ubuntu and all eight jobs successful. It proves a fixed-fixture scan/job transaction, tenant RLS, duplicate binding, actual pgmq rollback, concurrent revocation and orderly database restart. The [worker lease protocol](decisions/0036-fenced-observation-worker-leases.md) now adds private capabilities, immutable attempt facts, expiry/generation fencing, atomic completion and safe blocked/quarantine states. Its [validation](validation/worker-leases-v1.md) is in progress. **NEXT:** pass its gate, apply the lease migration, then run one real fixed observation through a trusted coordinator without exposing database/storage secrets to the browser. No hosted worker has executed.

Try `dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --view-fixture`. It opens a read-only synthetic fixture viewer for up to two minutes. Pause must clear both panes; Resume must restore them; End or closing the window must clean up both owned browsers. This is a manual acceptance task, not a completed human validation. The finite automated counterpart is `--private-client`; [the matrix](validation/private-fixture-client-v1.md) distinguishes actual browser/HTTP proofs from remaining hosted and human gates.

The [capture admission protocol](decisions/0019-capture-admission.md) fences observations and both keyboard paths, drains actual operations after caller cancellation, requires AX stop acknowledgement, preserves the same context, and resets the reader baseline across epochs. Failed or stalled transitions remain closed. The [controlled collector probe](decisions/0020-capture-collector-feasibility.md) adds separately opted-in synthetic recording segments with pending-write drain, decoded video/JPEG canaries, staging inspection, artifact hashes and explicit failure stages. Default sessions start no recording collector.

The [20-marker synchronization probe](decisions/0021-recording-synchronization.md) now uses raw Chromium JSON timestamps, independent visual frame identities and clock calibration. All twenty actual reader actions passed the unchanged 250 ms conservative target locally. The SDK single-precision timestamp is bypassed. Run `dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --recording-sync` to inspect its original video, event timeline, hashes and measured errors. Platform/CI evidence is tracked separately in [validation](validation/recording-synchronization-v1.md).

The [recorded traversal slice](decisions/0022-traversal-recording-evidence.md) now attaches original permitted video and calibrated capture/action/event intervals to four fresh input-free synthetic routes. Run `dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --traversal-recording` and open its printed index. Source/result observations, actual keys, goal verification and video timing remain separate. Missing/partial media never changes a barrier assessment.

The [passive local viewer](decisions/0023-passive-evidence-viewer.md) now supports node/action/event selection, original-video seeking and related timeline highlights, native keyboard operation and explicit unavailable/partial states. Selection sends no live input. The [reader-owned exclusion fence](decisions/0024-reader-capture-exclusion.md) now drains complete commands and idle sampling, blocks late results/timeline retention, rejects stale iterators and preserves same-context resume with a fresh baseline. The [bounded diagnostic inventory](decisions/0025-diagnostic-feature-provenance.md) now keeps declared hypotheses separate from exposed names, binds document/epoch/node evidence to authored traces, and preserves missing/stale cases. Run the fixed four-route demo with --features. The [controlled replay workflow](decisions/0026-eligible-fresh-run-replay.md) now compares fresh source equivalence and current environment/policy, rechecks before first dispatch and retains an independent new result. Run --replay for the fixed successful shop route. The [owned local pipeline](decisions/0027-owned-evidence-exclusion.md) now writes intent before dispatch, drains actual commands and file writes before exclusion acknowledgement, rejects stale epochs and requires a persisted fresh baseline before resume. Run --owned-scan for the real Chromium/files demo. The [exclusive local controller](decisions/0028-exclusive-local-controller.md) now checks expiring authority at command admission and final native dispatch, drains actual work before replacement and persists controller generation without bearer tokens. Run --controller to observe two generations sharing one real Chromium context. The [headed Linux proof](decisions/0029-headed-linux-session.md) now retains actual browser/context/target/document identity across controller replacement on a private Xvfb display, verifies native keys and fresh goal, and confirms browser/display/container/image cleanup. Run scripts/Test-HeadedSession.ps1 with Podman or Docker and open its returned folder. The [synthetic excluded-input handoff](decisions/0030-synthetic-excluded-input-handoff.md) now combines actual protected-field input, explicitly simulated assistance, actual input/write drain, fresh baseline and same-context continuity with canary absence across owned JSON and every original/staging media frame. Run --synthetic-handoff, or add -Scenario synthetic-handoff to the headed harness. Local Windows validation passed 982/982; local Linux/Podman headed acceptance passed separately. The [private read-only semantic viewer](decisions/0031-private-read-only-viewer.md) now binds a process-provisioned identity to one owned scan/session, exchanges a bootstrap once and enforces monotonic expiry and actual delivery drain/revocation through a loopback-only HTTP transport. Viewing uses the last acknowledged observation, sends no input or new capture, and joins actual capture exclusion. Run --private-viewer for the fixed Chromium/HTTP proof, which closes after acceptance and prints no credentials. The [owned shutdown repair](decisions/0032-owned-browser-shutdown.md) preserves actual callback joining and independent process/transport evidence; CI39548114638 passed all six jobs. The [private pixel relay](decisions/0033-private-pixel-viewer.md) passed scoped PNG delivery, exclusion, revocation, input denial and owned cleanup on Windows and headed Linux. The [private fixture client](decisions/0034-private-fixture-viewing-client.md) now supplies a separate read-only Chromium window with native controls and explicit fresh-pixel/retained-semantic timing. Its replacement seven-job CI passed, including Windows and Ubuntu1,247/1,247 each. **NEXT: leased fixture execution and recovery**; admission is installed and worker ownership is under validation. Human target input, real credentials, remote identity, hosted persistence and deployment remain separate gates.

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
    Pixels --> Client[Local read-only viewing client]
    Client --> Next[NEXT: validate leases and run fixture jobs]
    Next --> Durable[Durable hosted scan jobs and recovery]
    Client --> ViewerControl[Human takeover acceptance]
    classDef done fill:#dcfce7,stroke:#166534,color:#14532d
    classDef next fill:#dbeafe,stroke:#1d4ed8,color:#1e3a8a
    classDef planned fill:#f3f4f6,stroke:#6b7280,color:#374151
    class Reader,Trace,Privacy,Timing,Recording,Viewer,ReaderGate,Features,Replay,Owned,Controller,Headed,Handoff,PrivateViewer,Pixels,Client done
    class Next next
    class ViewerControl,Durable planned
    click Client "validation/private-fixture-client-v1.md"
    click Next "#next-leased-fixture-execution-and-recovery"
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
