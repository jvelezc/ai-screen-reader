# 0071: Restore publication maintenance through the existing readiness gates

Date: October31,2026. Status: implemented; acceptance is tracked in the
[validation record](../validation/publication-maintenance-operator-v1.md).

Actual original-account publication recovery exposed an operator composition gap.
The observation service is a dependency of control. A parallel systemctl start
can launch control before the reader host is ready; ActiveState alone does not
establish that the API is listening. The existing deployment runtime already
checks ordered worker/database readiness, control invocation, PID and HTTP.

Add a root-only maintenance entrypoint for one original organization/job/generation.
Keep the restricted deploy key's forced command unchanged. The operator validates
the managed base and exact current release, holds the same deployment lock, stops
control before observation, and reconciles native ownership through the existing
DeploymentRuntime. It then runs the existing storage-only recovery command in a
new private transient unit under hsr-worker. The existing database/S3 environment
files and explicit evidence directory are supplied server-side. No browser runtime,
new execution lease or replacement input is requested.

The actual transient unit must be inactive/failed with MainPID0 before polling
can resume. Killing or timing out the systemd-run waiter is not process settlement.
Unknown settlement leaves services stopped. After confirmed exit, malformed output
stays Unconfirmed; a known failed/rejected/busy publication can restore service
readiness without claiming publication. Native recovery runs again before the
existing ordered start_ready path. A failed readiness check cannot return Ready.

Maintenance events use a separate schema and retain original scope and release.
The output distinguishes PublicationState from ServicesReady and preserves overall
NotAssessed. Closed receipt validation rejects substituted scope, input authority,
private extra fields and unsafe diagnostic text. The shared bounded process runner
keeps zero as its default accepted exit; this operator explicitly allows the
worker's documented exit3 so its safe JSON failure remains inspectable.

This is explicit private maintenance. It does not enable automatic restart, expose
public MCP/plugins, recover unfinished child media or establish legal sufficiency.
