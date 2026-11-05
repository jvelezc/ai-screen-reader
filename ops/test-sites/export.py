"""Export the fixed lab v1 catalogue. This creates data only; it never deploys services."""

import argparse
import base64
import hashlib
import json
import re
from pathlib import Path


SCENARIOS = {
    "checkout-working": "MockCheckoutWorkingV1",
    "checkout-keyboard-blocked": "MockCheckoutKeyboardBlockedV1",
    "control-named": "MockNamedControlV1",
    "control-unnamed": "MockUnnamedControlV1",
}


def collect(repo: Path) -> dict[str, bytes]:
    source = repo / "ops/test-sites/v1"
    original = (source / "manifest.json").read_bytes()
    manifest = json.loads(original)
    if manifest["schema"] != "hsr-mock-sites-v1" or manifest["version"] != 1:
        raise ValueError("Unsupported catalogue version")
    scenarios = manifest["scenarios"]
    if len(scenarios) != len(SCENARIOS) or {s["id"] for s in scenarios} != set(SCENARIOS):
        raise ValueError("Catalogue scenario set differs from v1")
    assets = {"manifest.json": original, "index.html": (source / "index.html").read_bytes()}
    fixture_root = repo / "src/HeadlessScreenReader.Browser.Playwright/Fixtures"
    caddy = (repo / "ops/ingress/hsr-canary.caddy").read_text(encoding="utf-8")
    for scenario in scenarios:
        name = scenario["id"]
        fixture = SCENARIOS[name]
        if scenario["fixture"] != fixture or scenario["path"] != f"/lab/v1/{name}.html":
            raise ValueError("Fixture identity or public path differs from v1")
        data = (fixture_root / f"{fixture.lower()}.html").read_bytes()
        if hashlib.sha256(data).hexdigest() != scenario["contentSha256"]:
            raise ValueError(f"Fixture bytes changed: {name}")
        scripts = re.findall(rb"<script>(.*?)</script>", data, re.DOTALL)
        if len(scripts) != 1:
            raise ValueError("Expected one fixed inline script")
        script_hash = "sha256-" + base64.b64encode(hashlib.sha256(scripts[0]).digest()).decode("ascii")
        if f"'{script_hash}'" not in caddy:
            raise ValueError("Caddy must allow the exact fixture script hash")
        if scenario["path"] not in caddy:
            raise ValueError("Caddy must explicitly allow each fixture path")
        assets[f"{name}.html"] = data
    return assets


def export(repo: Path, destination: Path) -> dict[str, str]:
    assets = collect(repo)
    destination.mkdir(parents=True, exist_ok=False)
    for name, data in assets.items():
        with (destination / name).open("xb") as output:
            output.write(data)
    return {name: hashlib.sha256(data).hexdigest() for name, data in assets.items()}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    print(json.dumps(export(Path(__file__).resolve().parents[2], args.destination), indent=2))
