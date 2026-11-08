"""Root-only component monitoring and bounded, ownership-confirmed recovery. No job replay."""
import datetime
import fcntl
import http.client
import json
import os
from pathlib import Path
import shutil
import sys
import time
import uuid
sys.path.insert(0, str(Path(__file__).resolve().parent))
from activation import current_commit
from control_runtime import DeploymentRuntime
from deploy import MARKER
from host_runtime import run_checked
from release_bundle import require, unique_object

BASE = Path('/srv/headless-screen-reader')
COMPONENTS = {'DatabaseQueue', 'ReaderBroker', 'PublicProcessor', 'EvidenceDisk', 'EvidenceStorage'}
DEPENDENCIES = {'DatabaseQueue', 'EvidenceDisk', 'EvidenceStorage', 'DiskCapacity', 'ContainerImage'}
LOCAL = {'WorkerService', 'ControlService', 'CoreApi', 'ReaderBroker', 'PublicProcessor'}
COOLDOWN = 900
WINDOW = 3600
MAX_ATTEMPTS = 3
SERVICE = ('[Unit]\nDescription=Privensic component health and owned recovery\nAfter=network-online.target\n'
           '[Service]\nType=oneshot\nUser=root\nUMask=0077\nLimitCORE=0\nTimeoutStartSec=1200\n'
           'ExecStart=/usr/bin/python3 -I -B /usr/local/libexec/headless-screen-reader/health.py\n')
TIMER = ('[Unit]\nDescription=Privensic component health schedule\n[Timer]\nOnBootSec=90\n'
         'OnUnitInactiveSec=60\nAccuracySec=5\nUnit=hsr-health.service\n[Install]\nWantedBy=timers.target\n')


def validate_health(value):
    require(isinstance(value, dict) and set(value) == {'Schema', 'Components', 'InputsDispatched'}
            and value['Schema'] == 'hsr-worker-health-v1'
            and type(value['InputsDispatched']) is int and value['InputsDispatched'] == 0
            and isinstance(value['Components'], list) and len(value['Components']) == len(COMPONENTS),
            'HEALTH_RECEIPT_INVALID')
    facts = {}
    for check in value['Components']:
        require(isinstance(check, dict) and set(check) == {'Component', 'State'}
                and check['Component'] in COMPONENTS and check['Component'] not in facts
                and check['State'] in ('Ready', 'Unavailable', 'NotConfigured')
                and (check['State'] != 'NotConfigured' or check['Component'] == 'EvidenceStorage'),
                'HEALTH_RECEIPT_INVALID')
        facts[check['Component']] = check['State']
    return facts


def decide(facts, failures, attempts, now):
    """Dependency outages never trigger restart storms; external components are observation-only."""
    if not any(facts.get(name) != 'Ready' for name in LOCAL):
        return 'Healthy' if all(value in ('Ready', 'NotConfigured', 'Live') for value in facts.values()) else 'Degraded'
    if not all(facts.get(name) in ('Ready', 'NotConfigured') for name in DEPENDENCIES):
        return 'DependencyUnavailable'
    if failures < 2:
        return 'ConfirmingFailure'
    recent = [stamp for stamp in attempts if now - stamp < WINDOW]
    if any(stamp > now for stamp in attempts) or recent and now - max(recent) < COOLDOWN:
        return 'Cooldown'
    if len(recent) >= MAX_ATTEMPTS:
        return 'AttemptLimit'
    return 'Recover'


def recover(runtime, commit):
    try:
        runtime.stop()  # Admission closes before worker quiescence.
        runtime.recover(commit, 'evidence')  # Only journal-bound resources; unknown cleanup holds closed.
        runtime.probe(commit)  # Separate input-free native canaries, never a queued task retry.
        runtime.start_ready()  # Database/queue/broker readiness before HTTP admission.
        runtime.control.http_ready(commit)
        return 'Recovered'
    except Exception:
        try:
            runtime.stop()
        except Exception:
            pass
        return 'HeldClosed'


def http_live(host, path, method, statuses, headers=None):
    connection = http.client.HTTPSConnection(host, timeout=5)
    try:
        connection.request(method, path, headers=headers or {})
        response = connection.getresponse()
        return 'Live' if response.status in statuses else 'Unavailable'
    except Exception:
        return 'Unavailable'
    finally:
        connection.close()


def observe(runtime, commit):
    facts = {}
    def check(name, action):
        try:
            action()
            facts[name] = 'Ready'
        except Exception:
            facts[name] = 'Unavailable'
    def unit(name):
        text = run_checked(['/usr/bin/systemctl', 'show', name, '--property=ActiveState', '--property=MainPID'])
        data = dict(line.split('=', 1) for line in text.splitlines())
        require(data.get('ActiveState') == 'active' and data.get('MainPID', '0').isdigit()
                and int(data['MainPID']) > 0, 'HEALTH_UNIT_UNAVAILABLE')
    check('WorkerService', lambda: unit('hsr-observation.service'))
    check('ControlService', lambda: unit('hsr-control.service'))
    check('CoreApi', lambda: runtime.control.http_ready(commit))
    check('DiskCapacity', lambda: require(shutil.disk_usage(BASE / 'state').free >= 256 * 1024**2, 'HEALTH_DISK_LOW'))
    image = runtime.manifest(commit)['image']
    def image_ready():
        actual = run_checked(['/usr/sbin/runuser', '-u', 'hsr-worker', '--', '/usr/bin/podman', 'image',
                              'inspect', '--format', '{{.Id}}', image], env=runtime.environment(), timeout=10)
        require('sha256:' + actual.removeprefix('sha256:') == image, 'HEALTH_IMAGE_UNAVAILABLE')
    check('ContainerImage', image_ready)
    unit_name = 'hsr-health-probe-' + uuid.uuid4().hex
    try:
        text = run_checked(['/usr/bin/systemd-run', '--quiet', '--wait', '--pipe', '--collect', '--unit=' + unit_name,
            '--property=User=hsr-worker', '--property=Group=hsr-worker', '--property=UMask=0077',
            '--property=NoNewPrivileges=yes', '--property=RuntimeMaxSec=60', '--property=LimitCORE=0',
            '--property=EnvironmentFile=' + str(BASE / 'config' / 'worker.env'),
            '--property=EnvironmentFile=-' + str(BASE / 'config' / 'evidence.env'),
            '/usr/bin/python3', '-I', '-B', '/usr/local/libexec/headless-screen-reader/health_start.py'], timeout=70)
        facts.update(validate_health(json.loads(text, object_pairs_hook=unique_object)))
    except Exception:
        facts.update({name: 'Unavailable' for name in COMPONENTS})
    # These are liveness only: no tenant session, admission or evidence conclusions are invented.
    facts['WebFrontend'] = http_live('screenreader.privensic.com', '/', 'HEAD', (200,))
    facts['EdgeGateway'] = http_live('gpnijaxrgtwzwtrqsbcu.supabase.co', '/functions/v1/hsr-core', 'OPTIONS', (200, 204))
    facts['Authentication'] = http_live('gpnijaxrgtwzwtrqsbcu.supabase.co', '/auth/v1/health', 'GET', (200,),
        {'apikey': runtime.control.public_key()})
    return facts


def load_state(path):
    if not path.exists():
        return {'Failures': 0, 'Attempts': []}
    require(not path.is_symlink() and path.stat().st_uid == 0 and path.stat().st_mode & 0o077 == 0
            and path.stat().st_size < 4096, 'HEALTH_STATE_INVALID')
    value = json.loads(path.read_text(), object_pairs_hook=unique_object)
    require(set(value) == {'Failures', 'Attempts'} and type(value['Failures']) is int and 0 <= value['Failures'] <= 100
            and isinstance(value['Attempts'], list) and len(value['Attempts']) <= MAX_ATTEMPTS
            and all(type(stamp) in (int, float) and 0 <= stamp <= 1e12 for stamp in value['Attempts']), 'HEALTH_STATE_INVALID')
    return value


def save(path, value):
    temporary = path.with_suffix('.new')
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, 'w') as output:
        json.dump(value, output, separators=(',', ':'), allow_nan=False)
        output.flush()
        os.fsync(output.fileno())
    os.replace(temporary, path)
    descriptor = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def main():
    require(os.geteuid() == 0 and BASE.is_dir() and not BASE.is_symlink()
            and BASE.stat().st_uid == 0 and BASE.stat().st_mode & 0o022 == 0
            and (BASE / '.managed').read_text().strip() == MARKER, 'HEALTH_OWNERSHIP_INVALID')
    os.umask(0o077)
    monitor(BASE)


def monitor(base):
    with (base / 'deployment.lock').open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            print(json.dumps({'Schema': 'hsr-health-v1', 'State': 'DeploymentBusy'}))
            return
        commit = current_commit(base)
        require(commit is not None, 'HEALTH_RELEASE_MISSING')
        runtime = DeploymentRuntime(base)
        path = base / 'deployments' / 'health-state.json'
        state = load_state(path)
        facts = observe(runtime, commit)
        state['Failures'] = min(100, state['Failures'] + 1) if any(facts.get(name) != 'Ready' for name in LOCAL) else 0
        now = time.time()
        state['Attempts'] = [stamp for stamp in state['Attempts'] if now - stamp < WINDOW]
        action = decide(facts, state['Failures'], state['Attempts'], now)
        if action == 'Recover':
            state['Attempts'].append(now)
            save(path, state)  # Charge the attempt durably before any lifecycle mutation.
            action = recover(runtime, commit)
            if action == 'Recovered':
                facts = observe(runtime, commit)
                if all(facts.get(name) == 'Ready' for name in LOCAL):
                    state['Failures'] = 0
                else:
                    action = 'RecoveryDegraded'
        save(path, state)
        receipt = {'Schema': 'hsr-health-v1', 'State': action, 'ReleaseCommit': commit, 'Components': facts,
            'ConsecutiveFailures': state['Failures'], 'AttemptsLastHour': len(state['Attempts']),
            'Utc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'JobInputsReplayed': 0}
        save(base / 'deployments' / 'health-latest.json', receipt)
        print(json.dumps(receipt, separators=(',', ':')))


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('{"Schema":"hsr-health-v1","State":"MonitorUnconfirmed"}')
        sys.exit(3)
