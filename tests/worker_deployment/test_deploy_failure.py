import contextlib
import errno
import fcntl
import hashlib
import io
import json
import os
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'ops' / 'worker'))
import deploy
from activation import current_commit
from release_bundle import ReleaseError
from test_activation import SimulatedRuntime

COMMIT = 'a' * 40
PREVIOUS = 'b' * 40
CANARY = 'private-token=https://private.example/path?secret=never-log-this'


class DeploymentFailureTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='hsr-deploy-failure-')
        self.root = Path(self.temporary.name)
        self.root.chmod(0o700)
        (self.root / '.managed').write_text(deploy.MARKER, encoding='utf-8')
        for name in ('deployments', 'incoming', 'releases'):
            (self.root / name).mkdir()
        (self.root / 'releases' / PREVIOUS).mkdir()
        (self.root / 'current').symlink_to('releases/' + PREVIOUS, target_is_directory=True)
        self.sentinel = self.root / 'unrelated'
        self.sentinel.write_bytes(b'original-current-pointer-sentinel')
        self.scope = contextlib.ExitStack()
        self.scope.enter_context(patch.object(deploy, 'BASE', self.root))
        self.scope.enter_context(patch.object(sys, 'argv', ['hsr-deploy']))
        self.scope.enter_context(patch.dict(os.environ, {'SSH_ORIGINAL_COMMAND': 'deploy ' + COMMIT,
                                                       'HSR_WORKER_DATABASE': CANARY}))
        # CI runs unprivileged. Simulate only the deployment identity and this one
        # fixture root's owner; files, modes, links, archive, flock and pointers are real.
        self.owner = 0
        original_stat = Path.stat
        def root_stat(path, *args, **kwargs):
            facts = original_stat(path, *args, **kwargs)
            if path == self.root:
                return os.stat_result(tuple(facts[:4]) + (self.owner,) + tuple(facts[5:]))
            return facts
        self.scope.enter_context(patch.object(Path, 'stat', root_stat))
        self.scope.enter_context(patch.object(deploy.os, 'geteuid', return_value=0))

    def tearDown(self):
        self.scope.close()
        self.assertEqual(b'original-current-pointer-sentinel', self.sentinel.read_bytes())
        self.temporary.cleanup()

    def invoke(self, expected_current=PREVIOUS):
        output, error = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(error):
            status = deploy.entrypoint()
        self.assertEqual(3, status)
        self.assertEqual('', output.getvalue())
        self.assertNotIn(CANARY, error.getvalue())
        receipt = json.loads(error.getvalue())
        self.assertEqual('hsr-private-deployment-failure-v1', receipt['Schema'])
        self.assertEqual('Unconfirmed', receipt['State'])
        self.assertFalse(receipt['AutomaticRetry'])
        self.assertEqual(expected_current, current_commit(self.root))
        return receipt

    def test_nonroot_identity_is_denied_before_commit_or_run_is_accepted(self):
        with patch.object(deploy.os, 'geteuid', return_value=1000):
            receipt = self.invoke()
        self.assertEqual(('DEPLOY_IDENTITY_INVALID', 'Identity', None, None, 'NotCreated', 'NotStarted'),
                         tuple(receipt[k] for k in ('Code', 'Phase', 'Commit', 'Run', 'FailureEvent', 'Cleanup')))
        self.assertEqual([], list((self.root / 'deployments').iterdir()))
        self.assertEqual([], list((self.root / 'incoming').iterdir()))

    def test_malformed_forced_command_is_not_reflected_or_treated_as_a_commit(self):
        with patch.dict(os.environ, {'SSH_ORIGINAL_COMMAND': 'deploy ' + CANARY}):
            receipt = self.invoke()
        self.assertEqual(('DEPLOY_COMMAND_INVALID', 'Command', None, None),
                         tuple(receipt[k] for k in ('Code', 'Phase', 'Commit', 'Run')))
        self.assertFalse((self.root / 'deployment.lock').exists())

    def test_extra_arguments_are_denied_before_forced_command_or_root_access(self):
        with patch.object(sys, 'argv', ['hsr-deploy', CANARY]):
            receipt = self.invoke()
        self.assertEqual(('DEPLOY_IDENTITY_INVALID', 'Identity', None, None),
                         tuple(receipt[k] for k in ('Code', 'Phase', 'Commit', 'Run')))
        self.assertFalse((self.root / 'deployment.lock').exists())

    def test_invalid_managed_root_is_closed_before_any_staging_or_receipt(self):
        (self.root / '.managed').write_text(CANARY, encoding='utf-8')
        receipt = self.invoke()
        self.assertEqual(('DEPLOY_ROOT_INVALID', 'Root', COMMIT, None),
                         tuple(receipt[k] for k in ('Code', 'Phase', 'Commit', 'Run')))
        self.assertEqual([], list((self.root / 'incoming').iterdir()))
        self.assertEqual([], list((self.root / 'deployments').iterdir()))

    def test_writable_or_linked_roots_cannot_reach_deployment_lock(self):
        linked = self.root / 'alias'
        linked.symlink_to(self.root, target_is_directory=True)
        with patch.object(deploy, 'BASE', linked):
            receipt = self.invoke()
        self.assertEqual(('DEPLOY_ROOT_INVALID', 'Root'), (receipt['Code'], receipt['Phase']))
        self.root.chmod(0o777)
        try:
            receipt = self.invoke()
        finally:
            self.root.chmod(0o700)
        self.assertEqual(('DEPLOY_ROOT_INVALID', 'Root'), (receipt['Code'], receipt['Phase']))
        self.assertFalse((self.root / 'deployment.lock').exists())

    def test_foreign_root_owner_cannot_reach_deployment_lock(self):
        self.owner = 1001
        receipt = self.invoke()
        self.assertEqual(('DEPLOY_ROOT_INVALID', 'Root', COMMIT, None),
                         tuple(receipt[k] for k in ('Code', 'Phase', 'Commit', 'Run')))
        self.assertFalse((self.root / 'deployment.lock').exists())

    def test_real_busy_flock_does_not_read_archive_create_stage_or_touch_runtime(self):
        with (self.root / 'deployment.lock').open('a') as held:
            fcntl.flock(held, fcntl.LOCK_EX | fcntl.LOCK_NB)
            with patch.object(deploy, 'extract_verified') as extract, \
                    patch.object(deploy, 'DeploymentRuntime') as runtime:
                receipt = self.invoke()
            extract.assert_not_called()
            runtime.assert_not_called()
        self.assertEqual(('DEPLOY_LOCK_BUSY', 'Lock', COMMIT, None, 'NotStarted'),
                         tuple(receipt[k] for k in ('Code', 'Phase', 'Commit', 'Run', 'Cleanup')))
        self.assertEqual([], list((self.root / 'incoming').iterdir()))
        self.assertEqual([], list((self.root / 'deployments').iterdir()))

    def test_archive_failure_keeps_exact_code_run_and_immutable_failure_event(self):
        with patch.object(deploy, 'extract_verified', side_effect=ReleaseError('RELEASE_DIGEST_MISMATCH')):
            receipt = self.invoke()
        self.assertEqual(('RELEASE_DIGEST_MISMATCH', 'Receiving', COMMIT, 'Persisted', 'Completed'),
                         tuple(receipt[k] for k in ('Code', 'Phase', 'Commit', 'FailureEvent', 'Cleanup')))
        files = sorted((self.root / 'deployments').iterdir())
        events = [json.loads(p.read_text(encoding='utf-8')) for p in files]
        self.assertEqual(['Receiving', 'Unconfirmed'], [e['State'] for e in events])
        self.assertEqual({receipt['Run']}, {e['Run'] for e in events})
        self.assertRegex(receipt['Run'], r'\A[a-f0-9]{32}\Z')
        self.assertEqual(('RELEASE_DIGEST_MISMATCH', 'Receiving'),
                         (events[1]['Code'], events[1]['Phase']))
        self.assertNotIn('Code', events[0])
        self.assertNotIn(CANARY, ''.join(p.read_text(encoding='utf-8') for p in files))
        self.assertEqual([], list((self.root / 'incoming').iterdir()))

    def test_unknown_exception_text_is_never_a_diagnostic_code(self):
        for failure in (RuntimeError(CANARY), ReleaseError(CANARY), ReleaseError([CANARY]),
                        ReleaseError({'secret': CANARY}),
                        ReleaseError('DEPLOY_COMMAND_INVALID ' + CANARY)):
            with self.subTest(kind=type(failure).__name__):
                with patch.object(deploy, 'extract_verified', side_effect=failure):
                    receipt = self.invoke()
                self.assertEqual(('PRIVATE_DEPLOYMENT_UNCONFIRMED', 'Receiving', 'Persisted', 'Completed'),
                                 tuple(receipt[k] for k in ('Code', 'Phase', 'FailureEvent', 'Cleanup')))

    def test_storage_errno_is_classified_without_path_or_exception_text(self):
        for number, code in ((errno.ENOSPC, 'DEPLOY_STORAGE_CAPACITY'),
                             (errno.EDQUOT, 'DEPLOY_STORAGE_CAPACITY'),
                             (errno.EACCES, 'DEPLOY_FILESYSTEM_DENIED'),
                             (errno.ENOENT, 'DEPLOY_PATH_UNAVAILABLE')):
            with self.subTest(errno=number):
                with patch.object(deploy.tempfile, 'mkdtemp', side_effect=OSError(number, CANARY)):
                    receipt = self.invoke()
                self.assertEqual((code, 'Staging', 'NotCreated', 'NotStarted'),
                                 tuple(receipt[k] for k in ('Code', 'Phase', 'FailureEvent', 'Cleanup')))

    def test_unavailable_failure_event_cannot_replace_the_primary_failure(self):
        original = deploy.Receipt.record
        def record(receipt, state, **facts):
            if state == 'Unconfirmed':
                raise OSError(errno.ENOSPC, CANARY)
            return original(receipt, state, **facts)
        with patch.object(deploy, 'extract_verified', side_effect=ReleaseError('RELEASE_DIGEST_MISMATCH')), \
                patch.object(deploy.Receipt, 'record', record):
            receipt = self.invoke()
        self.assertEqual(('RELEASE_DIGEST_MISMATCH', 'Receiving', 'Unavailable', 'Completed'),
                         tuple(receipt[k] for k in ('Code', 'Phase', 'FailureEvent', 'Cleanup')))
        events = [json.loads(p.read_text(encoding='utf-8')) for p in (self.root / 'deployments').iterdir()]
        self.assertEqual(['Receiving'], [e['State'] for e in events])

    def test_failed_cleanup_is_explicit_and_does_not_hide_the_primary_cause(self):
        with patch.object(deploy, 'extract_verified', side_effect=ReleaseError('RELEASE_DIGEST_MISMATCH')), \
                patch.object(deploy.shutil, 'rmtree', side_effect=PermissionError(errno.EACCES, CANARY)):
            receipt = self.invoke()
        self.assertEqual(('RELEASE_DIGEST_MISMATCH', 'Receiving', 'Unconfirmed', 'DEPLOY_FILESYSTEM_DENIED'),
                         tuple(receipt[k] for k in ('Code', 'Phase', 'Cleanup', 'CleanupCode')))
        self.assertEqual(1, len(list((self.root / 'incoming').iterdir())))

    def archive(self):
        files = {'fixture.tar': b'fixture bytes', 'worker/HeadlessScreenReader.Worker.dll': b'worker',
                 'worker/HeadlessScreenReader.Worker.runtimeconfig.json': b'{}',
                 'worker/HeadlessScreenReader.Worker.deps.json': b'{}'}
        manifest = {'schema': 'hsr-worker-release-v1', 'commit': COMMIT, 'image': 'sha256:' + 'c' * 64,
                    'files': {name: hashlib.sha256(value).hexdigest() for name, value in files.items()}}
        stream = self.scope.enter_context(tempfile.TemporaryFile())
        with tarfile.open(fileobj=stream, mode='w') as archive:
            for name, value in (files | {'manifest.json': json.dumps(manifest).encode('utf-8')}).items():
                item = tarfile.TarInfo(name)
                item.size = len(value)
                archive.addfile(item, io.BytesIO(value))
        stream.seek(0)
        return stream

    def test_ready_is_printed_only_after_verified_archive_activation_and_cleanup(self):
        runtime = DeploymentTestRuntime(self.root)
        output, error = io.StringIO(), io.StringIO()
        with patch.object(deploy.sys, 'stdin', self.archive()), \
                patch.object(deploy, 'DeploymentRuntime', return_value=runtime), \
                contextlib.redirect_stdout(output), contextlib.redirect_stderr(error):
            status = deploy.entrypoint()
        receipt = json.loads(output.getvalue())
        self.assertEqual(0, status)
        self.assertEqual('', error.getvalue())
        self.assertEqual(('hsr-private-deployment-v1', 'Ready', COMMIT, PREVIOUS),
                         tuple(receipt[k] for k in ('Schema', 'State', 'Commit', 'Previous')))
        self.assertEqual(COMMIT, current_commit(self.root))
        self.assertEqual([('prepare:' + COMMIT, PREVIOUS), ('stop', PREVIOUS),
                          ('recover:' + PREVIOUS + ':evidence', PREVIOUS), ('probe:' + COMMIT, PREVIOUS),
                          ('start:' + COMMIT, COMMIT), ('enable', COMMIT)], runtime.calls)
        self.assertEqual([], list((self.root / 'incoming').iterdir()))
        self.assertEqual(b'worker', (self.root / 'releases' / COMMIT / 'worker/HeadlessScreenReader.Worker.dll').read_bytes())

    def test_native_prepare_failure_reports_its_phase_and_never_switches_release(self):
        runtime = DeploymentTestRuntime(self.root, ['prepare:' + COMMIT])
        with patch.object(deploy.sys, 'stdin', self.archive()), \
                patch.object(deploy, 'DeploymentRuntime', return_value=runtime):
            receipt = self.invoke()
        self.assertEqual(('PRIVATE_DEPLOYMENT_UNCONFIRMED', 'PreparingRuntime', 'Persisted', 'Completed'),
                         tuple(receipt[k] for k in ('Code', 'Phase', 'FailureEvent', 'Cleanup')))
        self.assertEqual([('prepare:' + COMMIT, PREVIOUS)], runtime.calls)

    def test_unknown_native_stop_is_reported_without_start_or_recovery(self):
        runtime = DeploymentTestRuntime(self.root, ['stop'])
        with patch.object(deploy.sys, 'stdin', self.archive()), \
                patch.object(deploy, 'DeploymentRuntime', return_value=runtime):
            receipt = self.invoke()
        self.assertEqual(('PRIVATE_DEPLOYMENT_UNCONFIRMED', 'Stopping', 'Persisted', 'Completed'),
                         tuple(receipt[k] for k in ('Code', 'Phase', 'FailureEvent', 'Cleanup')))
        self.assertEqual([('prepare:' + COMMIT, PREVIOUS), ('stop', PREVIOUS)], runtime.calls)

    def test_rolled_back_response_remains_unchanged_and_never_reports_ready(self):
        runtime = DeploymentTestRuntime(self.root, ['start:' + COMMIT])
        output, error = io.StringIO(), io.StringIO()
        with patch.object(deploy.sys, 'stdin', self.archive()), \
                patch.object(deploy, 'DeploymentRuntime', return_value=runtime), \
                contextlib.redirect_stdout(output), contextlib.redirect_stderr(error):
            status = deploy.entrypoint()
        receipt = json.loads(output.getvalue())
        self.assertEqual(3, status)
        self.assertEqual('', error.getvalue())
        self.assertEqual(('hsr-private-deployment-v1', 'RolledBack', COMMIT, PREVIOUS),
                         tuple(receipt[k] for k in ('Schema', 'State', 'Commit', 'Previous')))
        self.assertEqual(PREVIOUS, current_commit(self.root))
        self.assertEqual([('recover:' + COMMIT + ':evidence', COMMIT),
                          ('recover:' + COMMIT + ':readiness', COMMIT),
                          ('probe:' + PREVIOUS, COMMIT), ('start:' + PREVIOUS, PREVIOUS)], runtime.calls[-4:])
        self.assertEqual([], list((self.root / 'incoming').iterdir()))

    def test_enable_failure_retains_the_new_pointer_and_cannot_emit_ready(self):
        runtime = DeploymentTestRuntime(self.root, ['enable'])
        with patch.object(deploy.sys, 'stdin', self.archive()), \
                patch.object(deploy, 'DeploymentRuntime', return_value=runtime):
            receipt = self.invoke(expected_current=COMMIT)
        self.assertEqual(('PRIVATE_DEPLOYMENT_UNCONFIRMED', 'Enabling', 'Persisted', 'Completed'),
                         tuple(receipt[k] for k in ('Code', 'Phase', 'FailureEvent', 'Cleanup')))
        self.assertEqual(('enable', COMMIT), runtime.calls[-1])

    def test_cleanup_failure_after_ready_state_cannot_emit_a_ready_response(self):
        runtime = DeploymentTestRuntime(self.root)
        # Install this exact tiny archive first. The second call verifies its existing release,
        # leaving only its new private staging directory to be cleaned after activation.
        with patch.object(deploy.sys, 'stdin', self.archive()), \
                patch.object(deploy, 'DeploymentRuntime', return_value=runtime), \
                contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, deploy.entrypoint())
        runtime = DeploymentTestRuntime(self.root)
        with patch.object(deploy.sys, 'stdin', self.archive()), \
                patch.object(deploy, 'DeploymentRuntime', return_value=runtime), \
                patch.object(deploy.shutil, 'rmtree', side_effect=PermissionError(errno.EACCES, CANARY)):
            receipt = self.invoke(expected_current=COMMIT)
        self.assertEqual(('DEPLOY_FILESYSTEM_DENIED', 'Cleanup', 'Unconfirmed', 'DEPLOY_FILESYSTEM_DENIED'),
                         tuple(receipt[k] for k in ('Code', 'Phase', 'Cleanup', 'CleanupCode')))
        self.assertEqual(1, len(list((self.root / 'incoming').iterdir())))

    def test_failure_event_rejects_unknown_code_or_phase_without_a_partial_file(self):
        receipt = deploy.Receipt(self.root, COMMIT)
        for code, phase in ((CANARY, 'Receiving'), ('DEPLOY_LOCK_BUSY', CANARY),
                            ([], 'Receiving'), ('DEPLOY_LOCK_BUSY', [])):
            with self.subTest(value_type=type(code).__name__), self.assertRaises(ReleaseError):
                receipt.record('Unconfirmed', code=code, phase=phase)
        self.assertEqual(0, receipt.sequence)
        self.assertEqual([], list((self.root / 'deployments').iterdir()))


class DeploymentTestRuntime(SimulatedRuntime):
    """Actual archive/pointer orchestration, simulated systemd/browser lifecycle only."""
    def prepare(self, commit):
        self.invoke('prepare:' + commit)

    def enable(self):
        self.invoke('enable')


if __name__ == '__main__':
    unittest.main()
