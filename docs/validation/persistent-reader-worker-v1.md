# Persistent reader child acceptance

Source `bfd31ad` implements the fixed Foundation child described in
[decision 0045](../decisions/0045-persistent-fixture-reader-worker.md).
Local Windows passed all 1,702 cases, including 95 new worker cases, with zero
failures/skips. Nonincremental Release build and format verification passed.

## CI issue and repair

[CI 39873239079](https://github.com/jvelezc/ai-screen-reader/actions/runs/39873239079)
passed eight jobs. Its Ubuntu TRX confirms 1,702/1,702 including all 95 worker
cases. Windows passed 1,701/1,702; all 95 new worker cases passed there too.
The failing existing case was
`OwnedTransport_StopsAtTwoSecondsOfTotalNativeTimeAndStillRequiresActualExit`
with contextTime 250 and sdkAcknowledged false. The five-second wait for its
transport-stop signal expired. This failed run did not authorize promotion.

The test signaled browser start before `WaitAsync` registered its relative
grace timer. A continuation could advance the fake clock between calculating
the remaining grace and registering that timer, moving its due instant past
the intended 2,000 ms. The repair uses a delegating test TimeProvider to signal
after the actual timer for that instant has been registered. Both tests that
resume from asynchronous context shutdown wait for it before advancing time.

Production shutdown code, the original 1,999/2,000/3,999 ms assertions and
four/five-second native limits are unchanged. No tests or assertions were
removed, deadlines expanded, or real cleanup replaced with fake success.
The focused repaired suite passes 109/109 locally, including the worker cases.
Full local and replacement Windows/Linux CI results remain separate until
their reports confirm acceptance.

## Remaining boundary

This proves the child protocol and actual Chromium lifecycle, not a hosted
isolated session. Container supervision, a separate reader journal, reserved
launch ownership, independent native removal and authenticated API composition
are the next delivery steps. The accepted signed-in shop/S3 workflow is a
separate one-shot path. Actual human/AT acceptance remains open.
