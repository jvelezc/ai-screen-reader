"""Package the immutable image and the exact worker binaries exercised in CI."""
import argparse
import hashlib
import json
from pathlib import Path
import tarfile
from release_bundle import IMAGE, SHA, require, safe_name, verify_directory
from image_archive import verify_image_archive


def package(stage, output, commit, image, tested_worker, tested_control=None):
    stage, output, tested_worker = Path(stage), Path(output), Path(tested_worker)
    tested_control = Path(tested_control) if tested_control is not None else None
    require(SHA.fullmatch(commit) is not None and IMAGE.fullmatch(image) is not None,
            "RELEASE_IDENTITY_MISMATCH")
    require(not (stage / "manifest.json").exists() and not output.exists(), "RELEASE_OUTPUT_EXISTS")
    require(not output.resolve().is_relative_to(stage.resolve()), "RELEASE_OUTPUT_INSIDE_STAGE")
    verify_image_archive(stage / "fixture.tar", image)
    files = {}
    for path in stage.rglob("*"):
        require(not path.is_symlink(), "RELEASE_TYPE_INVALID")
        if not path.is_file():
            continue
        name = path.relative_to(stage).as_posix()
        safe_name(name)
        with path.open("rb") as source:
            files[name] = hashlib.file_digest(source, "sha256").hexdigest()
        if name.startswith("worker/"):
            original = tested_worker / name[7:]
            require(original.is_file(), "RELEASE_TESTED_BINARY_MISSING")
            with original.open("rb") as source:
                require(hashlib.file_digest(source, "sha256").hexdigest() == files[name],
                        "RELEASE_TESTED_BINARY_MISMATCH")
        elif name.startswith("control/"):
            require(tested_control is not None, "RELEASE_CONTROL_NOT_TESTED")
            original = tested_control / name[8:]
            require(original.is_file() and not original.is_symlink(), "RELEASE_TESTED_CONTROL_MISSING")
            with original.open("rb") as source:
                require(hashlib.file_digest(source, "sha256").hexdigest() == files[name],
                        "RELEASE_TESTED_CONTROL_MISMATCH")
    tested_files = {path.relative_to(tested_worker).as_posix()
                    for path in tested_worker.rglob("*") if path.is_file()}
    require(tested_files == {name[7:] for name in files if name.startswith("worker/")},
            "RELEASE_TESTED_CLOSURE_MISMATCH")
    if tested_control is not None:
        tested_files = {path.relative_to(tested_control).as_posix()
                        for path in tested_control.rglob("*") if path.is_file()}
        require(tested_files == {name[8:] for name in files if name.startswith("control/")},
                "RELEASE_TESTED_CONTROL_CLOSURE_MISMATCH")
    manifest = {"schema": "hsr-worker-control-release-v2" if tested_control is not None else "hsr-worker-release-v1",
                "commit": commit, "image": image, "files": files}
    with (stage / "manifest.json").open("x", encoding="utf-8") as destination:
        json.dump(manifest, destination, sort_keys=True, separators=(",", ":"))
    verify_directory(stage, commit)
    # Regular files only. Never include caller metadata, links, environment or the outer archive.
    with output.open("xb") as destination:
        with tarfile.open(fileobj=destination, mode="w:gz", compresslevel=1, format=tarfile.USTAR_FORMAT) as archive:
            for path in sorted(stage.rglob("*")):
                if path.is_file():
                    archive.add(path, arcname=path.relative_to(stage).as_posix(), recursive=False)
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--stage", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--tested-worker", required=True)
    parser.add_argument("--tested-control")
    args = parser.parse_args()
    package(args.stage, args.output, args.commit, args.image, args.tested_worker, args.tested_control)
