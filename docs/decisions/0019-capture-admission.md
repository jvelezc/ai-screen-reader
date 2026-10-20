# Capture admission and observation epochs

The browser adapter now owns one admission gate for accessibility snapshots, ordinary keys and scoped listbox characters. Exclusion closes admission before draining the underlying operations, including operations whose callers have cancelled or timed out. A late observation is rejected. A keyboard call already admitted may have executed; its eventual error does not mean input was denied before dispatch.

Only after drain and `Accessibility.disable` acknowledgement does the trusted fixture controller receive an opaque exclusion token. The token is bound to one adapter and epoch. It grants no human input authority. The adapter currently has no video, screenshot, trace, HAR, clipboard, network-body or typed-value collectors enabled.

Resumption requires the current token and a fresh AX baseline before admission opens. The fixture owner must first clear sensitive content; this protocol cannot determine whether arbitrary page content is safe. The browser context survives. A new capture epoch resets reader cursor, mode, text navigation and live-region history. A retained reader baseline emits `CaptureBaselineReset`; browser lifecycle evidence marks every exclusion even when no reader command ran during it. Do not infer continuity or actions through the gap.

Drain, collector stop and baseline acknowledgements each have a five-second deadline. Failure is terminal for this adapter: admission remains closed with a sanitized error and explicit Faulted status. Late completion cannot reopen it. Close the adapter and start a fresh session; automatic retry would require a separately proven recovery protocol. Lifecycle events contain only phase, epoch, sequence and monotonic elapsed milliseconds. Their 256-event retention limit is explicit.

Public-page capture control is unavailable. The fixture capability is not exposed through MCP. No credential entry, human takeover, screen recording or capture privacy certification is enabled by this increment.

## Next acceptance gate

Add an explicitly controlled collector feasibility probe. Prove stop acknowledgements, frame callback drain and segment boundaries using synthetic visual canaries. Inspect decoded frames and persisted/staging artifacts, rather than searching compressed video bytes for text. Prove restart failure stays closed and preserve the same context. Only then enable permitted original recordings and measure at least 20 action markers against the proposal's 250 ms synchronization target. Each additional collector must join the exclusion protocol before it can be enabled.
