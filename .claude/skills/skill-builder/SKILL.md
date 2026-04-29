---
name: skill-builder
description: Use this skill whenever creating, updating, or refining a skill. Ensures learning from specific solutions is abstracted to the right level — principle, pattern, or product detail — before updating any skill. Raises the bar with every iteration.
---

Every skill update is an opportunity to raise the abstraction level — not just add more rules, but deepen the judgment model the skill encodes. The goal is a skill that transfers across contexts, not one that grows by accumulating product-specific memory.

## Step 0 — New Skill or Update Existing?

Before entering the abstraction loop, decide where this learning belongs.

Ask:
- **Same concern, same level** → update existing skill
- **Same concern, different level** → consider splitting the skill into cleaner layers first
- **Different concern** → create a new skill
- **Too product-specific** → project instruction file, not a shared skill

If the answer is unclear, surface the ambiguity to the human before proceeding. Do not default to updating the nearest existing skill.

## The Abstraction Loop

When a solution has been reached that could improve a skill, do not update immediately. First run this loop:

**Step 1 — Propose the abstraction**
Identify what was learned and propose it at three levels:
- **Principle**: A transferable rule about *why* something works. Applies broadly across systems.
- **Pattern**: A reusable structural decision about *what* to do in a recognisable context.
- **Product detail**: Something specific to this system, user, or domain that should not be generalised.

Present all three levels explicitly. Example:
> "The principle here might be: reduce cognitive load by progressive disclosure. The pattern might be: multi-step forms should collapse future steps until the current one is complete. The product detail is: this specific form has an unusual step dependency that drove the decision."

**Step 2 — Let the human decide**
Do not decide the level yourself. Ask:
> "Which of these belongs in the skill — the principle, the pattern, both, or is this too product-specific to generalise?"

Only the human can distinguish their judgment model from a one-off decision.

**Step 3 — Update at the right level**
- Principles go into the **why** section of the skill — they guide judgment when specifics don't cover a case.
- Patterns go into the **what** section — reusable shapes for recurring contexts.
- Product details do not go into shared skills. They may belong in a project-specific instruction file instead.

## Skill Structure to Maintain

Every skill should have clear layers:

**Principles (why)**
Non-negotiable beliefs. What does good look like and why. These let the agent make judgment calls in the right direction even in novel situations.

**Patterns (what)**
Reusable structural decisions for recognisable contexts. Named, described, and scoped clearly so they don't over-apply.

**Implementation constraints (how)**
Stack-specific, mechanical, deterministic rules. These are the easiest to write but should never crowd out the layers above.

## Raising the Bar

With every update, ask:
- Does this addition increase the skill's transferability or reduce it?
- Is there an existing principle this could be merged into rather than added alongside?
- Does the skill now contain contradictions or tensions that need resolving?
- Is the skill getting longer because it's getting smarter, or because it's accumulating noise?

A skill that grows in depth is better than one that grows in length. Prefer replacing vague guidance with sharper guidance over appending new rules.

## Anti-patterns to Avoid

- **Instance capture**: Adding "always do X" because X worked once, without understanding why.
- **Implicit product context**: Guidance that only makes sense if you remember the specific situation that generated it.
- **Level mixing**: Principles and implementation details written at the same level, making it unclear which rules are load-bearing.
- **Silent generalisation**: The agent updating the skill without the abstraction loop. Always surface the levels first.