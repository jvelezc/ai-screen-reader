# 0043: Publish the bounded core HTTP contract

Status: implemented and locally validated. Windows: 1,560/1,560 tests passed,
including 34 new contract cases; warnings-as-errors build and format verification
passed. Current-source CI and deployed/provider acceptance are separate gates.

## Consumer boundary

The Lovable UI is an independent consumer. Its application API requests go through
Supabase Edge Functions in project `gpnijaxrgtwzwtrqsbcu`; those functions call the
core's authenticated API. AI clients use the core's MCP implementation. Core reader,
jobs and evidence logic remain here. No UI proxy or Edge Function implementation
is introduced by this increment.

## Contract

`GET /openapi/v1.json` now publishes OpenAPI 3.1 with contract version `1.0.0`.
Existing route names, operation IDs, request bodies and authorization remain
compatible. It describes the current fixed-fixture workspace/report service;
it does not promise the whole interactive reader over HTTP.

Response schemas are derived from the actual DTO serialization metadata using
[the SDK's JSON schema exporter](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/extract-schema).
The exporter operates on a copy of the serializer settings, so schema generation
does not mutate runtime JSON behavior. Output schemas require all serialized
properties, retain camelCase names and nullability, and disallow extra DTO members.
The document is cached after generation and contains no per-request credentials.
Future wire/profile changes require an explicit contract-version and compatibility
review; adding fields is not automatically compatible with strict consumers.

Protocol constraints also describe nonzero admission IDs, current fixture/profile,
bounded histories and nodes, explicit evidence states, and zero dispatched inputs
with an unverified goal. Conditional report schemas reject Available-without-an-
observation and other contradictions between execution and evidence availability.
Null required state remains unknown; it is distinct from observed `false`.

Every consumer success response and safe Problem Details response has a content
schema. Workspace bootstrap documents both 201 creation and 200 resumption.
Creation responses document `Location`; authentication failures document
`WWW-Authenticate: Bearer`. Listener readiness has an explicit DTO and retains
`appJwtValidated: false` and `publicMcpEnabled: false`.

The current auth contract remains a confirmed application user JWT verified with
the configured Supabase Auth project and tenant RLS. Edge-to-core authentication
must preserve that authority. A privileged service key or client actor field does
not provide delegated tenant authorization. Remote MCP audience validation remains
a separate core requirement.

See [the HTTP request examples](../protocol/core-api.http). They contain no real
credentials; use an authorized client's private environment when exercising them.

## Validation and limits

`PortalOpenApiTests` reads the actual Kestrel endpoint and compares real HTTP
payloads with the published schemas. It exercises create/resume/read/admission,
all current report evidence outcomes, nullable semantics, safe denial payloads
and creation/auth headers. Concrete invalid payloads prove that missing fields,
extra members, fake goal/input claims, malformed identifiers and contradictory
report states are rejected by the documented constraints.

The test evaluator is bounded to the emitted schema keywords; it is not a general
JSON Schema implementation or an OpenAPI client generator. The required full suite
also retains the real Chromium acceptance paths. Controlled Auth/project test
doubles do not establish hosted tenant acceptance or real provider credentials.

No new package, database/auth configuration, target policy, public listener,
deployment or browser credential entry is added. The live private host remains a
separate accepted release until an explicitly accepted artifact is promoted.

## Next

Map the existing MCP reader capabilities against the HTTP surface and implement
the next shared, authenticated core capability rather than adding frontend work.
Complete the gateway authentication/delegation design and approved hosted network
path before implementing a live Supabase adapter. A private localhost SSH forward
is not reachable from a hosted Edge Function. Official plugin publication remains
separate from core MCP readiness.
