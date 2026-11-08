import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'ops' / 'worker'))
import health
from release_bundle import ReleaseError


class HealthTests(unittest.TestCase):
    def ready(self):
        return dict.fromkeys(health.COMPONENTS | health.DEPENDENCIES | health.LOCAL, 'Ready') | {
            'WebFrontend': 'Live', 'EdgeGateway': 'Live'}

    def test_healthy_and_external_outages_never_restart_worker(self):
        facts = self.ready()
        self.assertEqual('Healthy', health.decide(facts, 2, [], 10000))
        for name in ('WebFrontend', 'EdgeGateway', 'EvidenceStorage', 'DatabaseQueue', 'DiskCapacity', 'ContainerImage'):
            with self.subTest(component=name):
                self.assertEqual('Degraded', health.decide(facts | {name: 'Unavailable'}, 2, [], 10000))

    def test_restart_requires_two_failures_and_each_dependency_ready(self):
        facts = self.ready() | {'WorkerService': 'Unavailable'}
        self.assertEqual('ConfirmingFailure', health.decide(facts, 1, [], 10000))
        self.assertEqual('Recover', health.decide(facts, 2, [], 10000))
        for name in health.DEPENDENCIES:
            with self.subTest(component=name):
                self.assertEqual('DependencyUnavailable', health.decide(facts | {name: 'Unavailable'}, 2, [], 10000))
                self.assertEqual('DependencyUnavailable', health.decide({k: v for k, v in facts.items() if k != name}, 2, [], 10000))

    def test_recovery_cooldown_hourly_limit_and_clock_rollback(self):
        facts = self.ready() | {'ReaderBroker': 'Unavailable'}
        self.assertEqual('Cooldown', health.decide(facts, 2, [9101], 10000))
        self.assertEqual('Recover', health.decide(facts, 2, [9100], 10000))
        self.assertEqual('Cooldown', health.decide(facts, 2, [10001], 10000))
        self.assertEqual('AttemptLimit', health.decide(facts, 2, [7000, 8000, 9000], 10000))
        self.assertEqual('Recover', health.decide(facts, 2, [6400, 8000, 9000], 10000))

    def test_optional_storage_does_not_invent_hosted_storage_acceptance(self):
        facts = self.ready() | {'EvidenceStorage': 'NotConfigured', 'WorkerService': 'Unavailable'}
        self.assertEqual('Recover', health.decide(facts, 2, [], 10000))

    def test_probe_rejects_duplicate_unknown_missing_or_input_dispatch_receipts(self):
        good = {'Schema': 'hsr-worker-health-v1', 'Components': [
            {'Component': name, 'State': 'Ready'} for name in sorted(health.COMPONENTS)], 'InputsDispatched': 0}
        self.assertEqual(dict.fromkeys(health.COMPONENTS, 'Ready'), health.validate_health(good))
        cases = [good | {'InputsDispatched': False}, good | {'InputsDispatched': 1}, good | {'Extra': 0}]
        for field, value in [('Component', 'unknown'), ('Component', good['Components'][1]['Component']),
                             ('State', 'Listening'), ('State', 'NotConfigured')]:
            changed = copy.deepcopy(good)
            changed['Components'][0][field] = value
            cases.append(changed)
        cases.append(good | {'Components': good['Components'][:-1]})
        for case in cases:
            with self.subTest(receipt=case), self.assertRaises(ReleaseError):
                health.validate_health(case)

    def test_owned_recovery_orders_stop_cleanup_probe_start_and_http_proof(self):
        calls = []
        class Runtime:
            def stop(self): calls.append('stop')
            def recover(self, commit, scope): calls.append(('recover', commit, scope))
            def probe(self, commit): calls.append(('probe', commit))
            def start_ready(self): calls.append('start')
            def http_ready(self, commit): calls.append(('http', commit))
        runtime = Runtime()
        runtime.control = runtime
        self.assertEqual('Recovered', health.recover(runtime, 'a' * 40))
        self.assertEqual(['stop', ('recover', 'a' * 40, 'evidence'), ('probe', 'a' * 40), 'start', ('http', 'a' * 40)], calls)

    def test_uncertain_cleanup_never_probes_or_starts_another_worker(self):
        calls = []
        class Runtime:
            def stop(self): calls.append('stop')
            def recover(self, commit, scope): calls.append('recover'); raise RuntimeError('private-detail')
            def probe(self, commit): self.fail('unexpected')
            def start_ready(self): self.fail('unexpected')
        self.assertEqual('HeldClosed', health.recover(Runtime(), 'a' * 40))
        self.assertEqual(['stop', 'recover', 'stop'], calls)

    def test_state_is_durable_private_and_malformed_state_cannot_clear_cooldown(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'health-state.json'
            self.assertEqual({'Failures': 0, 'Attempts': []}, health.load_state(path))
            value = {'Failures': 2, 'Attempts': [10000]}
            health.save(path, value)
            self.assertEqual(0o600, path.stat().st_mode & 0o777)
            # Preserve actual file IO; substitute only the root owner metadata in unprivileged CI.
            from unittest.mock import patch
            import os
            facts = list(path.stat())
            facts[4] = 0
            with patch.object(Path, 'stat', return_value=os.stat_result(facts)):
                self.assertEqual(value, health.load_state(path))
            self.assertEqual(value, json.loads(path.read_text()))
            path.write_text('{"Failures":0,"Attempts":[NaN]}')
            with self.assertRaises(ReleaseError): health.load_state(path)

    def test_deployment_lock_skips_probes_and_recovery(self):
        import contextlib
        import fcntl
        import io
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            with (base / 'deployment.lock').open('a') as lock:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                output = io.StringIO()
                with patch('health.DeploymentRuntime') as runtime, contextlib.redirect_stdout(output):
                    health.monitor(base)
                runtime.assert_not_called()
                self.assertEqual({'Schema': 'hsr-health-v1', 'State': 'DeploymentBusy'}, json.loads(output.getvalue()))

    def test_monitor_charges_attempt_before_mutation_and_persists_safe_held_receipt(self):
        import contextlib
        import io
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            (base / 'deployments').mkdir()
            def recover(runtime, commit):
                saved = json.loads((base / 'deployments' / 'health-state.json').read_text())
                self.assertEqual({'Failures': 2, 'Attempts': [10000]}, saved)
                return 'HeldClosed'
            output = io.StringIO()
            with patch('health.current_commit', return_value='a' * 40), patch('health.DeploymentRuntime'), \
                 patch('health.load_state', return_value={'Failures': 1, 'Attempts': []}), \
                 patch('health.observe', return_value=self.ready() | {'WorkerService': 'Unavailable'}), \
                 patch('health.time.time', return_value=10000), patch('health.recover', side_effect=recover) as recovery, \
                 contextlib.redirect_stdout(output):
                health.monitor(base)
            recovery.assert_called_once()
            receipt = json.loads(output.getvalue())
            self.assertEqual('HeldClosed', receipt['State'])
            self.assertEqual(1, receipt['AttemptsLastHour'])
            self.assertEqual(0, receipt['JobInputsReplayed'])
            self.assertEqual(receipt, json.loads((base / 'deployments' / 'health-latest.json').read_text()))

    def test_isolated_entrypoint_imports_helpers_before_closed_ownership_failure(self):
        import subprocess
        root = Path(health.__file__).parent
        # Run a copy whose fixed BASE is absent; this must never inspect or mutate a real installation.
        with tempfile.TemporaryDirectory() as folder:
            script = (root / 'health.py').read_text().replace("BASE = Path('/srv/headless-screen-reader')",
                'BASE = Path(' + repr(str(Path(folder) / 'absent')) + ')')
            entry = Path(folder) / 'health-entry.py'
            script = script.replace('str(Path(__file__).resolve().parent)', repr(str(root)))
            entry.write_text(script)
            run = subprocess.run([sys.executable, '-I', '-B', str(entry)], capture_output=True, text=True, timeout=10)
            self.assertEqual(3, run.returncode)
            self.assertEqual({'Schema': 'hsr-health-v1', 'State': 'MonitorUnconfirmed'}, json.loads(run.stdout))
            self.assertEqual('', run.stderr)


if __name__ == '__main__':
    unittest.main()
