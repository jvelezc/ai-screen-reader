# Prototype container controls

Containerfile pins the Playwright 1.63.0 noble image by digest. The browser package version remains centrally pinned in Directory.Packages.props. Build-IsolatedReader publishes the Linux x64 Playwright driver even when building from Windows.

playwright-seccomp.json is the published [Playwright Docker seccomp profile](https://github.com/microsoft/playwright/blob/v1.63.0/utils/docker/seccomp_profile.json), from Microsoft Playwright under the [Apache 2.0 license](https://github.com/microsoft/playwright/blob/v1.63.0/LICENSE). It extends the normal syscall policy for sandbox user namespaces; it does not disable seccomp. The launcher applies it for Docker. Podman uses its default seccomp profile, verified locally with the actual sandboxed browser. The separate namespace firewall remains the external egress boundary in either runtime.

No image or container is deployed as a public service. Launch and validation procedures are in docs/prototype/claude.md.
