---
name: pattern-public-api-contract
description: Expose a curated, versioned subset of HTTP endpoints as the stable public contract — composed security helpers so no endpoint ships half-secured, a scope filter on every public endpoint, a dual CSRF gate for cookie+bearer auth, a consistent error shape, and SemVer discipline. Use when adding or reviewing an endpoint, deciding whether it's public, or designing the API's security wiring.
---

# Public API contract

## Principle (why)

An HTTP surface serves two very different audiences: internal callers (the app's own UI, admin tooling) and external integrators (their scripts, SDKs, CI). Treating all endpoints as "the API" forces a choice between under-documenting the internal ones or over-committing to stability on endpoints you want to keep fluid. The resolution is a **curated subset**: a deliberately-marked set of endpoints is the stable, documented, SemVer-governed contract; everything else stays functional but undocumented and free to change.

The second principle is **security as a bundle, encoded once.** A public mutating endpoint needs several things in concert — authorization, a scope check, CSRF handling, and inclusion in the published doc. Requiring authors to remember and hand-assemble that chain at every call site means it will eventually ship with a leg missing (it did, twice, before it was encoded). So the bundle becomes a single composed helper that can't be partially applied. (This is `principle-encode-over-document` applied to the API surface.)

## Pattern (what)

**Mark the public subset with composed helpers, not hand-assembled chains.**
- Two helpers — one for reads, one for mutations — each bundling the scope filter + (for mutations) antiforgery handling + inclusion in the generated OpenAPI document. Authors call one method; no leg can be forgotten. Validation filters chain *before* the helper so validation runs ahead of the scope check.
- Only marked endpoints appear in the published doc. A raw, un-composed "mark as public" call on a single endpoint is a review flag — the only sanctioned bare uses are group-level marking and a documented self-service exception (below).

**Scope filter on every public endpoint.**
- Every public endpoint carries a scope requirement matching its operation (`read`/`write` per resource). Cookie/JWT principals pass through unchanged; token principals are checked against their granted scopes.
- **The one sanctioned exception** is a self-service surface a token must *never* reach (e.g. minting/revoking tokens): instead of a scope filter, it carries a hard guard inside the handler that rejects token principals outright (no scope, not even a wildcard, is enough), with a comment pointing back at this rule. See `pattern-pat-scope-enforcement`.

**Dual CSRF gate for mixed cookie + bearer auth.**
- A route group that hosts any mutating endpoint chains an "antiforgery-unless-bearer" filter at the group level — it validates a CSRF header for cookie principals and no-ops for bearer/anonymous. This is the *actual* CSRF defense.
- Per-endpoint, the framework's built-in antiforgery is disabled (so bearer clients, which can't supply the token, aren't rejected). **Both must be present together** — disabling the built-in pipeline without the group filter leaves cookie-authed callers (e.g. a UI under XSS) able to mutate without a token.

**One consistent error shape.**
- All endpoint errors flow through the result→HTTP translation (`pattern-service-result`), producing one documented error object (`code`/`message`/`details`). When an error must be produced *before* the service call (e.g. file-binding validation), use the same error-shape factory — never an anonymous object, which breaks SDK consumers reading the schema.

**SemVer on the marked subset.** Renames, removals, and type changes to public endpoints are breaking changes — they need a version bump or a deprecation path. Internal endpoints carry no such obligation.

## Boundaries

- "Public" is a deliberate decision per endpoint, not a default. Admin/UX-helper endpoints stay unmarked and mutable.
- The composed helpers are the enforcement point — if you find yourself hand-assembling the scope+antiforgery+visibility chain, that's the signal a helper is missing or being bypassed.
- Gating the published doc UI by environment (open in dev, admin-gated elsewhere) is part of this pattern but its mechanism is host-specific.
