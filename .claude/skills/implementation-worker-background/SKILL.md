---
name: implementation-worker-background
description: Conventions for message handlers and scheduled background services in a worker host — placement by data ownership, per-item resilience in batch loops, scope-per-iteration for scoped dependencies, and cancellation/logging discipline. Use when adding a message handler, a recurring background job, or deciding which host runs it.
---

# Worker handlers & background services

## Principle (why)

Background work has two failure modes that foreground request/response code doesn't: a single bad item can poison a whole batch, and a long-running loop that ignores cancellation can't be shut down cleanly. The conventions here exist to contain both — isolate per-item failures so one bad message doesn't stop the queue, and make every loop cancellation-aware so the host stops promptly. The third concern is *placement*: as a system grows, "where does this job run?" stops being obvious, and an unprincipled answer scatters related work across hosts.

## Pattern (what)

**Placement follows data ownership, not convenience.** When background work is hosted by more than one process, put each job where the data it touches lives / where its triggering loop belongs:
- The dedicated worker host owns processing pipelines and scheduled sweeps (retention, cleanup, digests) — work with no interactive caller.
- An interactive host (e.g. the API) may host the background consumers that *complete an interactive loop it owns* (state transition + fan-out on a just-finished operation; sync jobs serving interactive flows).
- New background work defaults to the worker; it goes in an interactive host only when it closes a request/response loop that host owns. The handler/service rules below apply identically either way.

**Message handlers** (broker-dispatched):
- The broker auto-discovers handler methods by convention; each is a sealed class with constructor-injected dependencies and a single handle method that processes one message and returns any follow-up events.
- Message contracts live in the shared application layer, not the handler.
- Rely on the broker's retry-with-backoff for transient failures; don't hand-roll retry inside the handler.

**Scheduled background services** (timer-driven):
- A periodic-timer loop; acquire scoped dependencies via the scope factory and **create one scope per iteration** — never inject scoped services into the singleton service directly (a captured scope leaks and goes stale).
- **Per-item try/catch inside batch loops** so one item's failure doesn't abort the batch; log the failure and continue.
- `ThrowIfCancellationRequested()` inside long loops; catch the cancellation exception at the top level and exit cleanly (with a one-line reason, not an empty catch).

## Implementation constraints (how)

- Logging by level: start + completion-summary at info (always with counts); per-batch progress / "nothing to do" at debug; per-item failures and cancellation at warning.
- Idempotency: a handler may be redelivered (retry, at-least-once delivery) — handlers tolerate "already processed" and missing-row states rather than assuming exactly-once.

## Boundaries

- "Per-item try/catch" is for *batch loops over independent items*. A handler processing a single message lets a genuine fault bubble to the broker's retry — it doesn't swallow it.
- The placement rule is about *ownership*, not load — don't move a job to another host for throughput reasons without a real bottleneck.
