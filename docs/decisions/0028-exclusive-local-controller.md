# Exclusive local controller

Run the real Chromium demonstration:

~~~powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --controller
~~~

Two sequential AI controllers share the same context. The first sends Tab; after release and actual drain, a second generation resumes from a fresh persisted baseline, sends Activate and verifies the checkout-preview heading. Contending and stale leases are rejected. Open the printed folder for command receipts and the token-free controller lifecycle summary.

The controller privately composes the owned reader/store pipeline. Every command requires the current scan ID, bearer token and generation. One lease lasts from one second through two minutes; expiry uses the monotonic clock, independently of wall-clock changes or delayed timer callbacks. Lease expiry timestamps are informational. Admission is checked again before calling either adapter input method: ordinary keys or listbox characters. An already admitted adapter operation may finish after expiry and remains owned until stop/drain acknowledgement. Reader policy still decides whether a particular key is permitted.

Release or expiry closes controller admission immediately, clears authority and starts owned capture exclusion. No replacement is granted until whole actual commands, actual adapter operations and owned writes have drained and exclusion has acknowledged. Cancelling a command or release wait does not release actual work. A pending write or native call remains tracked; timeout or unconfirmed drain faults closed. A late acknowledgement cannot reopen a faulted controller. Reacquisition uses the same browser context, fresh capture epoch and persisted baseline before granting a new generation. The supported first scope has no human input during the gap.

Generation is included in command intent/completion evidence. Lease bearer tokens stay out of persisted evidence and status. Input requested, native calls completed, uncertainty, reader outcome and verified destination remain separate. A stored result with a browser error is still a browser error. Policy denial does not become an accessibility barrier. The last actual snapshot does not imply a post-input observation.

Each scan grants at most 32 leases and retains at most 256 metadata lifecycle events. Existing owned command/file limits also apply. There is no renewal or automatic takeover: release or let the bounded lease expire, then explicitly acquire. Closing revokes admission, drains actual work and closes the native browser; an unconfirmed close remains explicit. The trusted process must compose all its input through this owner. Its registry prevents duplicate controller wrappers on the same adapter; it is not a host/tenant security boundary and cannot block an external process with direct desktop access.

No public MCP change, human role, remote viewer, credential entry, arbitrary website mutation or general recording is enabled. Hosted leases, durable recovery and fencing against stale remote workers require later integration. See [validation](../validation/exclusive-local-controller-v1.md).

## Next: headed Linux session feasibility

Prove the same bounded controller flow with headed Chromium on Xvfb while retaining the same process/context across controller replacement and capture exclusion. Use a task-owned display and cleanup, retain actual process/display metadata and explicit failures, and run the experiment in Linux CI. Headless and headed launch modes must never switch mid-scan. This is a prerequisite to the later private viewer and human-input protocol; a headed test alone does not prove VNC authorization, viewer revocation, accessibility of remote authentication or sensitive-input exclusion.
