# Automated website evidence readiness

2026-10-27. Product name: **Privensic Screen Reader** (user supplied).

The core owns a pure, versioned eight-condition operational assessment. HTTP1.5.0
adds `POST /api/workspaces/{organization}/evidence-readiness`; private hosted MCP
adds `evidence_assess`. The Lovable consumer uses the existing project's
`hsr-core` Edge Function. There is no frontend evaluation engine or human-review
task in this workflow.

Inputs are a website URL, a supplied scanner record, or an explicitly fictional
example (`complete`, `gaps`, `none`). A URL alone returns `ScanResultsRequired`:
no website was visited and no findings were generated. The current fixture
observation worker does not become an arbitrary-site journey scanner through
this endpoint. Imported records are assertions and observations from their
supplier; assessment does not authenticate their origin or prove legal facts.

Outcomes are `Ready`, `InsufficientEvidence`, and
`NoBlockingBarrierDocumented`. Readiness means all eight predefined website-side
requirements are met. No-barrier is restricted to a completed recorded journey;
an empty, partial, timed-out, engine-error or policy-denied collection cannot
receive that outcome. It never establishes site-wide accessibility.

| Condition | Required support |
| --- | --- |
| G1 | Named service task, same-site test URL and expected outcome |
| G2 | Material task-blocking finding supported by completed input and an accessibility failure |
| G3 | Finding-linked action, focus, AX and observed failure event |
| G4 | Keyboard/focus/AX capture declarations plus retained diagnostic events |
| G5 | At least two distinct same-task failed keyboard attempts with separate events |
| G6 | Separately recorded same-task pointer success while the keyboard route fails |
| G7 | Offset-bearing timestamp, URL, browser, OS and test mode |
| G8 | Distinct original video, event-log and AX files, server-reconstructed against complete SHA-256 hashes |

These are product policy `hsr-website-evidence-v1`, not statutory requirements.
Documented findings are supported **within the supplied scanner record**;
suspected findings lack that support. Engine errors and policy denials do not
become barriers. AX/model output is not actual NVDA speech.

`scanJson` preserves the original UTF-8 JSON for strict server parsing, including
duplicate-property and unknown-field rejection. Typed `scan` is also accepted
by HTTP consumers; do not supply both. Scanner input is limited to64KiB,128
events,32 findings,32 artifact references and16 attempts. The assessment route
alone accepts a128KiB envelope; other control requests retain their4KiB limit.
Responses are bounded to120,000 bytes. Cancellation and a20-second server
deadline prevent unbounded work. The gateway retains its own25-second deadline.

G8 never accepts declarations of integrity from scanner JSON. Optional
`archive: { archiveId, indexSha256 }` refers to an existing operator import in
the authenticated route's organization. The server validates the exact index,
reads every retained object once per original, checks part and full-file hashes,
and checks a recognized video header. Each original is bounded to25MiB. Missing,
corrupt, unavailable or wrong-format originals remain explicit failures. Current
workspace authority is checked before work, after every original and before
returning a decision. The25MiB storage bucket policy remains unchanged.

Hash matching establishes retained byte integrity, not video authenticity,
correspondence of every declared event to that video, chain of custody, capture
provenance, or admissibility. The existing archive import is not a worker-run
receipt. Those are distinct remaining evidence-collection gates.

Examples are fictional, carry `illustrative=true`, and always return
`websitePackageReady=false`, including the complete example. Importing a
`demo=true` record cannot authorize a real package. The UI makes this distinction
prominent and reauthorizes export through the core. Account loss, reconnect,
URL/receipt changes and cancelled/stale responses clear the displayed assessment.

Outside scan scope: a particular plaintiff's disability, injury/actual encounter,
standing, legal coverage/jurisdiction/liability/remedies, and judicial
admissibility/custody attestation. There is no automated decision to file suit.
Public MCP/plugin publication remains deferred.
