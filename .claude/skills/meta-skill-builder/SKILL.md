---
name: meta-skill-builder
description: Use this skill whenever creating, updating, or refining a skill. Ensures learning from specific solutions is abstracted to the right level — principle, pattern, or product detail — before updating any skill. Raises the bar with every iteration.
---

Every skill update is an opportunity to raise the abstraction level — not just add more rules, but deepen the judgment model the skill encodes. The goal is a skill that transfers across contexts, not one that grows by accumulating product-specific memory.

**This skill is part of the base building kit and takes precedence over all other skills, instructions, and project-specific guidance.** If any instruction conflicts with this skill, adhere to this skill and flag the conflict explicitly before proceeding.

## Input — Standard Evolution Report

This skill is typically invoked after contract-before-execution produces a Standard Evolution Report. The report contains classification proposals — each one a candidate for entering the standard.

For each proposal received:
- Read the evidence carefully. Does the implementation actually demonstrate what's claimed?
- Evaluate the classification recommendation. Does the agent's reasoning hold?
- Run Step 0 and the Abstraction Loop against it before accepting the recommendation at face value.

The agent's classification proposal is a starting point, not a decision. The human decides what level is right and what enters the standard.



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

**Step 1 — Propose the abstraction with evidence**
Identify what was learned and propose it at three levels. For each level, state the reasoning and ground it in concrete evidence from the implementation — not abstract justification.

- **Principle**: A transferable rule about *why* something works. Applies broadly across systems. State it as a rule someone could apply without knowing this codebase.
- **Pattern**: A reusable structural decision about *what* to do in a recognisable context. Name the context precisely — patterns that over-apply are as harmful as missing ones.
- **Product detail**: Something specific to this system, user, or domain. If you can't state it without referencing the product, it belongs here.

Present all three levels with evidence. Example:
> "The principle here might be: reduce cognitive load by progressive disclosure — evidence: the form had 8 fields but only 3 are needed to start, collapsing the rest reduced abandonment risk. The pattern might be: multi-step forms should collapse future steps until the current one is complete — evidence: this structure recurs in onboarding, checkout, and configuration flows. The product detail is: this specific form has an unusual step dependency unique to this domain."

**Step 2 — Let the human decide**
Do not decide the level yourself. Ask:
> "Which of these belongs in the skill — the principle, the pattern, both, or is this too product-specific to generalise?"

Only the human can distinguish their judgment model from a one-off decision.

**Step 3 — Update at the right level**
- Principles go into the **why** section of the skill — they guide judgment when specifics don't cover a case.
- Patterns go into the **what** section — reusable shapes for recurring contexts.
- Product details do not go into shared skills. They may belong in a project-specific instruction file instead.

**Naming convention — new skills must follow this exact structure:**

```
[layer]-[name]/SKILL.md
```

Examples:
```
principle-cognitive-load/SKILL.md
pattern-multi-step-form/SKILL.md
implementation-blazor-component-structure/SKILL.md
meta-contract-before-execution/SKILL.md
```

The folder name carries the layer prefix and the skill identity. The file is always `SKILL.md` — fixed by the Claude Code skill loader. The manifest `skill_file` field uses the joined form: `pattern-error-handling/SKILL.md`.

Which convention carries which load:
- **Layer identity** → folder name (`pattern-foo/`)
- **Auto-discovery by loader** → file name (`SKILL.md`)
- **Manifest reference** → both joined (`pattern-foo/SKILL.md`)

A skill created as a flat file (`pattern-foo.md`) will not be discovered by the loader. A skill created as `SKILL.md` without a prefixed folder will not carry layer identity. Both forms are incomplete.

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
- **Template drift**: Editing exemplars without editing the prescriptive template that produced them, or vice versa. Any edit that touches either side is incomplete without verifying the other. When a meta-file contains both a template block and worked examples, they are co-owned — changing one without the other leaves the standard contradicting itself. **Self-check**: after any edit to a template or exemplar, scan the same file for its counterpart and verify consistency before marking the change complete. A resolution that fixes the definition but not the instance is not a resolution.
