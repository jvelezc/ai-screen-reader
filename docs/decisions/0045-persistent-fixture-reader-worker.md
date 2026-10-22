# Persistent fixed-fixture reader worker

Date: October 22, 2026.

The hosted reader registry needs a persistent browser outside the trusted
control service. The first delivery step implements its child protocol and real
Chromium lifecycle. Container supervision, the separate recovery journal and
hosted API composition follow; this child alone does not establish isolation.

`--reader-fixture` accepts only the embedded Foundation fixture. Domain owns the
request/response records and their validation. Worker owns framing and strict
JSON decoding. CLI owns browser launch, sequential captures and actual shutdown.
No new project or dependency direction is introduced.

## Exchange

Each frame is a four-byte big-endian positive byte length followed by exactly
that many UTF-8 JSON bytes. Request limit: 1,024 bytes. Response limit: 262,144
bytes. A length is validated before payload allocation; partial headers/payloads,
missing fields, duplicate properties, unknown properties and numeric operations
are rejected. Neither runtime exception messages nor untrusted payloads are
returned as diagnostics.

Requests contain `Schema: hsr-reader-worker-v1`, a nonempty nonsecret `SessionId`,
`Sequence` and `Operation`. Sequence 1 must be Open. Subsequent requests must
match that session and increase by exactly one. Only Observe and Close follow
Open. There are no URL, key, credential, capture-configuration or recording
fields. The session ID binds messages; it does not authenticate a tenant.

Open launches one Chromium context and returns its first observation. Observe
captures that same context; every response is bound to the original document,
capture epoch 1, fixture content hash, request identity and sequence. At most
100 captures including Open and 2,048 nodes per snapshot are admitted. After
capture 100 only Close is valid. The lifetime is two minutes from worker start,
including launch and protocol waits.

Close carries no snapshot and is acknowledged only after actual browser disposal
and validated native shutdown, process exit and transport settlement. The
existing strictly-under-4-second exit confirmation and under-5-second native
shutdown bounds remain. All responses preserve zero dispatched inputs and
goalVerified false. A failed or incomplete Close is not a cleanup receipt.

Malformed requests, EOF, cancellation, expiry or write failures stop the loop,
join actual disposal and exit 3 with only `READER_WORKER_UNCONFIRMED` on stderr.
The supervisor must retain recovery intent and capacity until it independently
confirms native process/container absence; a child exit code alone is insufficient.

## Acceptance and next boundary

Direct contract/wire tests and real Chromium tests cover strict decoding,
identity/sequence/state validation, exact limits, same-context DOM changes,
capture budget, EOF/cancellation/expiry and native shutdown. An actual subprocess
test launches the CLI's own build output, reads two further observations and
validates Close before confirming process exit. It uses a closed environment;
it does not prove production container isolation.

Do not enable this child directly inside the hosted control service or install
host Chromium as a shortcut. The next delivery steps connect rootless native
supervision, a separate reader journal, packaged readiness and fresh tenant
authorization. The one-shot queue journal remains owned exclusively by its
worker. Private deployment, authenticated lifecycle acceptance, Edge-to-core
reachability and actual human/AT acceptance remain distinct gates.
