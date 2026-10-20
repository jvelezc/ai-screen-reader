// Each browser test collection owns a real Chromium process tree. Bound concurrent
// collections on small CI hosts while preserving the production capture deadline.
[assembly: Xunit.CollectionBehavior(MaxParallelThreads = 2)]
