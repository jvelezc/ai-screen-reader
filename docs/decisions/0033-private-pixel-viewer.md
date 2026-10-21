# Private read-only pixel relay

The existing scoped viewer now has an optional PNG transport. This is an equivalent narrow relay for the fixed headed Chromium/Xvfb feasibility gate; no RFB/VNC server, CDP connection, input API or public listener is exposed. The semantic endpoint still returns the last persisted observation. Pixels are explicitly fresh fixture pixels, not semantic observations or goal verification.

## Admission and ownership

Only `LaunchPrivatePixelFixtureAsync` enables capture, and only for the bundled SyntheticHandoff fixture. Normal launches, including an ordinary launch of that fixture, deny pixels. The factory starts no recording collector. Encoded payloads are immutable, in memory, limited to 4 MiB and the 320 by 240 PNG envelope. Envelope validation does not certify decoding; the real proof independently decodes the actual delivered bytes.

The adapter joins the actual screenshot and surrounding document observations through CaptureAdmission. It checks expected epoch and document before and after capture, and rejects a remaining synthetic canary. It returns the actual native task even after caller cancellation. The scan owner serializes pixels with commands, requires an acknowledged baseline, joins native capture and actual publication to exclusion drain, and rejects late, stale or misbound results. Pixels create no evidence write.

The private broker shares its existing hashed exact scan/session/subject authority, once-only bootstrap, monotonic expiry, per-connection busy fence, four-read total concurrency bound and actual source/delivery revocation drain. Semantic and pixel reads contend for the same slot. A cancelled waiter cannot acknowledge actual work. Already delivered bytes cannot be recalled; revocation acknowledges denial of new dispatch and completion of admitted work.

## HTTP contract

The loopback server maps `GET /viewer/{scanId}/{sessionId}/pixels?epoch=...` only when trusted composition provides the pixel service. It requires the same process identity and connection headers as semantic viewing. Host, Origin, Cookie, query credentials, malformed epochs and GET bodies remain denied. Responses are `image/png`, no-store, with epoch, baseline ordinal, document SHA-256, pixel kind and prior simulated-assistance headers. No exception text, bearer, raw document ID or pixel payload enters a report or log. The authenticated OpenAPI document adds the pixel path only when enabled.

Keyboard, pointer, clipboard, navigation, WebSocket and raw browser-control endpoints do not exist. They are rejected at the actual HTTP routing boundary before any source call. This header-client proof does not implement browser authentication, a remote identity provider or human control.

## Acceptance and use

Run the finite local proof:

```powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --private-pixels
```

Run the independent headed Linux/Xvfb proof:

```powershell
pwsh -NoProfile -File scripts/Test-HeadedSession.ps1 -Runtime podman -Scenario private-pixels
```

Use `docker` for the runtime where available. The headed harness retains process/context/target identity, actual kernel/display restrictions, original delivered-byte hashes and decoded color counts, server/browser closure and task-owned display/container/image cleanup. It saves no PNG payload. The fixed demo finishes and closes its service; it is not a long-running viewer for another AI.

Actual HTTP-delivered PNGs decode to green before assistance and blue after cleared assistance. A separate, explicitly authorized memory-only diagnostic screenshot proves that red pixels exist during the excluded synthetic gap. That positive control is never delivered or saved. Valid viewer authority is denied during the gap, for the old epoch and after revocation. Actual focus/value/input counters are unchanged across each viewing interval. The permitted initial Tab and excluded synthetic input have their own intent/completion receipts; they are not attributed to viewing. Assistance remains simulated and cumulative, and pixels never verify a whole-path goal.

See the [validation matrix](../validation/private-pixels-v1.md). Local, Linux, CI and human/AT gates remain separately reported.

## Next: accessible private viewing client

Build a local, accessible client for this bounded read-only relay. Keep viewer credentials out of URLs, browser storage, logs and reports. Prove client origin/authority binding, visible expired/revoked/excluded states, keyboard controls, actual pixels and complete client/server/browser cleanup. Keep actual target input and clipboard paths unavailable. A human must then exercise the viewing client before human-control acceptance; remote identity, real credentials, hosted storage and public MCP/plugin publication remain separate gates.
