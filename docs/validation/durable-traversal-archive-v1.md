# Durable original traversal archive validation

This storage increment imports an already verified authored recording package.
It does not enable hosted recording or add application-user HTTP/MCP retrieval.
The [archive contract](../decisions/0055-durable-traversal-archive.md) keeps those
gates separate from object persistence and exact-byte readback.

## Local Windows checks

- Strict nonincremental Release build:0 warnings/errors. Architecture dependency
  checks run through Directory.Build.targets; no project/package references changed.
- Full `HeadlessScreenReader.slnx` Release suite:2,112 discovered/executed/passed,
  0 failed/skipped. The TRX counters were parsed. All prior source tests are
  unchanged;65 new cases cover archive engine, transports and CLI composition.
- Full `dotnet format --verify-no-changes --no-restore` passed.
- The new CI archive command block was executed locally with its actual receipt
  assertions. Remote Windows/Linux execution and private promotion are separate.

| Requirement | Evidence |
| --- | --- |
| Exact original reconstruction through fresh bounded pages, including empty and adjacent64KiB/4MiB boundaries | `FinalizeAndReconnect_PreserveEveryOriginalByteAtPageAndObjectBoundaries` |
| Organization/archive/digest scope and malformed metadata/resource limits | `ObjectAddressAndIndex_BindOrganizationArchiveAndExternallyRetainedDigest`; `ReadIndex_RejectsMalformedMetadataEvenWithAMatchingEnvelopeDigest`; `ReadIndex_TotalBytesBoundaryIsExactAndDoesNotClaimObjectAvailability`; `Finalize_AcceptsExactlyTheMaximumFileInventory` |
| Publication only after verified originals; source changes, interrupted writes and explicit retry remain honest | `Finalize_SourceMutationCannotPublishAnIndex`; `Finalize_InterruptedWriteRetainsUnpublishedPartsAndExplicitRetryReusesIdentity`; `Finalize_AcknowledgementWithoutMatchingReadbackRemainsUnconfirmed` |
| No stale success after cancellation, missing or corrupt objects | `Cancellation_PreventsLateWriteOrReadFromBecomingConfirmed`; `Read_ReportsMissingAndCorruptOriginalsWithoutRepair`; `S3Store_CancelledLateTransportCannotPublishSuccess` |
| Actual local persistence and signed S3 protocol, bounded streamed reads, authority/server errors and no corrupt-object repair | `FileStore_ReconnectReadsExactBytesAndCorruptExistingObjectIsNotOverwritten`; `S3Store_SignsScopedBinaryObjectsAndReusesOnlyVerifiedOriginals`; `S3Store_RejectsCorruptOrOversizedReadsWithoutOverwriting`; `S3Store_AuthorityAndServerFailuresAreUnavailableRatherThanMissing` |
| Original package integrity before writes; unchanged cancelled-route and comparison facts | `Import_VerifiedPackageRetainsOriginalManifestAndHonestCancelledRouteFacts`; `Import_RejectsUnverifiedSourceBeforeAnyArchiveObjectIsWritten`; `Command_LocalImportAndFreshVerificationReturnExactScopedReceipts` |

## Actual original-media and off-host checks

The real four-route Chromium package previously collected under the controlled
synthetic fixture was imported using the separately retained source manifest
`076701f222b0b3890ad94857474bfb342416ed81a45f2b8a0193c73c85c36685`.
Both local and private Supabase S3 imports retained101 files and2,738,815 bytes,
including the unchanged original manifest. A separate CLI verification process
reconstructed every original through the new page reader and matched all hashes.
The local and S3 imports used distinct operator-selected organization/archive UUIDs.
Neither scope is an application-user tenant authorization acceptance claim.

Local retained index SHA-256:
`01b6fff3a717a8eabf58345c6d078eff0458070f9040f40e1e36e12c6421f1de`.
Private S3 retained index SHA-256:
`cf1a8b181668cee94a3ab72d8a11e3231674cbaeff70bb4102cf3e1cf2ffbd6e`.
The differing identities retain their own organization/archive scope; the original
source manifest digest and complete original bytes match.

The S3 proof used the existing private bucket and existing process-scoped S3
credentials. No bucket size/MIME policy, permissions, database schema, credential
or Edge deployment changed. Original files and provider objects were not deleted.
Content addressing/readback is separate from provider Object Lock, trusted time,
custody review, human/NVDA operation or legal acceptance.

## CI, deployment and following acceptance

The Windows/Linux CI CLI stage now independently imports and verifies its newly
recorded four-route package after ZIP extraction. Archive objects remain in its
private runner temporary directory; the two JSON receipts are retained with the
existing foundation artifacts. No live project credentials enter CI.

Remote CI results and the deployed source must be reported separately after the
run completes. A deployed archive library/CLI does not expose a new HTTP endpoint
or MCP tool. Next: fresh original-user authorization on each bounded archive
manifest/file response in the core API and private MCP, then Edge and notebook
original-evidence graph/media consumption. Queue-produced archives require their
own actual admission/attempt linkage. Public MCP/plugins stay deferred.
