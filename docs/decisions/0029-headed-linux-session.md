# Headed Linux session feasibility

Run the actual fixture experiment from Windows or Linux with an available Linux container engine:

~~~powershell
pwsh -NoProfile -File scripts/Test-HeadedSession.ps1 -Runtime podman
# Use -Runtime docker for Docker.
~~~

The script uses the repository-pinned host SDK to publish the CLI with the Linux Playwright driver. It builds the existing digest-pinned Playwright image, creates a uniquely labeled task container with networking disabled, then runs headed Chromium on a private Xvfb display. No host directory is mounted. The trusted fixture container has a writable disposable layer so its evidence can be copied after exit; it is not the read-only hostile-site worker.

Xvfb is started once with private authority and TCP listening disabled. The proof checks its actual process, display, file permissions and kernel capability sets. CapEff, CapPrm, CapInh, CapBnd and CapAmb must each be zero or contain only SYS_CHROOT; effective user must be nonroot and no-new-privileges must be set. The container's inspected network mode, user, memory/PID limits, privilege mode and absence of host bind mounts are retained. These observations do not replace the independent public-reader egress/isolation acceptance.

The browser is launched headed once with sandboxing enabled. CDP reports actual browser process, context and target identity. Linux process arguments establish whether headless or no-sandbox switches are present; the raw arguments are never retained. The first controller sends Tab, release drains actual input/writes, a new generation resumes from a fresh persisted baseline, and Activate verifies the checkout-preview heading. Process/context/target/document identity must remain unchanged. Headless/headed launch mode is never switched mid-scan.

The proof then closes Chromium and confirms its process exited. The outer Xvfb runner waits for the owned display's process, lock and socket to disappear. The host copies evidence before removing the matching labeled container and its unique image. Copy or cleanup failures remain explicit; unconfirmed resources are retained by exact name rather than silently discarded. The run has a 120-second limit, with separate browser/transition bounds. Each batch has a new folder and support ID.

Open the returned folder. runtime.json records orchestration, observed constraints and cleanup; run.json and the nested receipt.json contain browser/controller proof. cleanup.json acknowledges the separate display shutdown. Failed batches retain safe stages and error codes. The named acceptance case is HeadedChromium_TwoControllerGenerationsPreserveProcessContextAndCleanOwnedDisplay. The Linux CI job runs the same experiment using Docker.

No VNC server, public viewer, human controller, real credential entry, recording or arbitrary target is enabled. The existing local controller has only an AI role. See [validation](../validation/headed-linux-session-v1.md).

## Next: synthetic excluded-input handoff

Before a private viewer can forward human keys, prove an explicit assistance channel while owned capture is excluded. Keep it limited to fixed synthetic fixture input. Grant a new fenced input generation only after actual browser/reader/collector/write exclusion acknowledges; revoke and drain that input before a fresh baseline can resume capture. Reject stale authority at the adapter boundary. Label the test channel as simulated assistance, never real human acceptance or independent reader success. Prove same-context continuity and absence of the fixed canary from owned files and opted-in original media. Keep general credential entry and private viewer forwarding disabled until this combined proof passes.
