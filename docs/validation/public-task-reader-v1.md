# Public task reader integration

Accepted November15,2026. Core6f90588, HTTP1.23 /34 private MCP tools, existing Supabase Edge18 and separate UIdec5057. See [decision0090](../decisions/0090-public-task-reader-execution.md).

| Requirement | Evidence |
| --- | --- |
| Actual reader execution | `ReaderTaskUsesRealReaderTabAndRoleBasedActivation`; `ActualChromiumReaderCommandsAndFocusMatchTheRecordedSource` |
| Uncertain/denied input without retry | `ReaderTaskUncertainInputIsNeverPromotedToCompleted`; `ReaderTaskDeniedInputRemainsUnattemptedAndIsNotRetried` |
| Bounded text and truthful provenance | `ReaderAnnouncementIsBoundedWithoutSplittingUnicodeOrLosingItsFullFingerprint`; `ReaderRecordsRejectUnsupportedClaimsAndChangedEvidence` |
| Original event and snapshot binding | `ReaderRecordsMustMatchTheirOriginalEventAndInitialSnapshot`; `ReaderInitialObservationMustMatchTheRetainedSnapshotContents` |
| Missing records cannot support readiness | `ReaderProfileWithoutRecordedCommandsCannotEstablishReaderTaskReadiness` |
| Historical/strict-client compatibility | `HistoricalTasksDoNotFabricateReaderTranscripts`; `ReaderApiIsOriginalBoundAndLegacyEventsKeepTheirExactShape` |
| Paging, digest and current authority | `ReaderPagesBoundEscapedTextWithoutSkippingCommands`; `PrivateMcpReadsOnlyPinnedReaderOriginalsAndDeniesRevokedAccess` |
| Real isolated Linux collector | Four native scenarios require the reader profile, initial read and reader records for completed inputs. The explicit timed-out pointer fixture may have no post-click read; its missing record remains visible and its assessment insufficient. `NativeProofAcceptsOnlyTheExplicitIncompletePointerComparisonWithoutInventingReaderOutput` pins that boundary. CI/native receipt required. |

## Independent release checks

- Canonical pinned-SDK Windows:4,092 discovered,4,092 passed, zero failed/skipped; format verification passed. Earlier failed decoder and invalid exFAT-temp runs remain in the issue catalogue and are not erased by this successful run.
- [Core CI43765670827](https://github.com/jvelezc/ai-screen-reader/actions/runs/43765670827): all nine jobs passed. Windows and Linux each discovered/passed4,092, zero failed/skipped. Four actual isolated Linux Chromium scenarios passed79/79 native assertions with40 hashed originals. The timed-out pointer control retains its one missing post-click reader record and insufficient outcome.
- Exact promotion ffadc3318cfb44cd9d5676edce425d3c:164 installed files/4,012,024,744 bytes independently match their tested artifact. Immutable image sha256:71a75c90e5902271d832d624dea3c7a2dd7bd06c50eeeadb19b7f5b4250fecb3 matches. All13 component health checks pass; recovery attempts0/input replays0.
- Edge18 is active; all three uploaded source modules match local payloads. UI [CI43766148191](https://github.com/jvelezc/ai-screen-reader-ui/actions/runs/43766148191) passes1,074 tests on each OS. The dependency publication gate first held a premature release, then [publication43768060016](https://github.com/jvelezc/ai-screen-reader-ui/actions/runs/43768060016) passed after upstream readiness. All47 published files match the source-bound artifact.

## Original-account production acceptance

The restored application account admitted one task on November15 at22:16:56UTC, saved22:18:10UTC. Parent review fd84324b-7f51-449c-b140-115835106bb4, task54704427-fcab-401e-adaf-25c9ed8b2cc6, worker c27e8f8a-5ae6-425e-8822-203e28b3c3a5 and execution248dcf19-14d5-4eb4-9357-0c20b8e0935b remain distinct identities. Original report SHA-25646e927a98e70f9a87c62ed016b85fa93fe2e9d6f3f5b0073bc0bc44f79e6f480 binds the read and collector.

All three GentleBirth pricing attempts reach their destination. Completed inputs13/13/1 and actual reader records14/14/2 are independently retained; there are no missing reader records. All ten originals/8,156,768 bytes match their hashes and installed collector. Graphs retain14 states/13 actions for each keyboard attempt and2 states/1 action for pointer. The three videos play fully to native ended without error at23.16s,17.8s and4.36s. The recordings retain579,445 and109 decoded frames respectively.

Reload restores the original account, same saved review/task and reader transcript. The124 scoped control requests contain exactly one task admission, all on the tested release. Zero policy interruptions and zero dropped traces accompany the observed goals; six cancelled-request observations remain original technical facts. A historical task correctly reports reader output not retained. At320px the original text and hashes wrap without horizontal overflow; keyboard Enter opens the native evidence disclosure with visible focus.

Independent Maestro run36a7fcb66632417980f2f90d48374961 passes one anonymous sign-in case with zero failures/errors/skips and a matching screenshot. It is separate from authenticated application acceptance and collector evidence; it does not validate reader semantics or external assistive technology.

The supported public task still follows one service link, without form submission, sign-in or purchase. It does not establish site-wide conformance or legal standing. Browser recordings contain no external screen-reader speech. Event/video alignment is unverified; intermediate snapshot contents are not retained. Historical originals remain unchanged.
