# Native search input authority v1

Local SDK prerequisite, November15,2026. `LaunchGuardedSyntheticSearchRecordingAsync` is a separate entry point for the two known embedded search sources. It adds an independent decision before every actual input without changing the original local launch signatures or granting public-site entry. The receiver returns Granted, Revoked, Expired or Unconfirmed; undefined decisions deny input.

| Requirement | Evidence |
| --- | --- |
| Bounded identity, source hash and exact key/character/mouse shape | `SearchAuthorityIntentRejectsInvalidNativeScopes` |
| Nonnull receiver and known source before output creation | `GuardedSearchRejectsUnrelatedSourcesAndMissingAuthority` |
| Exactly one scoped request for each native key, including all13 query characters | `ActualGuardedSearchUsesOneFreshScopedGrantPerNativeInput` |
| Revocation before character7 retains six entered characters and sends no later query key | `MidQueryAuthorityRevocationStopsTheNextCharacterWithoutReplay` |
| Late permission cannot dispatch after the wait deadline | `LateAuthorityResultCannotDispatchAfterItsDeadline` |
| Deadline remains active through final native field/scope checks | `AuthorityDeadlineCoversNativeChecksAfterTheGrant` |
| Changed focus or a faked search role on an unsupported native input cannot redirect entry | `ScopeChangeDuringAuthorityWaitCannotRedirectTheNextCharacter` |
| Permission failure hides its raw message and sends no key | `AuthorityFailureCannotDiscloseItsMessageOrDispatchInput` |
| Expired, revoked, undefined and unconfirmed decisions deny ordinary keys | `NonGrantedAuthorityCannotDispatchAnOrdinarySearchKey` |
| Bound receiver cannot be replaced; unsupported keys/sources cannot acquire grants | `GuardedSearchAuthorityCannotBeReplacedOrGrantUnsupportedKeys` |
| Caller cancellation cannot dispatch a later grant | `CancelledAuthorityWaitCannotDispatchALaterGrant` |
| Capture exclusion joins pending authority and denies input during the acknowledged gap | `CaptureExclusionJoinsPendingAuthorityBeforeAcknowledgingTheGap` |
| Mouse focus and activation have independent actual dispatch facts | `MouseSearchActionsCannotBypassTheirOwnAuthority` |

Initial focused run23/23 passes; expanded source/scope/cancellation checks31/31 pass. A further native fault deliberately delays the field getter3,400ms after a grant. Its independent listener observes13 keys before the owner-lifetime fix and0 after it; the same assertion fails then passes. Failed and corrected original videos are retained with their fingerprints in PSR-SEARCH-007. This is measured revision-bound evidence, not inferred mutation coverage.

The corrected native/search/legacy group passes75/75, zero failed/skipped, and solution format verification passes. Capture-pause interaction has its separate covering check. Canonical Windows and Windows/Linux CI remain independent gates recorded after completion. No durable hosted job, public search, API/MCP admission or Edge/app search feature is claimed by this input hook. The published service-link workflow remains separate. The last completed search display/history source dd8613e passes canonical Windows4,174/4,174 and all nine CI43787118946 jobs,4,174 per OS.

The capture interaction also exposed PSR-SEARCH-008: the reader withheld confirmation after exclusion began, but an independent native listener received one key. Phase plus epoch are now rechecked after the response and immediately before dispatch; the covering regression passes1/1 with zero actual/confirmed keys and joined acknowledgement. Both failed receipts remain retained.
