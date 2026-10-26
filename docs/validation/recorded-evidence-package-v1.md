# Recorded traversal package validation

October 25, 2026, local Windows: **2,047 discovered/executed/passed, zero failed/skipped**. The valid full TRX includes all 42 new package cases. Repository-pinned SDK 10.0.402; nonincremental Release build completed with zero warnings/errors, and full formatting verification passed. Receipts: `artifacts/evidence-package-validation/sealed-full/` and `sealed-narrow/`. A separate earlier narrow rebuild overlapped a running Windows suite and failed with MSB3021/MSB3027 file locks; the final build, narrow tests, full suite and formatting ran sequentially and passed.

## Actual recorded shop and independent verification

The final CLI ran all four fresh Chromium routes and returned `RecordedRoutesReady=true`. Batch `6ebd63946e154b019339a47b8cdb4d00` retains 100 inventoried artifacts totaling 2,719,667 bytes, plus its manifest, in the original directory and portable ZIP. Original-media/source-frame counts vary with actual frame acquisition; the verifier requires the exact inventory from each run rather than an expected global count.

- Manifest SHA-256: `076701f222b0b3890ad94857474bfb342416ed81a45f2b8a0193c73c85c36685`.
- ZIP SHA-256: `47c2a73b16919bab1837aac4b4fbf518067cfb7bf6f39a9369f6e0125517e413`.
- The retained route outcomes are Reached / NotReached / Unknown / Unknown; only the disabled route reports an observed barrier. Each route has its own original recording, graph and passive viewer. No submitted order, human operation or NVDA recording is claimed.
- An independent Python `zipfile`/`hashlib` check compared every ZIP entry to its declared size/hash and original file bytes, verified both externally retained digests, checked all four outcome/provenance records, and extracted a fresh review copy. The actual `--verify-evidence` CLI then returned `IntegrityMatches=true` against that copy and the separately retained digest.
- The passive viewer was exercised separately: selecting the activation edge showed source `o2`, actual result `o3`, completed Enter input, goal verification Satisfied and its covered recording interval. Selecting report evidence only seeks video; it does not execute a browser command. This is browser interaction testing, not human/AT acceptance.

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| Portable original media, graph, reports, fixture/build provenance and exact extracted bytes | `Export_PreservesOriginalBytesGraphsAndHonestScopeInPortableArchive`; four real Chromium cases in `FourOriginalRecordings_LinkRealCaptureAndInputIntervalsWithoutChangingRouteClassification` |
| Separately anchored integrity and safe failure output | `Verify_DetectsTamperingAgainstSeparatelyRetainedDigest`; `VerifyCommand_UnreadablePackageReturnsFailureWithoutExposingLocalPaths` |
| Unsafe paths, ambiguous names, links and invalid manifests cannot pass | `Verify_RejectsUnsafeOrAmbiguousInventoryEvenWithItsMatchingDigest`; `Verify_RejectsInvalidSchemaScopeReferencesAndResourceDeclarations`; `Export_RejectsDirectoryLinksInsteadOfIncludingOutsideFiles` |
| Preserve cancelled/incomplete collection without upgrading outcomes | `Export_RetainsFailedRecordingAndPartialCoverageWithoutCreatingSuccessClaims`; `CancellationBeforeLaunch_RetainsFailureAndCreatesNoInputOrRecordingClaim` |
| Original source/fixture binding and append-only publication | `Export_RefusesChangedSourcesOrUnfinishedAndExistingOutputs`; `Export_IsAppendOnlyAndVerifierRequiresAnExternalValidDigest` |
| Bounded documents, files and routes; streaming exact bytes | `Verify_AcceptsValidManifestImmediatelyBelowAndAtTheByteLimit`; `Verify_RejectsOversizedManifestBeforeReadingItsContents`; `Export_RejectsOversizedOriginalBeforeReadingOrPublishingManifest`; `Export_RejectsBatchesOutsideTheFourPlannedRoutesBeforeCreatingFiles`; `Export_StreamsMultipleBlocksAndRetainsEmptyArtifactsWithoutChangingTheirBytes` |

Narrow command: `dotnet test tests/HeadlessScreenReader.Tests/HeadlessScreenReader.Tests.csproj -c Release --no-build --filter FullyQualifiedName~TraversalEvidencePackageTests --logger trx --results-directory artifacts/evidence-package-validation/sealed-narrow` — 42/42.

Full command: `dotnet test HeadlessScreenReader.slnx -c Release --no-build --logger trx --results-directory artifacts/evidence-package-validation/sealed-full` — 2,047/2,047. Both TRX files were parsed and nonzero discovery/all-case accounting confirmed.

## Separate gates

Windows/Linux CI now extracts the real four-route ZIP and invokes the actual verifier in addition to the full suite; a remote result must be recorded separately. This increment does not deploy hosted recording, change the Edge gateway or Lovable UI, collect real-site or human/NVDA evidence, provide an external custody/time attestation, or establish legal admissibility. API deployment remains independently accepted at 614d4b1. The [next recorded-evidence steps](../decisions/0054-recorded-traversal-package.md#next-acceptance-steps) retain these boundaries.
