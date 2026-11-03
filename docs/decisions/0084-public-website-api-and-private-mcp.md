# 0084: Public website observation API and private MCP

## Decision

Expose the existing approved-public read-only worker through a separate tenant-
scoped resource in core contract1.18. The Lovable application remains a consumer
through the existing Supabase Edge gateway. The private MCP client consumes the
same HTTP API; it does not own admission, worker execution or evidence logic.

`POST /api/workspaces/{organization}/website-scans` accepts only `requestId` and
`url`. The caller retains a nonempty request UUID before sending. A202 receipt
identifies the durable scan/job and canonical source; it does not acknowledge
execution or a completed evidence package. An uncertain response requires an
explicit retry with the same UUID and URL. There is no automatic resubmission.
An exact existing request can be reconciled while the processor is unavailable;
a new admission requires the private broker's public-worker readiness signal.

The trusted poll worker advertises readiness only after schema/recovery checks.
The independent fixture broker does not advertise public-job support. The new
`hsr.read_public_scan_status` migration must be installed before promoting the
worker version that requires it. Readiness is operational availability, not a
tenant permission, collection receipt or future execution guarantee.

## Reads and authority

The list returns at most20 recent metadata rows within128KiB and explicitly
reports omitted history. Status reads join the exact scan/job/lease metadata in
one provider snapshot. Leased jobs report Active, Expired or Missing worker
permission at a recorded provider time. An expired lease does not authorize a
consumer replay or assert native process termination.

Each API operation checks the original caller's current workspace membership
before and after work, including mapped failures. Provider requests retain the
original bearer and fixed project. Foreign/duplicate/unknown fields, conflicting
source identity and malformed provenance fail closed. No private lease token or
queue capability appears in the contract.

Completed originals are read only after validating the exact organization,
scan, job, generation, producer, canonical source and whole-byte SHA. Metadata
is checked again after reading; a changed result or lost access cannot return
previously loaded observations. Original payloads retain their existing1MiB
database bound. This is separate from the private evidence bucket's25MiB limit.
Only the successful original-row response has the larger JSON envelope budget;
authentication, errors and other provider responses keep their existing bounds.

Capture pages contain at most100 nodes and128KiB. Continuations pin the original
SHA and preserve the exact next offset; an oversized first node is an explicit
failure, never silently skipped. Original byte pages are at most64KiB, aligned
to the byte-page boundary or exact EOF, and include whole/page hashes. A consumer
must reauthorize at EOF before handing over a reconstructed original. Selectors
are path parameters because the host continues to reject query strings.

## Scope of results

This profile retains two passive same-document captures, actual source semantics,
zero dispatched inputs and an unassessed goal. Page text is untrusted website
data. These observations do not become a task attempt, recorded user interaction,
actual screen-reader speech, proven barrier or sufficient evidence package.
The API and MCP preserve those limits in every completed page. Worker errors,
policy denials, absent originals and unsupported targets remain distinct.

Private MCP0.3 adds `website_scan_start`, `website_scan_list`,
`website_scan_status`, `website_observation` and `website_original`, bringing its
hosted tool count to26. The default local tool set is unchanged. The client is
still private through SSH; this change does not publish an MCP endpoint/plugin.

## Acceptance

See the [requirement-to-test matrix](../validation/public-website-api-v1.md),
[HTTP examples](../protocol/core-api.http) and existing
[actual isolated collection proof](../validation/public-website-collection-v1.md).
Local synthetic-provider protocol tests, real database/native checks, CI, exact
private promotion and original-account application acceptance are separate gates.
The [customer release gate](../product-access.md) is one published application
link, sign-in, URL entry and persisted actual results through Edge.
