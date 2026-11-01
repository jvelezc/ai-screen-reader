# Bounded complete-video decoding

November 1, 2026. Product: Privensic Screen Reader. The original proposal under
`outputs/` is unchanged. [Decision0078](../decisions/0078-bounded-complete-video-decoding.md).

## Current delivery and new original-account failure

Core9cb3dae passes all nine [CI41244915189](https://github.com/jvelezc/ai-screen-reader/actions/runs/41244915189)
jobs: Windows/Ubuntu each3060 discovered/executed/passed,0 failed/skipped;
native71/database461/operations172 pass separately.
[Private delivery41246097683](https://github.com/jvelezc/ai-screen-reader/actions/runs/41246097683)
matches all151 actual CI artifact files and all151 installed files; manifest
`17d931e1d6b1fd34e1f5341e8299e9cdc59652aca65b44385c102d744469d713`.
Both unchanged actual service invocations, Restart=no/unfrozen and actual dedicated
rootless native0 are checked again on November1 before admission and after the
new failure; both actual service invocations remain unchanged.

The original Chrome profile reconnects and restores the existing application
session asynchronously, without another password entry. The original personal
workspace opens normally. New scan `3e635654-1b82-4b2a-9c2f-b71e3d91f021`, job
`0241d2cf-fbeb-4bcc-b2f8-5da30ec22b9e`, request
`49c26e7e-3efa-4778-8623-10777812e1e2`, generation1, producer
`1932446a-cc05-4556-a784-3635a230548f` is admitted under the original account.
This distinct normal disabled-control route blocks with
`WORKER_PROGRESS_RECORDING_FINALIZATION_UNCONFIRMED`, one attempt, zero leases
and no result/archive. No observer, interruption or replay is introduced.

The account reads the same failed report, seven checkpoints, three intents/three
observed results and238 retained original-image entries. The last Activate
result alone confirms0 browser calls/Blocked. Overall input and goal remain
Unconfirmed; partial originals do not establish completed recording or a barrier.
The restored session closes the earlier browser-access blocker, not the recording
fault. Preserve this attempt and all old attempts without replay.

## Reproduction and local validation

An actual Chromium positive control records more than10seconds with independent
first/last red/green pixel controls. The old compiled decoder fails
`CAPTURE_FRAME_COUNT_INVALID`; the repaired decoder passes and preserves the
complete original video's SHA256. No first/tail trimming or sampling is used.
The initial pipe experiment failed because the pinned ffmpeg build only supplies
image2 output; its actual help/configuration was inspected and the production
path uses that supported muxer/protocol, without another dependency.

The focused decoder/privacy/traversal/timing/synthetic-media/framed-failure set
discovers, executes and passes162,0 failed/skipped. The full pinned Windows
Release suite discovers, executes and passes3,106,0 failed/skipped, in8m20s.
Strict Release build has0 warnings/errors and full format verification passes.
New-source CI, exact private delivery and fresh hosted
recording acceptance remain gates. A count or passing local control alone is not
proof of the precise earlier hosted exception.

CI41350330957 passes all71 actual Linux worker cases, then fails release-image
export with `no space left on device`; no release archive is produced or promoted.
The worker job now reclaims only the unused Android installation at the verified
literal `/usr/local/lib/android` on GitHub-hosted runners and prints disk capacity
before/after. It preserves the tested image, release binding and all existing
gates. The [runner image inventory](https://github.com/actions/runner-images/blob/main/images/ubuntu/Ubuntu2404-Readme.md#android)
documents that installation. A fresh complete CI run is still required.

CI41351099639 verifies that disk repair: native71/database461/operations172 and
the complete151-file release archive pass. Both OS suites pass3,106 tests with
zero failures/skips, but the20minute Windows job limit cancels final CLI checks;
the check-run annotation explicitly confirms that deadline. The earlier complete
Windows batch took19m26s, and browser installation varies. The matrix CI budget is
now25minutes; product/input/shutdown/decode deadlines are unchanged. Neither
incomplete CI run authorizes private promotion. Require a fresh all-job success.

| Requirement | Evidence |
| --- | --- |
| Decode a complete >10second actual native video without modifying originals | `NativeDecoderRetainsWholeValidVideoBeyondLegacyTenSecondCeiling` |
| Preserve every framed byte/order across short reads and ancillary chunks | `ConcatenatedFramesPreserveEveryByteAndOrderAcrossShortReads`, `AncillaryChunksAreRetainedWithoutTreatingFramingAsPixelVerification` |
| Admit exact count/individual/aggregate bounds and reject adjacent overflow | `NativeFrameCountMatchesTheExistingArchiveBoundAndRejectsTheNextFrame`, `PerFrameAdmissionIncludesTheSignatureChunkHeadersAndCrc`, `AggregateAdmissionCannotReturnAnAlreadyReadSuccessfulPrefix` |
| Reject malformed/truncated output with closed errors and no partial success | `TruncatedFrameNeverReturnsEarlierCompleteFrames`, `MalformedNativeFramingIsSanitizedWithoutReturningAFrame` |
| Deny huge allocation/dimensions before browser inspection; retain valid boundaries | `DeclaredOversizedChunkIsDeniedBeforeAllocatingOrReadingItsPayload`, `OversizedOrEmptyDimensionsAreRejectedBeforeBrowserPixelDecoding`, `ValidDimensionBoundariesRetainTheExactNativeBytes` |
| Cancellation cannot return a completed prefix; internal limits cannot expand defaults | `CancellationAtOrInsideTheNextFrameCannotReturnTheCompletedPrefix`, `InternalBudgetOverridesCanOnlyTightenProductionLimits`, `AStricterFrameLimitIsEnforcedWithoutChangingTheProductionDefault` |
| Preserve actual native invalid-input and first/last pixel behavior | `NativeDecoder_InvalidMediaFailsWithSanitizedCode`, `VideoDecoder_InspectsActualFirstAndLastFramesWithIndependentSolidColorControl` |

## Next acceptance gate

Require both CI platforms and successful release export, promote the exact tested
artifact privately, then admit distinct normal recordings under the original
account. Read complete originals, source/action links and actual native playback
on successful runs; use closed failure reasons for any stop. Do not replay the
failed run or turn an engine/media limit into a website barrier. Exact delivery,
local/native success and actual account acceptance remain separate. Wider failure,
authority, website, intervention, operations and human/AT work remains in the
[ordered completion plan](../completion-plan.md).
