"""Install only the reviewed HSR Caddy import; reload is not TLS acceptance."""
import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile

SITE_NAME = "hsr-api.caddy"


class IngressError(RuntimeError):
    pass


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read_file(path):
    try:
        with os.fdopen(os.open(path, os.O_RDONLY | os.O_NOFOLLOW), "rb") as stream:
            info = os.fstat(stream.fileno())
            if not stat.S_ISREG(info.st_mode) or info.st_uid != os.geteuid() or info.st_mode & 0o022:
                raise IngressError("INGRESS_FILE_OWNER_DENIED")
            data = stream.read(262145)
            if len(data) > 262144:
                raise IngressError("INGRESS_FILE_TOO_LARGE")
            return data
    except OSError as error:
        raise IngressError("INGRESS_FILE_UNCONFIRMED") from error


def snapshot(root):
    root = Path(root)
    if root.is_symlink() or not root.is_dir() or (root / "sites").is_symlink() or not (root / "sites").is_dir():
        raise IngressError("INGRESS_DIRECTORY_DENIED")
    for directory in (root, root / "sites"):
        info = directory.stat()
        if info.st_uid != os.geteuid() or info.st_mode & 0o022:
            raise IngressError("INGRESS_DIRECTORY_DENIED")
    main = read_file(root / "Caddyfile")
    if f"import {root}/sites/*.caddy" not in [line.strip() for line in main.decode("utf-8").splitlines()]:
        raise IngressError("INGRESS_IMPORT_REQUIRED")
    return {"MainSha256": digest(main), "UnrelatedSites": {
        path.name: digest(read_file(path)) for path in sorted((root / "sites").glob("*.caddy")) if path.name != SITE_NAME}}


def require_snapshot(root, expected):
    actual = snapshot(root)
    if actual != {key: expected[key] for key in ("MainSha256", "UnrelatedSites")}:
        raise IngressError("INGRESS_BASELINE_CHANGED")
    return actual


def validate_caddy(path):
    result = subprocess.run(["/usr/bin/caddy", "validate", "--config", str(path), "--adapter", "caddyfile"],
                            capture_output=True, timeout=30, check=False)
    if result.returncode:
        raise IngressError("INGRESS_CADDY_VALIDATION_UNCONFIRMED")


def reload_caddy(root):
    result = subprocess.run(["/usr/bin/caddy", "reload", "--config", str(root / "Caddyfile"), "--adapter", "caddyfile"],
                            capture_output=True, timeout=30, check=False)
    if result.returncode:
        raise IngressError("INGRESS_CADDY_RELOAD_UNCONFIRMED")


def rollback(root, expected, expected_site_hash, reload=reload_caddy):
    root = Path(root)
    require_snapshot(root, expected)
    owned = root / "sites" / SITE_NAME
    if owned.is_symlink() or not owned.exists() or digest(read_file(owned)) != expected_site_hash:
        raise IngressError("INGRESS_ROLLBACK_OWNERSHIP_DENIED")
    owned.unlink()
    try:
        reload(root)
    except Exception as error:
        raise IngressError("INGRESS_ROLLBACK_UNCONFIRMED") from error
    require_snapshot(root, expected)
    return {"State": "RolledBack", "CaddyReloaded": True, "TlsAccepted": False}


def install(root, site, expected, expected_site_hash, validate=validate_caddy, reload=reload_caddy):
    root = Path(root)
    if len(site) not in range(1, 8193) or not re.fullmatch("[a-f0-9]{64}", expected_site_hash) or digest(site) != expected_site_hash:
        raise IngressError("INGRESS_REVIEWED_SITE_REQUIRED")
    require_snapshot(root, expected)
    owned = root / "sites" / SITE_NAME
    if owned.is_symlink():
        raise IngressError("INGRESS_SITE_OWNERSHIP_DENIED")
    if owned.exists():
        if read_file(owned) != site:
            raise IngressError("INGRESS_SITE_OWNERSHIP_DENIED")
        return {"State": "AlreadyInstalled", "SiteSha256": expected_site_hash, "CaddyReloaded": False, "TlsAccepted": False}
    descriptor, name = tempfile.mkstemp(prefix=".hsr-review-", dir=root)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(read_file(root / "Caddyfile") + b"\n" + site)
        validate(Path(name))
        require_snapshot(root, expected)
    finally:
        Path(name).unlink(missing_ok=True)
    try:
        descriptor = os.open(owned, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o644)
    except OSError as error:
        raise IngressError("INGRESS_SITE_OWNERSHIP_DENIED") from error
    try:
        with os.fdopen(descriptor, "wb") as stream:
            os.fchmod(stream.fileno(), 0o644)
            stream.write(site)
            stream.flush()
            os.fsync(stream.fileno())
        require_snapshot(root, expected)
        reload(root)
    except Exception as error:
        # Refuse to delete or reload if another operator changed the reviewed inputs.
        try:
            rollback(root, expected, expected_site_hash, reload)
        except Exception as rollback_error:
            raise IngressError("INGRESS_ACTIVATION_AND_ROLLBACK_UNCONFIRMED") from rollback_error
        raise IngressError("INGRESS_ACTIVATION_FAILED_ROLLED_BACK") from error
    require_snapshot(root, expected)
    return {"State": "Installed", "SiteSha256": expected_site_hash, "UnrelatedConfigurationPreserved": True,
            "CaddyReloaded": True, "TlsAccepted": False}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("operation", choices=("install", "rollback"))
    parser.add_argument("--site", required=True)
    parser.add_argument("--baseline", required=True)
    parser.add_argument("--site-sha256", required=True)
    args = parser.parse_args()
    if sys.platform != "linux" or os.geteuid() != 0:
        raise IngressError("INGRESS_ROOT_LINUX_REQUIRED")
    root = Path("/etc/caddy")
    with os.fdopen(os.open(root / ".hsr-api.lock", os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600), "wb") as lock:
        info = os.fstat(lock.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1 or info.st_mode & 0o077:
            raise IngressError("INGRESS_LOCK_OWNER_DENIED")
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise IngressError("INGRESS_OPERATOR_BUSY") from error
        expected = json.loads(read_file(Path(args.baseline)))
        if args.operation == "install":
            receipt = install(root, read_file(Path(args.site)), expected, args.site_sha256)
        else:
            receipt = rollback(root, expected, args.site_sha256)
        print(json.dumps({"Schema": "hsr-core-api-ingress-v1", **receipt}))


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(str(error) if isinstance(error, IngressError) else "INGRESS_OPERATION_UNCONFIRMED", file=sys.stderr)
        sys.exit(1)
