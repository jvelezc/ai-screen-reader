# Public review size failure and operational diagnostics

## Measured cause

On November 5, 2026, the original signed-in GentleBirth review failed before
publication. Scan `0ca7e225-4ea3-4ff6-9665-abc26b607cc5`, job
`10b1d877-8a92-4967-8f9b-c776fd5473a2`, generation 1, ran from 22:36:58 to
22:37:19 UTC and retained `PUBLIC_WEBSITE_SOURCE_UNCONFIRMED` without an original.
That historical attempt stays failed; it is not replayed or relabeled.

A credential-free probe used the installed immutable image, original rootless
worker, approved source, sandbox and egress fence. Navigation reached HTTP 200
and the actual load event. The 1,327-node capture serialized to 1,140,112 bytes;
the shared 262,144-byte IPC response ceiling rejected it. No redirect or navigation
timeout was observed. A candidate assembly overlay with the separate 2 MiB public
response ceiling completed Open/Observe/Close, returned 2,270,336 wire bytes and
confirmed owned removal. This probe is not database or customer acceptance.

## Bounded repair

Public-worker responses now allow 2 MiB. The Foundation worker's shipped 256 KiB
constant and request/input authority remain unchanged. Retained passive originals
allow 4 MiB across codec, database completion/column constraint, HTTP/MCP reads
and consumer original offsets. `MaximumBytes` retains its legacy 1 MiB constant;
new consumers use `MaximumSupportedBytes`. Existing small originals remain valid.
Older independently compiled consumers with a 1 MiB ceiling need this capacity
update to read larger results. Per-page originals remain 64 KiB and public replies
128 KiB, with exact digest/producer/scope checks and fresh authorization at EOF.
The separate private storage bucket remains 25 MiB. Target, isolation, membership,
lease/producer, native shutdown and no-input/no-goal gates are unchanged.

## Finding a failed review

The customer notice shows an opaque job reference. Restore UUID hyphens if
needed, then inspect that job's attempt and the worker service journal. Do not
copy original page data or credentials into operational logs or support messages.

* `hsr-public-website-operation-v1`: Claimed, Collected and Finished events include
  organization/scan/job/generation/worker IDs, elapsed milliseconds, capture node
  counts, terminal state, approved error code and confirmed cleanup facts when
  actually known. Unknown cleanup facts remain null.
* `hsr-public-reader-failure-v1`: session-bound, strictly validated child metadata
  includes stage/cause, sequence, observed node count, bounded HTTP error status,
  payload/budget bytes and elapsed time. Malformed, oversized, foreign-session or
  extra stderr cannot become trusted diagnostics. Native cleanup remains a
  separate authority gate; a diagnostic cannot confirm it.
* `hsr-control-request-v1`: each request gets a server-generated reference shared
  by `X-HSR-Request-Id`, error problem and structured journal event. It records the
  release commit, finite operation/method, HTTP status, approved failure category,
  elapsed time and authenticated route UUIDs. Caller-selected references are not
  accepted. Unknown routes/causes use fixed fallback labels.
* Existing Supabase `hsr-core` Edge logs join successful/failed finite operations,
  timing, validated request/scan/workspace IDs and the validated core reference.
  Edge support references survive safe upstream failures. HTTP 200 status polling
  alone is not successful collection; inspect the worker terminal event.

No event contains URL/query, email, bearer/cookie, lease capability, page/AX text,
request/response body, exception message or stack. Logging sink failures cannot
change the operation result. Child stdout remains the framed protocol; host
diagnostics use stderr. Journal retention follows the existing host policy and
Supabase logs follow project retention; off-host draining, alerts and permanent
incident retention are not introduced by this repair. Durable attempts preserve
coarse causes; detailed transient child metadata lives in operational logs.

## Validation and delivery

The pinned disposable PostgreSQL suite passes 663/663, zero failures/skips, and
confirms container removal. New SQL cases prove above-legacy and exact 4 MiB
completion, rejection of 4 MiB plus one, and existing capability/scope rules.
Migration `public_website_large_page_budget` is applied to the existing project;
only the worker role can finish a public scan. Local focused table/API checks pass
46/46, including real Chromium and large provider originals with bounded pages
and fresh exact-EOF authorization. The first full Windows run was stopped after
an existing native-close deadline failure (1 failed, 1,663 passed before abort);
the affected real table cases pass independently. Cleanup deadlines were not
weakened. A fresh complete Windows run and both-OS CI are required for acceptance.

The UI source is `caaa2e6`: local 955/955 tests, typecheck/build pass, lint has
zero errors and six existing component-export warnings. Edge publication, exact
private release promotion, static publication and the original signed-in
GentleBirth retry remain separate until their actual receipts are appended.

CI at `7361f66` rejected the published `--check-control` probe because default
request diagnostics polluted its deliberately single-JSON merged output. Request
logging is now explicitly composed only by the production host. Readiness probes
and test hosts without a sink remain quiet; the real service keeps structured
stderr events. The existing actual-publication case
`PublishedControlRuntimeChecksHttpGuardAndJoinsStopWithoutTenantClaim` enforces
this boundary. The failed CI artifact is not a deployment candidate.

The following CI run also exposed a merged-stream assumption in the public
collection acceptance script. That script now retains operational stderr JSON
lines separately and parses only the single stdout result. It does not disable
worker tracing or treat diagnostics as completion authority. Fresh native and
both-OS gates are required after this harness correction.

Supabase pre-change health advisories are empty. Existing security notices include
intentional worker-table default-deny RLS and checked authenticated definer RPCs;
the unrelated public `rls_auto_enable` grant and disabled leaked-password check
remain an operator backlog. See [definer-grant remediation](https://supabase.com/docs/guides/database/database-linter?lint=0028_anon_security_definer_function_executable)
and [password-protection guidance](https://supabase.com/docs/guides/auth/password-security#password-strength-and-leaked-password-protection).

Regression names: `PublicResponseAcceptsTwoMiBButFoundationBudgetAndOversizedHeaderRemainBounded`,
`LargeCapturedContentRoundTripsAboveLegacyLimitWithExactDigestAndProducer`,
`LargeProviderOriginalRetainsBoundedPagesAndFreshAuthorizationAtExactEof`,
`RequestsAndProblemsShareGeneratedReferencesWithoutLoggingCallerContent`,
`BoundChildFailureReachesTerminalLogWhileForeignDiagnosticsAreDiscarded`,
`LoggingSinkFailureCannotPreventAuthoritativePublication`, and
`PublicConstructorCannotBypassTheLogAllowlist`.

## First hosted acceptance and remaining failure-path repair

Core `44c1b219a979ab39958c2f2a8f4b0df9acaf67fa` passed all nine jobs in
[CI42131419973](https://github.com/jvelezc/ai-screen-reader/actions/runs/42131419973).
Windows and Linux each discovered/executed/passed 3,752 tests, with zero failures
or skips. The complete local Windows run also passed 3,752/3,752 in 9m38s; the
strict build and format checks passed. Linux worker acceptance passed 73/73 and
confirmed public native absence and owned database removal. The exact tested
archive was promoted: deployment `2d9c88a6f3144e93b3c3fd09f8838928` reports Ready
at that commit, replacing `237d309`. Automatic private promotion remains held.

UI `caaa2e6a4dc6639be54c5e7b67f5918bfcf9b721` passed both CI platforms
([42129314640](https://github.com/jvelezc/ai-screen-reader-ui/actions/runs/42129314640))
and was published by [42129939417](https://github.com/jvelezc/ai-screen-reader-ui/actions/runs/42129939417):
47 exact files, entry SHA-256 `f8ddc4aac212d6804b1ac689e18f6cc63c42701303ff2f960d778e804c86121b`.
Edge v15 readback matches all three committed files and retains its existing
in-function identity checks. The 4 MiB database bound and worker-only completion
grant are confirmed by readback; existing security advisories are unchanged.

The original application account remained signed in after reload. Its old failed
GentleBirth attempt displays the matching job reference. A fresh GentleBirth
review completed and the actual UI reported that the original download was
verified and sent. This is passive page-review acceptance, not a completed task
or a barrier/evidence-sufficiency conclusion.

A separate real HTTP 404 review (job `98bba754-e161-40fb-99ed-fc1422f66215`)
retained its blocked state/reference and coarse worker lifecycle, but exposed a
remaining detailed-cause gap: after child failure, the launcher waited for a
successful-Close input handoff and added `PUBLIC_READER_INPUT_TRANSFER_UNCONFIRMED`.
The strict diagnostic parser correctly rejected that mixed stderr. The launcher
now requires settled input for successful exit only; failed readers keep their
nonzero exit, drained typed diagnostics and independent native cleanup. The
actual Linux collection acceptance adds a second, controlled 404 job and checks
bound Navigation/TARGET_HTTP_ERROR/404 metadata, no original, retired queue/lease
and actual native absence. Its fresh CI and promotion are a separate gate.
