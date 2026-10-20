# Reader v10 bounded tables

Date: October 19, 2026. Profile: hsr-desktop-reader-v10. This increment implements read-only native table navigation in trusted fixtures. See [decision 0011](../decisions/0011-bounded-native-table-navigation.md).

For example, the Products fixture returns "$3, cell, native table 3 rows, 2 columns, row 2, column 2, row header Apples, column header Price". Native geometry is source-tagged, includes hidden positions and is distinct from exposed peer counts. Header association requires explicit row/col scope and exposed local ownership. Missing headers stay unknown.

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| Native geometry validation and source-tagged row/cell/header context | TableSessionTests.TableGeometry_ValidatesNativePositions; TableHeaders_AreSourceTaggedAndReadCurrentRetainsContext |
| Read-only cell/row/column movement and table boundaries | TableSessionTests.TableMovement_PreservesCoordinatesAndFocus; TableBoundaries_DoNotWrapOrEnterAnotherTable; TableDirections_RequireCellBeforeCoordinateMovement; TableRowEntry_UsesOnlyCellsInCurrentRow |
| Empty snapshots, missing native semantics and unsupported geometry stay explicit | TableSessionTests.TableDiscovery_EmptySnapshotAndMissingNativeGeometryRemainExplicit; TableEmptyAndUnsupportedGeometry_StayExplicit |
| Missing, unnamed, foreign or unscoped headers cannot be invented | TableSessionTests.TableHeaders_MissingUnnamedForeignAndUnscopedRemainUnknown |
| Malformed ownership, coordinates and refresh cannot manufacture movement | TableSessionTests.TableOwnership_MalformedOrForeignCannotManufactureCellNavigation; TableCoordinates_ConflictingCellsRemainUnknown; TableRefresh_StaleCellAndNewDocumentResetCursor |
| All eight table commands reject focus mode without input | TableSessionTests.TableCommands_FocusModeNeverDispatchesInput |
| Real Chromium geometry, headers, both directions, boundaries and unchanged focus/page | ChromiumTableTests.Tables_Chromium_NativeGeometryHeadersAndCellMovementAreReadOnly |
| Real missing/unnamed headers and hidden positions | ChromiumTableTests.Tables_Chromium_MissingOrUnnamedHeadersRemainUnknown; Tables_Chromium_HiddenContentIsExcludedAndNativePositionsAreNotExposedCounts |
| Multiple/nested tables, unsupported spans/grid/virtual geometry/header links and child-document ownership | ChromiumTableTests.Tables_Chromium_UnsupportedLayoutsNeverReceiveGuessedNavigation; Tables_Chromium_NestedTableUsesOnlyItsOwnHeaders; Tables_Chromium_ChildDocumentOwnershipExcludesParentHeaders |
| External removal invalidates stale cells and refreshes dimensions/headers | ChromiumTableTests.Tables_Chromium_ExternalRemovalInvalidatesStaleCellAndRefreshesHeaders |
| Previous profiles cannot reuse reference evidence | ReferenceValidationTests.Assessment_PreviousReaderProfile_RequiresFreshEvidence |

## Validation

```powershell
dotnet build HeadlessScreenReader.slnx -c Release --no-incremental -p:RestoreLockedMode=true
dotnet test HeadlessScreenReader.slnx -c Release --logger "trx;LogFileName=reader-v10-windows.trx" --results-directory artifacts/reader-v10
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
dotnet run --project src/HeadlessScreenReader.Cli -c Release --no-build -- --tables
```

Local Windows: 337 discovered/executed, Passed: 337, Failed: 0, Skipped: 0. This comprises 278 core and 59 real Chromium cases. All 277 v9 test identities remain. The increment adds 60 cases: 48 core and 12 real Chromium. Solution discovery and TRX result records agree. Clean locked Release build passed with zero warnings/errors and architecture enforcement; format verification passed. The table CLI returned all five flags true: TableHeadersObserved, CellNavigationObserved, BoundariesPreserved, MissingHeadersRemainUnknown and ReadOnlyBrowseObserved. Evidence is retained under artifacts/reader-v10.

The focused suite initially exposed a PreviousTable enclosure bug: from a cell it could return the current table. It now skips that enclosing table, with both core and real child-document regressions. Two initial core failures concerned the test setup navigating through deliberately foreign headers and ImmutableArray reference equality; assertions now directly inspect the retained current cell and header contents. No cases were skipped and no engine failure was converted into an accessibility barrier.

The CI workflow runs the full suite and --tables demo on Windows and Ubuntu after every push to main. Remote CI receipts are separate from local results. Local Ubuntu WSL, fresh v10 NVDA comparison, hosted deployment and customer acceptance have not been performed for this increment.

Generalized tables, implicit/complex header algorithms, editable grids/treegrids, spans, declared virtual geometry, full text flow, speech arbitration, durable artifacts and capture/egress isolation remain open. Milestones 0 and 1 remain partial. The next planning action is to review the remaining reader acceptance matrix and select one bounded increment.
