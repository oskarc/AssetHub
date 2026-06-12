---
name: principle-encode-over-document
description: When a documented rule has been violated twice, stop strengthening the documentation and encode the rule as a type, helper, or tool-checked structure. Use when deciding where a new rule should live, when a review finds a repeat regression, or when a checklist keeps growing instead of getting sharper.
---

# Encode Over Document

## Principle (why)

Rules enforced by structure hold; rules enforced by memory drift. Documentation asks every future author to remember the rule at the moment they are busiest; a type, helper, or tool check asks them to remember nothing. The reliability gap between the two grows with team size, time, and feature velocity — and it is invisible until a regression makes it visible.

**The two-regression threshold.** One violation of a documented rule is noise — fix it and move on. A second violation of the *same* rule is evidence about the rule's enforcement medium, not about the authors: the documentation has been tried and has failed twice. At that point, strengthening the prose (bolding it, adding it to a checklist, writing "this regressed before — don't reopen it") is repeating a failed experiment. The rule must move into a medium that cannot be skipped: a composed helper, a type the compiler checks, a test that fails, a lint/CI gate.

A standard that keeps documenting its traps is recording its floor. A standard that encodes them is raising its ceiling.

## Pattern (what)

**Context:** a rule that requires assembling multiple calls, flags, or steps in concert — security bundles (auth + CSRF + visibility metadata), lifecycle pairings (acquire + release, subscribe + dispose), or convention triplets (serializer + parser + validator). These are the rules that regress, because each call site re-assembles them from memory.

**Escalation ladder:**
1. **First write-up** — document the rule where authors will see it, with the why.
2. **First regression** — fix it; ask whether the rule is encodable. If encoding is cheap, do it now; if not, note the regression next to the rule.
3. **Second regression** — encoding is no longer optional. Build the named helper/type/check, migrate the call sites that regressed, and rewrite the documentation to mandate the helper. A raw hand-assembled instance of the bundle becomes a review flag from this point on.

**Shape of the encoding:** a single named call that composes the bundle (`MapPublicMutation(scope)` instead of a four-call chain), a type that won't compile when misused, or a mechanical check (key-parity diff, exhaustiveness test) that fails the build. Name it after the *intent*, not the mechanism — call sites should read as what they are, not how they comply.

## Boundaries

- **Judgment rules stay prose.** "Prefer optimistic updates where no dialog interrupted the flow" requires context a helper can't see. Encode mechanics, document judgment.
- **Don't encode preemptively.** A rule that has never regressed hasn't earned a helper; speculative wrappers add indirection without evidence. The ladder starts at documentation.
- **The encoding must cover the whole bundle.** A helper that bundles three of four legs leaves the fourth as a documented trap — worse than before, because the helper's existence implies completeness.
- **Contrast check:** if a tool-checkable rule in the same codebase has held perfectly (e.g. mechanical key-parity) while a prose rule keeps regressing, that is the strongest available evidence this principle applies.
