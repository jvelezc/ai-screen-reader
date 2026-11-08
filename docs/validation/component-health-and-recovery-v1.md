# Component health and bounded recovery

The November 8 incident reached the API with support reference
`def680bb93074971b9d5ee520ad85962` and returned 503. The observation worker had
exited at 10:39 UTC with only `WORKER_STOPPED_UNCONFIRMED`. The exact triggering
exception was not retained; a database outage must not be asserted as its cause.
Ordered recovery passed native probes and restored admission. Retrying the same
request in the original signed-in Chrome session completed the GentleBirth page
review with 1,313 elements. This was passive collection, without task testing.

## Checks and authority

The root-only `health.py` systemd oneshot runs on a 60-second inactive timer and
takes the exclusive deployment lock. Busy deployments skip probes and repair.
Root-owned mode0600 state and latest receipts live under `deployments/`.

| Component | Actual check | Recovery authority |
| --- | --- | --- |
| Worker/control | systemd active state and live main PID | Ordered local recovery |
| Core API | Exact current commit and loopback broker readiness | Ordered local recovery |
| Database queue | TLS runtime login and read-only schema query | Observe provider recovery |
| Reader/public processor | Protected loopback protocol readiness | Ordered local recovery |
| Container runtime/image | Dedicated rootless account inspects pinned image ID | Observe; no global restart/prune |
| Evidence disk | Separate disposable file, flushed and read back | Observe; never delete originals |
| Disk capacity | At least 256 MiB free | Observe; no evidence deletion |
| Private S3 | Signed HEAD of configured bucket | Observe; no object read/write or key rotation |
| Web frontend | Fixed public HTTPS HEAD | Liveness only |
| Edge gateway | Fixed HTTPS OPTIONS | Liveness only |
| Authentication | Fixed project health with existing public key | Liveness only |

The separate probe runs as `hsr-worker`, with the existing protected runtime
database/storage files. No credentials enter arguments or logs. It claims no
job, opens no website, sends no input and saves no original. Missing storage is
explicitly `NotConfigured`. Public frontend/Edge/auth liveness does not prove an
authenticated tenant lifecycle. Native canaries run only during ordered recovery
in the separate readiness scope.

## Recovery policy

Two consecutive local service/readiness failures are required. Database,
evidence, disk-capacity and pinned-image checks must be ready first (storage
may explicitly be unconfigured). External component failure alone does not
restart the reader. Attempts are fsynced before lifecycle changes, with a
15-minute cooldown and a maximum of three in a rolling hour. Clock rollback
cannot bypass cooldown.

Recovery closes API admission, stops the worker, reconciles journal-bound native
resources, runs probes, then starts the worker/API with fresh readiness checks.
Unknown cleanup holds services closed. Recovery never replays task input, erases
failed attempts, repairs evidence or resets uncertain customer requests. Existing
database fencing/expiration rules still apply. Both application units retain
`Restart=no`.

Install the eleven pinned operator modules via the root-owned bootstrap. Deploy
a release supporting `--check-health` before enabling the new timer:

```sh
systemctl enable --now hsr-health.timer
systemctl start hsr-health.service
systemctl status hsr-health.timer
cat /srv/headless-screen-reader/deployments/health-latest.json
journalctl -u hsr-health.service --no-pager
```

Pause automatic repair with `systemctl stop hsr-health.timer`. Monitor failure
exits nonzero as `MonitorUnconfirmed`. Timer activity and its journal need
independent host monitoring: an unreachable VPS or provider outage cannot be
repaired by this process. Off-host alerting/disaster recovery is not implemented
by this monitor.

## Diagnostics and validation

Fatal worker logs now retain a closed stage, exception category, allowlisted
SQLSTATE and transient classification. They exclude exception messages/stacks,
page data, URL, email, connection strings and credentials. Core request logs keep
specific safe processor/database/auth dependency codes and response references.

Focused Windows checks passed 12/12. A real input-free VPS probe returned Ready
for database/schema, broker, public processor, evidence disk and S3, with zero
inputs. Linux operational checks passed 191/191, including actual files and locks
and simulated service lifecycle calls. Full Windows tests, Windows/Linux CI and
installed timer acceptance remain separate release gates; use the release and
host receipts for their current results. No customer-work crash was injected and
no real assistive-technology acceptance is claimed by these checks.
