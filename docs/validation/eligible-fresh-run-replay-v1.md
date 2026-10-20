# Eligible fresh-run replay v1 validation

October 20, 2026, local Windows: **807 discovered/executed/passed, 0 failed/skipped**. All 757 preceding identities are retained; 50 cases were added, including five real Chromium cases. Discovery and TRX identity sets match. Nonincremental Release build: zero warnings/errors. Formatting verification passed. Receipt: artifacts/recording-validation/fresh-replay-windows-final.trx. The standalone replay demo passed at artifacts/replay/54464bdca36f4a50a286dc0356c87bd1/index.md: fresh goal Reached, distinct new run, original receipt unchanged. Remote CI is tracked on the corresponding main push.

| Requirement | Evidence |
| --- | --- |
| Fresh identities normalize without discarding ordered semantics or ownership | EquivalentFreshIdentities_NormalizeWithoutDroppingReaderOrderSemanticsOrOwnership |
| Changed reader facts change the source proof despite the same URL | ChangedReaderFacts_ChangeSourceContractDespiteSameUrl |
| Unsupported frame/epoch and malformed identities cannot produce a source proof | UnsupportedOrAmbiguousSource_CannotProduceAnEquivalenceProof |
| Eligible execution creates a separate verified receipt and preserves history | EligibleFreshRun_CreatesIndependentReceiptAndLeavesHistoricalEvidenceUntouched |
| Environment/profile/policy incompatibility rejects before capture or input | IncompatibleEnvironmentOrPolicy_RejectsBeforeCaptureOrInput |
| Uncertain, truncated or ineligible historical evidence rejects before input | IneligibleOrUncertainHistoricalEvidence_RejectsBeforeCaptureOrInput |
| Source drift at preflight, reader refresh or immediately before dispatch prevents native input and retains the observed rejection | ChangedSourceAtPreflightOrBeforeFirstNativeDispatch_RejectsWithNoNativeKeys |
| Current policy denial is not an accessibility barrier | CurrentPolicyDenial_RemainsDeniedWithoutAnAccessibilityBarrier |
| Post-input browser failure retains partial calls and uncertainty | PostInputBrowserFailure_PreservesPartialKeysAndUncertaintyInNewReceipt |
| Cancellation after input retains a new cancelled/uncertain result | CancellationAfterInput_RetainsCancelledUncertainAttemptInsteadOfHistoricalSuccess |
| Stalled capture/input has bounded failure without inherited goal success | StalledCaptureOrInput_HasBoundedErrorAndNeverInheritsHistoricalGoal |
| Pre-start cancellation is explicit and sends no input | CancellationBeforeReplay_IsExplicitAndSendsNoInput |
| Real fresh Chromium instances use native keys, independent goals and immutable history | EquivalentFreshChromiumInstance_CreatesNewVerifiedTraceWithNativeKeysAndImmutableHistory |
| Same-URL changed disabled state is rejected before native input | SameUrlChangedDisabledState_RejectsBeforeAnyNativeInput |
| Equivalent observed starts with changed behavior preserve fresh Unknown | EquivalentObservedStartWithChangedBehavior_PreservesFreshUnknownInsteadOfHistoricalSuccess |
| A real source change immediately before dispatch denies without keys or input uncertainty | SourceChangesImmediatelyBeforeNativeDispatch_DeniesWithoutKeysOrInputUncertainty |
| Additional real frame coverage cannot be silently omitted | AdditionalFrameCoverage_RejectsUnsupportedSourceWithoutInput |

The first CLI build caught a span held across an await in its before/after hash comparison; reading the bytes before comparing corrected it. A report-copy change interrupted an existing scope assertion; the sentence order was corrected while preserving passive-selection behavior. Failed receipts remain separate from final acceptance.

The real changed-behavior control replaces the exposed button with an equivalent clone without its fixture listener. Replay is eligible from the observed source but its new goal times out as Unknown. This demonstrates the limit of observed source equivalence.

No local Linux, deployment, new assistive-technology comparison, public replay, Claude-client acceptance or hosted persistence-privacy result is claimed. Scan-owned persistence exclusion, human ownership and general sensitive input remain separate gates.
