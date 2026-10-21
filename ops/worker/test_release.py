"""Linux operational checks with explicit nonzero discovery and a safe CI receipt."""
import argparse
import json
from pathlib import Path
import sys
import unittest


class RecordingResult(unittest.TextTestResult):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.successes = set()

    def addSuccess(self, test):
        super().addSuccess(test)
        self.successes.add(test.id())


def flatten(suite):
    for item in suite:
        if isinstance(item, unittest.TestSuite):
            yield from flatten(item)
        else:
            yield item.id()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="artifacts/deployment-validation/unit.json")
    args = parser.parse_args()
    if sys.platform != "linux":
        raise SystemExit("Private worker deployment checks require actual Linux filesystem/process semantics.")
    root = Path(__file__).resolve().parents[2]
    suite = unittest.defaultTestLoader.discover(str(root / "tests" / "worker_deployment"))
    names = list(flatten(suite))
    result = unittest.TextTestRunner(verbosity=2, resultclass=RecordingResult).run(suite)
    failed = {getattr(test, "test_case", test).id() for test, _ in result.failures + result.errors}
    skipped = {getattr(test, "test_case", test).id() for test, _ in result.skipped}
    complete = bool(names) and len(set(names)) == len(names) and result.testsRun == len(names)
    passed = complete and result.wasSuccessful() and not skipped and not result.expectedFailures
    receipt = {"Schema": "hsr-private-deployment-unit-v1", "Discovered": len(names),
               "Executed": result.testsRun, "Passed": len(result.successes),
               "Failed": len(failed), "Skipped": len(skipped), "Ready": passed, "Tests": names,
               "HostedDeploymentPerformed": False}
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
