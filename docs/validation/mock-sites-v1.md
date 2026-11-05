# Fixed mock-site catalogue v1

November5,2026. Four authored synthetic sites cover working/keyboard-blocked
checkout and named/unnamed details controls. The source and expected facts are in
[the catalogue manifest](../../ops/test-sites/v1/manifest.json). The public lab
uses the existing hostname and serves data only. Public API/MCP input authority
is unchanged; local fixture acceptance is distinct from hosted task acceptance.

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| Four stable source-bound scenarios; preserve old fixture identities | `CatalogueBindsFourAuthoredScenariosToExactEmbeddedSource` checks literal IDs, version, source SHA-256, authored name/goal/outcome fields and unchanged old enum values. |
| Working/broken keyboard pair, attempted/completed inputs, goal and actual graph | `CheckoutKeyboardPairRetainsActualInputsGoalAndGraph` runs real Enter/Space, verifies actual keys, goal/focus outcome, three/four observed graph nodes and linked two/three command edges. Failed input does not fabricate a barrier. |
| Independent pointer experiment and reset | `FreshPointerComparisonAndResetKeepSyntheticStateExplicit` uses a fresh actual browser, mouse click, focused goal, then reset to the same observed action ID and hidden result. |
| Missing names stay missing without inventing activation failure | `MissingControlNameStaysMissingWithoutInventingAnActivationFailure` checks actual AX names, reader announcements, zero-input cursor movement and successful goal activation. |
| Actual local stdio MCP provenance/input/goal receipts | `StdioMockSitesKeepSourceNamesInputAndGoalOutcomesSeparate` discovers all four identities, opens real Chromium, matches source hash, reads names and exact completed keys, checks goal result and closes the session. |
| No credential-entry authority | `OtherFixtures_CannotGrantTheSyntheticInputChannel` now includes all four mocks and confirms denial without active authority. |
| Export exact original bytes and preserve existing output | `test_exports_exact_original_bytes_and_only_six_fixed_files`, `test_refuses_existing_destination_without_mutating_its_files`. |
| Reject source/identity/version/script/path drift | `test_rejects_changed_fixture_before_creating_output`, `test_rejects_wrong_version`, `test_rejects_missing_or_duplicate_scenarios`, `test_rejects_changed_fixture_identity_and_path`, `test_rejects_missing_script_hash_policy`, `test_rejects_missing_explicit_public_path`, `test_rejects_an_unexpected_script_even_with_an_updated_source_hash`. |
| Keep static lab separate from customer/API fallback | `test_fixed_lab_is_ordered_before_consumer_fallback_and_unknown_paths_deny`; actual Caddy configuration and HTTPS checks are separate deployment proof. |

The narrow Windows run discovers22 and passes22, with0 failures/skips; three are
previously existing synthetic-channel cases. The increment adds19 .NET cases.
The solution entry point discovers every new case. Export checks pass10/10.
The full Windows solution run discovers3,722 and passes3,722, with0 failures/skips;
its parsed TRX contains all19 new cases. Strict nonincremental Release build
passes with0 warnings/errors, and format verification passes. Independent VPS
Linux export checks pass10/10. Both CI operating systems and actual static
deployment are tracked separately; no CI/deployment success is inferred locally.

## Remaining scope

The public URL collector is still `hsr-approved-public-readonly-v1`: zero target
inputs, no task attempt and no evidence-sufficiency verdict. Local graph tests
are actual synthetic actions, not a served public website task or real-user AT
recording. Missing source-bound video/repeats/comparators remain missing. Wider
mock features and bounded website tasks follow [the ordered plan](../completion-plan.md).
No plaintiff disability, injury, standing, liability or admissibility follows
from these fixtures or their test outcomes.
