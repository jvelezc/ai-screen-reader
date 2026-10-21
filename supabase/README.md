# Hosted database foundation

The admission migration is installed in the selected project `gpnijaxrgtwzwtrqsbcu` as `20261021201121`. It was applied through Supabase's migration API after schema/history inspection and eight-job CI acceptance. The source filename matches the API-assigned version; its SQL bytes are unchanged. The worker-lease migration is local and awaits its own acceptance before deployment. Inspect existing schema/grants, history and extension compatibility before each subsequent migration. Admission intentionally fails on an existing `hsr` schema or `hsr_scan_jobs` queue rather than silently adopting unrelated data.

Run the isolated integration proof from the repository root:

```powershell
pwsh -NoProfile -File scripts/Test-DurableDatabase.ps1 -Runtime podman
```

Use `-Runtime docker` on a Docker host. No hosted credentials are needed. Never run `tests/database` scripts against the hosted project: they create synthetic users and use test-only administrative sessions.

Admission currently queues only a fixed synthetic fixture. The local lease protocol does not start a browser or worker. [Admission scope](../docs/decisions/0035-tenant-scan-admission.md), [worker ownership](../docs/decisions/0036-fenced-observation-worker-leases.md) and [lease acceptance](../docs/validation/worker-leases-v1.md) distinguish tested database contracts from actual execution and deployment.
