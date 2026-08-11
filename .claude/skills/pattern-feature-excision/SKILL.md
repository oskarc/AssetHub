---
name: pattern-feature-excision
description: Remove a shipped feature completely — with a regression baseline as the before/after frame, the compiler as the completeness authority, a residual sweep as the acceptance gate, and the standard's prose corrected in the same change. Use when deleting a feature, subsystem, or integration whose absence must leave every remaining flow byte-for-byte intact. The cut-side mirror of "no half-implemented features": no half-deleted ones either.
---

# Feature excision

## Principle (why)

A removal has the same completeness bar as an addition, inverted. A half-deleted feature — an orphaned resource key, a dead endpoint, a stale sentence in the standard claiming the feature exists — is drift wearing a cleanup's clothes. And a removal has one risk an addition doesn't: it must prove a *negative* — that nothing else moved. That proof cannot come from the survey that planned the cut (surveys truncate, names evade greps); it comes from the compiler, the test baseline, and a scoped residual sweep, in that order.

## The sequence (what)

1. **Roll the regression baseline forward** to the current green commit — after checking the gate's own preconditions (for a Testcontainers-backed suite: `docker info`; a runtime-down result is an abort, never data — see `pattern-regression-baseline`).
2. **Survey to plan, never to gate.** Inventory named files, grep the touchpoints untruncated, check for public-API markers (a marked endpoint makes the cut a SemVer event to surface), verify each Add migration is EF-discoverable before choosing plain drops. The survey sizes the contract; it is not the completeness authority.
3. **Classify every touchpoint into one of three cut sub-cases** (they have different verification bars — see below).
4. **Delete the named set**, then apply touchpoint edits.
5. **Build-as-authority loop.** Compile; the error list *is* the straggler enumeration — files whose names evaded the survey, emptied namespaces, mocks in shared tests. Iterate to zero errors, then run a warning census against the baseline build: warnings at lines the diff touched are the cut's own orphans (unused helpers, dangling conditionals) — fix, don't suppress.
6. **Migration** dropping the feature's schema, audited per the migration standard, model-sync clean.
7. **Residual sweep as the acceptance gate**: case-insensitive grep of the feature's terms over `src`, `tests`, `docs`, `docker`, and config — the canonical scope; narrower scopes are how escapes happen (a removed feature living on in the use-case catalog or the instructions file). Survivors must belong to a named class (below); any other hit fails the gate.
8. **Correct the standard's prose in the same change** (the Stale-standard rule): project instructions, use-case catalog, memory entries → historical, manifest notes on kit nodes whose instantiation the cut removed.
9. **Commit** with the verification evidence in the message: baseline diff (zero regressions; removed tests all feature-named), warning delta, sweep verdict.

## The three cut sub-cases (classify per touchpoint, up front)

- **Bounded-module deletion** — files that exist only for the feature. Verification: absence (build + sweep).
- **Revert-to-simpler** — code where the feature *changed* existing behavior and removal restores the older, simpler behavior (a conditional download path collapsing back to one branch). Verification: **behavioral** — the restored behavior's tests, updated assertions included; absence checks are not enough.
- **Cross-cutting detachment** — a dependency injected across many consumers (an event publisher with N call sites). Five-part checklist per site: constructor parameter, call sites, helper methods that existed only to build its payloads, suppression-justification texts naming it, test mocks. The bar: every surrounding flow's tests stay green **untouched** (except removing the mocks) — a behaviorally-changed test here is a stop-and-surface, not an edit.

## Predictable straggler classes (the build will find them; know them anyway)

- **Emptied namespaces**: deleting a folder's last file kills its namespace — `_Imports` / global usings break.
- **Payload-only helpers and guard flags**: methods and locals that existed solely to feed the removed calls.
- **Dangling conditionals**: line-level deletion leaving an `if` whose body was the deleted statement.

## Survivor taxonomy (sweep triage is a checklist, not judgment)

1. **Historical-data labels** — resource keys that render event types still present in stored rows (audit logs). They label data, not the feature; they stay.
2. **Same-word false positives** — the feature's name used in unrelated senses (an infra default credential, an idiom, a high-water-mark variable, an app-name "branding" test).
3. **Migration history** — never edited; the removal ships a *new* migration.
4. **Shared helpers with remaining users** — a guard or utility the feature used but did not own stays until its last consumer goes; verify remaining usage by grep, not recollection.

## Boundaries

- Data remnants outside the codebase (object-storage blobs, IdP accounts, historical DB rows) are **named in the contract as accepted remnants with an owner note** — not silently left, not swept by the code cut.
- Scope guard: adjacent findings get reported, not fixed; an excision contract's diff contains the cut and nothing else.
- This pattern removes; it does not decide *what* to remove — that judgment (identity, plan, contract) sits upstream.
