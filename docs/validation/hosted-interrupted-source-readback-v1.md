# Hosted interruption and partial-original readback

October 31, 2026. This is an actual admitted private-VPS failure injection and
original-application-account readback, separate from the earlier normally
completed 71-image run and local/CI crash cases. The original proposal is unchanged.

## Release and scope

Core `6cbec97530dc7633e03acc6133b43e1bceb0a34a` passes all nine
[CI jobs](https://github.com/jvelezc/ai-screen-reader/actions/runs/41205547398):
Windows and Ubuntu independently parsed TRX reports each contain 2,858 discovered,
executed and passed tests, with zero failures or skips. Its
[private deployment](https://github.com/jvelezc/ai-screen-reader/actions/runs/41206968955)
is independently verified against all 151 files, default TLS, API1.13 and anonymous
route guards. UI `4018a55ab9ab0355e7a7b1610c0d42b4f9307765` passes
[both CI jobs](https://github.com/jvelezc/ai-screen-reader-ui/actions/runs/41205546961),
705 tests/32 files per OS. Its source remains `166746e`; existing Edge v10 consumes
the published API with the original user's authentication.

| Identity | Actual value |
| --- | --- |
| Organization | `88060ee5-1eb1-4d74-9fdd-c1f91b9641c7` |
| Scan | `328822fe-ed1c-40c5-bdae-3030e2c178b1` |
| Job / generation | `0cf42a49-02cc-4b02-8058-a2bb1dd09a2b` / `1` |
| Actual original producer | `755fb580-7ccc-48fb-895c-00282caee9d5` |
| Native owner | `f715bd2c-0b8a-40dd-81a2-e4010c2d7fc6` |
| Inspected container ID | `5916d0def79950d17d61d6f9a7dd7c350b12f0a05af6239b999b3a5cb11babb2` |

The original Chrome account admits exactly one fixed sanitized route4. Database
metadata confirms the same actor as the prior accepted run, initially queued at
generation0 with zero attempts/leases. No privileged admission replaces that user.

## Actual interruption

Preflight finds zero unfinished jobs, leases, native containers and pending native
intents. The idle observation unit is briefly frozen while its dependent control
API remains ready, then thawed after the exact scan/job observer is armed. Stopping
observation also stops its dependent control service, so stop/start alone is not
an admission hold; actual HTTP readiness is checked after restoring both services.

The private observer holds the deployment mutex. It requires a durably checked
Observed checkpoint containing one real InputCompleted event and one browser
keystroke, plus committed source originals. All five native labels, full ID,
immutable image, fixed command, user and isolation settings must match. Only that
inspected full container ID receives KILL; the coordinator and unrelated containers
are untouched. Eight observer safety tests pass on Windows and Linux; a real
historical sealed recording is refused before injection.

Ninety committed images exist before the kill. The existing worker records owned
removal at `2026-10-31T13:52:19.4243277Z`; rootless `hsr-worker` independently confirms
current absence by full ID and name. All ninety originals remain unchanged in a
117-image, 1,576,655-byte subset after in-flight writes settle. Every retained file
matches its recorded length, SHA-256 and raw presentation timestamp. There is no
output seal or archive. Both scan and job are blocked with
`FIXTURE_OBSERVATION_UNCONFIRMED`, one attempt, the same producer/generation and no
lease. Both services are active and observation is thawed; automatic restart stays
disabled. No input or replacement attempt is dispatched by recovery.

## Original-account readback and uncertainty

The existing signed-in notebook reads the blocked report, progress and all117 image
metadata through Edge/core. Its retained graph has two observed states, two intents
and only one result: command1 Tab has one confirmed input and Observed outcome;
command2 Tab has no result checkpoint. Its input and outcome stay Unknown, with
an explicit instruction not to repeat uncertain input. Overall goal stays
NotAssessed. Four checkpoints are available; these are diagnostic records.

The user-facing source picker saves source117 as 13,489 bytes. Direct comparison
with the private host original is byte-for-byte equal, with SHA-256
`586f031a9ae5b1ce30fd0bf9c97c08905350ee897420ae9b38a291e843ddc6af`.
Raw timestamp `1793454738445.3499` ms and image-set fingerprint
`98326717b4ea030a0ef282be4b4233ce305904fc7092784a766e7525f75c6198`
match. A real workspace reconnect rereads the same blocked attempt, producer,
117 images, fingerprint and selected image metadata without another sign-in.

The saved-recording disclosure reports unavailable. Assessment says **Original
recording required**; it does not invent a barrier, Ready decision or completed
recording from partial images. This is controlled-fixture technical acceptance,
not website-side sufficiency, actual AT speech, custody or judicial admissibility.

**Next:** validate/install the [operator directory fix](operator-command-directory-v1.md), then prove first publication through the ordered maintenance wrapper using one scoped storage
failure. Remaining network/disk/cancellation/authority-loss, controlled external
website, hosted intervention and human/AT acceptance remain open. Public MCP/plugin
publication stays deferred.
