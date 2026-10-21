// Real Chromium collections share the hosted runner's CPU, disk and encoder resources.
// Run collections sequentially: overlapping browser/media work can exhaust the same
// capture, clock-calibration and shutdown deadlines that this suite must preserve.
// Concurrency contracts are exercised explicitly inside their deterministic tests.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
