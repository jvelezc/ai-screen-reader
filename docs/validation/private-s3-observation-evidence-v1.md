# Private S3 observation evidence validation

Fixed synthetic fixtures, no browser credential entry, no new target inputs, no goal verification. Public MCP/plugin work remains deferred.

| Requirement | Evidence |
| --- | --- |
| Fixed authority and server-only credentials | `ConfigurationHasFixedProjectAuthorityAndCannotSerializeCredentials`; `IncompleteOrPrivilegedCredentialsCannotBecomeS3Authority`; `test_only_complete_s3_authority_is_copied` |
| Signed upload requires original readback | `MissingObjectRequiresSignedUploadAndActualExactReadback`; `SuccessfulPutHeaderCannotSubstituteForMissingOrCorruptReadback` |
| Scope, native facts, size and application write identity | `ContentAddressIncludesEveryScopeAndDigestWithoutAnArbitraryPath`; `ConflictingExistingBytesNeverAuthorizeAnOverwrite`; `ExactMaximumPayloadIsValidatedAndTheNextByteIsRejected`; `MissingContentLengthCannotBypassTheEmptyOrOversizedBodyGuard`; `CallerMutationAfterDispatchCannotAlterTheUploadedEvidence` |
| Local flush, storage, then fenced completion | `RemoteReadbackMustSettleAfterLocalFlushAndBeforeFencedCompletion`; `RemoteFailureRetainsLocalBytesAndBlocksWithoutInventingCompletion`; `VerifiedOffHostBytesDoNotImplyDatabaseCompletion` |
| Cancellation joins callbacks | `CancellationJoinsTheActualProviderCallbackAndNeverReturnsLateMissingSuccess`; `StopDuringFinalizationJoinsActualCallbackBeforeBlockingTheJob` |
| Authorization before remote reads | `RevokedIdentityOrRlsHiddenScanCannotReadRemoteEvidence`; `InvalidMetadataCannotReachObjectStorage` |
| Remote read, explicit fallback, distinct outage/corruption | `VerifiedRemoteEvidenceIsReadableWithoutTheLocalFileAndPreservesReaderFacts`; `OnlyAnActuallyMissingObjectAllowsExplicitLocalFallback`; `RemoteFailureNeverSilentlyFallsBackToAValidLocalCopy`; `PortalIndependentlyValidatesRemoteHashScopeAndNativeFacts` |
| Understandable storage source, unchanged goal claims | `EvidenceCopyLabelDistinguishesRemoteLegacyAndUnknownWithoutChangingGoalClaims` at320/736/1120px; existing real native portal journey |
| Root configuration and credential exclusion | `test_root_file_is_optional_but_existing_file_requires_exact_private_shape`; `test_worker_and_control_entrypoints_forward_only_the_valid_pair_to_server_processes`; `test_native_readiness_does_not_inherit_s3_or_database_credentials` |
| Real isolated Chromium and authenticated S3 service | Worker case `ActualSignedS3FinalizationAndOffHostReadbackPreserveNativeFacts`; ten checks in `s3/protocol.json`, owned cleanup in `s3/run.json` |

Windows: `dotnet test HeadlessScreenReader.slnx -c Release`, strict Release build, and `dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore`. Linux operations: `python3 ops/worker/test_release.py`. The existing leased-worker CI job invokes `scripts/Test-ObservationWorker.ps1`, including S3 proof against the exact publication later packaged. Missing/failed/skipped cases fail acceptance. RustFS is a disposable local protocol fixture; production remains the selected Supabase project.

Local Podman worker acceptance before portal composition changes passed21/21, with ten nested S3 checks, database/image/server/network removal and native recovery confirmed. This does not attest to hosted S3. CI's Linux launcher adds signal/concurrency checks and expects23 worker cases.

Hosted gates: dedicated S3 key, private bucket, accepted root helper/unit upgrade, actual provider readback after local-file removal, anonymous denial, credential exclusion and cleanup. Readiness without storage authority is intentionally insufficient. Real confirmed app-user sign-in, tenant admission/report and assistive-technology feedback remain separate. Automatic deployment is held while storage configuration and the first human portal test are pending.

Final local Windows Release regression passed1,516/1,516, failed/skipped0; all1,453 prior accepted case identities and multiplicities retained. Strict warnings-as-errors Release build passed with0 warnings/errors; full solution formatting verification passed. Final Linux operational suite passed118/118 using an isolated, network-disabled container with actual Linux filesystem semantics. Four responsive storage-label Chromium cases passed, including the inherited-property `constructor` regression; bounded visual finish review accepted this label addition only. Receipts: `artifacts/s3-validation/full-s3-final.trx`, `s3-ui.trx`, and the worker/S3 receipts above. Remote CI and hosted acceptance remain separate gates.
