# Scoped hosted full-staging-disk failure v1

Status: actual scoped volume exhaustion, retained-byte restoration and a distinct
normal-capacity job pass. The fault exposed an empty cleanup-acknowledgement
startup gap; its fix passes161 focused and3,145 full Windows tests, strict build
and formatting, with CI, exact candidate delivery and actual original-stage
recovery still pending.
The verified live baseline is
core5053852, all nine CI jobs/3,129 per OS, native71/database461/operations172,
151 exact deployed files and accepted controlled long-video graph/playback.
[Timing receipt](long-video-timing-v1.md).

## Declared and exercised fault

Admit one new sanitized checkout through the original application account while
the idle observation coordinator is frozen and control remains available. Confirm
the queued/gen0 job has zero attempts/leases and its original actor/scope.

Limit only that future job's gen1 recorded-output directory with a64KiB private
nodev/nosuid/noexec tmpfs. Pin the underlying device/inode and exact owned mount;
verify visibility in the worker namespace. The host disk and unrelated jobs are
not the fault target. Keep the source/progress/native-removal journals on their
normal durable filesystem.

Let the regular worker run once. Verify an actual full staging volume, a closed
output failure, no seal/archive/successful goal and independently confirmed exact
native absence. Inspect every retained source/progress fact; identify unavailable
original media explicitly. A full-disk failure cannot claim bytes it did not save.

Before removing the fault, freeze the idle coordinator again, durably back up and
hash every retained stage byte, unmount only the pinned owned mount, confirm its
original empty underlying inode and exclusively restore all retained bytes with
the original ownership. Verify every hash and absence of the temporary mount.
Thaw and verify original service readiness, no native leak and truthful incomplete
output state. A distinct normal job must prove capacity recovery without replaying
the failed attempt or restarting the worker.

## Actual original-account receipt

Original workspace88060ee5-1eb1-4d74-9fdd-c1f91b9641c7/actor9a9c1d87-1d46-4171-9f21-409546e772b4
admitted scand02b0b62-2fc0-483a-bf7b-d21f46eb7c4d/job
e1f2dc63-4b1b-4961-ac3e-490f8dbead3b while idle coordinator was frozen: queued/gen0,
zero attempts/leases. Only its gen1 staging directory received the declared64KiB
nodev/nosuid/noexec tmpfs; its exact mount ID/source and original inode/device
were pinned and visible inside the worker namespace. The normal worker then ran
once: blocked/FIXTURE_OBSERVATION_UNCONFIRMED, one gen1 attempt, zero leases,
no digest/reference/index/manifest/archive and zero rootless native containers.
The scoped filesystem actually had **zero available bytes** and no seal.

| Retained stage file | Bytes | SHA-256 |
| --- | ---: | --- |
| start.json | 1,009 | 260f3c85c2e355582b289889f3da4830b93d7e4531966a1654fa51b8af76b523 |
| bundle.bin.pending | 61,440 | 2c9f2a63714ce7df87bba8802956d9b60367502ccb9d419363ef7aeb3e60e1ac |
| native-removal.json.pending | 0 | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |

Every stage byte was exclusively backed up/fsynced/hashed outside evidence,
the pinned owned mount removed, original empty underlying inode confirmed, then
every retained byte restored and independently matched against its backup. No
test mount/pause remains. Both original service invocations are running, unfrozen,
Restart=no.71 source JPEGs/1,012,060 bytes independently match their private
manifest. They are partial source evidence, never a completed video or archive.

The actual original account sees blocked/NotCollected, input and overall objective
Unconfirmed. Fresh retained-progress reads show5 checkpoints/2 intents/2 observed
results, latest Activate with1 completed input and Satisfied command verification.
That local command result does not promote the incomplete overall report.

## Distinct normal-capacity control

Without restarting the same worker, the same original account admitted
scan718d1a1c-e928-4e02-bf97-9856a3558089/job19927afb-6c84-4f63-9a5b-da3eeb47454a.
It completed once/gen1 on original producercdcefa51-728c-406e-86cd-9708bea63dc8,
zero leases/native containers,2 completed keystrokes/Reached/NoneObserved,
3 nodes/2 edges,58 source images/78 decoded video frames and closed capture.
All five originals independently match private S3 and the1,098,605-byte archive:
index4a0ed416797d1bc8fb16e184ae6b30d983521de9cd8bed462d1b8705997a5f08,
manifest752bf60ec0c12f3cf47ec2f76cb6a4f35c952c948c4313790460ec7a8a92a65d.
Both worker/control PIDs and invocation IDs are unchanged from5053852. This proves
capacity recovery without replaying the failed job. Actual consumer playback of
this control and startup recovery are separate checks.

## Exposed recovery gap and next gate

The full volume left an empty native-removal.json.pending. The existing recovery
requires an exact completed pending acknowledgement, so it rejects this actual
state. Four compiled old-code regression cases reproduce the empty/short-prefix
failure. The narrow fix can append only an authoritative exact metadata prefix,
after validating the actual separate native-removal journal, scope/header and any
existing destination. It cannot seal, publish or repair partial recording bytes.
Wrong/corrupt/foreign/unconfirmed/oversized metadata stays denied and unchanged.

161 focused and3,145 full Windows cases pass locally, zero failed/skipped; strict
build has zero warnings/errors and format verify/no-restore exits0. Both CI OSs,
exact successful-CI artifact and actual original-stage cleanup recovery remain.
Deployment first recovers with the previous binary, so promotion must be held
until the exact successful candidate can reconcile this metadata safely. Preserve
the original failed run, every retained media/header byte and the independent
native-removal proof. No successful startup recovery is claimed yet.

| Requirement | Deterministic regression evidence |
| --- | --- |
| Repair only actual empty/short acknowledgement prefixes; preserve61,440 partial media bytes, header, no seal, idempotence/startup | StartupCompletesOnlyTheAuthoritativePendingPrefixWithoutSealingPartialMedia (4 lengths; compiled old-code red4) |
| Foreign or unconfirmed native proof cannot extend even a matching prefix | ForeignRemovalCannotExtendEvenAnOtherwiseValidPendingPrefix (5 cases) |
| Every prefix byte must match; mismatches remain byte-identical and unacknowledged | MismatchedPendingPrefixIsPreservedWithoutExtendingOrAcknowledging (3 cases) |
| Expected length+1 cannot be truncated into a receipt | PendingRecordOneByteBeyondExpectedLengthCannotBeTruncatedIntoAnAcknowledgement |
| Missing native proof cannot fill an empty record | MissingNativeRemovalCannotFillAnEmptyPendingAcknowledgement |
| Cancellation cannot extend the empty record | CancelledRecoveryCannotExtendTheEmptyPendingRecord |
| Existing corrupt final marker prevents any pending mutation | CorruptFinalRecordPreventsAnyExtensionOfTheValidPendingPrefix |

Local commands use repository-pinned SDK10.0.402: strict Release build with
warnings as errors, `dotnet test HeadlessScreenReader.slnx -c Release` and
`dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore`.
Linux/CI, exact hosted repair/delivery, actual account and real human/AT acceptance
remain separate. No test is presented as an actual full-disk write; the scoped
hosted fault supplies that evidence independently.

**Done when:** actual volume exhaustion stays an engine/storage failure, originals
and uncertainty remain explicit, no partial bundle becomes an archive, owned native
cleanup is independently confirmed, every retained byte survives fault removal,
all temporary mounts/guards settle, a distinct normal job recovers capacity and
the exposed incomplete-acknowledgement startup state is safely reconciled.
Keep network outage, lease expiry, provider loss and human/AT gates separate.
