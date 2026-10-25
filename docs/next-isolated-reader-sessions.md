# Signed-in private reader accepted; Edge connection next

**Delivery accepted:** fixed-fixture isolated sessions, read-only navigation,
paged events and state/edge evidence are privately deployed at1a5c6dc after
CI40229319948 and deployment40230505705. Windows/Ubuntu each passed2,004;
native Linux30 and operator122 passed. Independent VPS native and API checks
passed with explicitly synthetic identities.

**Confirmed application workflow accepted:** the [real original-user JWT/RLS
check](validation/private-tenant-reader-v1.md) passed observations, heading/button
navigation, matching paged events and graph evidence, denied authority and
independent normal native close. Actual membership revocation/expiry and human/AT
remain separate. **Do next:** [connect hosted Supabase Edge Functions to the
core](next-edge-to-core-connection.md); the core remains private through SSH
until an authenticated reachable path is chosen.

**Current core increment:** [durable scan/report tools](decisions/0052-private-mcp-durable-scans.md) complete fixed-fixture admission, recent history and saved evidence through private MCP, alongside live reading and transient graph pages. Explicit retries retain the original request UUID; report pages recheck authority and immutable response identity after reconnect. These synthetic protocol/Chromium tests remain separate from the accepted real HTTP account workflow above. Public MCP/plugin publication stays deferred.

**Accepted reader repair:** [text ownership reset and re-anchor](decisions/0050-text-cursor-ownership.md)
prevents stale offsets after a reused node changes frame/document. Its
[validation record](validation/text-cursor-ownership-v1.md) separates local,
CI, private delivery and actual application acceptance. This does not expand
the hosted fixture allowlist or enable input.

The implementation sequence below is historical delivery context. Its old
pending-gate language is superseded by the status above and the current
[architecture status](development-status.md#next-isolated-hosted-reader-sessions).

## Historical delivery sequence

Status: in progress. The signed-in shop run and its private S3 report are
accepted. The first delivery step now implements the persistent fixed-fixture
child and strict framed protocol; see [its contract](decisions/0045-persistent-fixture-reader-worker.md).
Native container supervision and separately locked recovery are now implemented;
see [decision 0046](decisions/0046-isolated-reader-supervision.md). Authenticated
registry composition and host recovery/readiness integration are implemented in
[decision0047](decisions/0047-isolated-reader-http-composition.md). The current
isolated lifecycle source3471fd3 passed all nine CI jobs39924056549, exact private
deployment39925519783 and independent VPS service/broker/native checks. Real
application-identity acceptance remains open. [Document-fenced browse navigation and history](decisions/0048-hosted-read-only-navigation.md)
are accepted as4468405 in CI39992719717 and private deployment39995270368.
The current increment adds [actual state nodes, cursor edges and snapshot evidence](decisions/0049-hosted-reader-state-edge-trace.md);
its [delivery checks](validation/hosted-reader-trace-v1.md) remain separate from that identity gate.

The Windows shutdown repair is accepted as `2bc8405`: all nine jobs in CI39909237769,
exact private deployment39910679532 and the actual VPS reader-runtime canary.
Internal native grace is one second with a scoped root stop, and 250ms
for driver-only disposal. Both retain exit confirmation strictly below four
seconds and the
five-second ownership bound. See the [shutdown repair evidence](validation/browser-shutdown-v1.md#one-second-grace-with-unchanged-exit-and-ownership-deadlines).

The next increment lets an authorized API consumer open one reader, inspect the
same page more than once, and close it reliably. The browser runs in its own
restricted worker, without account tokens, database passwords or storage keys.
This is the foundation for later reader navigation and command timelines.

## What we will build

1. **Implemented; validation tracked below:** fixed Foundation-fixture child mode with a bounded, versioned
   open/capture/close exchange. Validate response identity, sequence and size;
   unexpected or incomplete output is an execution failure. No URL, credential
   entry, keyboard commands or recording is admitted in this increment.
2. **Implemented; current delivery gate:** connect the existing tenant-owned HTTP session registry to that child.
   Subject ownership and fresh workspace authorization remain in the trusted
   API. Preserve two-minute expiry, capacity and observation limits, cancellation
   drain and quarantine after uncertain shutdown. Reserve ownership/capacity
   before native launch; a failed launch with unconfirmed cleanup must retain
   its reservation and recovery intent even if no open response was returned.
3. **Implemented; packaged validation required:** reuse the existing native ownership journal, container inspection, resource
   limits and recovery. Use a closed child environment, no network and no secret
   or host-directory mounts. Keep Domain and ScreenReader independent of the
   process/container transport. Use a separate reader journal, not the queue
   worker's exclusively owned journal; recovery must drain the API before
   inspecting either scope.
4. **Implemented; current delivery gate:** extend packaging and readiness to execute the actual packaged child in
   isolation. Retain compatibility with the previous accepted release and its
   rollback/recovery protocol. Stop the API before native recovery; allow only
   reader journal owned by the trusted coordinator. The HTTP service keeps its
   read-only filesystem and uses the worker's private loopback broker.
5. Validate the same browser context across two observations, then actual
   shutdown. Cancellation or an elapsed timeout must not free capacity while a
   browser can still be running. Preserve recovery intent until native removal
   is confirmed.
6. Pass Windows, Linux, formatting and all CI jobs before promoting the exact
   accepted artifact privately. Verify the deployed lifecycle through an
   authenticated tenant consumer. A listening HTTP route alone does not pass.

## Done means

| Gate | Required evidence |
| --- | --- |
| Functional | Open, read metadata, observe twice in the same browser context, close; observation facts retain their uncertainty |
| Authority | Other subjects/tenants cannot access the session; revoked membership blocks subsequent operations |
| Isolation | Real packaged browser child receives no user, database or S3 credentials and has no external network access |
| Failure handling | Bad/oversized output, launch failure, cancellation, expiry and failed cleanup cannot produce false success or reusable occupied capacity |
| Native recovery | Real process/container cleanup and journal retirement; interrupted ownership can be recovered without deleting unrelated resources |
| Delivery | Required local checks, nine successful CI jobs, exact-artifact private deployment and authenticated VPS acceptance |

The existing local Chromium lifecycle tests are useful regression evidence, but
they do not establish the new worker boundary or deployed readiness. The
accepted shop/S3 run is also a separate one-shot workflow.

## First delivery step validation

The focused worker suite passes **95/95 locally on Windows**, including the
actual CLI closure and real Chromium. The full Windows solution passes
**1,702/1,702**, with zero failures/skips, a nonincremental Release build with
zero warnings/errors and format verification. Current-source Windows/Linux CI
and any subsequent deployment remain separate gates, recorded when accepted.
The production HTTP composition now uses the worker's isolated sessions through
a private broker. The packaged real Chromium proof passes28/28 on Windows-launched
Linux/Podman, including global capacity, caller scope, fresh document observations,
native cleanup and sibling survival. Its identities are synthetic. Current-source
CI, private promotion and signed-in tenant acceptance are recorded separately in
[HTTP composition validation](validation/isolated-reader-http-v1.md).

Document-fenced read-only navigation and timeline operations are implemented;
see [their exact validation gates](validation/hosted-reader-navigation-v1.md).
An approved authenticated Edge-to-core network path is still needed for the
separate Lovable consumer. Public MCP hosting, plugins, human takeover and real
assistive-technology acceptance remain distinct later gates.

See [current status and diagram](development-status.md#next-isolated-hosted-reader-sessions),
[owned session contract](decisions/0044-tenant-reader-sessions.md), and
[accepted tenant/S3 evidence](validation/supabase-s3-provider-v1.md#signed-in-tenant-acceptance).
