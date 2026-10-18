# Widget milestone validation

Date: October 17, 2026

The second local increment implements bundled widget observations, keyboard interaction, bounded destination verification, and an ordered event timeline. The original architecture plan under outputs/ is preserved. This is not completion of the full foundation milestones.

## Acceptance results

| Gate | Actual result |
| --- | --- |
| Windows local Release build | Passed with 0 warnings and 0 errors; warnings treated as errors |
| Windows test discovery/execution | 43 discovered, 43 executed/passed, 0 failed, 0 skipped: 34 core cases and 9 real Chromium cases |
| Windows locked restore and format verification | Passed |
| Ubuntu 24.04.4 under WSL2 | Release build passed with 0 warnings/errors; 43 discovered, 43 executed/passed, 0 failed, 0 skipped, including all 9 real Chromium cases |
| Linux locked restore and format verification | Passed; 28 source/configuration/lock files matched Windows by SHA-256 |
| Bundled CLI acceptance | Both demos passed on Windows and Linux; widget summary reports FixtureGoalReached=true, BlockedRouteObserved=true, TimelineTruncated=false |
| Timeline export | Windows widget demo: 15 command results, 134 events with consecutive event sequences |
| CI | Windows/Ubuntu workflow updated to run both demos; no remote run has occurred |
| Deployment | Not performed |
| Real assistive technology | NVDA, JAWS, VoiceOver and customer usability validation have not run |

The test counts above come from the final TRX counters and individual test result records, including the parameterized key-policy cases. Earlier runs identified fixture/normalization issues; the final runs contain no failures or skipped tests.

## Behavioral evidence

| Requirement | Evidence |
| --- | --- |
| Dialog focus containment and restoration using keyboard input | ChromiumWidgetTests.ModalDialog_TabStaysInsideAndEscapeRestoresInvoker |
| Exposed validation message, focus, invalid state and required combobox | ChromiumWidgetTests.InvalidForm_ExposesErrorFocusAndWidgetValidationState |
| Active descendant remains separate from actual DOM focus; keyboard selection commits | ChromiumWidgetTests.Combobox_TracksActiveDescendantSeparatelyFromDomFocusAndCommitsChoice; WidgetSessionTests.ActiveDescendant_AnnouncesOptionButActivatesDomOwner |
| Checkbox, native radio, combobox and delayed successful save; disabled route has no dispatched input | ChromiumWidgetTests.PreferenceWorkflow_CheckboxRadioAndCombo_SaveThenObserveBlockedRoute |
| Missing destination leaves the outcome unknown | ChromiumWidgetTests.MissingDestination_TimesOutAsUnknownAfterRealKeyboardInput; WidgetSessionTests.Wait_Deadline_ReturnsUnknownAndPreservesCompletedInput |
| Delayed focus is announced after verification | WidgetSessionTests.Wait_DelayedFocus_VerifiesDestinationAndAnnouncesNewHeading |
| Poll budget and cancellation retain input uncertainty | WidgetSessionTests.Wait_PollLimit_StopsWithoutAdditionalInput; WidgetSessionTests.Wait_CancelledAfterInput_ReportsInterruptedWithoutInventingBarrier |
| Ordered timeline and explicit truncation | WidgetSessionTests.Timeline_OrdersInputObservationVerificationAndAnnouncement; WidgetSessionTests.Timeline_AtCapacity_ReportsTruncationAndKeepsCommandResult |
| Failed input cannot invent completion, verification or a website barrier | WidgetSessionTests.InputFailure_RecordsAttemptWithoutInventingCompletionOrBarrier |
| Missing active descendant safely falls back; predicates require requested state/focus | WidgetSessionTests.ActiveDescendant_MissingTarget_FallsBackToDomOwner; WidgetSessionTests.Expectation_RequiresRequestedStateAndEffectiveFocus |
| Validation announcements and synthetic input canary exclusion | WidgetSessionTests.WidgetAnnouncement_IncludesValidationAndDescription; ChromiumWidgetTests.Timeline_DoesNotCaptureFixtureInputCanaries |
| Existing cursor, routing, budget, concurrency, cancellation and keyboard policy behavior | ScreenReaderSessionTests and ChromiumFixtureTests, all passing |

## Reproduce on Windows

```powershell
dotnet restore HeadlessScreenReader.slnx --locked-mode
dotnet build HeadlessScreenReader.slnx -c Release --no-restore
pwsh -NoProfile -File src/HeadlessScreenReader.Cli/bin/Release/net10.0/playwright.ps1 install chromium
dotnet test HeadlessScreenReader.slnx -c Release --logger "trx;LogFileName=widgets-windows.trx" --results-directory artifacts/widgets-final-windows
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
dotnet run --project src/HeadlessScreenReader.Cli -c Release --no-build
dotnet run --project src/HeadlessScreenReader.Cli -c Release --no-build -- --widgets
```

SDK: repository-pinned 10.0.402. Playwright: centrally pinned 1.63.0. Chromium: 153.0.8010.12, Playwright build 1243. The test-only FakeTimeProvider package stays outside Domain and ScreenReader.

Final local evidence is ignored build output, retained for inspection:

- artifacts/widgets-final-windows/widgets-windows.trx
- artifacts/linux-local-libs/widgets-linux.trx
- artifacts/widgets.jsonl (Windows widget workflow)
- artifacts/widget-base-final.jsonl (Windows base workflow)
- artifacts/linux-local-libs/demo.jsonl and widgets.jsonl
- artifacts/source-manifest.json (source/configuration/lock hashes)

## Linux environment and acceptance limits

The Linux run used Ubuntu 24.04.4, WSL2 kernel 6.6.87.2, and unprivileged user jcolon (UID 1000). SDK 10.0.402 was installed in /home/jcolon/.local/share/headless-screen-reader/dotnet, and source was copied into the separate widgets-vTZQUD directory beneath the same parent. Windows bin/obj output was excluded.

The first Linux run passed 33 core cases and failed all 9 browser launches because shared libraries were absent. Chromium was installed into the user's Playwright cache. Ubuntu packages libnspr4, libnss3, and libasound2t64 were downloaded and extracted into /home/jcolon/.local/share/headless-screen-reader/browser-deps/root, without an administrator install. The cached Ubuntu updates index had a stale ALSA URL; its available Noble base package was used. LD_LIBRARY_PATH supplied the extracted libraries only to the local test/demo process. This test setup is not a deployment dependency image.

The successful Linux test command used the same solution, Release configuration and TRX logger, with:

```bash
LD_LIBRARY_PATH=/home/jcolon/.local/share/headless-screen-reader/browser-deps/root/usr/lib/x86_64-linux-gnu \
  /home/jcolon/.local/share/headless-screen-reader/dotnet/dotnet test HeadlessScreenReader.slnx -c Release \
  --logger 'trx;LogFileName=widgets-linux.trx' --results-directory artifacts/linux-local-libs
```

Both platforms launch Chromium with ChromiumSandbox=true. No no-sandbox option or kernel/AppArmor change was introduced. Successful trusted-fixture launches do not prove arbitrary-site egress isolation, hostile browser containment, hosted Linux acceptance, Xvfb takeover, or tenant authorization. The normal CI workflow installs Playwright system dependencies with --with-deps; that remote environment remains untested.

## Remaining gates

Compare the existing fixture's command, focus and announcement behavior with real assistive technology. Then prove capture exclusion before credential entry or recordings. Target policy and external egress isolation must be implemented and tested before arbitrary-site navigation. Child frames, broader widget patterns, live-region speech scheduling, durable evidence, human takeover, hosted authorization and deployment remain future work.
