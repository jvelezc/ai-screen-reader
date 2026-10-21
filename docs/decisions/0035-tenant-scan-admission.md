# Tenant-authorized scan admission

The original hosted execution milestone starts with durable admission. The reader's Domain, ScreenReader and Playwright dependencies remain unchanged. This increment adds database functions and actual PostgreSQL integration checks; a worker process is subsequent work.

The migration creates a dedicated `hsr` schema, organization membership, scans and initial stage jobs. Organization provisioning is administrative. Authenticated clients can read their organization's scans/jobs through RLS, and only their own membership entries. They cannot write tables directly, grant memberships or access the underlying queue/archive. `create_scan` derives its actor from `auth.uid()` and locks current membership until transaction completion. It accepts only the bundled `synthetic-shop` fixture and reader profile v13. No URL, credential or target input is dispatched.

The organization and request UUID identify an admission. Repeating the same creator's request returns its exact receipt. A different creator cannot claim it; another organization may independently use the same request UUID. The unique database constraint serializes overlapping admissions. Scan creation, the actual pgmq send and the job binding share one transaction, so a failed job write rolls back both the scan and queue message. Sequence gaps are permitted. Composite foreign keys bind jobs to scans in the same organization.

Security-definer functions have an empty search path and explicit client execute grants. A browser receives no database/service-role secret. This SQL authorization proof supplies subjects through PostgreSQL's request setting; it does not validate real JWT signatures, membership onboarding or an identity-provider handshake. The `hsr` schema is not added to the project's public Data API configuration by this migration.

The integration runner uses a digest-pinned Supabase PostgreSQL17.6.1.167 image with pgmq1.5.1 and pgTAP1.3.3. It creates a uniquely labeled, disposable container without networking, published ports or host bind mounts, checks the exact TAP plans/results and removes only that owned container and its anonymous volumes. The shared base image is retained. Restart tests prove committed admission survives an orderly database restart. Concurrent tests use two actual database sessions, observe the second waiting for the first transaction, then compare receipts and queue cardinality. A separate overlapping revocation proves membership locking and subsequent denial.

The test-only concurrency coordinator uses the disposable image's `supabase_admin` role to open local Unix-socket sessions. Both admission calls run as `authenticated`; no application grant is widened to accommodate the test. The migration and ordinary tests run as Supabase's non-superuser `postgres` role.

The selected hosted project is `gpnijaxrgtwzwtrqsbcu`. Before deployment, inspect its existing schema, queue names, grants, migration history and extension version; resolve any collision explicitly. Apply the migration transactionally through the project's migration mechanism. Do not run the test fixtures or concurrency coordinator against the hosted database.

Admission deployment completed after CI39665891030 passed all eight jobs. Supabase recorded version20261021201121; the repository filename was aligned without changing SQL bytes. Readback confirmed four RLS-enabled admission tables, denied anonymous access and an empty queue with no app users or scans. The [lease protocol](0036-fenced-observation-worker-leases.md) remains a separate increment.

The user selected an existing shared Contabo VPS. SSH read-only inspection confirmed Podman4.9.3, active Caddy and existing applications on loopback8080/8081/8082/8083/8085. No host service or Caddy configuration was changed. Deployment must use separate resources and an available loopback port, with dedicated CI authorization and a tested rollback. Public MCP hosting/plugins remain deferred.

Remaining gates are live project access and identity acceptance, a narrow worker capability, expiring fenced leases, durable attempt/action records, stage completion/deduplication and crash recovery, private object finalization and actual deployment. A queued job is not an executed scan or verified reader goal.

Sources: [Supabase RLS](https://supabase.com/docs/guides/database/postgres/row-level-security), [Supabase pgmq](https://supabase.com/docs/guides/queues/pgmq).
