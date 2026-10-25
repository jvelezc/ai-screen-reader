# Signed-in private reader acceptance

Accepted October 25, 2026 at `1a5c6dc1a54cae7a934288d9f6f0f1c5c88575c5`.
[CI 40229319948](https://github.com/jvelezc/ai-screen-reader/actions/runs/40229319948)
passed all nine jobs: Windows/Ubuntu each 2,004/2,004, native Linux 30/30
and operator checks 122/122. The exact tested backend was promoted by
[private deployment 40230505705](https://github.com/jvelezc/ai-screen-reader/actions/runs/40230505705).
Four independent sequential VPS probes verified release/image hashes,
service and broker ownership, isolated reading, scoped retirement, native
absence and recovery. Public ports and Caddy were unchanged.

The separate confirmed-account check used the original application user's
provider bearer through the private SSH connection. Credentials were entered
in a local masked prompt and were not saved or placed in chat or Git.
Provider authentication and the API's session identity agreed; workspace
bootstrap and subsequent operations used original-user RLS.

| Gate | Observed result |
| --- | --- |
| Application identity | `AppJwtValidated=true`, `OriginalUserRlsChecked=true` |
| Deployed reader lifecycle | `HostedTenantReaderAccepted=true`; Foundation session opened and closed through the actual API |
| Fresh reading | Two explicit observations plus two navigation captures; one document, 31 exposed nodes |
| Navigation evidence | Heading/button cursor progression, matching paged events and directed state/edge evidence |
| Authority | Anonymous and foreign-workspace access denied; closed session subsequently unavailable |
| Independent cleanup | Actual container absence and durable normal-removal receipt confirmed; recovery did not substitute for close |
| Input and goals | Zero dispatched inputs; goal verification remains false |

The first run reached successful API close but its independent Podman verifier
inherited `/root`, which the worker account could not access. That verifier
failed visibly. Changing only its working directory to the worker's home
reproduced a passing independent absence check; a fresh complete account run
then produced the accepted receipt. No product deadline or assertion changed.

This accepts the deployed HTTP reader with a real application identity. The
stdio protocol tests use synthetic API identities and actual Chromium; this
record does not relabel them as provider-authenticated MCP execution. Actual
membership revocation/expiry, human UI/AT operation, authenticated Edge-to-core
connectivity and public MCP/plugin publication remain separate. Saved shop/S3
acceptance also remains separate from transient live reader graphs.

See [the next hosted connection gate](../next-edge-to-core-connection.md).
