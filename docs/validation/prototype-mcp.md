# Local MCP prototype validation

The local prototype composes the v12 reader with official ModelContextProtocol 2.2.0 over stdio. Installation and use are documented in [Use HeadlessScreenReader with Claude](../prototype/claude.md); see [decision 0014](../decisions/0014-local-mcp-prototype.md).

| Requirement | Evidence |
| --- | --- |
| Real stdio discovery and usable tool schemas | ChromiumMcpTests.StdioDiscovery_AdvertisesBoundedReaderToolsAndUsableSchemas |
| Real Chromium browse/focus/activation, goal verification, timeline and close | ChromiumMcpTests.StdioWorkflow_RealChromium_BrowseFocusActivateVerifyTimelineAndClose |
| Immutable pagination and explicit observation invalidation | ChromiumMcpTests.StdioPagination_IsImmutableAndCannotReuseInvalidatedObservation |
| Replacement sessions and changed documents cannot authorize stale input | ChromiumMcpTests.StdioSessionReplacement_RejectsOldOwnerAndStaleDocumentWithoutInput; McpContractTests.DocumentPrecondition_RejectsChangedDocumentBeforeInput |
| Absolute lease, automatic disposal and exact operation bound | ChromiumMcpTests.SessionLease_ExpiresAndDisposesBrowserWithoutNewInput; SessionBudget_StopsFurtherOperationsAtExactBound |
| Disabled controls and disallowed printable keys do not produce input | ChromiumMcpTests.StdioBlockedControlAndDisallowedTextKey_RemainExplicitWithoutInput |
| Idle observation is bounded, sampled and read-only | ChromiumMcpTests.StdioIdleObservation_IsBoundedSampledAndReadOnly |
| URLs/paths/numeric fixture names and invalid bounds are rejected | McpContractTests.FixtureOpen_RejectsUrlsPathsNumbersAndUnknownNames; SnapshotBounds_RejectInvalidPagesBeforeOpeningBrowser; Workspace_RejectsUnboundedObservationAndUnknownCommandAndMissingSession |
| Protocol error flags, structured results and response bounds preserve uncertainty | McpContractTests.ToolErrors_SetProtocolErrorAndStructuredContent; OversizedReply_PreservesInputUncertaintyWithinResponseBudget |
| Editable/unsupported focus, printable routing keys and editable role overrides cannot receive input | McpInputPolicyTests.EditableOrUnsupportedFocus_IsPolicyDeniedWithoutDispatchOrBarrier; PrintableRoutingKeys_CannotBecomeTextEntry; EditableRoleOverride_CannotBypassInputGate; EditableActiveDescendant_IsRejectedEvenWithNoneditableOwner |
| Traversal can leave editable fields while real account/password entry is denied and values excluded | McpInputPolicyTests.TraversalKeys_CanLeaveEditableControlsWithoutTyping; ChromiumMcpTests.StdioEditableAndPasswordFocus_RejectsInputAndExcludesValues |

The focused run passed 36 cases: 27 core and nine real Chromium/MCP cases. Tests use the official MCP client to launch the actual server executable from an unrelated working directory, open real Chromium, and assert returned JSON and protocol flags. These are not mocked browser acceptance results. Review added an explicit no-editable-entry browser gate: printable routing keys are denied, and nontraversal keys require supported, noneditable focus. AX editability carries provenance. Policy rejection returns Denied/InputDenied with zero completed keys and no input uncertainty; it cannot become a browser error or accessibility barrier.

The installer published successfully and registered a local-project Claude Code entry. `claude mcp get headless-reader` reported Connected. A real Claude model acceptance attempt returned Not logged in before any model tokens or tool calls, despite cached account metadata being present. Client authentication is required before claiming a completed Claude-driven task. The independent stdio workflow has passed.

Exact full-suite Windows and Windows/Ubuntu CI receipts are recorded in GitHub Actions and the architecture Page. Local Linux, public-site isolation, hosted deployment and fresh assistive-technology validation remain separate gates.

Local Windows: 467 discovered, executed and passed; zero failures/skips, comprising 384 core and 83 real Chromium cases. All 431 previous v12 test identities remain. Clean locked Release build, architecture enforcement, formatting verification, published server configuration and Claude Code MCP connectivity passed. Discovery identities and TRX counters/records agree. Reports are retained locally in artifacts/prototype-validation; client model authentication remains pending.
