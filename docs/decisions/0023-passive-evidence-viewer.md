# Passive local evidence viewer

The recorded traversal command now emits route-N.html beside its existing JSON, Markdown, SVG and original media. Open the index printed by:

~~~powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --traversal-recording
~~~

Select an observation, action or timeline event. The viewer pauses and seeks the original recording to the retained conservative interval, shows actual source/result and verification, and highlights related events. Native video controls support playback. Keyboard users can Tab to an evidence button and press Enter; equivalent observation tables and readable/structured reports remain available.

This is a passive local projection. It has no live browser connection, network dependencies, replay command or automatic input. Selection leaves original media unchanged. A strict content security policy and encoded source strings keep exposed website names inert. The writer retains its run/document/hash binding and append-only preflight, including HTML and pending HTML files. A 4 MiB display limit falls back to the full readable and structured evidence.

Unknown, partial and outside-clip timing never acquires an invented frame. Missing or failed media retains its stage and does not change destination classification. Execution, policy, error codes, attempted/completed keys, unexecuted instructions and timeline truncation remain visible. An accessibility acquisition interval is not an atomic screenshot; interval overlap does not establish causation.

Only the existing separately opted-in, input-free synthetic fixture can produce these recordings. This does not enable general recording, credential entry, human takeover or hosted durability. See [validation](../validation/passive-evidence-viewer-v1.md).

## Next: reader-wide capture exclusion

Close and drain complete reader commands and their retained evidence before the browser exclusion token is acknowledged. The existing adapter fences captures and keys; an already-running reader command can still retain or announce data after its browser call returns. A single trusted session transition must prevent that late retention, preserve same-context resume and clear cached reader state. Exercise cancellation, stalled operations, timeline access and every command entry point with deterministic and real Chromium tests. Keep sensitive entry and human input disabled until the broader gate is proven.
