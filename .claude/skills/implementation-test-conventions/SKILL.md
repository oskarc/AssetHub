---
name: implementation-test-conventions
description: Structural conventions for an automated test suite — naming, real-dependency fixtures vs mocked externals, shared test-data factories, lifecycle, and a test tree that mirrors the source. Use when adding or organizing tests. Pairs with the scaffolding/verification skills that generate and run them.
---

# Test conventions

## Principle (why)

A test suite is read far more than it's written — when one fails, someone unfamiliar with it has to understand *what* broke and *why* in seconds. Conventions buy that: a name that states the scenario and expectation, a known place to find the test, and shared fixtures/factories so each test expresses only what's unique to it. The second principle is **test against real dependencies where they're the thing under test, mock only what's incidental** — a repository test against a real database catches the SQL bug an in-memory fake hides, while an external service the test doesn't care about is mocked away.

## Pattern (what)

**Naming states scenario and expectation:** `MethodName_Condition_ExpectedResult` (e.g. `UpdateAsync_EmptyTitle_ReturnsBadRequest`). The name alone tells you what failed.

**Fixtures by what's real:**
- A real-datastore fixture (e.g. an ephemeral containerized database) for repository/persistence tests — shared via a test collection so the container is reused, not spun up per test.
- A host/application-factory fixture for endpoint/integration tests: real datastore, **mocked externals** (object storage, mail, scanners), a fake auth handler that lets a test assume any identity/role.
- A fake auth provider with convenience identities (default user, admin, arbitrary role).

**Shared test-data factories** produce valid entities with optional overrides (`CreateEntity(title: ...)`) — a test names only the fields it depends on, never hand-rolls a full object.

**Lifecycle** is explicit: seed in the async setup hook, clean up in the async teardown hook, so tests don't leak state into each other.

**Structure mirrors the source tree** — a reader navigates to a test the same way they navigate to the code (`Services/`, `Repositories/`, `Endpoints/`, plus an edge-cases area).

**End-to-end / browser tests** use a page-object pattern (one object per screen), shared helpers, centralized config, and ordered/numbered specs for a readable run.

## Boundaries

- Real-dependency testing is for the layer *under test*. Don't stand up a real database to test a pure mapping function — that's a unit test with no I/O.
- These are *structural* conventions (naming, placement, fixtures). What to cover for a given class, and running the suite, are the scaffolding/verification skills' job — this skill is how the tests are shaped, not which ones exist.
