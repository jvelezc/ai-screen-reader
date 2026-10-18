# Manual reference validation

Date: October 17, 2026  
Status: Optional comparison tooling implemented; operator comparison pending

HeadlessScreenReader is an independent screen reader for AI agents. Its primary acceptance criteria are structured observations, navigation semantics, explicit action outcomes, and real-browser fixtures. NVDA is an optional external benchmark, not a runtime dependency or required acceptance gate. Actual NVDA behavior must come from real observations, separately from automated HSR results. NVDA 2026.2 was installed locally after the comparison tooling was prepared.

Keep the reference case catalog, worksheet assessment and manual runner in the CLI host. Domain and ScreenReader remain platform-independent and do not gain NVDA or browser-control dependencies. The Chromium adapter adds trusted embedded content identity, actual runtime versions and a close signal; it exposes no arbitrary target or focus-injection command. The manual branch creates no ScreenReaderSession and sends no automatic input. Its headed session ends when the fixture window closes, on cancellation, or after a ten-minute bound.

Reference results start NotRun. Assessment checks schema/profile/content identity, the complete case set, environment metadata, actual operator notes and explanations of differences. A complete assessment can include differences; it means selected cases were reviewed from operator-supplied data. It is not independent attestation that NVDA ran, nor a compatibility or accessibility conformance claim.

The test project references the CLI assembly to exercise its non-trivial worksheet validation directly. This does not change production dependency direction or add a new project/package. Real Chromium tests still validate actual fixture semantics and lifetime. Headed NVDA interaction remains a separate manual acceptance step described in docs/validation/nvda-reference.md.
