# Recorded synthetic traversal evidence package

The recorded-shop runner now exports a portable ZIP alongside its append-only batch directory. The package includes each route's original video and source frames, JSON receipt, observed node/action graph, SVG, readable report and passive HTML viewer. It also retains the exact embedded recording-safe fixture as text, collector assembly hash/version, runtime/platform, UTC package creation time, route outcomes and an exhaustive SHA-256 file inventory.

[Local validation](../validation/recorded-evidence-package-v1.md): 2,047/2,047 Windows tests, exact real Chromium route media and independent ZIP/file verification. Remote CI and hosted recording remain separate gates.

## Use and verify

Run from this checkout:

```powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --traversal-recording
```

The final JSON prints `RecordedRoutesReady`, the directory and ZIP paths, `ManifestSha256` and `ArchiveSha256`. Keep these digests separately from the ZIP. Extract the entire ZIP before opening `route-1.html` through `route-4.html`; relative original-media links must stay intact. Select an observed node, action or event to seek its conservatively bounded video interval. The viewer does not operate a browser or replay a task.

Verify the extracted directory against the separately retained manifest digest:

```powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --verify-evidence <extracted-directory> <trusted-manifest-sha256>
```

Exit 0 with `IntegrityMatches=true` means the exact listed file set, lengths and hashes match that digest. Missing, changed or extra files, a different manifest, invalid paths, links, pending outputs and resource-limit failures return nonzero. The verifier is read-only; it never opens the HTML, runs fixture code, extracts an untrusted ZIP or repairs evidence. CI exercises ZIP extraction and this actual CLI command on Windows and Linux.

## Preserve the collection facts

The manifest calls this `ControlledSyntheticFixture`, operated by `HeadlessScreenReaderAuthoredCommands`. NVDA comparison and human validation are `NotPerformedForThisPackage`. Four routes are planned; only actually attempted routes are listed. A cancelled first launch can produce an intact failure package with one attempted route, no recording, Unknown reachability and no assessed barrier. Package integrity is independent of route readiness.

Before sealing, the exporter compares retained receipts, graphs, recording metadata and environment to the actual runner results, and rechecks declared original-media hashes. Every archived byte is hashed while copied. Failed publication retains its `.pending` output and never overwrites an earlier package. The complete archive gets its own digest. Bounded streaming allows up to 128 MiB per artifact, 512 MiB total, 4,096 artifacts and a 4 MiB manifest; these are local package limits, separate from hosted Storage configuration.

The existing four scenarios remain bounded authored experiments: checkout preview reached, disabled checkout observed, validation dialog instead of the intended destination, and unchanged feedback remaining Unknown. They do not cover all shopping use cases, search all alternative routes, measure human burden or establish findings about another website. Reader commands, actual keys, observations, goal verification and recording timing stay separate in the original receipts.

## What the digest does not establish

A digest supplied with replaced files can match the replacement. Preserve the original printed digest independently; this increment provides no external timestamp, collector signature or custody attestation. The assembly hash identifies collector bytes rather than asserting a Git revision. Package creation time comes from the host clock. No legal-admissibility, accessibility-conformance, real-person or NVDA-validation status is inferred from successful verification.

## Next acceptance steps

1. Add durable tenant-scoped traversal/media finalization and authorized package retrieval in core API/MCP before exposing it through Edge and the notebook. Preserve the existing capture-exclusion and credential gates.
2. Bind notebook graph selections to original evidence intervals; clearly distinguish live transient traces, saved observation trees and durable recorded traversals.
3. Add separately operated, configured human/NVDA comparison runs with their own recordings and provenance. Do not reuse AI-authored traces as proof of a person's experience.
4. Establish collection/custody procedures and expert review requirements before representing an export as suitable for a particular court proceeding. General real-site recording remains a separate gated capability.
