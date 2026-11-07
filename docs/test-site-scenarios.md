# Versioned mock-site validation plan

The [recorded local task suite](validation/recorded-mock-tasks-v1.md) now adds
three fresh keyboard experiments and an independent recorded mouse comparison
for each v1 scenario. Whole-clip playback, source JPEGs, graph/event reports and
integrity verification are available locally. Event-to-video synchronization is
explicitly unverified on these markerless pages. The hosted admission/HTTP/MCP
and consumer connection remain the next gate. Independent Maestro flows under
`ops/test-sites/maestro/v1/` verify the served pointer behaviors and save screenshots.

Requested November2,2026; first implementation November5. Four fixed v1 scenarios
now have embedded source, an authored machine-readable contract and actual local
Chromium/stdio MCP coverage. The remaining scenarios below are planned. The
catalogue uses the existing test website; no new DNS, public MCP, plugin or
Supabase project. Product acceptance still precedes final documentation, then
full best-practice/complexity refactoring.

## First catalogue

Source: [authored manifest](../ops/test-sites/v1/manifest.json). Public route prefix:
`https://screenreader.privensic.com/lab/v1/`. All scenarios are synthetic; none
submits a real order, payment or account credential.

| Path | Local bundled-fixture identity | Authored expectation |
| --- | --- | --- |
| `checkout-working.html` | `MockCheckoutWorkingV1` | Native Enter, Space and independent pointer click reveal/focus Sample order placed. |
| `checkout-keyboard-blocked.html` | `MockCheckoutKeyboardBlockedV1` | Enter/Space complete as inputs but do not reveal the goal. A fresh pointer click does. |
| `control-named.html` | `MockNamedControlV1` | The button exposes Open sample details; native activation succeeds. |
| `control-unnamed.html` | `MockUnnamedControlV1` | Visual text stays absent from its exposed name; native activation can still succeed. |

Local `reader_status` lists these identities; `reader_open_fixture` opens their
exact embedded source. A fresh `reader_snapshot` supplies the document fence.
Use Tab then Activate for native Enter, or Tab → EnterFocusMode → PressKey Space.
Inspect actual `reader_execute` results and `reader_timeline`; do not infer a goal
from a completed input. Reset scenario hides the result and focuses the action;
opening a fresh fixture starts a new independent experiment.

The graph test retains one node per actual observation and one edge per command,
including focus-mode changes that send no key. Failed keyboard attempts stay
Unknown/TimedOut with no automatic barrier verdict. Pointer comparisons use a
separate fresh actual Chromium mouse experiment; they are not MCP keyboard events.
New mock identities cannot obtain the synthetic credential-entry channel.

`python ops/test-sites/export.py <new-output-directory>` creates six data-only
assets. It checks authored source fingerprints, fixed identities/paths and the
exact inline-script CSP hash before creating output, and refuses an existing
destination. The root-owned Caddy lab handler precedes the consumer UI fallback,
allows only the six files (plus the index directory alias), denies unknown lab
paths, and adds no upstream or core endpoint. Preserve the current CI pages and
full site configuration validation before reload. Retain/verify the preceding
site configuration and release for rollback. Worker automatic promotion remains
held; serving static fixtures does not promote the worker.

These local fixtures use Chromium SetContent with all network requests blocked.
Published copies have separately checked HTTP bytes and script policy. Their
matching hashes do **not** establish an interactive public API/MCP run. The hosted
public profile still sends zero inputs and collects page observations only. Its
hosted task graph/video, repeated attempts, separately scoped comparator and assessment
mapping remain the next implementation gate. Local collection now exists separately.
Exact checks and receipts belong in
[mock-site validation](validation/mock-sites-v1.md).

## Wider scenario plan

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
