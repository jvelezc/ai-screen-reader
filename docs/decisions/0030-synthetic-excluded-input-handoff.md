# Synthetic excluded-input handoff

The local controller can now grant a fixed, explicitly simulated assistance role during an acknowledged capture gap. This experiment proves the combination of actual protected-field input, owned evidence exclusion, original-media exclusion, same-context resume and cumulative assistance provenance. It introduces no real human or credential-entry API.

## Run the bounded proof

~~~powershell
dotnet run --project src/HeadlessScreenReader.Cli -c Release -- --synthetic-handoff
pwsh -NoProfile -File scripts/Test-HeadedSession.ps1 -Runtime podman -Scenario synthetic-handoff
# Use -Runtime docker for Docker.
~~~

Open the printed report. The standalone demo must report SyntheticHandoffReady=true; the headed harness must additionally confirm private Xvfb, restricted actual kernel capabilities, browser/display exit and task-owned container/image removal. [Validation and exact cases](../validation/synthetic-handoff-v1.md) distinguish each platform gate.

## Authority and capture sequence

1. Automation focuses the fixed empty protected field through an actual Tab command. Its owned intent/result/completion are acknowledged.
2. Release closes automation admission, drains actual reader/native work and owned writes, and acknowledges the stopped AX and media collectors.
3. A short-lived SimulatedAssistance lease binds the current scan, generation and actual adapter exclusion epoch. It never resumes observations. Ordinary reader commands and native keys remain denied.
4. The only actions are EnterCanary and ClearCanary. The adapter generates the value internally; callers cannot provide text, credentials or keys. Actual enabled protected-field focus is checked without repairing it. Each native key rechecks current authority.
5. Typed, payload-free assistance metadata is acknowledged before dispatch. Requested calls, completed calls, uncertainty and safe error codes remain separate. Whole commands and actual writes are counted even after a caller cancels its wait. Malformed provider results fault closed.
6. Release or expiry closes native admission before draining actual input and completion writes. An uncleared canary vetoes automation resume while leaving the gap available for an explicitly granted clearing lease.
7. Only a verified clear, revoked/drained native channel and persisted fresh reader baseline permit a new automation generation. The browser process, context, target and document stay the same. Later command intents retain PriorSimulatedAssistance=true.

The native lease independently expires monotonically at the same bounded 1-second to 2-minute lifetime. Tokens never enter owned evidence. A failed drain or store acknowledgement remains faulted after a late completion; cancellation cannot grant replacement authority. Unsupported fixtures and stores cannot enable the channel. DEBUG/PWDEBUG diagnostic logging is rejected for this fixed proof.

## Media proof and limits

The new trusted fixture is separately opted in to two permitted video/JPEG segments. During exclusion, actual native input places a unique canary in the protected field and in a red visual surface. A memory-only screenshot and boolean DOM guard provide the positive control. The probe clears with native select-all/Backspace, checks the empty value, waits for the compositor, revokes input, then resumes capture with a fresh baseline. After closing Chromium it decodes every original and staging video/JPEG, hashes the original bytes and checks the whole owned evidence tree for the generated value. Safe-looking segments without a positive control cannot pass.

This is a fixed synthetic feasibility experiment on a trusted fixture. It does not prove general secret redaction, arbitrary-site credential entry, real human acceptance, remote authentication accessibility, independent whole-path success, hosted persistence fencing or external-consumer revocation. Default sessions still create no recording collector. Published earlier results cannot be revoked by a local capture gap.

## Next: private viewer authorization and read-only transport

This increment is now implemented as [scoped private semantic viewing](0031-private-read-only-viewer.md): process-provisioned identity, single-exchange bootstrap, exact owned scan/session binding, expiry and actual delivery revocation/drain. The [validation](../validation/private-viewer-v1.md) distinguishes its loopback-only proof from remote identity-provider, pixel-viewer and actual human acceptance. The current next step is private pixel-viewer feasibility. The existing offline evidence viewer remains available; public MCP hosting and plugins stay deferred.
