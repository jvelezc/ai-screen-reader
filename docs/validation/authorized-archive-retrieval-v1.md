# Authorized archive retrieval validation

October 26, 2026. Implementation: [0056](../decisions/0056-authorized-archive-retrieval.md).

Local Windows: **2,199 discovered and passed, zero failed/skipped**, including
**87 new cases**. The actual full-suite TRX was parsed and accounted for every
case. Strict nonincremental Release build passed with zero warnings/errors;
full format verification passed. The pinned SDK10.0.402 was restored into a
private local tools directory after the current shell lost that SDK. The
repository pin and system-wide installation were not changed.

The focused checks exercise the real loopback ASP.NET API with synthetic provider
identities and actual archive bytes. `ArchiveRetrievalHttpTests` verifies exact
metadata paging, source/index identities, original byte reconstruction after
reconnect, 64 KiB/4 MiB boundaries and empty/EOF, auth-before-storage, foreign/revoked
denials, membership loss during storage, missing/corrupt/unavailable objects and
strict request errors. The previous `PortalArchiveTests` for observation storage
remain separate and unchanged.

`HostedArchiveTests` directly checks the private MCP client's original bearer,
fixed scope, returned identity/cursor/hash/length validation, bounded bytes,
malformed responses, explicit denial/error propagation, cancellation joined
before disposal and no retry/cache fallback. A fresh client per page reconstructs
the exact original over the actual HTTP API.

`HostedArchiveChromiumTests.StdioReconnectRetrievesActualChromiumOriginalAndDeniesLostAuthority`
collects a real Chromium observation, archives those exact original bytes, starts
the actual MCP executable, disconnects/reconnects, retrieves and independently
hashes the complete original, then proves authority loss prevents storage reads.
This is a synthetic API identity, not an original-user JWT/RLS/S3 acceptance claim.

`PortalOpenApiTests.ArchivePayloadsAndOptionalOffsetMatchPublishedContract`
checks actual manifest/file JSON against the published shapes, authenticated
route parameters and optional default offset. The contract version is 1.4.0.

Required final commands use the pinned SDK:

```powershell
dotnet build HeadlessScreenReader.slnx -c Release --no-incremental
dotnet test HeadlessScreenReader.slnx -c Release
dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore
```

Windows local results, Windows/Linux CI, exact VPS deployment and real-user
acceptance are reported separately. Source implementation does not establish
remote acceptance. Next: bounded Edge adaptation and notebook retrieval using
an original application user, preserved import identity and unchanged full hashes.
Hosted recording/admission binding, actual revoked/expired provider sessions,
human/NVDA and custody review remain distinct gates.
