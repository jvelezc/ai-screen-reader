# Recorded evidence claim verification

Source: [decision 0062](../decisions/0062-recorded-evidence-claims.md).
The original architecture proposal under `outputs/` is unchanged.

## Behavior evidence

| Requirement | Evidence |
| --- | --- |
| File integrity cannot verify uploaded barrier or success assertions | `VerifiedFileHashesCannotVerifyUploadedActionsBarrierOrSuccessfulOutcome` |
| Complete admitted originals preserve actual facts and controlled scope | `ActualAdmittedOriginalsVerifyRecordedFactsWithoutInventingWebsiteBarrierRepeatsOrMouseSuccess` |
| Altered target, keys, focus, AX, outcome, environment, attempts and files stay insufficient | `UploadedClaimsMustMatchOriginalTargetKeysStatesOutcomeContextAttemptsAndFiles` |
| Missing/corrupt originals, authority loss, changed attempt, foreign scope and cancellation cannot release verified facts | `MissingChangedForeignOrUnauthorizedOriginalsCannotReleaseVerifiedFacts` |
| Older originals do not acquire missing context or a full AX bundle | `OlderOriginalsRetainMissingCollectorContextAndBaselineOnlyAxWithoutFabricatingCompleteness` |
| Actual Chromium Unknown survives independent HTTP/stdio reconnect and assessment | `AdmittedKeyboardGraphOriginalSurvivesHttpAndStdioReconnectWithoutPromotingUnknown` |
| Whole-original reads are bounded, read each object once and reject corruption/cancellation | `WholeOriginalVerificationReadsEachObjectOnceAndRejectsCorruptionAndCancellation` |

These Chromium and protocol tests use a synthetic provider identity. They do not
replace original-user provider/RLS, private S3 and hosted acceptance.

## Local Windows

The final source run discovers, executes and passes **2,397/2,397**, with zero
failed or skipped cases. The strict Release build has zero warnings/errors;
full solution format verification passes. The actual Chromium/HTTP/stdio and
legacy-original cases above are included. Linux, CI, deployment and actual-user
acceptance are separate gates.

## Delivery gates

Core source **ad50291a209666f4688595b792fe245b339a7993** passes all nine jobs in
[CI40728261620](https://github.com/jvelezc/ai-screen-reader/actions/runs/40728261620).
Downloaded Windows and Ubuntu TRX each report total/executed/passed **2,397**,
failed/not-executed0. Separate actual Linux worker receipts pass45/45 and isolated
database275/275. This is remote CI, not a local Linux full-suite run.

[Deployment40731060134](https://github.com/jvelezc/ai-screen-reader/actions/runs/40731060134)
promotes that exact release. Independent VPS verification checks151 files and
the exact release path; both services are active and default-trust HTTPS reports
1.9.0. Anonymous `/api/session` returns401; `/`, `/mcp`, `/host.json` return404.
Public MCP remains deferred. Existing Edge `hsr-core` v6 is ACTIVE and all three
files match reviewed source; existing authentication is unchanged.

UI **e8cfb2d17c5339f064ef882d0f665bfecb1bbf17** passes439/439 on both OSes in
[UI CI40731180478](https://github.com/jvelezc/ai-screen-reader-ui/actions/runs/40731180478),
with types, lint0 errors/six existing warnings and production build.

## Original application account acceptance

The existing signed-in notebook at localhost:6087 uses the original caller
through Edge v6 to deployed core1.9. No token or password was extracted, and no
privileged identity substituted for the user. Reconnected requests reauthorize
the workspace and read complete originals again.

| Case | Actual result |
| --- | --- |
| Legacy scan6d693885-2adb-4213-989a-ae56690e570a | Source Verified, environment unverified, AX BaselineOnly,2/8, Insufficient; no invented context |
| Fresh route1 scan319a522a-776f-4040-95a8-3e0531e74d5b | Actual job/archive d89f6000-a0f2-45ca-8ca3-dfc8dcaade60, generation1, two completed events, one task attempt, Reached; source/context/originals verified,4/8, NoBlockingBarrierDocumented for the controlled task only |
| Fresh route3 scan49775312-ea4a-40b4-891a-6423ad21d31d | Actual job/archive e3939cea-feec-4079-8d05-72e12a58d98a, generation1, three completed events, one task attempt, Unknown; source/context/originals verified,4/8, Insufficient |
| Intentionally altered route3 outcome | Identical original event/file references with keyboard outcome changed to completed and observed outcome to Reached; source Mismatch, outcomeVerified false,2/8, Insufficient, websitePackageReady false; UI labels it Reported outcome (unverified) and Supplied event timeline |

Actual worker4014b020-8944-4903-9570-9fddd5da1caf produced both fresh runs.
Both retain pointer not-tested, repetitions1 and no trusted accessibility-failure
classification. Current controlled fixtures cannot authorize a website package.

Independent deployed CLI S3 reconstruction verifies five originals per run:
route1 **531,779 bytes**, index
`f039c7cc7197e468d8ffcabf372c0c1a6cbdc6cd9d0e6832762bb7521438cf06`;
route3 **923,494 bytes**, index
`f3016a367aed602509e8f0cc9c618a4cdd184de32df1d24648b569673f470089`.
Both return IntegrityMatches true. Separate rootless Podman inspection finds no
remaining observation containers. Storage remains25MiB with no MIME restriction.

Fresh browser downloads retain these actual response hashes:

| Assessment | Bytes | SHA256 |
| --- | --- | --- |
| Legacy | 9,937 | `128200f9258222dbcaa5f2d2d0dff569f1a8a9f95ce13779272921a11eec3d57` |
| Reached | 9,694 | `e7489b26c31a1197b9318908d840e57da9377335691eceaf4d8fbfd6f887e582` |
| Unknown | 10,560 | `c9ef6db82d8bba23954ad355cf67097ac49b3607c5f40c2606dbf544cf35e9d1` |
| Altered claim rejected | 10,904 | `3597a0b331f27e1aebba83a1623e472a84f7fc4fc9642ab0fad2c5bc178bc146` |

Full reload restores the same account without sign-in and clears the assessment,
source selection and media. Fresh reauthorization of route3 downloads an
assessment byte-identical to the10,560-byte original. Keyboard activation focuses
the verdict; native Space toggles a checklist summary and retains its focus.
Actual320x900 assessment layout reports client/scroll width305/305, with no
horizontal overflow. The viewport is restored.

The same route3's graph/video loader verifies all five originals and restores
four states/three action edges. Selecting a3 retains Unknown/TimedOut and Covered
interval0.540440–1.166958s. Timing is calibrated with63.6668156249998ms uncertainty;
native seek is0.540439s and the2.2s original plays to its end without media error.
Reconnect then removes graph/video, assessment and source selection. This is
agent-operated browser acceptance, not human or assistive-technology acceptance.

The architecture Page records the accepted slice and makes interrupted-recording
recovery the clickable NEXT. Its saved diagram matches the locally tested source;
NEXT/Back, keyboard/focus and320px behavior are verified in its sandbox preview.

## Remaining original-plan gates

[The completion plan](../completion-plan.md) orders durable attempt recovery,
hosted authority loss, bounded website collection, hosted intervention and real
assistive-technology acceptance, then release operations. This implementation
does not complete those gates or turn controlled fixtures into website evidence.
Public MCP and plugins remain deferred.
