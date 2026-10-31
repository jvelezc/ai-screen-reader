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

**Accepted private delivery:** source63bf550 passes all nine
[CI jobs](https://github.com/jvelezc/ai-screen-reader/actions/runs/41208728788),
Windows/Ubuntu2,858 each, operational172 and exact151-file private deployment.
The guarded root-owned module installation preserves the original backup and
checks its old digest against the accepted source before atomic replacement.
Installed SHA-256 is `0f7cc84e637895720b0d1dad60f71b9dbf642d6be61506f64571a98a1d96651d`.
The same actual rootless Podman read now succeeds from both `/root` and `/`.
No service configuration or credential changes are made. Actual
[first publication through the wrapper](first-publication-maintenance-v1.md) is
separately accepted; the directory repair alone was not used as proof of it.
