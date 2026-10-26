# 0056: Authorized original archive retrieval

Accepted October 26, 2026. Extends [0055](0055-durable-traversal-archive.md).

Core contract 1.4.0 adds two read-only POST operations:
`/api/workspaces/{organization}/archives/{archive}/manifest` and `/file`.
POST keeps exact paths and retained digests in a strict, small JSON body rather
than URL query strings. No upload, listing, signing URLs or capture is added.

The original confirmed application user is validated by existing Auth middleware.
Every operation uses that user's bearer with workspace RLS before storage and
again before returning the page. Anonymous, foreign and revoked access cannot
fall back to a privileged user or previously cached bytes. The deployment's S3
credentials remain server-side. Absent archive configuration returns503; local
files never silently replace missing hosted originals.

The caller must retain the import receipt's organization, archive and index SHA256.
The scoped, hash-checked root is reread on every request. A manifest projection
contains at most 32 sorted file entries, with original lengths and hashes and explicit
nextOffset. It does not expose the potentially 4MiB internal chunk index or prove
that every original remains available. File pages contain at most 64KiB of original
bytes as base64, expected whole-file identity, actual page SHA256 and nextOffset.
The actual addressed part is checked before any bytes are returned. Clients must
reconstruct all pages and compare the full file hash before claiming whole-file
integrity. Empty original/EOF is distinguishable from a missing or corrupt file.

Private HTTP-backed stdio MCP adds `archive_manifest` and `archive_file` alongside
the existing ten tools. The adapter preserves the original caller and fixed
workspace, independently rejects wrong identity/path/digest/cursors/lengths,
checks page SHA256, caps output at 128KiB and never retries automatically. It is
stateless across archive reads, so reconnect uses the same explicit identities.
The local fixture-only MCP mode is unchanged; no public MCP endpoint is added.

An operator import is authorized at organization membership scope, not tied to a
scan admission or worker attempt. There is no trusted archive discovery catalog
yet. Storage availability/integrity is separate from actual source graph reachability,
collection exclusions, independent NVDA comparison, custody review and legal
acceptance. Source files, original collection outcomes and uncertainty stay intact.

The Lovable UI remains a separate consumer through existing-project Edge
Functions. Edge/notebook archive operations and a fresh original-user retrieval
walkthrough are the next acceptance gate, not implied by synthetic-auth HTTP,
real Chromium/stdio tests, infrastructure health or S3 operator proof.
