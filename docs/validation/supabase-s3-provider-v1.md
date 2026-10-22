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
were exposed. GitHub automatic deployment remains disabled, and public MCP stays
disabled. Shared Caddy was not modified by the application provisioning code.

## Remaining acceptance

Sign in to the private screen-reader application at `http://localhost:6086`,
then admit a tenant fixture, observe the real isolated worker completion and
verify the authorized report against its S3 object. Supabase dashboard/MCP
administration is a separate identity. Hosted interactive reader sessions also
need a validated browser runtime on the control host; local Chromium tests and
publication of their API routes do not prove that deployed capability. The
approved Edge-to-core network path and actual human/AT acceptance remain open.

Source references: [Supabase S3 authentication](https://supabase.com/docs/guides/storage/s3/authentication),
[Supabase MCP tool groups](https://supabase.com/docs/guides/ai-tools/mcp#available-tools).
