# Hosted database foundation

Admission20261021201121 and worker leases20261021210027 are installed in the selected project `gpnijaxrgtwzwtrqsbcu`. Each passed its eight-job CI before application through Supabase's supported migration API and readback. Source filenames match API-assigned versions; SQL bytes are unchanged. Inspect schema/grants, history and extension compatibility before each subsequent migration. Admission fails on an existing `hsr` schema/queue rather than adopting unrelated data.

Run the isolated integration proof from the repository root:

```powershell
pwsh -NoProfile -File scripts/Test-DurableDatabase.ps1 -Runtime podman
```

Use `-Runtime docker` on a Docker host. No hosted credentials are needed. Never run `tests/database` scripts against the hosted project: they create synthetic users and use test-only administrative sessions.

Admission queues only the fixed synthetic fixture. The [native worker](../docs/decisions/0037-isolated-observation-worker.md) now runs this stage against a disposable database and isolated Chromium in local acceptance. Hosted native execution, runtime credential provisioning, JWT onboarding and S3 finalization remain separate gates. Never run tests/database scripts hosted or grant clients the worker role. [Admission scope](../docs/decisions/0035-tenant-scan-admission.md), [worker ownership](../docs/decisions/0036-fenced-observation-worker-leases.md) and [lease acceptance](../docs/validation/worker-leases-v1.md) retain earlier protocol evidence.
