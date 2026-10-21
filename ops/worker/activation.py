"""One owned release pointer; native recovery precedes activation or rollback."""
import os
from pathlib import Path
import uuid
from release_bundle import SHA, ReleaseError, require


def current_commit(base):
    path = Path(base) / "current"
    if not path.is_symlink():
        require(not path.exists(), "DEPLOY_CURRENT_INVALID")
        return None
    target = os.readlink(path)
    require(target.startswith("releases/") and SHA.fullmatch(target[9:]) is not None,
            "DEPLOY_CURRENT_INVALID")
    commit = target[9:]
    release = Path(base) / "releases" / commit
    require(release.is_dir() and not release.is_symlink(), "DEPLOY_CURRENT_INVALID")
    return commit


def replace_current(base, commit):
    require(SHA.fullmatch(commit) is not None, "RELEASE_COMMIT_INVALID")
    base = Path(base)
    release = base / "releases" / commit
    require(release.is_dir() and not release.is_symlink(), "DEPLOY_RELEASE_INVALID")
    pending = base / (".current-" + uuid.uuid4().hex)
    try:
        pending.symlink_to("releases/" + commit, target_is_directory=True)
        os.replace(pending, base / "current")
        if os.name == "posix":
            descriptor = os.open(base, os.O_RDONLY | os.O_DIRECTORY)
            try:
                os.fsync(descriptor)
            finally:
                os.close(descriptor)
    finally:
        if pending.is_symlink():
            pending.unlink()


def activate(base, commit, runtime, record):
    """Runtime operations raise on any unknown outcome; never infer safe stop."""
    previous = current_commit(base)
    record("Stopping")
    runtime.stop()
    if previous is not None:
        record("RecoveringPrevious")
        runtime.recover(previous, "evidence")
    switched = False
    try:
        record("CheckingRuntime")
        runtime.probe(commit)
        switched = True
        replace_current(base, commit)
        record("Starting")
        runtime.start_ready()
        record("Ready")
        return {"State": "Ready", "Commit": commit, "Previous": previous}
    except Exception:
        # A timeout, service failure or canary failure never authorizes blind restart.
        runtime.stop()
        if switched:
            runtime.recover(commit, "evidence")
        runtime.recover(commit, "readiness")
        record("FailedDeploymentDrained")
        if previous is None:
            current = current_commit(base)
            require(current is None or current == commit, "DEPLOY_CURRENT_CHANGED")
            if current == commit:
                (Path(base) / "current").unlink()
                if os.name == "posix":
                    descriptor = os.open(base, os.O_RDONLY | os.O_DIRECTORY)
                    try:
                        os.fsync(descriptor)
                    finally:
                        os.close(descriptor)
            record("Stopped")
            raise ReleaseError("DEPLOY_FIRST_RELEASE_FAILED") from None
        runtime.probe(previous)
        replace_current(base, previous)
        runtime.start_ready()
        record("RolledBack")
        return {"State": "RolledBack", "Commit": commit, "Previous": previous}
