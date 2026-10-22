"""Fixed portal entrypoint; no worker database credential enters this process."""
import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from activation import current_commit
from control_runtime import control_environment
from host_runtime import HostRuntime, DOTNET, evidence_environment
from release_bundle import require

BASE = Path("/srv/headless-screen-reader")


def main():
    runtime = HostRuntime(BASE)
    require(os.geteuid() == runtime.account.pw_uid, "CONTROL_START_IDENTITY_INVALID")
    commit = current_commit(BASE)
    require(commit is not None and runtime.manifest(commit)["schema"] == "hsr-worker-control-release-v2",
            "CONTROL_RELEASE_MISSING")
    env = control_environment(BASE, commit, runtime.environment(), os.environ.get("HSR_CONTROL_PUBLISHABLE_KEY"))
    env |= evidence_environment(os.environ)
    os.execve(DOTNET, [DOTNET, str(BASE / "releases" / commit / "control" /
                                 "HeadlessScreenReader.Cli.dll"), "--hosted-control"], env)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        print("CONTROL_START_UNCONFIRMED", file=sys.stderr)
        sys.exit(3)
