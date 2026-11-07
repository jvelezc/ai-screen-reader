# 0085: Screen captured controls before selecting a website task

The requested investigation is whether a website prevented meaningful access to
a service and whether the saved records explain that failure. A passive page
capture cannot answer that question. Keep potential page issues, actual task
outcomes, evidence sufficiency and legal conclusions separate.

## First increment

HTTP contract 1.19 adds
`GET /api/workspaces/{organization}/website-scans/{scan}/screening/{digest}`.
The existing private stdio MCP gains the read-only `website_screening` tool,
bringing its hosted tool count to 27. There is no public MCP or plugin publication.

The core verifies the current caller's membership, completed source/job/generation/
producer and exact original SHA before and after retrieval. It inspects the whole
retained final capture, including nodes beyond the first display page. The result
includes scan identity, original SHA, document identity, capture index/epoch,
policy version and original limitations. Original observation bytes and reader
announcements are unchanged.

The diagnostic engine flags an empty exposed name only on a known interactive
role as `PotentialIssue`. It does not diagnose unnamed layout nodes, headings or
possibly decorative images. A disabled control remains explicitly disabled;
neither that state nor a missing name establishes a failed task. A named
control can still have problems outside this narrow check.

English word heuristics on actual link/button names suggest service discovery,
pricing, enrollment, account access or support journeys. These are investigation
leads, not verified destinations or permission to execute an action. Unknown
purposes remain unclassified. For GentleBirth, prioritize an observed course or
service journey, pricing/enrollment and support over unrelated page navigation.
Do not invent a checkout or assume an existing account is required.

Every suggestion is `NotTested`, with `ExecutionAuthorized=false`. The report
always retains `TaskExecution=NotStarted`, `TaskBarrier=NotAssessed`,
`EvidenceReadiness=InsufficientTaskEvidence` and `LegalCaseDetermined=false`.
The approved-public worker still sends zero target inputs and has no task video.
This increment does not modify that worker profile or enable arbitrary input.

HTTP replies contain at most 40 issues, 20 task suggestions and 128 KiB. Full
counts and separate truncation flags identify omitted items; source names are
never shortened to fit. Private MCP independently validates the response and
fits its own envelope within 128 KiB. Current authorization loss, changed original
or oversized metadata produces an explicit failure without partial observations.

## Next: source-bound task collection

Implement a separately admitted bounded task profile, first against the existing
working/broken mock catalogue. Prove input policy, task-specific success checks,
capture exclusion, owned cleanup and authority loss before public website input.
Retain actual keyboard attempts as nodes/edges, focus and accessibility captures,
timed recording, repeat attempt, and pointer comparison where required by the
predefined evidence policy. Engine/policy failures stay separate from barriers.
Only collected artifacts may satisfy the automated evidence assessment.

Then connect the new core capability through the existing Supabase Edge gateway
to the separate Lovable UI. Do not implement task inference or evidence decisions
in Edge or the browser. Source implementation, CI, private promotion, Edge/UI
delivery and original-account acceptance remain separate gates.

## Basis and limits

[WCAG-EM 2.0](https://www.w3.org/TR/wcag-em-2/) describes identifying essential
functionality and evaluating complete processes. It does not make this heuristic
a conformance evaluation or a legal sufficiency rule.
[DOJ web accessibility guidance](https://www.ada.gov/resources/web-guidance/)
discusses keyboard access and labels, and the limitations of automated checking.
The product's eight evidence conditions are an internal documentation policy,
not eight legal elements. Disability, personal encounter, injury, standing,
applicable law and jurisdiction require information outside a website scan.

See the [validation matrix](../validation/website-screening-v1.md).
