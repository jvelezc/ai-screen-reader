# First publication through ordered maintenance

October 31, 2026. Actual private-VPS acceptance at core
`63bf550fb24e495046112830ad661764f770a156`, existing Edge10 and notebook source166746e
(documentation f84804c). This closes the first-publication wrapper gate, separately
from the earlier CLI first publication and wrapper AlreadyCompleted acceptance.

## Current delivery

All nine [core CI jobs](https://github.com/jvelezc/ai-screen-reader/actions/runs/41208728788)
pass. Downloaded Windows/Ubuntu TRX each independently report2,858 discovered,
executed and passed, zero failures/skips. Operational CI reports172/172, including
the real-child directory regression; native Linux71 and database361 remain green.
[Private deployment41210213592](https://github.com/jvelezc/ai-screen-reader/actions/runs/41210213592)
independently matches151 files, default TLS and anonymous guards. The tested
root-owned directory repair is separately installed and backed up; native reads
now succeed from both `/root` and `/`. [Operator receipt](operator-command-directory-v1.md).
UI [CI41208731405](https://github.com/jvelezc/ai-screen-reader-ui/actions/runs/41208731405)
passes705 per OS. No API, Edge, service-credential or public-MCP exposure change is
needed for this acceptance.

## Scoped failure and original attempt

The original signed-in application account explicitly admits one sanitized
route1 while the idle observation worker is frozen and control HTTP is ready.
Metadata confirms the original actor, one queued/gen0 job and zero attempts/leases.

| Identity | Actual value |
| --- | --- |
| Organization | `88060ee5-1eb1-4d74-9fdd-c1f91b9641c7` |
| Scan | `ba452b08-ba42-4efe-8d2d-020e4969cbbd` |
| Job / generation | `87c54b05-7bbd-45c9-8886-891d34d0d549` / `1` |
| Original actual producer | `b2275e08-9f02-4428-9d2e-72780d8de9fd` |
| Complete retained bundle |1,869,713 bytes |
| Bundle SHA-256 | `911714af4f6e5fe1168a82eaea068c8453d1ea47c459e666ffa7a2d35b726387` |

Only that new job's baseline destination is an owned empty test directory; its
device/inode is retained. Thawing normal observation executes the route once.
Local persistence then fails after complete-output retention and native cleanup.
Both the original account and database report blocked/NotCollected with
`FIXTURE_OBSERVATION_UNCONFIRMED`, one attempt, original producer, no lease/archive.
The output seal and exact native-removal acknowledgement are validated. Current
absence is independently checked as rootless `hsr-worker`, not root's namespace.

Before recovery, the complete four-file output stage, six progress files and108
source-image journal/files are independently hashed. The helper removes only its
same empty test-directory inode; it deletes no original file. There is no bad S3
credential, one-shot worker or idle-coordinator kill. Those alternatives were not
executed. This is a scoped actual **local publication-path failure**, not proof
of a network outage or a full staging disk.

## Actual first wrapper publication

The official root maintenance wrapper runs from `/root`, under its deployment
mutex. It stops control before observation, reconciles native ownership, waits
for the actual independently managed publisher to settle and restores ordered
worker/control readiness. It returns **Published**, not AlreadyCompleted, with
`InputsDispatchedByRecovery=0`, `CanReplay=false`, and overall goal NotAssessed.
The original producer, complete bundle hash and reference match. Database job
state becomes completed with one original attempt, no error and no lease. The
publication audit contains exactly Claimed and Completed, with no error.

The full output/progress/source-image inventories remain byte-identical. Fresh
native absence, actual HTTP/TLS/151-file release checks, both service invocation
IDs and FreezerState=running are verified again. No publication transient remains
active. Automatic restart stays disabled; no test-directory fault remains.

Independent signed private S3 reads compare every stored part, all five originals,
the producer manifest, index and separate baseline with the retained source.
Archive size is1,870,309 bytes; index SHA-256 is
`9e0709317abc5100ab45ea0137844e8c425007b3bfa712588d53da55c9d27806`,
producer-manifest SHA-256 is
`7cc1e92d0585e14623b6e05e36006243a575a2e205d42dc8b23b6e82fef3439d`.
Credentials remain in the private verifier process and are not printed.

The original account refreshes Observed/Available and verifies all five originals
through existing Edge/core. The recovered route has three observed states/two
actual edges and two attempted/completed calls. Selecting a2 shows its recorded
Observed/Satisfied result; baseline inputs remain0 and baseline objective false.
The5.2-second original video loads at readyState4, seeks to2.564045s, plays via the
native player and naturally ends at5.2s with no error. This is agent-operated
recording playback, not human usability or actual assistive-technology speech.

**Next:** caller-scoped cancellation of queued/active admitted runs, preserving
uncertain input and originals while fencing execution and publication. Remaining
network/disk/expiry/authority-loss, bounded external websites and hosted human/AT
acceptance stay open. Controlled fixtures still cannot authorize a website-side
evidence package; public MCP/plugin publication remains deferred.
