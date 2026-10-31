# Operator command directory

October 31, 2026. During actual hosted interruption acceptance, a read-only check
using the installed `host_runtime.run_checked` fails from `/root` with
`DEPLOY_COMMAND_FAILED`, while the identical rootless `hsr-worker` Podman command
from `/` verifies native absence. No input, job admission or publication is involved
in that probe. Native stderr identifies an inaccessible inherited caller directory.

All host commands now start from the traversable filesystem root. Their existing
absolute paths, argument lists, environment, timeout/process-group ownership,
accepted-code/output bounds and private-error handling remain intact. The caller
graph includes image preparation, native recovery/probes, service readiness and
publication settlement; these helpers must work from an operator's root home.

The real-child regression
`HostCommandTests.test_actual_child_uses_traversable_root_instead_of_operator_caller_directory`
launches the helper from an unrelated temporary directory and observes the child's
actual working directory. It fails before the fix, then passes. The focused Linux
suite passes 7/7. The repository's actual Linux operational runner discovers,
executes and passes **172/172**, with zero failures/skips; its JSON receipt is
independently parsed, including the exact new test. Source/tests are copied onto
native Ubuntu filesystems for these checks; Windows filesystem behavior is not
substituted. No additional dependencies or test framework are introduced.

**Remaining acceptance:** green main CI, exact private release delivery, guarded
installation of the tested root-owned operator module, and the same read-only
probe succeeding from `/root`. First publication through the ordered wrapper is
a separate subsequent test; a working-directory repair alone does not prove it.
