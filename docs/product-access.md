# Product access: sign in, enter a URL, see results

Requested November2,2026. This is the required customer journey and release gate.
It is not a claim that a shareable application is ready today.

## What a person receives

One published application link, access through their own confirmed account, and
three instructions: sign in, enter a website URL and start the test, then read the
automated results. An invite is needed only if access is restricted to invited
accounts. The customer does not need source-repository, Supabase dashboard, VPS,
API-key or MCP setup access. Never share the owner's password or infrastructure
credentials as the product access package.

## Current verified limits

The notebook has been accepted at `http://localhost:6087/` on the developer's
computer. That address cannot be sent as an application link for another person.
No published UI origin is recorded in the repo; the Edge gateway defaults to the
two exact local origins. The authenticated core API is independently reachable,
but its API URL is not the customer application. The public test website is also
not the customer application.

The current UI can use recorded controlled-fixture results and explicitly imported
scanner data. A URL alone requires real backend scan results; it must not fabricate
findings. The approved-public read-only worker base now has real source and native
cleanup proof. Its managed ownership and actual abrupt-exit recovery pass in
CI41506870871 on Windows and Ubuntu with3519 tests each; all nine jobs pass.
Private deployment41510425776 promotes fdb867c. This is operational deployment,
separate from the last original-account HTTP1.17/private MCP21 acceptance.

The next local increment now connects canonical public URLs to scoped queue jobs,
an actual isolated worker and two retained same-document captures. Its actual
[collection receipt](validation/public-website-collection-v1.md),631 database
checks and180 Linux operational checks pass. Core API/MCP admission and result
retrieval, existing Edge consumption and the customer form remain to be connected.
Read-only observations leave task outcomes and evidence sufficiency unassessed.
This increment is not hosted; automatic promotion is temporarily held until the
tested schema and operational readiness are installed together.
Existing signup requires confirmed email; a new account must
receive its own authorized workspace or an explicitly granted membership.

## Required implementation and acceptance

1. Connect the approved public-source worker to trusted tenant-scoped admission,
   isolated execution and retained results through the independent core API/MCP.
   Preserve target policy, actual source URL, partial/unsupported/error states and
   cleanup uncertainty. Do not relabel a fixture report as a submitted website.
2. Connect the URL form through the existing Supabase Edge gateway to that API.
   Show queued/running/complete/failed states and persisted actual results.
   Keep proven barriers, suspected issues and missing supporting evidence distinct.
3. Publish the separate Lovable-compatible UI to a real HTTPS application origin,
   configure that exact Edge origin and appropriate email callbacks, and verify
   normal signup/sign-in and caller-owned workspace onboarding.
4. Verify the complete journey through the intended original account, refresh and
   reconnect, plus cross-account isolation, denied/unsupported targets and genuine
   failed work. Use the [mock sites](test-site-scenarios.md) as reproducible working
   and broken feature cases, with actual MCP/API receipts. Keep human/AT validation
   separate from automated and agent-operated acceptance.

Only after those checks may an owner send the customer the live application URL
and invite/signup instructions. The report assesses predefined website-side
evidence requirements; it does not establish disability, personal injury, standing,
liability or admissibility. No human-review task is added to the automated result.

The remaining product gates are in [the completion plan](completion-plan.md).
Final user/operator documentation follows accepted product completion, followed
by the requested full refactoring for best practices and lower complexity.
