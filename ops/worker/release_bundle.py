"""Bounded release extraction. No code executes until every byte is verified."""
import hashlib
import json
import os
import re
import tarfile
from pathlib import Path, PurePosixPath

SHA = re.compile(r"[a-f0-9]{40}\Z")
DIGEST = re.compile(r"[a-f0-9]{64}\Z")
IMAGE = re.compile(r"sha256:[a-f0-9]{64}\Z")
MAX_FILE = 4 * 1024**3
MAX_TOTAL = 6 * 1024**3
MAX_FILES = 4096


class ReleaseError(Exception):
    pass


def require(condition, code):
    if not condition:
        raise ReleaseError(code)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "RELEASE_JSON_DUPLICATE")
        result[key] = value
    return result


def safe_name(name):
    require(isinstance(name, str) and 0 < len(name) <= 240,
            "RELEASE_PATH_INVALID")
    require(re.fullmatch(r"[A-Za-z0-9_./-]+", name) is not None
            and not name.startswith("/")
            and all(part not in ("", ".", "..") for part in name.split("/")),
            "RELEASE_PATH_INVALID")
    require(name in ("manifest.json", "fixture.tar") or name.startswith("worker/"),
            "RELEASE_FILE_UNEXPECTED")
    return PurePosixPath(name)


def verify_manifest(root, commit, actual):
    require(SHA.fullmatch(commit) is not None, "RELEASE_COMMIT_INVALID")
    manifest_path = root / "manifest.json"
    require(manifest_path.is_file() and manifest_path.stat().st_size <= 1024**2,
            "RELEASE_MANIFEST_INVALID")
    try:
        data = json.loads(manifest_path.read_text(encoding="utf-8"),
                          object_pairs_hook=unique_object)
    except (ValueError, UnicodeError) as error:
        raise ReleaseError("RELEASE_MANIFEST_INVALID") from error
    require(isinstance(data, dict) and set(data) == {"schema", "commit", "image", "files"},
            "RELEASE_MANIFEST_INVALID")
    require(data["schema"] == "hsr-worker-release-v1" and data["commit"] == commit,
            "RELEASE_IDENTITY_MISMATCH")
    require(isinstance(data["image"], str) and IMAGE.fullmatch(data["image"]) is not None,
            "RELEASE_IMAGE_INVALID")
    files = data["files"]
    require(isinstance(files, dict) and len(files) > 1
            and set(files) == set(actual) - {"manifest.json"}, "RELEASE_FILES_MISMATCH")
    require({"fixture.tar", "worker/HeadlessScreenReader.Worker.dll",
             "worker/HeadlessScreenReader.Worker.runtimeconfig.json",
             "worker/HeadlessScreenReader.Worker.deps.json"} <= set(files),
            "RELEASE_RUNTIME_MISSING")
    for name, digest in files.items():
        safe_name(name)
        require(isinstance(digest, str) and DIGEST.fullmatch(digest) is not None
                and actual[name] == digest, "RELEASE_DIGEST_MISMATCH")
    return data


def extract_verified(stream, root, commit):
    """root is a freshly created, private operator staging directory."""
    require(SHA.fullmatch(commit) is not None, "RELEASE_COMMIT_INVALID")
    root = Path(root).resolve(strict=True)
    require(not any(root.iterdir()), "RELEASE_STAGE_NOT_EMPTY")
    actual = {}
    total = 0
    with tarfile.open(fileobj=stream, mode="r|*") as archive:
        for item in archive:
            name = item.name
            relative = safe_name(name)
            require(item.isreg() and not item.issparse(), "RELEASE_TYPE_INVALID")
            require(name not in actual and len(actual) < MAX_FILES,
                    "RELEASE_DUPLICATE_OR_COUNT")
            require(0 < item.size <= MAX_FILE and total + item.size <= MAX_TOTAL,
                    "RELEASE_SIZE_INVALID")
            require(name != "manifest.json" or item.size <= 1024**2,
                    "RELEASE_MANIFEST_INVALID")
            total += item.size
            destination = root.joinpath(*relative.parts)
            destination.parent.mkdir(parents=True, exist_ok=True)
            digest = hashlib.sha256()
            source = archive.extractfile(item)
            require(source is not None, "RELEASE_CONTENT_MISSING")
            remaining = item.size
            with destination.open("xb") as output:
                while remaining:
                    chunk = source.read(min(1024**2, remaining))
                    require(bool(chunk), "RELEASE_CONTENT_TRUNCATED")
                    output.write(chunk)
                    digest.update(chunk)
                    remaining -= len(chunk)
                output.flush()
                os.fsync(output.fileno())
            actual[name] = digest.hexdigest()
    return verify_manifest(root, commit, actual)


def verify_directory(root, commit):
    root = Path(root).resolve(strict=True)
    actual = {}
    total = 0
    for path in root.rglob("*"):
        require(not path.is_symlink(), "RELEASE_TYPE_INVALID")
        if path.is_dir():
            continue
        name = path.relative_to(root).as_posix()
        safe_name(name)
        require(path.is_file() and len(actual) < MAX_FILES, "RELEASE_TYPE_INVALID")
        size = path.stat().st_size
        require(0 < size <= MAX_FILE and total + size <= MAX_TOTAL, "RELEASE_SIZE_INVALID")
        total += size
        with path.open("rb") as source:
            actual[name] = hashlib.file_digest(source, "sha256").hexdigest()
    return verify_manifest(root, commit, actual)
