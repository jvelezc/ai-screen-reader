"""Unprivileged, read-only health process; inherits only the service's private configuration."""
import os
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parent))
from activation import current_commit
from host_runtime import DOTNET, HostRuntime, evidence_environment
from release_bundle import require

BASE = Path('/srv/headless-screen-reader')


def main():
    runtime = HostRuntime(BASE)
    require(os.geteuid() == runtime.account.pw_uid, 'HEALTH_IDENTITY_INVALID')
    commit = current_commit(BASE)
    require(commit is not None, 'HEALTH_RELEASE_MISSING')
    env = runtime.environment() | evidence_environment(os.environ) | {
        'HSR_WORKER_IMAGE': runtime.manifest(commit)['image'],
        'HSR_WORKER_EVIDENCE': str(BASE / 'state' / 'evidence'),
        'HSR_WORKER_DATABASE': os.environ.get('HSR_WORKER_DATABASE', '')}
    os.execve(DOTNET, [DOTNET, str(BASE / 'releases' / commit / 'worker' /
                                 'HeadlessScreenReader.Worker.dll'), '--check-health'], env)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('HEALTH_PROCESS_UNCONFIRMED', file=sys.stderr)
        sys.exit(3)
