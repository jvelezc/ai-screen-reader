# HeadlessScreenReader

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

AI agents inspect websites and perform bounded keyboard traversals. The project owner reviews observations, traces, and evidence.

## Product Purpose

Build an independent screen reader for AI use, preserving observed semantics and uncertainty while tracing actions as nodes and edges. A future path assessment will distinguish demonstrated difficulty from an unsearched or failed route.

## Operating Context

.NET on Windows and Linux, Chromium, local CLI and evidence reports. The architecture plan remains under outputs/. Development commits directly to main and requires Windows/Linux CI acceptance before the next increment.

## Capabilities and Constraints

The private viewer displays scoped pixels and retained reader observations. Its controls affect viewing only. Public MCP hosting, Claude onboarding and official plugin publication are deferred. Real credentials, human target input, remote identity, hosted durability and customer acceptance remain separate gates. See docs/development-status.md for current evidence.

## Product Principles

- Separate attempted input, completed input, observed state and verified goals.
- Preserve Unknown and explicit human assistance.
- Treat engine failures and policy denials separately from accessibility barriers.
- Keep observation exclusion and actual operation drain effective across cancellation and shutdown.

## Accessibility & Inclusion

Use native keyboard controls, visible focus, semantic headings and explicit state labels. Automated fixture proofs do not establish human or assistive-technology acceptance.
