# Evidence read recovery v1

An original-account reload of the unchanged GentleBirth task returned503
`CONTROL_PUBLIC_TASK_ORIGINAL_UNAVAILABLE`, reference
`44c4e6d8bd2b4cd681800f59c697c189`, after6758.3353ms. Later independent
S3 reads matched all ten originals/8,156,768 bytes, and the current Domain
validated the original report. Those later reads do not establish the cause of
the earlier failure. Issue PSR-STORAGE-001 retains that uncertainty.

The shared signed storage client now allows one recovery of a settled GET after
408/429/500/502/503/504, a transport failure or an interrupted response body.
It disposes the failed response before dispatching the same GET again, with the
existing twenty-second overall timeout and caller cancellation. Writes, HEAD,
input, changed fingerprints, authorization denials and invalid metadata are not
retried. Missing/oversized declared metadata remains subject to the existing
store's guard and failure classification. A known bounded successful body must
finish at its declared length before being delivered to the caller; caller
fingerprint and current-session checks still apply.

Closed `hsr-evidence-read-v1` records correlate generated request reference,
public-task organization/job when available, hashed object path, original
fingerprint when present, operation, attempt, outcome/cause, status, declared and
received byte counts, validated provider request ID, UTC and duration. The record
has `WebsiteInputRetried:false`. It contains no signed URL, query, credentials,
headers, response body or exception text. Missing IDs are null. Failed observers
cannot change authority or mask the storage outcome. A successful ordinary read
does not add a per-page log; failed and recovered reads do.

| Requirement | Evidence |
| --- | --- |
| One settled read recovery; original bytes and safe correlation | `SettledTransientGetRecoversOnceWithOriginalHeadersAndCorrelatedSafeFacts` covers six response codes; `SettledTransportFailureRecoversWithoutExposingExceptionText` checks the closed transport reason. |
| Actual truncated body settles before recovery | `ActualPrematureTcpBodySettlesBeforeTheOnlyRecoveryAndRetainsReceivedCount` uses a real TCP server: two of four bytes then disconnect, followed by four exact original bytes; exactly two requests. |
| Permanent failures, writes and cancellation remain terminal | `PermanentGetFailureIsReturnedWithoutRetry`, `NonGetOperationsAreNeverRepeatedOrBuffered`, `CancellationAfterFirstFailureCannotDispatchASecondRead`, `RepeatedTransientFailureStopsAfterTwoReads`. |
| Preserve guards, ranges, overwrite refusal and request scope | `OversizedMetadataKeepsTheCallersGuardWithoutReadingOrRetrying`, `RangeHeadersAndExactBytesSurviveTheReadWrapper`, `ConflictingExistingBytesNeverAuthorizeAnOverwrite`, `EvidenceReadCorrelationUsesEachGeneratedRequestReferenceWithoutCrossRequestLeakage`. |

Focused Windows checks pass229/229 with zero failures/skips. Earlier65-case
and128-case failures remain separate receipts under PSR-TEST-014; their metadata
classification and expected GET-count corrections are recorded. Canonical Windows TRX reports4,380/4,380, zero failures/skips. The original tool session was lost during runtime replacement; an external-process watcher could not recover its exit code. The complete TRX is retained separately from that missing exit fact. Solution format is verified independently. Exact-source Linux/CI, promotion and original-account same-task re-read remain separate gates. The active browser tool was removed during live
acceptance; that is not a successful application re-read or a new user-input
attempt. No browser credentials were extracted to replace the original session.

The same HTTP1.24.1 patch corrects the manifest-inclusive search original ID:
1400 originals plus their manifest permit final ID1400. Private MCP/OpenAPI and
the consumer now accept1399/1400 and deny1401. This is a source-proven edge case;
the hosted123-original working search did not hit that limit. Ordinary inventory
offsets remain24-aligned. See PSR-SEARCH-020 and the tenant search receipt.
