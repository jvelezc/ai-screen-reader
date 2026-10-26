# Durable original traversal archive v1

The existing authored recording package is the import boundary. The operator
supplies its separately retained manifest SHA-256 and verifies every declared
original before importing. This increment adds storage and readback; it does not
add a new recording, tenant admission, HTTP endpoint, MCP tool or UI operation.

Domain owns archive identities and immutable metadata contracts. Worker owns the
archive engine and local/S3 transports. CLI composes the existing package verifier
with those transports. Worker does not reference CLI, browser or ScreenReader;
Domain does not reference filesystem, HTTP, storage SDK or hosting implementations.

## Object and page contracts

An identity contains organization UUID, archive UUID and index SHA-256. The caller
retains that complete receipt separately. Possession of an identity is not tenant
authorization; a later API must check the original application user's current
workspace authority before every response.

Objects are stored under
`traversal-v1/{organizationN}/{archiveN}/{indexSha256}/`. The final descriptor is
`index.json`; original chunks are `objects/{chunkSha256}`. Addresses accept no
arbitrary URL, storage prefix, local path, listing or delete operation.

- Objects and the index are bounded to4MiB, below the existing private25MiB bucket.
- Original files retain their complete SHA-256 and byte count. Up to128MiB per
  original is split into ordered chunks; no transcoding, truncation or replacement.
- The original package manifest is itself retained, bounded to4MiB. Up to4097
  files and516MiB cover the existing4096-file/512MiB package plus its manifest.
- Returned original-file pages contain at most64KiB. Offsets are aligned except
  an explicit EOF request. Empty originals retain the SHA-256 of empty bytes.
- Each read verifies the actual index digest, organization/archive identity,
  file inventory, part lengths and requested part digest. Whole-original integrity
  is established by complete reconstruction and comparison with the original hash.
  A valid metadata page alone does not establish availability of every part.
- Readers reject duplicate/unknown JSON properties, ambiguous paths, case collisions,
  unsafe devices, invalid hashes, inconsistent parts and resource declarations.

## Finalization and interruption

The engine first validates and hashes all sources, then opens fresh source streams
and compares every original chunk while storing it. The index is published last,
after actual matching readback of every part. A source change, lost write or
cancellation cannot produce a successful finalization receipt. Explicit retries
use the same caller-selected organization/archive and retained source manifest;
identical bytes produce the same index identity. Unpublished parts can remain
after interruption; this increment does not list or delete orphan objects.

The local store publishes with an atomic no-overwrite move inside a private owned
directory and rejects filesystem links. The S3 store signs fixed-project GET/PUT
requests using the existing server-side S3 configuration, reuses only hash-matching
objects and rejects observed corrupt objects instead of repairing them by overwrite.
Both stores bound actual read streams and verify exact readback. Cancellation is
rechecked even when a transport returns late.

This is content-addressed integrity with readback at finalization. It is not
provider Object Lock, a trusted timestamp, storage immutability against credential
holders, a chain-of-custody attestation or a legal-admissibility conclusion.
See the provider's [S3 compatibility contract](https://supabase.com/docs/guides/storage/s3/compatibility).

Failures distinguish Missing, Invalid, Unavailable and WriteUnconfirmed. CLI errors
omit source paths, remote bodies and credentials. Collection facts remain in the
unchanged original package: attempted inputs, observed outcomes, goal checks,
partial/cancelled routes, capture exclusions and unperformed human/NVDA comparisons.

## Operator commands and next gate

`--archive-evidence-local <directory> <trusted-manifest-sha256> <organization-guid> <archive-guid> <owned-store-directory>`

`--archive-evidence-s3 <directory> <trusted-manifest-sha256> <organization-guid> <archive-guid>`

`--verify-archive-local <owned-store-directory> <organization-guid> <archive-guid> <index-sha256>`

`--verify-archive-s3 <organization-guid> <archive-guid> <index-sha256>`

S3 commands consume only the existing process-scoped `HSR_EVIDENCE_S3_ACCESS_KEY`
and `HSR_EVIDENCE_S3_SECRET`; they do not create credentials or change the bucket.
Use a new archive UUID for an independent import and keep the returned identity.
Verification is read-only and opens a fresh archive instance.

**Next:** add bounded, freshly tenant-authorized manifest/file retrieval to the
independent core API and private MCP, then existing-project Edge and the notebook's
original-evidence graph/media view. Bind any queue-produced archive to its actual
admission/attempt rather than inventing a scan from an operator import. General
hosted capture, credentials/human input, human/NVDA operation, wider recovery and
public MCP/plugin publication retain their separate gates.
