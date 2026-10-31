"""Root-only maintenance of one original recording; never a deploy-key command."""
import fcntl
import datetime
import json
import os
from pathlib import Path
import re
import stat
import sys
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parent))
from activation import current_commit
from control_runtime import DeploymentRuntime
from deploy import MARKER
from host_runtime import DOTNET, run_checked, validate_evidence_configuration
from release_bundle import ReleaseError, require, unique_object, verify_directory

BASE = Path('/srv/headless-screen-reader')


class MaintenanceReceipt:
    def __init__(self, base, commit, scope):
        self.folder, self.commit, self.scope = base / 'deployments', commit, scope
        self.run, self.sequence = uuid.uuid4().hex, 0

    def record(self, state):
        self.sequence += 1
        organization, job, generation = self.scope
        value = {'Schema': 'hsr-publication-maintenance-event-v1', 'ReleaseCommit': self.commit,
                 'Run': self.run, 'Sequence': self.sequence, 'State': state,
                 'Organization': organization, 'Job': job, 'Generation': generation,
                 'Utc': datetime.datetime.now(datetime.timezone.utc).isoformat()}
        path = self.folder / f'publication-{self.run}-{self.sequence:03}.json'
        with path.open('x', encoding='utf-8') as output:
            json.dump(value, output, separators=(',', ':'))
            output.flush()
            os.fsync(output.fileno())


def target(arguments):
    require(len(arguments) == 3, 'PUBLICATION_TARGET_INVALID')
    try:
        organization, job = (uuid.UUID(value) for value in arguments[:2])
        require(all(value.int != 0 and str(value) == raw for value, raw in zip((organization, job), arguments)),
                'PUBLICATION_TARGET_INVALID')
        require(arguments[2] in ('1', '2', '3'), 'PUBLICATION_TARGET_INVALID')
    except (ValueError, TypeError, AttributeError):
        raise ReleaseError('PUBLICATION_TARGET_INVALID') from None
    return str(organization), str(job), int(arguments[2])


def validate_result(value, organization, job, generation):
    require(isinstance(value, dict) and set(value) == {'Schema', 'State', 'Job', 'Producer', 'BundleSha256',
            'Reference', 'ErrorCode', 'InputsDispatchedByRecovery', 'CanReplay', 'IsCompletionReceipt', 'GoalVerification'}
            and value.get('Schema') == 'hsr-recorded-publication-recovery-v1'
            and value.get('State') in ('Published', 'AlreadyCompleted', 'Failed', 'Unconfirmed', 'Rejected', 'Busy')
            and value.get('InputsDispatchedByRecovery') == 0 and type(value.get('InputsDispatchedByRecovery')) is int
            and value.get('CanReplay') is False and value.get('GoalVerification') == 'NotAssessed'
            and isinstance(value.get('Job'), dict), 'PUBLICATION_RECEIPT_UNCONFIRMED')
    scope = value['Job']
    require(set(scope) == {'OrganizationId', 'ScanId', 'JobId', 'Generation', 'RecordObservation', 'TraversalRoute', 'Reference'}
            and scope.get('OrganizationId') == organization and scope.get('JobId') == job
            and type(scope.get('Generation')) is int and scope['Generation'] == generation,
            'PUBLICATION_RECEIPT_SCOPE_UNCONFIRMED')
    require(str(uuid.UUID(scope['ScanId'])) == scope['ScanId'] and uuid.UUID(scope['ScanId']).int != 0
            and str(uuid.UUID(value['Producer'])) == value['Producer'] and uuid.UUID(value['Producer']).int != 0
            and isinstance(value['BundleSha256'], str) and re.fullmatch('[a-f0-9]{64}', value['BundleSha256'])
            and scope['RecordObservation'] is True and type(scope['TraversalRoute']) is int
            and 0 <= scope['TraversalRoute'] <= 4, 'PUBLICATION_RECEIPT_UNCONFIRMED')
    reference = f"{uuid.UUID(organization).hex}/{uuid.UUID(scope['ScanId']).hex}/{uuid.UUID(job).hex}/{generation}/observation.json"
    require(scope['Reference'] == reference and value['Reference'] in (None, reference)
            and (value['State'] != 'Published' or value['Reference'] == reference and value['ErrorCode'] is None)
            and value['IsCompletionReceipt'] is (value['State'] == 'Published')
            and (value['ErrorCode'] is None or isinstance(value['ErrorCode'], str)
                 and re.fullmatch(r'WORKER_PUBLICATION_[A-Z0-9_]{1,100}', value['ErrorCode'])),
            'PUBLICATION_RECEIPT_UNCONFIRMED')
    return value


def settled(unit):
    text = run_checked(['/usr/bin/systemctl', 'show', unit, '--property=ActiveState',
                        '--property=MainPID', '--property=LoadState'])
    facts = dict(line.split('=', 1) for line in text.splitlines())
    return facts.get('LoadState') in ('loaded', 'not-found') and facts.get('ActiveState') in ('inactive', 'failed') and facts.get('MainPID') == '0'


def publish(base, commit, organization, job, generation):
    unit = 'hsr-publication-' + uuid.uuid4().hex
    arguments = ['/usr/bin/systemd-run', '--quiet', '--wait', '--pipe', '--unit=' + unit,
                 '--property=User=hsr-worker', '--property=Group=hsr-worker',
                 '--property=EnvironmentFile=' + str(base / 'config' / 'worker.env'),
                 '--property=EnvironmentFile=' + str(base / 'config' / 'evidence.env'),
                 '--property=WorkingDirectory=' + str(base / 'home'), '--property=NoNewPrivileges=yes',
                 '--property=UMask=0077', '--property=RuntimeMaxSec=660', '--property=SendSIGKILL=no',
                 '--setenv=HSR_WORKER_EVIDENCE=' + str(base / 'state' / 'evidence'),
                 DOTNET, str(base / 'releases' / commit / 'worker' / 'HeadlessScreenReader.Worker.dll'),
                 '--recover-recording', organization, job, str(generation)]
    value = None
    try:
        text = run_checked(arguments, timeout=720, accepted_codes=(0, 3))
        value = validate_result(json.loads(text, object_pairs_hook=unique_object), organization, job, generation)
    except Exception:
        pass  # No child stderr, credentials or malformed receipt enters the operator result.
    # A stopped waiter does not establish that the independently managed unit stopped.
    require(settled(unit), 'PUBLICATION_PROCESS_UNCONFIRMED')
    # Inactive successful units may already have been unloaded; no cleanup can revive them.
    return value


def recover(base, commit, scope, runtime, publication, record):
    organization, job, generation = scope
    record('Stopping')
    runtime.stop()
    record('RecoveringNative')
    runtime.recover(commit, 'evidence')
    record('Publishing')
    # publication returns only after actual process settlement; exceptions leave services stopped.
    value = publication(base, commit, organization, job, generation)
    if value is not None:
        validate_result(value, organization, job, generation)
    record(value['State'] if value is not None else 'PublicationUnconfirmed')
    runtime.recover(commit, 'evidence')
    record('Starting')
    runtime.start_ready()
    record('Ready')
    return {'Schema': 'hsr-publication-maintenance-v1', 'ReleaseCommit': commit,
            'PublicationState': value['State'] if value is not None else 'Unconfirmed',
            'ServicesReady': True, 'Publication': value, 'GoalVerification': 'NotAssessed'}


def main():
    require(os.geteuid() == 0, 'PUBLICATION_OPERATOR_IDENTITY_INVALID')
    scope = target(sys.argv[1:])
    os.umask(0o077)
    facts = BASE.lstat()
    require(stat.S_ISDIR(facts.st_mode) and facts.st_uid == 0 and facts.st_mode & 0o022 == 0
            and (BASE / '.managed').read_text().strip() == MARKER, 'PUBLICATION_OPERATOR_ROOT_INVALID')
    lock_path = BASE / 'deployment.lock'
    descriptor = os.open(lock_path, os.O_WRONLY | os.O_APPEND | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, 'a') as lock:
        facts = os.fstat(lock.fileno())
        require(stat.S_ISREG(facts.st_mode) and facts.st_uid == 0 and facts.st_mode & 0o022 == 0,
                'PUBLICATION_OPERATOR_LOCK_INVALID')
        fcntl.flock(lock.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        commit = current_commit(BASE)
        require(commit is not None, 'PUBLICATION_RELEASE_MISSING')
        verify_directory(BASE / 'releases' / commit, commit)
        validate_evidence_configuration(BASE)
        receipt = MaintenanceReceipt(BASE, commit, scope)
        runtime = DeploymentRuntime(BASE)
        try:
            value = recover(BASE, commit, scope, runtime, publish, receipt.record)
        except Exception:
            receipt.record('Unconfirmed')
            raise
        print(json.dumps(value, separators=(',', ':')))
        return 0 if value['PublicationState'] in ('Published', 'AlreadyCompleted') else 3


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception:
        print('PUBLICATION_MAINTENANCE_UNCONFIRMED', file=sys.stderr)
        sys.exit(3)
