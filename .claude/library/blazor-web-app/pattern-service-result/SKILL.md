---
name: pattern-service-result
description: Services report business outcomes with a ServiceResult/ServiceResult<T> return type instead of throwing exceptions; the API layer maps results to HTTP with one extension call. Use when designing a service/application layer, an endpoint's error handling, or deciding how a failure should travel from domain logic to the caller.
---

# ServiceResult — outcomes as values, not exceptions

## Principle (why)

Business failures are expected outcomes, not exceptional ones. "Entity not found", "user lacks permission", "name already taken" are part of a method's contract — the caller will hit them routinely and must handle them. Modeling them as exceptions forces every caller into try/catch, loses type information about *which* failures are possible, and conflates control flow with genuine faults (a dropped connection, a null deref). Modeling them as a returned `ServiceResult` makes the failure modes part of the signature, keeps the happy path linear, and reserves exceptions for what they are good at: truly unexpected infrastructure faults that should unwind the stack.

The payoff compounds at the boundary: when every service returns the same result shape, the transport layer needs exactly one translation — result → HTTP response — written once, not re-derived at every endpoint.

## Pattern (what)

**Context:** an application/service layer behind a transport layer (HTTP API, RPC, UI facade).

- **Services never throw for business errors.** Every public method returns `ServiceResult` (no value) or `ServiceResult<T>` (value on success). The happy path returns the value/success; failures return a typed error via a factory.
- **A fixed set of error factories**, each carrying a stable machine code and a transport-status mapping. The canonical set:

  | Factory | HTTP | Code | When |
  |---|---|---|---|
  | `NotFound(msg)` | 404 | `NOT_FOUND` | Entity not found |
  | `Forbidden(msg)` | 403 | `FORBIDDEN` | Caller lacks permission |
  | `BadRequest(msg)` | 400 | `BAD_REQUEST` | Invalid input |
  | `Conflict(msg)` | 409 | `CONFLICT` | Duplicate or state conflict |
  | `Validation(msg, details)` | 400 | `VALIDATION_ERROR` | Field-level errors |
  | `Server(msg)` | 500 | `SERVER_ERROR` | Unexpected failure |

- **One translation at the boundary.** The transport layer calls a single `.ToHttpResult()` (or equivalent) extension that inspects success/failure and produces the response — endpoints never branch on `IsSuccess` or hand-map status codes. A success-projection overload handles non-200 success (e.g. `201 Created`).
- **Exceptions are infrastructure-only.** A genuine infra fault (DB down, serialization blowup) may throw; the service catches it at its own boundary and wraps it as a `Server(...)` result, or lets it bubble to global middleware that renders the same error shape. Business code never catches its own thrown business errors — there are none to catch.

```csharp
// service
if (entity is null) return ServiceError.NotFound("Item not found");
return new ItemDto(entity);

// endpoint — no IsSuccess inspection
return (await svc.GetByIdAsync(id, ct)).ToHttpResult();
return (await svc.CreateAsync(dto, ct))
    .ToHttpResult(value => Results.Created($"/items/{value.Id}", value));
```

## Implementation constraints (how)

- Two types: `ServiceResult` and `ServiceResult<T>`; one `ServiceError` carrying status + code + message (+ optional field details).
- The error **code** is the stable contract for machine consumers; the HTTP status is derived from it. Don't let callers depend on message text.
- A UI/in-process facade over the same services may translate the result into an exception at *its* boundary (one place) so its callers get a value-or-throw surface — but the services themselves still return results.
- When wrapping a caught infra exception, log it at error level, then return `Server(...)` — never leak the raw exception/message to the transport response.

## Boundaries

- This is for **business** outcomes. Programmer errors (null args that should never be null, broken invariants) stay exceptions — they signal a bug, not a handled case.
- Don't invent per-method bespoke result types; the value is in *one* shared shape with *one* boundary translation. A second result shape doubles the translation surface.
