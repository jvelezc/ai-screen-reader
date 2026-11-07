# Private Maestro fixture checks

Use the configured private Maestro worker with a dedicated workspace, not the
whole repository:

```json
{"platform":"web","workspace":"ops/test-sites/maestro","flow":"v1"}
```

This is the `run_test_workspace` tool input. The full YAML flows include their
URL, separator and commands. They open the existing published v1 lab pages,
assert initial action/hidden goal, use a mouse click and assert the goal appears.
Every flow saves before/after screenshots. Require a passed run and JUnit
assertions; the screenshot alone is not an acceptance result. Inspect first when
using interactive tools. The current interactive hierarchy tool returned an empty
view; these selectors instead use the stable IDs in the authored fixture source.

Preserve the returned run ID and evidence ZIP. `DEVICE_BUSY` means no UI action
was attempted: wait or read the existing run rather than repeatedly resubmitting.
After a timeout, query that same run before submitting another copy.

These are independent pointer/UI checks, not keyboard screen-reader execution,
real-user validation, NVDA speech or legal findings. The native reader's recording
suite separately produces its own screenshots, video, graph and actual input
events. Never mix the two runs' original evidence.
