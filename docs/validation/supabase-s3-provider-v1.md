# Supabase private evidence acceptance

On October 22, 2026, Supabase MCP verified project `gpnijaxrgtwzwtrqsbcu`
(`ai-screen-reader`, healthy, `us-east-1`) and provisioned `hsr-evidence` as a
private bucket with a 1,048,576-byte limit and `application/json` uploads only.
The connected MCP tools do not create S3 access keys. The authorized dashboard
flow created `hsr-private-evidence-v1`; its project-wide Storage access and RLS
bypass were disclosed and confirmed before creation.

Credentials were written directly to a protected local operator file, read back
and compared, then transported over the existing trusted SSH connection to the
root-owned `0600` VPS configuration. No value appeared in chat, Git, command-line
arguments, release artifacts or readiness output. The local ACL permits only the
operator account and SYSTEM. The generated secret display was closed afterward.

## Actual selected-provider proof

The production `ObservationStore` and `S3ObservationArchive` handled a fresh real
Chromium fixture observation with actual native shutdown validation. The fixture
child received no S3 or database authority. The Windows operator proof confirmed:

| Check | Observed result |
| --- | --- |
| Real fixture, original native cleanup | Accepted, zero inputs, no verified goal |
| Signed upload and exact bounded readback | 31,507 bytes matched |
| SHA256 | `ddb9b48eb60cdc4038e212a805cc93794b8bfe90dffed19cd55411aa6e4acfc0` |
| Fresh archive/client readback | Identical original bytes |
| Existing verified object reuse | Accepted without repair overwrite |
| Different generation | Remained missing |
| Anonymous request for the existing object | HTTP 403 |

This is a provider-only synthetic proof, not a database-admitted tenant scan or
application-user JWT acceptance. One identifiable proof object remains private.
No disaster recovery, WORM, versioning or universal Storage policy claim is made.

## Private deployment

[CI 39854653555](https://github.com/jvelezc/ai-screen-reader/actions/runs/39854653555)
passed all nine jobs at `d0cc99f647e9cd788b898f63002ac4c5568904ea`, including
Windows and Ubuntu 1,599/1,599 each. The accepted artifact was downloaded without
rebuilding, reverified on the VPS, and activated through the existing native
readiness/recovery protocol. Deployment run `5ad82c0f7e284eef9757aeb00e8f9819`
reported Ready from previous release `91b3677`.

The first operator attempt verified the archive but stopped during image
preparation before activation. Both services remained stopped with the previous
release pointer retained. Its original command error was not captured sufficiently
to establish the cause. Resuming the exact reverified stored artifact from the
application home completed image preparation and normal activation. The operator
wrapper now uses that directory explicitly. No archive rebuild, pointer shortcut
or fabricated cleanup was used.

Independent readback found both services active and the private listener reporting
release `d0cc99f` and HTTP contract 1.1.0. Process-environment inspection reported
only presence flags: both trusted services have the S3 pair; only the worker has
the database credential; neither has a management token. No environment values
were exposed. GitHub automatic deployment was held during this promotion and
the first signed-in acceptance. After that gate passed, the existing restricted
CI deployment pipeline was re-enabled; each new promotion still requires every
CI job to pass for the exact current main revision. Public MCP stays disabled.
Shared Caddy was not modified by the application provisioning code.

## Signed-in tenant acceptance

The user signed in to the private application, separately from the Supabase
dashboard. Its actual authenticated workspace admitted shop scan
`7940901d-88f4-4053-9811-221a52767b4b`. The real isolated worker completed job
`02b57a37-6092-4f1d-ad8a-5d76a662b87b`, generation 1. No operator SQL admission,
fabricated bearer, or browser-session token extraction was used.

| Check | Observed result |
| --- | --- |
| Application and tenant authorization | Signed-in workspace admitted and displayed its own run |
| Durable queue completion | Scan observed, job completed, one attempt, no remaining lease or error |
| Authorized application report | Available; evidenceStorage S3; 32 exposed nodes |
| Original observation | 31,510 bytes; SHA256 `dd58ff977248946ab71666e3a3c949e8140be84b97dc05847bc0b9bdabe3bd56` |
| Independent VPS readback | Deployed production archive validated the exact object, job identity and native shutdown facts |
| Storage metadata | Private hsr-evidence object under the exact organization/scan/job/generation/digest path; JSON MIME |
| Anonymous access to that object | HTTP 403 |
| Downloaded report | Same digest and tenant scan; zero dispatched input; goalVerified false |

The separate VPS probe ran as `hsr-worker`, using the already configured control
service S3 pair and manifest-verified deployed libraries. That readback alone
does not exercise application authentication; the signed-in browser flow and
durable database readback supply the distinct admission/report evidence above.
The browser tab had loaded the portal before the latest deployment, so its
markup omitted the newer evidence-location label; the actual downloaded report
explicitly identified S3. No reload was forced because the session is in memory.

The first report completed while the recent-run row still displayed Queued.
Manual Refresh runs corrected it. A follow-up source fix synchronizes that row
from each accepted selected-report response; a real Chromium regression covers
Queued to Observing to both completed and blocked outcomes without manual refresh.
Its source/CI gate is separate from the deployed d0cc99f acceptance above.

## Remaining acceptance

The signed-in fixed-fixture/S3 gate is accepted. Hosted interactive reader
sessions still need a credential-free isolated browser execution boundary;
launching a browser directly inside the credential-bearing control service is
not the next deployment step. Local Chromium tests and published routes do not
prove that deployed capability. The approved Edge-to-core network path, broader
tenant restart/recovery exercises and actual human/AT acceptance remain open.
Public MCP/plugin publication remains deferred.

Source references: [Supabase S3 authentication](https://supabase.com/docs/guides/storage/s3/authentication),
[Supabase MCP tool groups](https://supabase.com/docs/guides/ai-tools/mcp#available-tools).
