from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "ops" / "worker"))
import ci_release

SHA = "a" * 40
NAMES = ["Build and test (windows-latest)", "Build and test (ubuntu-24.04)",
         "Leased observation worker (Linux)", "Durable scan admission and worker leases (Linux)",
         "Isolated public MCP prototype (Linux)", "Headed Chromium and Xvfb (controller, Linux)",
         "Headed Chromium and Xvfb (synthetic-handoff, Linux)",
         "Headed Chromium and Xvfb (private-pixels, Linux)",
         "Headed Chromium and Xvfb (private-client, Linux)"]


class CiReleaseTests(unittest.TestCase):
    def setUp(self):
        self.run = {"workflow_id": 42, "name": "CI", "status": "completed", "conclusion": "success",
                    "head_branch": "main", "event": "push", "head_sha": SHA,
                    "head_repository": {"full_name": "owner/project"}}
        self.workflow = {"id": 42, "path": ".github/workflows/verify.yml"}
        self.jobs = [{"name": name, "status": "completed", "conclusion": "success"} for name in NAMES]
        self.artifacts = [{"id": 55, "name": "private-worker-release", "expired": False}]

    def select(self, **changes):
        values = dict(run=self.run, workflow=self.workflow, repository="owner/project", head=SHA,
                      jobs=self.jobs, artifacts=self.artifacts) | changes
        return ci_release.select_release(**values)

    def test_only_complete_current_main_ci_selects_its_exact_artifact(self):
        self.assertEqual({"deploy": "true", "sha": SHA, "artifact": "55"}, self.select())

    def test_superseded_successful_source_is_skipped_without_selecting_an_artifact(self):
        self.assertEqual({"deploy": "false"}, self.select(head="b" * 40, artifacts=[]))

    def test_failed_incomplete_forked_or_other_branch_run_cannot_promote(self):
        cases = {"failed": {"conclusion": "failure"}, "pending": {"status": "in_progress"},
                 "branch": {"head_branch": "another"}, "event": {"event": "pull_request"},
                 "fork": {"head_repository": {"full_name": "other/project"}},
                 "workflow": {"workflow_id": 99}, "name": {"name": "Other CI"},
                 "hash": {"head_sha": "latest"}}
        for label, change in cases.items():
            with self.subTest(case=label), self.assertRaisesRegex(ValueError, "^PRIVATE_RELEASE_CI_UNCONFIRMED$"):
                self.select(run=self.run | change)

    def test_every_existing_acceptance_job_is_required(self):
        for index, missing in enumerate(NAMES):
            with self.subTest(case="missing_job_" + str(index)), \
                    self.assertRaisesRegex(ValueError, "^PRIVATE_RELEASE_CI_UNCONFIRMED$"):
                self.select(jobs=[job for job in self.jobs if job["name"] != missing])

    def test_skipped_failed_or_additional_failed_job_is_not_success(self):
        for label, jobs in {"failed": [self.jobs[0] | {"conclusion": "failure"}] + self.jobs[1:],
                            "skipped": [self.jobs[0] | {"conclusion": "skipped"}] + self.jobs[1:],
                            "extra_failed": self.jobs + [{"name": "new gate", "status": "completed", "conclusion": "failure"}],
                            "zero": []}.items():
            with self.subTest(case=label), self.assertRaisesRegex(ValueError, "^PRIVATE_RELEASE_CI_UNCONFIRMED$"):
                self.select(jobs=jobs)

    def test_wrong_workflow_file_cannot_borrow_the_ci_name(self):
        with self.assertRaisesRegex(ValueError, "^PRIVATE_RELEASE_CI_UNCONFIRMED$"):
            self.select(workflow=self.workflow | {"path": ".github/workflows/other.yml"})

    def test_expired_missing_ambiguous_or_invalid_artifact_is_rejected(self):
        cases = {"expired": [self.artifacts[0] | {"expired": True}], "missing": [],
                 "duplicate": self.artifacts * 2, "boolean_id": [self.artifacts[0] | {"id": True}],
                 "zero_id": [self.artifacts[0] | {"id": 0}]}
        for label, artifacts in cases.items():
            with self.subTest(case=label), self.assertRaisesRegex(ValueError, "^PRIVATE_RELEASE_CI_UNCONFIRMED$"):
                self.select(artifacts=artifacts)

    def test_unvalidated_run_id_cannot_change_the_github_api_path(self):
        argv = ["ci_release.py", "--repository", "owner/project", "--run", "../../secrets", "--output", "unused"]
        with patch("sys.argv", argv), patch("ci_release.api") as api:
            with self.assertRaisesRegex(ValueError, "^PRIVATE_RELEASE_CI_UNCONFIRMED$"):
                ci_release.main()
        api.assert_not_called()


if __name__ == "__main__":
    unittest.main()
