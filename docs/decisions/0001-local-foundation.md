# Local foundation architecture

Date: October 17, 2026  
Status: Accepted for the first implementation increment

## Context

The product's stable behavior is reader command interpretation and observation. Chromium, MCP, storage, and hosting are replaceable integrations. The plan already establishes stateful workflows, an independent reader engine, evidence provenance, eventual tenant boundaries, and a single-server starting deployment.

Those existing requirements supply the architecture context. Team size, future request rates, and regulatory certification are not assumed and do not justify additional services now.

## Decision

Use a small modular .NET solution with inward dependency direction: Domain at the center, ScreenReader and Browser.Playwright depending on Domain, and a CLI composing both. These are code boundaries, not a separate deployment per module.

Start with one trusted embedded fixture. Use Chromium's accessibility tree through Playwright CDP. Expose no browser page, locator click, arbitrary script, URL navigation, or focus injection through the reader contract. Implement only observed local semantics and explicit unsupported outcomes.

Keep mutable cursor/mode state inside a single ScreenReaderSession, with serialized input. The local gate does not implement distributed ownership, human takeover, or controller fencing.

Use sealed immutable records for observations and command results. Omit input values and editable descendants from normalized snapshots. Retain a separate input uncertainty indicator when a dispatched action has no reliable post-observation.

Use built-in .NET analyzers, central package versions, lock files, and an MSBuild dependency guard. Prefer constructors and small existing contracts; add abstractions only for real external boundaries or tested variation.

## Consequences

The core can be exercised without Chromium. Browser acceptance still uses Chromium so that incorrect CDP normalization cannot be hidden by a fake. The synthetic fixture reached through keyboard input provides an observable destination but says nothing about production-site usability.

Domain and ScreenReader cannot gain external package dependencies accidentally without breaking the build. More hosting, persistence, and evidence modules should be introduced only when they own implemented behavior.

The next increments must establish unsupported frames/widgets, durable observation/event contracts, and the remaining feasibility gates before expanding to live websites. See the development plan for hosted security, evidence, and takeover acceptance.

## Technical references

- [Playwright CDP sessions](https://playwright.dev/dotnet/docs/api/class-cdpsession)
- [Chromium accessibility protocol](https://chromedevtools.github.io/devtools-protocol/tot/Accessibility/)
