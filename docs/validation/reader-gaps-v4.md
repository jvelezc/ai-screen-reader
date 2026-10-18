# Reader gap closure: v4

Date: October 18, 2026. Scope: reader gaps first, as explicitly selected by the user. Profile: hsr-desktop-reader-v4.

## Implemented gaps

| Gap | Result | Requirement evidence |
| --- | --- | --- |
| Native checkbox required was missing | Fixed, read-only native property supplement with provenance; absent and false remain distinct. AX wins when available. Required changes are observed. | ChromiumGapTests.Required_ChromiumNativeCheckbox_HasProvenanceAndTracksChanges; ReaderGapTests.RequiredObservation_ExplicitFalseOverridesLegacyAndMissingRemainsUnknown |
| Exposed-peer counts could not express logical positions | Separate author-declared LogicalSetPosition supports known, unknown, partial, absent and invalid declarations. It never replaces the exposed-peer count. | ChromiumGapTests.LogicalSet_ChromiumDeclarations_AreSeparateFromExposedPeersAndValidateBounds; ReaderGapTests.LogicalSet_DeclaredValidityControlsAnnouncement |
| Missing backward semantic/document navigation | Previous heading/link/button/landmark and first/last exposed node commands, with no wrap or browser input. | ReaderGapTests.BackwardNavigation_FromEnd_FindsRoleWithoutInput; BackwardNavigation_UnsetCursor_StartsAtLastMatchWithoutWrapping; DocumentBoundaries_EmptyAndFocusMode_DoNotInventMovement; ChromiumGapTests.BackwardNavigation_Chromium_TraversesHeadingLinkAndLandmarkWithoutBrowserInput |
| Idle status changes required another command | Bounded async observation stream with explicit stop/error states; shared controller gate and budget. | ReaderGapTests.IdleObservation_ChangedStatus_StreamsWithoutCommandsOrInputAndStopsAtLimit; IdleObservation_CancellationOrDeadline_ReturnsExplicitTerminalStatus; IdleObservation_BudgetOrTruncation_IsExplicitAndStops; IdleObservation_BrowserFailure_RecordsErrorAndClosesCommandWithoutBarrier; IdleObservation_InvalidBounds_RejectsBeforeCapture |
| Concurrent/cancelled observation could interfere with controller use | Gate is released before yield; capture cancellation closes the trace and releases the gate. | ReaderGapTests.IdleObservation_YieldsControllerGate_ForInterleavedCommands; IdleObservation_CancelledDuringCapture_ReleasesControllerAndRecordsNoInput |
| Protected edit was not identified | Fixed native password-type observation; value and editable descendants remain excluded. No credential entry command was added. | ChromiumGapTests.ProtectedEdit_Chromium_AnnouncesProtectionWithoutExposingSeededValues |
| Idle API needed a concrete demonstration | --observe-live starts a trusted delayed update and logs the bounded observation batches and terminal status. | ChromiumGapTests.IdleObservation_Chromium_SeesDelayedUpdateWithoutAdditionalInput; Windows/Linux CLI artifacts |

An HSR widget transcript now announces Accept terms, checkbox, not checked, required, invalid, and records Required.Source=NativeHtmlProperty. A partially loaded list can report ExposedPeers.Count=2 while the focused option separately reports declared item 5 of 20. Invalid declarations remain available as invalid observations and are not announced as valid positions.

## Validation

Final local Windows and Ubuntu WSL runs each discovered/executed 122 tests: Passed: 122, Failed: 0, Skipped: 0, comprising 101 browser-independent cases and 21 real Chromium cases. All 96 v3 test identities remain present. New tests cover 21 core cases and five Chromium cases. Clean builds with locked dependencies, warnings as errors, Directory.Build.targets architecture checks and format verification passed on both platforms. The pinned SDK remains 10.0.402; no packages changed.

The first current-profile suite passed 121 tests. Final review added an unset-cursor backward-navigation case and extra partial/invalid semantic declarations; only the final 122-case runs are used for final acceptance. A Windows rebuild against normal output then failed because the open reference process locked its DLLs; it ran no tests. The final clean Windows build/test used isolated output, without closing the user's reference session or weakening warnings.

```powershell
dotnet build HeadlessScreenReader.slnx -c Release --no-incremental --artifacts-path artifacts/reader-v4/windows-final -p:RestoreLockedMode=true
dotnet test HeadlessScreenReader.slnx -c Release --artifacts-path artifacts/reader-v4/windows-final --no-build --no-restore --logger "trx;LogFileName=reader-v4-windows-final.trx" --results-directory artifacts/reader-v4
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
```

Evidence is under artifacts/reader-v4: final Windows/Linux TRX, discovery listing, same-source manifests, widget and idle-observation JSON transcripts. All 41 source/configuration/fixture/lock files were checked byte-for-byte against the isolated Linux copy. The Linux source copy excludes Windows outputs and uses the same sandbox-enabled Chromium setup and process-scoped dependency path as previous validation.

## Remaining boundaries and gates

| Item | Disposition |
| --- | --- |
| Every intermediate live update / speech arbitration | Explicitly outside the sampled observation contract. SamplingMayMissChanges=true; no lossless-feed or NVDA speech claim. |
| Logical positions absent or authored incorrectly | Preserve unknown/invalid state; do not guess a logical total from exposed peers. |
| Child frames, complete text/character/word/line reading and additional widget patterns | Still unsupported; this bounded increment does not claim complete screen-reader or website coverage. |
| Fresh NVDA validation | After the user reopened Speech Viewer, [targeted v4 checks](nvda-v4-targeted-comparison.md) confirmed required/protected semantics and backward headings/link navigation. Choices is Comparable; reading is Different because landmark navigation skips enclosing context in NVDA but enumerates its node first in HSR. Five cases remain unrun and exact settings unaudited; full assessment remains incomplete. |
| Exact NVDA settings/add-ons | Still unaudited; original worksheets are preserved. |
| Remote CI | Not run; no remote/committed repository is configured. Local solution checks do not substitute for a CI run. |
| Capture privacy, credentials, egress, hosting/MCP, deployment | Deferred by the user's reader-first scope. Existing gates remain in force. |

The v2 live comparison and v3 validations remain historical evidence. Neither is relabelled as a current-profile assistive-technology result.
