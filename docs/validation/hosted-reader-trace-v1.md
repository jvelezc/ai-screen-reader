# Hosted reader trace validation

The [state/edge contract](../decisions/0049-hosted-reader-state-edge-trace.md) retains actual completed browse-command evidence. Source, local checks, native Linux, CI, exact private promotion and real application-user acceptance are separate gates.

| Requirement | Evidence |
| --- | --- |
| Actual captures, immutable prior evidence, matching directed state/edge references and separate refresh links | `TraceLinksActualCapturesCursorStatesAndRefreshWithoutInventingInput` |
| Fresh document/epoch/node resets; new-document denial remains explicit | `RefreshResetsAStaleSourceCursorAndPreservesDocumentDenial` |
| Exact64-command prefix, truncation and retirement reset | `RetentionKeepsAnExactPrefixAndMarksTheFirstOmittedCommand` |
| Total-byte and entry-size caps; retention never resumes after a gap | `ByteRetentionAndOversizedEntriesCannotSilentlyResumeAfterAGap`; `EntryByteCeilingAcceptsTheBoundaryAndRejectsTheNextByte` |
| Atomic byte/count paging with snapshots and states together | `BytePagingNeverSeparatesAnEdgeFromItsSnapshotAndStateNodes`; `InvalidTracePagesAreRejected` |
| Subject/workspace/revocation/expiry/close scope and cleanup quarantine | `TraceAuthorizationAndLifetimeCannotExposeRetainedStates`; `QuarantinedCleanupCannotExposePriorTraceAndRetryConfirmsRetirement`; `HttpAuthenticationPrecedesEveryReaderResource`; `HttpOtherSubjectCannotUseOrCloseAnOwnedSession` |
| Strict HTTP body and actual DTO-derived OpenAPI truth constraints | `HttpTraceRejectsMalformedRequestsBeforeAccessingTheRegistry`; `ReaderLifecycleHttpPayloadsMatchPublishedContractsAndTruthConstraints` |
| Actual Chromium/Kestrel state nodes, refresh, focus and input facts | `RealChromiumTraceHasMatchingObservedNodesAndSeparateRefreshLinks` |
| Actual packaged credential-free worker, paged trace and native retirement | `IsolatedReaderHttpProof` requires `StateEdgeTraceConfirmed`; `Test-ObservationWorker.ps1` asserts it |

Local Windows passes **1,872/1,872**, zero failures/skips, including25 new cases. The focused trace/HTTP/contract suite passes85/85. The strict nonincremental Release build has zero warnings/errors, architecture dependency checks pass, and complete solution format verification passes. Real Chromium/Kestrel is included in the suite. The Windows-launched Linux/Podman worker harness separately passes **28/28**, including the packaged HTTP state/edge proof and actual native retirement; native Linux signal coverage remains a separate CI gate. Current-source CI and exact private deployment receipts are added to the architecture Page after their gates finish. The previous navigation baseline4468405 passed all nine CI jobs39992719717 and exact private deployment39995270368.

The packaged and VPS canaries use explicitly synthetic identities. `AppJwtValidated=false` and `HostedTenantReaderAccepted=false` remain accurate until the confirmed-account workflow passes. An earlier real one-shot tenant scan and S3 report do not establish interactive reader identity acceptance. No new browser input, recording, persistent trace storage or public ingress is enabled.
