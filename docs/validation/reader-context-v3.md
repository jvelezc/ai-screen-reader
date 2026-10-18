# Reader context and live observations

Date: October 18, 2026. Profile: hsr-desktop-context-v3.

The NVDA comparison motivated structured enclosing context, explicitly scoped peer positions and separate live changes. Announcements and activation policy remain as before. See ADR 0004 for semantics and limits.

For example, Express returns Context with the Delivery speed group and ExposedPeers.Position=2, Count=2. South returns the Delivery regions listbox while FocusedNodeId still identifies its combobox owner. Committing South produces a separate LiveRegionChanged event whose typed LiveRegion.Text is Delivery region: South. That event does not replace the current-control announcement or establish goal verification.

The new trusted LiveRegions fixture exercises nested status roots, busy updates and synthetic editable canaries. It is available through the closed fixture enum for acceptance tests; no arbitrary HTML/site entry point was added. The Foundation and Widgets content hashes are unchanged.

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| Groups, nested-group isolation and scoped radio positions without input | ReaderObservationTests.Context_ExposedRadioPeers_PreservesGroupAndScopesCount |
| Active option context remains distinct from DOM owner | ReaderObservationTests.Context_ActiveOption_UsesListboxInsteadOfDomOwnerGroup |
| Missing/cyclic ancestry, absent context and singleton peer boundaries | ReaderObservationTests.Context_BrokenOrCyclicAncestry_DoesNotInventCompletePeerSet; Context_UngroupedControlAndEmptySnapshot_DoNotInventContext; Context_SingleExposedOption_ReportsOnlyOneObservedPeer |
| Delayed verification returns destination context | ReaderObservationTests.Context_VerifiedDelayedFocus_ReturnsDestinationAncestry |
| Live events carry text/politeness separately and suppress duplicate observations | ReaderObservationTests.LiveChanges_ChangedStatus_IsSeparateFromFocusedAnnouncementAndDeduplicated |
| New assertive alerts, off/busy/empty/unchanged states and settling | ReaderObservationTests.LiveChanges_NewAssertiveAlert_RecordsOneObservation; LiveChanges_OffBusyEmptyOrUnchanged_DoesNotEmit; LiveChanges_BusyRegionSettles_EmitsLatestTextOnce; LiveChanges_OffThenPolite_DoesNotReplayExistingText |
| Reinsertion is observed and document replacement resets the baseline | ReaderObservationTests.LiveChanges_RemovedThenReinserted_ObservesInsertionButNotDocumentReplacement |
| Actual Chromium radio/option/dialog relationships and committed status | ChromiumObservationTests.Context_ChromiumWidgets_ExposesRadioOptionAndDialogContainers |
| Chromium nested busy state and text extraction exclude editable values | ChromiumObservationTests.LiveChanges_ChromiumText_ExcludesEditableContentsAndSeparatesNestedRoots |
| Chromium validation exposes the assertive alert without replacing command speech | ChromiumObservationTests.LiveChanges_ChromiumValidation_ExposesAssertiveAlertWithoutReplacingAnnouncement |

The new tests include 16 browser-independent cases and three Chromium cases. All 77 previous test identities remain in the full run. Initial implementation checks caught Chromium's numeric busy property and a test that counted an unrelated status reinsertion after dialog closure; both were corrected before final validation. No earlier failed or build-only run is credited as passing.

## Local verification

| Gate | Result |
| --- | --- |
| Windows Release build | Clean build, locked restore, 0 warnings/errors; Directory.Build.targets dependency guard applied |
| Windows tests | 96 discovered/executed; Passed: 96, Failed: 0, Skipped: 0; 80 core and 16 Chromium cases |
| Linux WSL tests | Same pinned SDK 10.0.402 and source/configuration bytes; 96 discovered/executed; Passed: 96, Failed: 0, Skipped: 0; all 16 Chromium cases ran |
| Linux Release build | Locked restore, 0 warnings/errors, sandbox enabled and existing user-local browser dependencies |
| Formatting | dotnet format verification passed on Windows and Linux |
| CLI | Widget goal and observed disabled route succeeded on both platforms; JSON includes Context and typed live events |
| Reference evidence | Historical v2 worksheet remains rejected by v3 with HSR_PROFILE_MISMATCH; original worksheet is preserved |
| Remote CI / deployment | Not run / not performed |
| Real AT on new profile | Not repeated; the earlier bounded NVDA comparison covers v2, not these new fields |

Windows commands:

```powershell
dotnet build HeadlessScreenReader.slnx -c Release --no-incremental -p:RestoreLockedMode=true
dotnet test HeadlessScreenReader.slnx -c Release --no-build --no-restore --logger "trx;LogFileName=context-v3-windows.trx" --results-directory artifacts/context-v3
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
```

Evidence is under artifacts/context-v3: Windows/Linux TRX, discovery listing, widget JSON transcripts, source manifests and historical-reference assessment. Linux used a fresh isolated source copy identified by linux-workspace.txt; no Windows build outputs were copied into it.

Snapshot changes are not an exhaustive event stream. Missing AX states remain unknown, peer counts are limited to exposed controls, and live text has the same limited minimization as other semantic text rather than general PII redaction. Arbitrary-site navigation, credentials and recordings remain unavailable. This increment does not complete the broader foundation/privacy/isolation milestones.
