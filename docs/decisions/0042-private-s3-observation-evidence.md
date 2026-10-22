# 0042: Private S3 observation evidence

## Decision

Keep the existing local flush/readback, then optionally finalize the exact observation in the selected Supabase project's private `hsr-evidence` bucket before fenced database completion. Use the official AWS .NET Core SigV4 handler against the fixed HTTPS project endpoint in `us-east-1`. S3 credentials stay in the trusted server processes, outside the fixture browser, browser client, database, CI and readiness probes.

The object key includes organization, scan, job, generation and the original observation SHA256. Read and validate an existing object before writing. Conflicting bytes fail closed without a repair overwrite. A new upload requires an actual bounded GET and matching bytes, digest, scope and native shutdown facts. An accepted PUT header or ETag alone proves nothing. Content addressing is an application invariant; this is not WORM or object-versioning acceptance.

Archive failure retains local bytes and blocks the job with the existing worker outcome. Safe receipts distinguish storage failure from an accessibility judgment and verified object bytes from database completion. Cancellation joins actual I/O before releasing ownership.

Report access first requires the current user and tenant-visible scan/job metadata. Read the exact authorized object and independently validate it. A missing object may use a verified legacy local file, explicitly labeled `Server disk; object copy not found`. Denial/outage remains unavailable; corrupt objects remain invalid even when a valid local file exists. No traversal, input or objective claim is added.

## Operator boundary

The optional root-owned, mode0600 `/srv/headless-screen-reader/config/evidence.env` has exactly two unquoted lines in order: `HSR_EVIDENCE_S3_ACCESS_KEY`, then `HSR_EVIDENCE_S3_SECRET`. Both must be dedicated S3 credentials. Both systemd services consume this file; their fixed entrypoints copy only the validated pair. Root preflight rejects unsafe file type, symlink, ownership, permissions, size or shape. Missing configuration retains local-only operation. Readiness probes receive neither this pair nor database credentials and make no S3 durability claim.

Generated Supabase S3 keys bypass Storage RLS across buckets, so treat them as trusted server authority. They confer no database authority. Provision through the project's Storage S3 settings and the existing trusted operator SSH connection. Never substitute an Auth JWT, service-role, publishable or management key. Keep credentials out of chat, arguments, GitHub and source. The inspected public Management API has no S3 key-generation endpoint. Verify the private bucket and retain actual selected-provider upload/readback/anonymous-denial proof before accepting hosted durability.

## Validation boundary

Deterministic protocol, coordination, authorization and native UI tests accompany a disposable private RustFS S3 service. The worker harness uploads a real isolated Chromium observation, verifies signatures/readback, deletes only its owned local file, reads the object again, and rejects anonymous/wrong-key access, missing generations, oversized and corrupt objects. Owned service, volumes, network and native ownership are cleaned up. The service stays on an internal network. Podman uses a loopback publication; Linux Docker can use the exact owned container's private bridge address when Docker omits that publication. Only fixed disposable credentials can use this bounded proof endpoint; production authority stays fixed to HTTPS Supabase. Local protocol acceptance stays separate from hosted Supabase, real user Auth, assistive technology and disaster recovery acceptance.

Sources: [Supabase S3 authentication](https://supabase.com/docs/guides/storage/s3/authentication), [compatibility](https://supabase.com/docs/guides/storage/s3/compatibility), [AWS SigV4 handler](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/Runtime/TSigV4SigningHandler.html).
