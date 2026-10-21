# Hosted database foundation

The migration is prepared for the selected project `gpnijaxrgtwzwtrqsbcu`. It has only been applied to a disposable local database. Inspect live schema/grants, migration history and extension compatibility before applying it transactionally. It intentionally fails on an existing `hsr` schema or `hsr_scan_jobs` queue rather than silently adopting unrelated data.

Run the isolated integration proof from the repository root:

```powershell
pwsh -NoProfile -File scripts/Test-DurableDatabase.ps1 -Runtime podman
```

Use `-Runtime docker` on a Docker host. No hosted credentials are needed. Never run `tests/database` scripts against the hosted project: they create synthetic users and use test-only administrative sessions.

Admission currently queues only a fixed synthetic fixture. It does not start a worker or send browser input. [Decision and remaining gates](../docs/decisions/0035-tenant-scan-admission.md) and [the acceptance matrix](../docs/validation/tenant-scan-admission-v1.md) explain the scope.
