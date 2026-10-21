"""Root-owned unit entrypoint. Executes as the dedicated unprivileged worker."""
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from activation import current_commit
from host_runtime import HostRuntime, DOTNET
from release_bundle import require

BASE = Path("/srv/headless-screen-reader")


def main():
    runtime = HostRuntime(BASE)
    require(os.geteuid() == runtime.account.pw_uid, "WORKER_START_IDENTITY_INVALID")
    commit = current_commit(BASE)
    require(commit is not None, "WORKER_RELEASE_MISSING")
    connection = os.environ.get("HSR_WORKER_DATABASE")
    require(bool(connection), "WORKER_DATABASE_CONFIGURATION_MISSING")
    env = runtime.environment() | {"HSR_WORKER_IMAGE": runtime.manifest(commit)["image"],
                                   "HSR_WORKER_RUNTIME": "podman",
                                   "HSR_WORKER_EVIDENCE": str(BASE / "state" / "evidence"),
                                   "HSR_WORKER_DATABASE": connection}
    os.execve(DOTNET, [DOTNET, str(BASE / "releases" / commit / "worker" /
                                 "HeadlessScreenReader.Worker.dll"), "--poll"], env)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        print("WORKER_START_UNCONFIRMED", file=sys.stderr)
        sys.exit(3)
