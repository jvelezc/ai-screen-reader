# Versioned mock-site validation plan

Requested November2,2026. Status: planned after the current approved-public worker
increment passes CI. Add the catalogue to the existing test website; no new DNS,
public MCP service, plugin or Supabase project is needed. Proposed `/lab/v1/`
paths are not yet deployed. Product acceptance still precedes final documentation,
then full best-practice/complexity refactoring.

Each scenario needs a stable ID/version, explicit task and initial state, supported
profile/commands, expected reader observations, expected attempted-input and goal
outcomes, evidence requirements and a reset action. A machine-readable manifest
will drive actual MCP/API runs against the served site. Expected facts come from
the authored scenario contract; do not regenerate expectations from test output.
Keep the current CI source/destination pages compatible.

| Mock site | Feature and comparison | Required proof |
| --- | --- | --- |
| Reference shop and keyboard-blocked checkout | Complete the same order by keyboard; compare a working native control with a pointer-only custom control | Actual command/result, focus, AX context, goal state and linked graph/events; separate pointer comparison where collected |
| Named and unnamed controls | Reader names, descriptions and diagnostic inference | Missing names stay missing; visual text or task hints cannot repair reader semantics |
| Forms and errors | Required, invalid, described errors and successful resubmission | Exact exposed state and error linkage before/after input; synthetic data only |
| Dialog and focus paths | Working modal, deliberate trap, restored/lost focus | Real Tab/Shift+Tab/Escape transitions and bounded traversal; a cycle alone is not proof of global unreachability |
| Live updates | Status, alert, busy and changing content | Recorded updates in sequence, with unavailable/missing observations explicit; do not call AX data actual AT speech |
| Tables and composite widgets | Table/header relationships, grid, tabs, listbox/combobox | Supported navigation, selected/expanded state and names; unsupported commands remain explicit |
| Multi-step and frame journeys | History, same-origin frames and dynamic steps | Source/document continuity, graph nodes/edges and actual state transitions; policy denials remain engine/policy outcomes |
| Short, long and looping paths | Objective paths and exploration budgets | Attempted edges, observed nodes, command/goal distinctions and exact stopping reason; no automatic reasonable-effort or legal verdict |
| Capture exclusion | Synthetic sign-in/intervention and resume | Proven AX/image/video exclusion, exclusive input, fresh baseline and explicit assistance history; no real account credentials |
| Environment failures | Missing page, denied destination and unsupported behavior | Explicit engine/policy/unsupported status; never classify these as accessibility barriers |

Every delivered pair must test detection and false positives. Start with the
reference/blocked checkout and names, using the existing private/local MCP client
and approved-host isolation. Add further scenarios when their core profile supports
the required commands. Preserve source URL, fixture identity/version and synthetic
scope in receipts and exported evidence. Mock data is not a real website complaint
or evidence of a particular plaintiff's disability, injury, standing or liability.

Done when the deployed catalogue, its manifest and actual MCP/API receipts agree,
the corresponding browser tests run in CI, and a failed or unsupported task cannot
produce a fabricated success or sufficient website-evidence package.
