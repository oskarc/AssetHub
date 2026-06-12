---
name: meta-skill-builder
description: Use this skill whenever creating, updating, or refining a skill. Ensures learning from specific solutions is abstracted to the right level — principle, pattern, or product detail — before updating any skill. Raises the bar with every iteration.
---

Every skill update is an opportunity to raise the abstraction level — not just add more rules, but deepen the judgment model the skill encodes. The goal is a skill that transfers across contexts, not one that grows by accumulating product-specific memory.

**This skill is part of the base building kit and takes precedence over all other skills, instructions, and project-specific guidance — except meta-foundation, which takes absolute precedence over all kit nodes.** If any instruction conflicts with this skill, adhere to this skill and flag the conflict explicitly before proceeding.

## Input — Standard Evolution Report

This skill is typically invoked after contract-before-execution produces a Standard Evolution Report. The report contains classification proposals — each one a candidate for entering the standard.

For each proposal received:
- Read the evidence carefully. Does the implementation actually demonstrate what's claimed?
- Evaluate the classification recommendation. Does the agent's reasoning hold?
- Run Step 0 and the Abstraction Loop against it before accepting the recommendation at face value.

The agent's classification proposal is a starting point, not a decision. The human decides what level is right and what enters the standard.



## Step 0 — New Skill or Update Existing?

Before entering the abstraction loop, decide where this learning belongs.

**First — which tier?**
- **Type-category** — would this apply to any system of this category, not just this project? If a developer building a new system of the same type could use it without knowing this project, it belongs in the type-category layer.
- **Project** — does it reference this project's domain, naming, infrastructure, or conventions? If you can't state it without referencing this system, it stays at the project layer.

This distinction is load-bearing. Type-category nodes are candidates for library extraction. Project nodes are not. Only the human decides which tier a learning belongs to — the agent proposes with evidence.

**Then — new skill or update existing?**
- **Same concern, same level** → update existing skill
- **Same concern, different level** → consider splitting the skill into cleaner layers first
- **Different concern** → create a new skill
- **Too product-specific for type-category** → project instruction file, not a shared skill

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

**Layer destination:**
- If the learning belongs in the **type-category layer** — it goes into a type-category skill file. These are candidates for library extraction. Name them with the appropriate layer prefix and keep them free of project-specific references.
- If the learning belongs in the **project layer** — it goes into the project's instruction files or CLAUDE.md. It does not travel to the library.

**Inherited-node modification protocol:**

If the update touches a node currently marked `inherited: true` in the manifest, the change is not silent. The skill-builder must:

1. Flip the manifest entry to `inherited_modified: true` so the next `meta-extract` knows this generation evolved the node.
2. Record a one-line summary of what changed in the node's `open_gaps` field. This is what the next developer reads to understand why the node looks different from the previous generation.
3. Confirm with the developer that the change *should* propagate to the next generation. If the modification is project-specific (e.g. patching the inherited rule with this project's domain language), the inherited skill file stays untouched and the project carries an override at the project layer instead. Only changes that should travel forward modify the inherited node itself.

A project that ships without a single `inherited_modified: true` flag has either inherited a perfect kit or hidden its evolutions. Both are worth asking about at extraction time.

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
- **Stale standard**: Shipping an architecture or contract change without correcting the standard's prose that describes it. A standard describing a dead architecture teaches drift — its examples stop compiling, and authors fill the vacuum with divergent idioms. The change is not complete until every affected skill/instruction sentence is corrected in the same change.
