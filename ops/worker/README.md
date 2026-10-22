# Private observation worker

The first host is private through SSH. The worker opens no HTTP port; the separate workspace portal binds only IPv4 loopback port8086 and is reached through an SSH forward at `http://localhost:6086`. These tools do not edit Caddy. Public MCP hosting and plugin publication remain deferred.

CI runs the real PostgreSQL/Chromium composition and the published CLI's `--check-control` before producing `private-worker-release/release.tar.gz`. The v2 bundle contains the complete executed worker and published control closures; its fixture image is exported by immutable ID. Every staged control byte and the complete file inventory must match that executed publication. Promotion downloads that exact artifact only after every main CI job succeeds. It never rebuilds the application after the acceptance gate. Existing worker-only v1 releases remain valid rollback targets.

The operator bootstrap is `provision.py`, run over the already trusted root SSH connection. It accepts an in-memory `CONFIG` containing only the selected project ID, accepted commit, restricted deploy public key, existing runtime password, pinned public CA and optional `PortalKey`; `SOURCES` contains exactly the eight runtime helper modules named in that file. `PortalKey` accepts a publishable key or the selected project's legacy anon key. It refuses unknown accounts, paths, certificate or project. Supply these through SSH stdin, with no credential values in arguments, chat, source or logs. The operator's existing root private key stays on their computer. An upgrade must first settle the owned worker and recover retained native ownership; preserve the existing CI key and database password.

The bootstrap installs:

- `/srv/headless-screen-reader`: root-owned immutable releases and deployment receipts; worker-owned private state and separate readiness/evidence roots.
- `hsr-worker`: a separate rootless Podman account with subordinate IDs and its own systemd user runtime.
- `hsr-deploy`: a separate SSH account whose key is restricted to the root-owned fixed deployment command, with no forwarding or PTY.
- `hsr-observation.service`: the trusted coordinator, `Restart=no`, with a private environment file. It is enabled only after actual release readiness succeeds.
- `hsr-control.service`: the loopback portal, `Restart=no`, with only public project configuration in its own environment file. Its closed process environment excludes database and management credentials. It starts after worker database readiness and is enabled only after current-invocation journal, live PID and matching HTTP readiness checks.
- A private app-specific Supabase CA file and configuration for the narrow `hsr_worker_runtime` database login. The operator provisions that login through the authorized management connection, granting only `hsr_worker`; management/service-role credentials are not deployed.

GitHub settings are dedicated to this project:

| Setting | Kind | Meaning |
| --- | --- | --- |
| `HSR_DEPLOY_PRIVATE_KEY` | Secret | Fresh key matching only the restricted deploy account |
| `HSR_DEPLOY_HOST` | Variable | Selected VPS address |
| `HSR_DEPLOY_USER` | Variable | Exactly `hsr-deploy` |
| `HSR_DEPLOY_KNOWN_HOSTS` | Variable | Existing verified SSH host key entry; do not substitute an unverified scan |
| `HSR_DEPLOY_ENABLED` | Variable | `true` only after operator bootstrap and credential setup |

The workflow also supports an explicit accepted `ci_run_id` for initial promotion/recovery. Expired or missing artifacts require a fresh full CI run; they do not authorize rebuilding an untested replacement inside deployment. A superseded source is skipped. Check the deployment receipt's `Ready` state and commit, not just a workflow with skipped steps.

The host validates the complete bundle before native changes, verifies actual image configuration bytes and loads the image as the worker. It confirms portal stop before stopping/reconciling worker ownership, then runs `--check-runtime` without database configuration and, for v2, the candidate's actual `--check-control`. The control probe verifies public configuration, listener readiness and an unauthenticated401, then joins actual HTTP shutdown. It explicitly leaves app JWT acceptance false. After atomically changing `current`, the host requires fresh worker database readiness and then current portal journal/HTTP/PID evidence. A failed release drains owned work before previous-release recovery/restart. A v1 rollback leaves the portal disabled. Unknown cleanup holds closed. An accepted rollback has state `RolledBack` and exit3 because the requested release was not accepted; it is not reported as a successful promotion.

Inspect the root-owned per-run JSON under `deployments/` and the service's journal through the authorized operator connection. They retain phase, commit and support run ID without credentials or raw browser observations. Interrupted/unknown promotions must be inspected before retry. Do not prune unrelated containers, images, state or shared services. Retained releases are not an off-host backup.

Optional S3 finalization uses the root-owned mode0600 `config/evidence.env` file described in [decision0042](../../docs/decisions/0042-private-s3-observation-evidence.md). Its dedicated pair enters only the trusted coordinator/portal; readiness and fixture processes receive no storage credentials. Both units declare the optional file, and root preflight validates it before promotion. No hosted storage claim follows from local protocol proof. Keep automatic deployment held until operator configuration and real provider acceptance are ready.

Run operational checks on Linux with `python3 ops/worker/test_release.py`. It rejects zero/incomplete discovery and retains a safe receipt. Windows native reader regression remains a separate required `dotnet test`/format gate. The [validation matrix](../../docs/validation/private-worker-deployment-v1.md) distinguishes actual files/processes, simulated systemd calls, real fixture composition and hosted acceptance; [S3 validation](../../docs/validation/private-s3-observation-evidence-v1.md) records the new storage boundary.

The public CA comes from [Supabase's pinned Studio certificate reference](https://github.com/supabase/supabase/blob/96d51239c65f22d751482bf15b5fcb2216991233/apps/studio/hooks/custom-content/custom-content.json) and the [official production CA](https://supabase-downloads.s3-ap-southeast-1.amazonaws.com/prod/ssl/prod-ca-2021.crt). SHA256 is `700723581420dd1ac98fd7e9ac529f0ef210eadcaf87fc868a3ad7d114c2f3b7`. VerifyFull remains required; no global trust change is made. Workflow artifact transfer follows the [official action contract](https://github.com/actions/download-artifact) and [workflow completion event](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_run), with additional source/job checks in this repository.
