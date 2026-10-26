# Owned public reader acceptance, v1

The project owns two static HTTPS pages on `screenreader.privensic.com`, whose
A record resolves to the existing VPS, `89.117.17.81`. The intermittent
`TARGET_HTTP_ERROR` from example.com in CI40248172759 prompted this replacement.
The new canary removes dependence on third-party markup and availability; it
does not remove network, DNS, TLS, isolation or reader checks.

## Fixed task

| Page | Reader contract |
| --- | --- |
| [Source](https://screenreader.privensic.com/ci/v1/source) | Title `Reader CI source v1`; deliberately no heading; paragraph starts `Controlled reader acceptance page.`; one focusable link to the exact destination |
| [Destination](https://screenreader.privensic.com/ci/v1/destination) | Title and heading `Reader CI destination v1` |

`PrototypeSmoke.RunAsync` drives the real stdio MCP server and sandboxed
Chromium through the egress fence. It requires private-target denial, title
verification, `NoMatch` for absent headings, paragraph reading, browse-only
cursor movement without focus changes, `FocusRequired`, exactly `Tab` then
`Enter`, a changed document, exact destination URL, verified heading and
matching cursor, session close and container release. Its receipt identifies
`CanaryVersion=hsr-public-reader-canary-v1`, source and destination. The default
operator allowlist and CI allowlist contain only `screenreader.privensic.com`.
Explicit operator-supplied exact hosts remain supported; the AI cannot expand
the allowlist. No retry, fallback, timeout increase or policy bypass was added.

## Separate host provisioning

[`hsr-canary.caddy`](../../ops/ingress/hsr-canary.caddy) serves only those two
inline HTML documents. It has no scripts, forms, external assets, proxy or file
server. GET/HEAD return the selected page, other paths return404 and other
methods return405. Responses have no-store, nosniff, no-referrer and a restrictive
CSP. The missing source heading is intentional negative test data.

Use root SSH to copy the reviewed site and `ops/ingress/api_site.py` to a private
staging directory. Capture `snapshot(Path('/etc/caddy'), site_name=CANARY_SITE_NAME)`
into root-owned mode0600 JSON; review main and every unrelated import fingerprint.
The baseline includes the existing `hsr-api.caddy`. Then run:

```sh
python3 api_site.py install --site hsr-canary.caddy --baseline reviewed-baseline.json --site-sha256 <reviewed-sha256> --site-name hsr-canary.caddy
```

The installer validates with the installed Caddy, rechecks the baseline, creates
only the selected file and reloads. The same operation with `rollback` removes
only matching owned bytes and requires the unchanged baseline. Unknown filenames,
symlinks and conflicting/concurrent changes fail closed. Both site names share
the existing exclusive operation lock. The restricted CI deployment account
does not gain permission to edit Caddy. Ordinary backend deployment stays separate.

## Acceptance on October25, 2026

Installed Caddy2.11.4 accepted and reloaded the candidate. Its reviewed SHA256 is
`8b85ee7497de5aca3cef64c51fc6e6940f32c07e765ec6c8739e5f8a138d1d36`.
Independent Ubuntu HTTPS requests used normal certificate verification, with no
trust bypass. DNS resolved only89.117.17.81. The leaf certificate expires
January24, 2027; future renewal remains an operational gate.

Both GET bodies matched the checked-in inline HTML. Both HEADs returned200 and
no body; POST/PUT returned405 with `Allow: GET, HEAD`. Root, `/api/jobs`, `/mcp`,
`/private` and a source subpath returned404. All responses retained the declared
headers. Independent readback confirmed unchanged main/API/unrelated Caddy
fingerprints. The existing IP API still returned401 for anonymous `/api/session`,
200 for OpenAPI and404 for `/mcp`.

The complete Linux operator suite passed **152/152**, zero failed/skipped,
including the eight new actual-filesystem cases below. Injected reload callbacks
prove file ownership behavior; live HTTPS acceptance is a separate check.

Local Windows full Release regression passed **2,047/2,047**, zero failed/skipped;
the TRX was parsed and all discovered cases accounted for. The build uses
warnings as errors, and full formatting verification passed. The real Linux Podman
fence/proxy probe passed every isolation field and six default-drop policies.
The real stdio MCP task then passed every reader assertion above and confirmed
container release. These are local checks; remote Windows/Linux CI and exact
backend promotion retain separate receipts.

| Requirement | Evidence |
| --- | --- |
| Preserve API and main during selected install and rollback | `CanaryIngressTests.test_canary_install_and_rollback_preserve_existing_api_and_main` |
| Reject borrowed baseline and API changes before write | `test_api_baseline_cannot_be_borrowed_for_canary_install`, `test_api_change_during_validation_prevents_canary_creation` |
| Failed reload rolls back only owned canary; concurrent API change remains explicit | `test_failed_reload_rolls_back_selected_canary_only`, `test_api_change_during_failure_denies_rollback_and_preserves_change` |
| Conflicts, links and arbitrary paths cannot replace existing files | `test_different_existing_canary_is_not_overwritten`, `test_canary_symlink_cannot_replace_or_remove_api`, `test_unknown_and_traversal_site_names_are_denied_without_mutation` |

Public MCP/plugin publication, hosted tenant recording, general-site capture,
actual human/NVDA comparisons and legal admissibility are separate gates. This
hostname publishes only fixed test pages. The synthetic shop's original route
media and graph package remain a different, local acceptance artifact.
