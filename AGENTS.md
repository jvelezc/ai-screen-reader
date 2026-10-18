# HeadlessScreenReader working rules

Work inside this checkout and preserve the existing architecture plan under outputs/.

## Git workflow

- This is a solo project. Commit directly to main; feature branches and pull requests are not required.
- Every push to main runs GitHub Actions CI on Windows and Linux. Fix a failed run before starting the next increment.
- Preserve published history; do not force-push or rewrite commits without explicit approval.

## Architecture

- Domain contains platform-independent observations, commands, outcomes, and contracts. It must not reference Playwright, ASP.NET Core, database/storage SDKs, or a DI container.
- ScreenReader depends only on Domain. Keep announcements and navigation semantics independent of browser implementation.
- Browser.Playwright implements the Domain contract and must not depend on ScreenReader.
- Compose implementations in the CLI or a later host. Do not introduce empty projects, a mediator, or a generic repository framework just to follow a template.
- Keep diagnostic feature inference separate from reader-visible semantics.
- Preserve uncertainty: attempted input, successful input, observed state, and goal verification are different facts.
- An engine error or policy denial is not an accessibility barrier. Human assistance must remain explicit when it is introduced.

## Validation

Use the repository-pinned SDK and central package versions. Build with warnings as errors. Verify project dependencies through Directory.Build.targets; the arch-check skill's Roslyn MCP server is currently unavailable.

Use the installed .NET skills when applicable. Keep test generation proportional. Verify reader behavior with deterministic tests and the real Chromium fixture; do not substitute fakes for the browser acceptance path.

Run dotnet test HeadlessScreenReader.slnx -c Release and dotnet format HeadlessScreenReader.slnx --verify-no-changes --no-restore after relevant changes. Install Chromium with the documented Playwright command if missing. Report the actual discovered/passed/failed test count.

Do not add arbitrary-site navigation until target policy and external egress isolation are implemented and tested. Do not add credential entry or recordings before the capture exclusion protocol is proven.

Report local Windows checks, Linux checks, CI, deployment, and real assistive-technology validation separately. The first base increment is not completion of the full foundation milestones.
