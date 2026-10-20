# 0014: Local MCP prototype before hosted services

Status: accepted. The user requested sustained work toward a usable prototype for another AI client, such as Claude. MCP integration takes priority over the previously queued listbox typeahead increment.

Add a nonempty HeadlessScreenReader.Mcp composition project using the official C# SDK over stdio. Domain, ScreenReader and Browser.Playwright retain their dependency boundaries. There is no HTTP listener, remote account, database or hosted authorization claim. Each client process owns one browser and one serialized controller.

Expose status, trusted fixture opening, immutable paginated snapshots, reader commands, bounded sampled idle updates, paginated timeline and explicit close. Return both text and structured JSON, with protocol error flags for host errors. All stdout belongs to MCP; page content and tool arguments are not logged. Page content is untrusted data and cannot authorize tool or policy expansion.

Compose the reader with an explicit no-editable-entry browser gate. Tab, reverse Tab and Escape allow traversal; other supported keys require observed noneditable control focus. Printable routing keys are denied. AX editability is source-tagged so role overrides do not authorize input into an editable control. A typed pre-dispatch policy exception produces Denied/InputDenied, zero completed input and no accessibility-barrier claim. This gate keeps entry unavailable; it is not the later capture-exclusion proof.

Session tokens prevent old owners from controlling replacement browsers. Every command carries a document precondition checked by ScreenReader after fresh observation and before routing input. Immutable snapshot pagination uses a separate observation token, invalidated by input or fresh capture. Absolute session lifetime is ten minutes, with automatic browser disposal, a 500-operation bound, 100-item page bounds and a 128 KiB response limit. Oversized input results retain input-completion and verification uncertainty in their error evidence.

Publish a framework-dependent .NET executable and generate absolute-path MCP configuration usable by Claude Code and Desktop. Register only this project's local Claude Code entry when requested. An existing same-name entry fails rather than overwriting configuration; unrelated entries remain intact. AI-model authentication remains owned by the client.

Public URLs stay disabled until target policy and external egress isolation are implemented and tested in a subsequent increment. Editable entry, credentials and recording stay disabled until capture exclusion is proven. This local connection does not complete the hosted MCP, capture, ownership, durable evidence or hostile-site isolation milestones.
