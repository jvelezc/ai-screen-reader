# Private observation worker

The first host is private through SSH. The worker opens no HTTP port, and these tools do not edit Caddy. Public MCP hosting and plugin publication remain deferred. The report/control API is separate remaining product work.

CI runs the real PostgreSQL/Chromium composition before producing `private-worker-release/release.tar.gz`. Its worker files are the complete tested runtime closure; its fixture image is exported by immutable ID. Promotion downloads that exact artifact only after every main CI job succeeds. It never rebuilds the application after the acceptance gate.

The one-time operator bootstrap is `provision.py`, run over the already trusted root SSH connection. It accepts an in-memory `CONFIG` containing only the selected project ID, accepted commit, fresh deploy public key, runtime password and pinned public CA; `SOURCES` contains exactly the six runtime helper modules named in that file. It refuses unknown accounts, paths, certificate or project. Supply these through SSH stdin, with no credential values in arguments, chat, source or logs. The operator's existing root private key stays on their computer.

The bootstrap installs:

- `/srv/headless-screen-reader`: root-owned immutable releases and deployment receipts; worker-owned private state and separate readiness/evidence roots.
- `hsr-worker`: a separate rootless Podman account with subordinate IDs and its own systemd user runtime.
- `hsr-deploy`: a separate SSH account whose key is restricted to the root-owned fixed deployment command, with no forwarding or PTY.
- `hsr-observation.service`: the trusted coordinator, `Restart=no`, with a private environment file. It is enabled only after actual release readiness succeeds.
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

The host validates the complete bundle before native changes, verifies actual image configuration bytes, loads the image as the worker, stops/reconciles previous ownership and runs `--check-runtime` without database configuration. It then atomically changes `current`, starts the service and requires a fresh invocation's actual database receipt plus a live PID/state sample. A failed release drains owned work before previous-release recovery/restart. Unknown cleanup holds closed. An accepted rollback has state `RolledBack` and exit3 because the requested release was not accepted; it is not reported as a successful promotion.

Inspect the root-owned per-run JSON under `deployments/` and the service's journal through the authorized operator connection. They retain phase, commit and support run ID without credentials or raw browser observations. Interrupted/unknown promotions must be inspected before retry. Do not prune unrelated containers, images, state or shared services. Retained releases are not an off-host backup.

Run operational checks on Linux with `python3 ops/worker/test_release.py`. It rejects zero/incomplete discovery and retains a safe receipt. Windows native reader regression remains a separate required `dotnet test`/format gate. The [validation matrix](../../docs/validation/private-worker-deployment-v1.md) distinguishes actual files/processes, simulated systemd calls, real fixture composition and hosted acceptance.

The public CA comes from [Supabase's pinned Studio certificate reference](https://github.com/supabase/supabase/blob/96d51239c65f22d751482bf15b5fcb2216991233/apps/studio/hooks/custom-content/custom-content.json) and the [official production CA](https://supabase-downloads.s3-ap-southeast-1.amazonaws.com/prod/ssl/prod-ca-2021.crt). SHA256 is `700723581420dd1ac98fd7e9ac529f0ef210eadcaf87fc868a3ad7d114c2f3b7`. VerifyFull remains required; no global trust change is made. Workflow artifact transfer follows the [official action contract](https://github.com/actions/download-artifact) and [workflow completion event](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_run), with additional source/job checks in this repository.
