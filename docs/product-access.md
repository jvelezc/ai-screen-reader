# Product access: sign in, enter a URL, see results

Updated November 4, 2026. Customer prototype: https://screenreader.privensic.com/ .
The product journey is sign in, enter a website URL, then see saved results.
Publication, original-account acceptance and complete task-evidence collection
are separate gates. The full product is not complete.

## What a person receives

One published application link, access through their own confirmed account, and
three instructions: sign in, enter a website URL and start the test, then read the
automated results. An invite is needed only if access is restricted to invited
accounts. The customer does not need source-repository, Supabase dashboard, VPS,
API-key or MCP setup access. Never share the owner's password or infrastructure
credentials as the product access package.

## Current delivery and limits

The separate Lovable-compatible application now has an HTTPS deployment path.
It uses the existing Supabase project and Edge Function, never a browser-to-core
proxy. Production Auth's site URL and exact callback are configured; existing
local callbacks are preserved. Edge v14 accepts the exact production origin and
retains caller/workspace authorization. Confirmed email remains required.

The [publication receipt](validation/customer-https-publication-v1.md) separates
source CI, verified HTTPS files and original-account browser acceptance. The
first sign-in on this new origin cannot inherit localhost's browser storage.
Once signed in, the existing SDK-managed persistent session remains in use.
No administrator or fabricated session can replace customer acceptance.

Core237d309 is privately deployed with HTTP1.18 and private MCP0.3/26 tools.
Original-account local acceptance already covers URL admission through Edge,
isolated execution, saved page results, original-byte download and restoration.
The current public-URL profile sends **zero target inputs** and makes **no task
attempt or evidence-readiness decision**. It retains actual page information;
it does not prove a blocked checkout, repeat failure or successful comparison.
Controlled fixture assessments and imported scanner results remain distinct.

Existing source and native checks preserve target/egress policy, current tenant
authority, original-byte integrity and cleanup uncertainty. A policy denial or
engine failure is not an accessibility barrier. Automatic core worker promotion
remains held; the separate static UI pipeline does not lift that hold.

## Required implementation and acceptance

1. Complete the original account's published HTTPS journey: sign in, submit a
   permitted URL, retrieve the same saved results after reload/reconnect, and
   verify workspace isolation and explicit failed/unsupported outcomes. Local
   acceptance alone does not establish this new-origin gate.
2. Add versioned working/broken [mock sites](test-site-scenarios.md), then one
   bounded website task at a time through core API/private MCP. Retain actual
   nodes/edges, input outcomes, goal checks, video and required repeat/pointer
   comparison records. Preserve missing or unsupported evidence.
3. Complete hosted intervention/capture-exclusion, human/real-AT, operational
   recovery and supported-profile acceptance in their own scopes.

The prototype link may be shared with its documented limits. A supported product
release requires the remaining gates; never share infrastructure credentials. The report assesses predefined website-side
evidence requirements; it does not establish disability, personal injury, standing,
liability or admissibility. No human-review task is added to the automated result.

The remaining product gates are in [the completion plan](completion-plan.md).
Final user/operator documentation follows accepted product completion, followed
by the requested full refactoring for best practices and lower complexity.
