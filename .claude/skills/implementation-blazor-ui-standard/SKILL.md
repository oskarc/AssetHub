---
name: implementation-blazor-ui-standard
description: The standard for a component-based UI over an in-process backend facade — single component library, page conventions, facade-only backend access, a default error-feedback wrapper, and the optimistic-vs-confirmed mutation decision. Use when building or reviewing any page/component, wiring a backend call, or choosing how a mutation updates the screen.
---

# Component-UI standard

## Principle (why)

A UI layer earns its keep by being *predictable*: one way to reach the backend, one way to surface an error, one way to decide how a mutation feels. Every additional idiom for these is a place where two screens behave differently for no reason the user can see, and a place where a reviewer has to re-derive intent. So the standard fixes the defaults and names the narrow exceptions, rather than leaving each call site to invent its own.

Two defaults carry most of the weight: **errors flow through one wrapper**, and **mutations pick from exactly two interaction modes**. Both exist because the alternative — bespoke try/catch and ad-hoc optimism per call site — is exactly the drift that accumulates invisibly until a review finds three different error behaviors on one page.

## Pattern (what)

**Backend access through one facade.**
- The UI depends only on the application contracts and reaches the backend through a single in-process facade — never by injecting backend services directly into components, never by constructing an HTTP client.
- The facade returns DTOs and throws a single typed UI exception on failure (it performs the result→exception translation once, at its boundary — see `pattern-service-result`). Components never see the raw result type.

**One default error idiom: the feedback wrapper.**
- Every user-initiated backend call goes through a feedback wrapper that runs the operation, shows a localized error (or optional success) message, and returns success/failure to the page:
  ```
  var (ok, value) = await Feedback.ExecuteWithFeedbackAsync(() => Api.Load(id), "load X");
  if (!ok) return;
  ```
- A hand-written `try / catch (specific UI exception)` is allowed **only** when the page reacts differently to a *specific* failure (404 → navigate away, 409 → offer reload): handle that case, delegate the rest to the wrapper's error handler. Catching the base exception type around a facade call is drift — the wrapper already handles unknown failures.
- The framework's render-error boundary is a last-resort net for *render-time* faults, not a substitute for the wrapper.

**Mutations: optimistic vs. confirmed — pick by whether the flow was already interrupted.**
- **Optimistic** (update local state first, roll back on failure) — for instant-feel actions where *no dialog interrupted the flow*: toggles, single-field edits/renames, removing an item from a list, reordering. Keep a reference to the changed item before mutating so rollback is trivial; on failure restore state (the wrapper already showed the error).
- **Confirmed (await-first)** — for flows that already passed through a confirmation dialog (destructive deletes, bulk ops): the dialog already broke the "instant" illusion, so optimism buys nothing. Await the call, then update local state on success. Do not retrofit confirm-gated deletes to optimistic.
- **Never optimistic, regardless of mode:** file uploads (real progress), multi-step wizards/bulk ops, validation-heavy forms (failure is common), and any mutation followed by navigation (just await and go).
- Don't optimistically update state other components derive from (e.g. a sidebar count) — let those refresh after confirmation.

**Source-tree organization mirrors one feature taxonomy.**
- Sibling concern-folders — components, dialogs, and any per-domain split of the facade (see `pattern-cohesive-type-split`) — use the *same* feature names. A developer learns the layer's map once and finds anything by feature, instead of re-learning a different grouping per folder.
- A flat "junk drawer" folder (every dialog in one directory, every component in one directory) is the signal to split. Split it into the feature buckets the rest of the layer *already* uses — never a fresh taxonomy invented for that one folder. When namespaces follow folders, the move's cost is a few import lines (in the project's import file *and* in test projects that bind the types); keep that one taxonomy and the cost stays mechanical.

**Component & page conventions.**
- One component library, used exclusively; no raw platform form controls where a library equivalent exists; no hard-coded visual values (consult the design-system reference).
- Authn attribute on every page except deliberately-public ones; async-dispose when holding subscriptions/timers/cancellation sources.
- Dialogs follow a consistent naming + return convention.
- State management uses framework primitives + scoped services — no third-party state container.
- Caching of backend data uses the shared cache (`pattern-hybrid-cache`), not browser/local storage.

## Boundaries

- The "reacts differently to a specific failure" carve-out is narrow: it's for *branching on a status*, not for showing a slightly different message. Silent degradation (an autocomplete that returns empty on error) and cancellation-aware loads that distinguish disposal from error are the other legitimate hand-written cases.
- Optimistic/confirmed is a *per-mutation* decision, not a per-page mode — the same page can have both.
