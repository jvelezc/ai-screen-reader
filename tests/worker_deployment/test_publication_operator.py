import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'ops' / 'worker'))
import recover_publication as operator
from release_bundle import ReleaseError

ORG = '88000000-0000-0000-0000-000000000001'
JOB = '88000000-0000-0000-0000-000000000002'
SCAN = '88000000-0000-0000-0000-000000000003'
PRODUCER = '88000000-0000-0000-0000-000000000004'
COMMIT = 'a' * 40
BASE = Path('/srv/headless-screen-reader')
SCOPE = (ORG, JOB, 1)


def receipt(state='Published'):
    reference = f'{ORG.replace("-", "")}/{SCAN.replace("-", "")}/{JOB.replace("-", "")}/1/observation.json'
    return {'Schema': 'hsr-recorded-publication-recovery-v1', 'State': state,
            'Job': {'OrganizationId': ORG, 'ScanId': SCAN, 'JobId': JOB, 'Generation': 1,
                    'RecordObservation': True, 'TraversalRoute': 1, 'Reference': reference},
            'Producer': PRODUCER, 'BundleSha256': 'b' * 64, 'Reference': reference,
            'ErrorCode': None, 'InputsDispatchedByRecovery': 0, 'CanReplay': False,
            'IsCompletionReceipt': state == 'Published', 'GoalVerification': 'NotAssessed'}


class Runtime:
    def __init__(self, fail=None):
        self.calls = []
        self.fail = fail

    def operation(self, name):
        self.calls.append(name)
        if name == self.fail:
            raise ReleaseError('SAFE_RUNTIME_UNCONFIRMED')

    def stop(self):
        self.operation('stop')

    def recover(self, commit, scope):
        assert commit == COMMIT and scope == 'evidence'
        self.operation('recover')

    def start_ready(self):
        self.operation('start_ready')


class PublicationOperatorTests(unittest.TestCase):
    def run_operation(self, state='Published', runtime=None, publication=None):
        runtime = runtime or Runtime()
        phases = []
        def publish(base, commit, organization, job, generation):
            self.assertEqual((BASE, COMMIT, *SCOPE), (base, commit, organization, job, generation))
            runtime.calls.append('publish')
            return receipt(state)
        result = operator.recover(BASE, COMMIT, SCOPE, runtime, publication or publish, phases.append)
        return result, runtime.calls, phases

    def test_actual_scope_is_canonical_before_any_service_operation(self):
        self.assertEqual(SCOPE, operator.target([ORG, JOB, '1']))
        for values in ([], [ORG, JOB, '0'], [ORG, JOB, '4'], [ORG, JOB, '01'],
                       ['not-a-guid', JOB, '1'], ['00000000-0000-0000-0000-000000000000', JOB, '1']):
            with self.subTest(values=values), self.assertRaises(ReleaseError):
                operator.target(values)

    def test_publication_waits_for_stop_cleanup_and_ordered_readiness(self):
        value, calls, phases = self.run_operation()
        self.assertEqual(['stop', 'recover', 'publish', 'recover', 'start_ready'], calls)
        self.assertEqual(['Stopping', 'RecoveringNative', 'Publishing', 'Published', 'Starting', 'Ready'], phases)
        self.assertEqual('Published', value['PublicationState'])
        self.assertIs(value['ServicesReady'], True)
        self.assertEqual('NotAssessed', value['GoalVerification'])

    def test_exact_completed_retry_preserves_a_historical_nonreceipt(self):
        value, calls, _ = self.run_operation('AlreadyCompleted')
        self.assertEqual('AlreadyCompleted', value['PublicationState'])
        self.assertIs(value['Publication']['IsCompletionReceipt'], False)
        self.assertEqual(['stop', 'recover', 'publish', 'recover', 'start_ready'], calls)

    def test_settled_failure_can_restore_services_without_claiming_publication(self):
        for state in ('Failed', 'Unconfirmed', 'Rejected', 'Busy'):
            with self.subTest(state=state):
                value, calls, phases = self.run_operation(state)
                self.assertEqual(state, value['PublicationState'])
                self.assertIs(value['Publication']['IsCompletionReceipt'], False)
                self.assertEqual('start_ready', calls[-1])
                self.assertEqual('Ready', phases[-1])

    def test_settled_unreadable_receipt_restores_only_service_readiness(self):
        value, calls, phases = self.run_operation(publication=lambda *args: None)
        self.assertEqual('Unconfirmed', value['PublicationState'])
        self.assertIsNone(value['Publication'])
        self.assertEqual(['stop', 'recover', 'recover', 'start_ready'], calls)
        self.assertIn('PublicationUnconfirmed', phases)

    def test_unknown_actual_publisher_cannot_restore_polling(self):
        runtime = Runtime()
        def unknown(*args):
            raise ReleaseError('PUBLICATION_PROCESS_UNCONFIRMED')
        with self.assertRaisesRegex(ReleaseError, '^PUBLICATION_PROCESS_UNCONFIRMED$'):
            self.run_operation(runtime=runtime, publication=unknown)
        self.assertEqual(['stop', 'recover'], runtime.calls)

    def test_unconfirmed_stop_or_native_recovery_cannot_launch_publication(self):
        for stage, expected in (('stop', ['stop']), ('recover', ['stop', 'recover'])):
            runtime = Runtime(fail=stage)
            with self.subTest(stage=stage), self.assertRaises(ReleaseError):
                self.run_operation(runtime=runtime)
            self.assertEqual(expected, runtime.calls)
        class PostPublicationFailure(Runtime):
            def recover(self, commit, scope):
                super().recover(commit, scope)
                if self.calls.count('recover') == 2:
                    raise ReleaseError('SAFE_POST_PUBLICATION_RECOVERY_UNCONFIRMED')
        runtime = PostPublicationFailure()
        with self.assertRaisesRegex(ReleaseError, '^SAFE_POST_PUBLICATION_RECOVERY_UNCONFIRMED$'):
            self.run_operation(runtime=runtime)
        self.assertEqual(['stop', 'recover', 'publish', 'recover'], runtime.calls)

    def test_failed_ready_check_cannot_return_a_ready_operator_receipt(self):
        runtime = Runtime(fail='start_ready')
        with self.assertRaises(ReleaseError):
            self.run_operation(runtime=runtime)
        self.assertEqual(['stop', 'recover', 'publish', 'recover', 'start_ready'], runtime.calls)

    def test_substituted_scope_and_unknown_private_fields_cannot_enter_a_receipt(self):
        changes = ({'Job': receipt()['Job'] | {'JobId': SCAN}}, {'PrivateToken': 'private-canary'},
                   {'InputsDispatchedByRecovery': False}, {'CanReplay': True}, {'GoalVerification': 'Satisfied'},
                   {'ErrorCode': 'private-database-password-canary'}, {'BundleSha256': 'invalid'}, {'Reference': None})
        for change in changes:
            with self.subTest(change=tuple(change)), self.assertRaises(ReleaseError):
                operator.validate_result(receipt() | change, *SCOPE)

    def test_transient_unit_uses_fixed_private_environment_and_no_browser_authority(self):
        commands = []
        def execute(arguments, **options):
            commands.append((arguments, options))
            if arguments[0] == '/usr/bin/systemd-run':
                return json.dumps(receipt())
            return 'LoadState=loaded\nActiveState=inactive\nMainPID=0'
        with patch.object(operator, 'run_checked', side_effect=execute):
            result = operator.publish(BASE, COMMIT, *SCOPE)
        self.assertEqual(receipt(), result)
        arguments, options = commands[0]
        self.assertEqual(720, options['timeout'])
        self.assertEqual((0, 3), options['accepted_codes'])
        self.assertIn('--setenv=HSR_WORKER_EVIDENCE=/srv/headless-screen-reader/state/evidence', arguments)
        self.assertIn('--property=EnvironmentFile=/srv/headless-screen-reader/config/worker.env', arguments)
        self.assertIn('--property=EnvironmentFile=/srv/headless-screen-reader/config/evidence.env', arguments)
        self.assertNotIn('--collect', arguments)
        self.assertEqual(['--recover-recording', ORG, JOB, '1'], arguments[-4:])
        self.assertFalse(any('HSR_WORKER_IMAGE' in item or 'HSR_WORKER_RUNTIME' in item for item in arguments))
        self.assertEqual('/usr/bin/systemctl', commands[1][0][0])

    def test_killed_waiter_cannot_substitute_for_an_active_unit(self):
        def execute(arguments, **options):
            if arguments[0] == '/usr/bin/systemd-run':
                raise ReleaseError('DEPLOY_COMMAND_TIMEOUT')
            return 'LoadState=loaded\nActiveState=active\nMainPID=9876'
        with patch.object(operator, 'run_checked', side_effect=execute), \
                self.assertRaisesRegex(ReleaseError, '^PUBLICATION_PROCESS_UNCONFIRMED$'):
            operator.publish(BASE, COMMIT, *SCOPE)

    def test_secret_bearing_child_json_is_discarded_after_confirmed_exit(self):
        def execute(arguments, **options):
            if arguments[0] == '/usr/bin/systemd-run':
                return json.dumps(receipt() | {'PrivateToken': 'private-token-canary'})
            return 'LoadState=not-found\nActiveState=inactive\nMainPID=0'
        with patch.object(operator, 'run_checked', side_effect=execute):
            self.assertIsNone(operator.publish(BASE, COMMIT, *SCOPE))

    def test_only_explicit_inactive_or_failed_zero_pid_metadata_confirms_settlement(self):
        for metadata, expected in (
                ('LoadState=loaded\nActiveState=failed\nMainPID=0', True),
                ('LoadState=not-found\nActiveState=inactive\nMainPID=0', True),
                ('LoadState=loaded\nActiveState=active\nMainPID=0', False),
                ('LoadState=loaded\nActiveState=inactive\nMainPID=345', False),
                ('ActiveState=inactive\nMainPID=0', False), ('', False)):
            with self.subTest(metadata=metadata), patch.object(operator, 'run_checked', return_value=metadata):
                self.assertIs(operator.settled('hsr-publication-owned'), expected)

    def test_maintenance_events_are_separate_ordered_scope_receipts(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            (base / 'deployments').mkdir()
            record = operator.MaintenanceReceipt(base, COMMIT, SCOPE)
            record.record('Stopping')
            record.record('PublicationUnconfirmed')
            events = [json.loads(path.read_text()) for path in sorted((base / 'deployments').iterdir())]
        self.assertEqual([1, 2], [event['Sequence'] for event in events])
        self.assertEqual(['Stopping', 'PublicationUnconfirmed'], [event['State'] for event in events])
        for event in events:
            self.assertEqual('hsr-publication-maintenance-event-v1', event['Schema'])
            self.assertEqual((COMMIT, *SCOPE), (event['ReleaseCommit'], event['Organization'], event['Job'], event['Generation']))


if __name__ == '__main__':
    unittest.main()
