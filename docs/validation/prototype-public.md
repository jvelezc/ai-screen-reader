# Approved public MCP prototype validation

The prototype uses the actual stdio server, Linux container fence, validating proxy and sandboxed Chromium. See [installation and first Claude task](../prototype/claude.md) and [decision 0015](../decisions/0015-isolated-public-reader-prototype.md).

| Requirement | Evidence |
| --- | --- |
| Exact HTTPS host policy and malformed/private targets rejected | PublicTargetPolicyTests.TargetPolicy_RejectsUnsupportedOrUnapprovedUrls; TargetPolicy_AllowsExactHttpsHostWithCaseAndDefaultPort; PolicyConfiguration_RejectsAmbiguousHosts |
| Private, metadata, reserved and mapped addresses denied | PublicTargetPolicyTests.AddressPolicy_RejectsPrivateSpecialAndMappedAddresses; AddressPolicy_AllowsObservedPublicAddresses |
| DNS rebinding cannot change the connected address; mixed answers denied | ProxyProtocolTests.DnsResolution_RejectsMixedPrivateAnswersAndPinsOnePublicAddressWithoutResolvingAgain |
| Exact header and tunnel limits; tunnel bytes never consumed as headers | ProxyProtocolTests.HeaderReader_StopsAtExactBoundaryAndNeverConsumesTunnelBytes; TunnelCopy_EnforcesExactByteLimit; ProxyProtocol_RejectsNonConnectAndAmbiguousRequestLines |
| Startup lease expires at 60 seconds; established lease at 15; wrong owners and expired leases cannot extend access | ProxyLeaseTests.StartupGrace_ExpiresAtSixtySecondsAndCannotBeRevived; Pulse_UsesFifteenSecondDeadlineAndWrongOwnerCannotExtendIt |
| Public input dispatch limited to Enter on actual link focus | McpInputPolicyTests.PublicPages_OnlyFocusedLinkEnterCanDispatchBeyondTraversal |
| Ordinary unisolated MCP cannot open public URLs | McpContractTests.Workspace_RejectsUnboundedObservationAndUnknownCommandAndMissingSession |
| Worker accepts zero capabilities or only SYS_CHROOT and rejects broader/invalid sets | IsolationProbeTests.CapabilityGate_AllowsNoPrivilegesOrOnlyChrootAndRejectsBroaderSets |
| Independent live fence, nonroot/capability/read-only checks and certificate-verified TLS | scripts/Test-ReaderIsolation.ps1: ControlTcpSentinel, ControlUdpSentinel, six DefaultDropPolicies and every IsolationReceipt field must pass |
| Real MCP public reading, preserved missing heading, read-only browse, FocusRequired, exactly Tab/Enter, new destination/heading verification and closed session | scripts/Test-ReaderPrototype.ps1 invokes PrototypeSmoke.RunAsync; all receipt flags and ContainersReleased must pass |

Local Windows: 525 discovered, executed and passed, zero failures/skips: 442 core and 83 real Chromium cases. All 467 prior identities remain. Locked clean Release build has zero warnings/errors; architecture dependency checks and formatting verification passed. Test identities, result records and TRX counters agree.

Local Linux: Podman Linux amd64 passed the live isolation gate and the complete public MCP task from example.com to IANA. The source actually exposed no heading; the reader preserved NoMatch while reading its paragraph and link. This is a live one-off receipt, not a promise that public markup or availability remains fixed. No page instructions influenced the declared goal. The proxy/fence resource expiry and cleanup are checked independently of semantic task success.

Claude Code: both headless-reader and headless-reader-web register locally and report Connected. A model-driven task remains pending client login: the attempted Claude run returned Not logged in before tools or model tokens. The independent MCP client completed the task. Remote CI evidence is linked from the architecture Page and GitHub Actions after the published increment passes. This is local software; no hosted deployment occurred. Fresh NVDA/other assistive-technology validation was not performed for this increment.
