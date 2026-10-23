# 0046: Isolated persistent reader supervision

Status: implemented; acceptance is recorded in [validation](../validation/isolated-reader-supervisor-v1.md).

## Ownership and transport

`IsolatedReaderSupervisor` retains an exclusive lock in `.reader-runtime`
under its configured state root. It never acquires the queue worker's
`.runtime` lock. Startup validates and recovers the whole pending reader
inventory before admitting a launch. The supervisor admits at most two live
connections and reserves their identities before native creation. An unreadable
or unconfirmed failed launch retains its reservation until reconciliation.

The existing immutable image, closed runtime-client environment, resource limits,
nonroot user, read-only filesystem, network-none policy and ownership-checked
removal are reused. A server-selected entrypoint runs only
`dotnet /app/cli/HeadlessScreenReader.Cli.dll --reader-fixture`. Reader inspection
also binds the image, organization/session labels and exact entrypoint/arguments.
There is no caller-provided URL, executable, argument or environment dictionary.

The native ownership record retains its existing schema. Within the separate
reader journal, its scan/job identifier slots both hold the reader session ID;
generation is one. These are nonsecret container labels, not queue admission,
a scan result, a database lease or evidence finalization. No caller JWT,
database credential or storage credential crosses the child boundary.

`IsolatedReaderSession` joins each framed request and validates schema, sequence,
session, fixture, document, snapshot bounds and explicit uncertainty through
the existing protocol. Open supplies the first capture; 99 further captures
are admitted. At the limit a further Observe is denied without preventing Close.
The parent enforces a two-minute monotonic connection lifetime including browser
startup, and a 20-second bound per protocol exchange. The supervisor reaps idle
expired connections. Cancellation during an exchange joins actual cleanup;
cancellation before admission does not start an exchange.

## Independent completion facts

A valid Closed response must include the original bounded native shutdown
receipt. The supervisor additionally requires clean stdout EOF, empty bounded
stderr and actual attach-client exit with code zero. These establish
`NativeShutdownConfirmed`. They do not prove container absence.

The owned container must independently be inspected, removed and confirmed
absent, and its durable removal acknowledgement written before retiring its
intent. Only after the attach client and diagnostics also settle does the
connection expose `ContainerAbsenceConfirmed`. A failed removal retains the
intent and reservation; retry can finish removal without sending another Close.
Forced removal after invalid output or cancellation can confirm absence while
native graceful shutdown remains unconfirmed. It never fabricates a Closed ACK.

Supervisor shutdown stops maintenance, joins all sessions and reconciles failed
launch intents before releasing the reader lock. Failure retains that lock for
an explicit retry or process restart. Recovery never authorizes deletion of a
same-name container with different ownership or image.

## Operator proof and remaining composition

`--check-reader-runtime` executes the actual packaged fixed-fixture lifecycle,
checks duplicate/capacity denial, observes one document three times and proves
capacity reuse only after removal. It reads no database configuration, admits
no scan and saves no observation. `--recover-reader` performs only the separately
locked reader recovery. Neither command enables an HTTP endpoint.

The Linux release harness executes these commands against the packaged image
while holding the queue journal lock; it checks durable retirement, a missing
image failure and a real interrupted-create container recovery. The recovery
case does not claim an interrupted browser run or tenant acceptance.

The authenticated registry now uses this supervisor through the trusted worker's
private loopback broker; see [decision0047](0047-isolated-reader-http-composition.md).
The API reserves subject capacity before initialization and keeps its read-only
filesystem. Fresh RLS checks and scoped retirement remain explicit. Current-source
CI, private delivery, hosted persistent-session acceptance, authenticated
Edge-to-core reachability and human/AT acceptance are tracked separately in
[the HTTP validation record](../validation/isolated-reader-http-v1.md).
