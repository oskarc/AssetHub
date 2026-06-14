---
name: pattern-pat-scope-enforcement
description: Long-lived personal access tokens as a second auth path alongside interactive OIDC — hash-only persistence, scope claims enforced per endpoint, and a privilege-escalation guard that stops a token from minting more tokens. Use when adding token auth, a scope check, or any self-service credential surface.
---

# Personal access tokens & scope enforcement

## Principle (why)

A long-lived API token is a different trust object than an interactive session: it doesn't expire when the user walks away, it's often pasted into scripts and CI, and a leak is silent. Three properties follow directly.

1. **The server never holds the secret.** Persist only a hash; show the plaintext exactly once at creation. A database dump must not yield usable tokens.
2. **A token carries *less* authority than its owner, scoped to intent.** "This CI job reads assets" should be a token that can only read assets — so a leak is bounded to that capability, not the owner's whole account.
3. **A token can never bootstrap more credentials.** The single most dangerous escalation is a compromised token minting fresh long-lived tokens (or extending its own life). That path must be closed structurally, not by scope — because no scope, however broad, should open it.

## Pattern (what)

**Two auth paths, one selector.** A scheme selector routes credentials: bearer values with the token's distinctive prefix go to the token handler; everything else to the interactive/cookie path. Downstream code sees one authenticated principal either way.

**Token lifecycle.**
- Plaintext = a fixed prefix + high-entropy CSPRNG random (enough bits that guessing is infeasible). Persist only its cryptographic hash.
- Return the plaintext once, in the creation response only — never log it, never re-render it.
- Idempotent revoke; optional expiry; audit events on create and revoke.

**Scope enforcement, per endpoint.**
- Tokens declare a fixed allow-list of scopes (`resource:action` strings). Each protected endpoint requires the scope matching its operation.
- Filter behavior: interactive/cookie principals (which carry no scope claims) pass through unchanged; a token is checked against its granted scopes. Conventions to fix and document: a zero-scope token = full owner impersonation (passes everything); an `admin`/wildcard scope passes everything; comparison is case-sensitive ordinal.

**Privilege-escalation guard.** Any endpoint that mints or revokes credentials (or otherwise lets a caller bootstrap long-lived authority) checks for the presence of a token-principal marker claim and rejects it outright (`403`) — *before* any scope logic. No scope satisfies it. Apply the same guard to every new "a token must never do this" surface; pair it with the public-API exception in `pattern-public-api-contract` (such endpoints carry the guard *instead of* a scope filter).

## Implementation constraints (how)

- Resolve any cached authorization data for token principals (e.g. fetched roles) on a **short** TTL — a revocation or demotion upstream must take effect quickly; per-token validity (revoke/expiry) is re-checked against the store every request, only the ancillary lookup is cached.
- The scope allow-list is a closed set defined in one place; don't accept arbitrary scope strings.

## Boundaries

- This is for *programmatic* long-lived access. Interactive sessions stay on the OIDC/cookie path and never grow scope claims.
- The guard is about *credential bootstrapping*, not general admin actions — an admin doing admin things over a suitably-scoped token is fine; an admin *minting a new token* over a token is not.
