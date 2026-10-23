# Next: isolated hosted reader sessions

Status: in progress. The signed-in shop run and its private S3 report are
accepted. The first delivery step now implements the persistent fixed-fixture
child and strict framed protocol; see [its contract](decisions/0045-persistent-fixture-reader-worker.md).
Native container supervision and separately locked recovery are now implemented;
see [decision 0046](decisions/0046-isolated-reader-supervision.md). Authenticated
registry composition, host recovery/readiness integration and signed-in hosted
session acceptance remain the next gates.

The next increment lets an authorized API consumer open one reader, inspect the
same page more than once, and close it reliably. The browser runs in its own
restricted worker, without account tokens, database passwords or storage keys.
This is the foundation for later reader navigation and command timelines.

## What we will build

1. **Implemented; validation tracked below:** fixed Foundation-fixture child mode with a bounded, versioned
   open/capture/close exchange. Validate response identity, sequence and size;
   unexpected or incomplete output is an execution failure. No URL, credential
   entry, keyboard commands or recording is admitted in this increment.
2. Connect the existing tenant-owned HTTP session registry to that child.
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
4. Extend packaging and readiness to execute the actual packaged child in
   isolation. Retain compatibility with the previous accepted release and its
   rollback/recovery protocol. Stop the API before native recovery; allow only
   the narrow writable journal path needed by the trusted coordinator.
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
The new worker is not connected to hosted HTTP sessions. The isolated supervisor and its separate recovery journal now have 32 focused
Windows tests. Next, change the registry and host recovery/readiness composition;
see [supervisor acceptance](validation/isolated-reader-supervisor-v1.md).

After this gate, add document-fenced reader navigation and timeline operations.
An approved authenticated Edge-to-core network path is still needed for the
separate Lovable consumer. Public MCP hosting, plugins, human takeover and real
assistive-technology acceptance remain distinct later gates.

See [current status and diagram](development-status.md#next-isolated-hosted-reader-sessions),
[owned session contract](decisions/0044-tenant-reader-sessions.md), and
[accepted tenant/S3 evidence](validation/supabase-s3-provider-v1.md#signed-in-tenant-acceptance).
