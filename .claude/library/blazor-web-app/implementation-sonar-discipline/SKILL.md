---
name: implementation-sonar-discipline
description: When a static-analysis suppression is legitimate versus when it's hiding a real problem — the four conditions a suppression must meet, the smallest-scope rule, and the requirement that every suppression carry a recorded reason. Use when adding or reviewing any analyser suppression (NOSONAR, SuppressMessage, NoWarn, lint-disable).
---

# Static-analysis suppression discipline

## Principle (why)

A suppression is a documented engineering decision, not a way to make a warning go away. The analyser exists to catch real problems; every suppression silences it for one spot, so each one must justify why *this* spot is a false positive rather than a real issue. Undocumented suppressions rot: six months later nobody knows whether the rule was wrong or the code was, and the next person either cargo-cults the suppression onto new code or wastes time re-litigating it. The discipline keeps the analyser trustworthy — a clean report means clean code, not a pile of silent overrides.

## Pattern (what)

**Suppress only when all four hold:**
1. **The rule's *behaviour* is satisfied even though its *syntax* isn't** — the thing the rule protects is actually true here, the analyser just can't see it (e.g. a field IS read, but through a template the C# analyser doesn't follow; an API genuinely has no async variant on this platform; a shell function terminates via `exit` where the rule wants `return`).
2. **The "fix" would make the code worse** — unreachable statements, a parameter-holder that just relocates a count without reducing it, a dead branch.
3. **Smallest possible scope** — a line-level inline suppression over a file-level pragma; a member attribute over a project-wide rule disable. Never silence a rule globally to quiet one site.
4. **A reason is recorded inline** — a `Justification = "..."` argument or a one-line comment after the suppression marker. No bare suppression ships.

**Suppression is *wrong* when the rule is right.** If it fires because the behaviour is genuinely off — a real unread field nobody uses, a real empty catch swallowing a real exception, a real complexity score meaning the method is too long — fix the code. The rule found a bug; suppressing it ships the bug.

**Cluster check.** Legitimate suppressions tend to cluster around a few structural realities of the stack (composition-root constructors with many dependencies, template-bound fields the analyser can't trace, platform APIs lacking an async form). Maintain a short catalog of the *known* clusters and their standing reasoning so they aren't relitigated — and treat a *new* cluster that doesn't match any of them as a design smell: a recurring suppression in fresh code usually means the design is fighting the rule, so push back on the design before adding the suppression.

## Boundaries

- This governs *suppressions*, not the rules themselves. Turning a rule off project-wide because it doesn't fit the codebase is a different (rarer, higher-bar) decision than suppressing it at a justified site.
- "Behaviour satisfied, syntax not" is the load-bearing test. If you can't articulate why the rule's intent is met despite the warning, you don't have a suppression — you have a bug to fix.
