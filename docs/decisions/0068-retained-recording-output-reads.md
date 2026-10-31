# 0068: Read retained recording output under the original caller and producer

Date: October 30, 2026. Status: implemented; delivery evidence is tracked in the
[validation record](../validation/retained-recording-output-v1.md).

Decision0067 saves complete validated child output before native cleanup. A lost
publication response can therefore leave complete bytes while the queue job is
blocked. Consumers need to inspect those bytes without treating them as a
completed job, an admitted published archive or permission to repeat input.

HTTP contract1.12 adds two protected operations:

| Operation | Purpose |
| --- | --- |
| `GET /api/workspaces/{organization}/scans/{scan}/recorded-output` | Resolve the current admitted attempt and actual producer; describe retained output or its explicit absence. |
| `POST /api/workspaces/{organization}/scans/{scan}/recorded-output/bytes` | Read one page using `{ "bundleSha256": "<manifest digest>", "offset": 0 }`. |

Private stdio MCP exposes the same independent core behavior through
`scan_output_manifest` and `scan_output_file`. There are eighteen private tools.
This does not publish an MCP endpoint or plugin. The separate Lovable consumer
must still use the existing Supabase Edge boundary.

The manifest distinguishes `NotApplicable`, `NotStarted`, `NotRetained`,
`Incomplete`, `CleanupUnconfirmed` and `Available`. Only Available carries an
export descriptor. That requires a complete validated original bundle and its
matching historical native-removal acknowledgement. Header-only or partially
written output remains incomplete. Old absent media cannot be reconstructed
from command diagnostics. Cleanup acknowledgement does not prove current
custody, database completion, evidence publication or an overall verified goal.

The core resolves workspace, scan, job, generation and actual worker under the
original actor. It checks that scope before and after local reads, including
known storage failures. A changed attempt or producer rejects the response.
Caller-supplied worker IDs, generations, host paths and native ownership cannot
select an export. Passive recordings resolve their producer too; their existing
command-progress endpoint remains NotApplicable.

Exports contain the exact original complete child-output ZIP, bounded at25MiB.
The native journal header/removal wrapper, container identity and image are
excluded from consumer metadata. The Domain descriptor contains only portable
identity, recording type, byte count, digest and fixed diagnostic flags. Page
size is64KiB; offsets must be aligned or equal exact EOF. Every page rereads and
validates the full staged bundle. This favors correctness over a new cache or
storage abstraction; it has a bounded per-request allocation, not a global
retention/quota guarantee.

Consumers compare original attempt and producer metadata on every page, verify
each page hash and the reconstructed whole-bundle hash, then make a fresh exact-
EOF authorization read before delivering bytes. Changed or denied data requires
rereading the same attempt, never automatic retries or uncertain-input replay.
Private MCP validates response identity, lengths, hashes, flags and pagination;
strict JSON rejects extra native wrappers and missing required diagnostic flags.
Page/ZIP contents remain untrusted website data.

Publication reconciliation under the original fenced attempt, restart-time
acknowledgement repair, partial live-media salvage, actual storage/disk fault
acceptance, Edge/notebook delivery and real provider authority loss remain
separate gates. Automatic worker restart stays disabled.
