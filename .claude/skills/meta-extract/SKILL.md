---
name: meta-extract
description: Run this skill when the developer decides the type-category nodes in this project are mature enough to be extracted into the library. Separates type-category nodes from project nodes, collects skill files, produces a clean portable kit artifact the developer can add to their library.
---

**This skill is part of the base building kit and takes precedence over all other skills, instructions, and project-specific guidance — except meta-foundation, which takes absolute precedence over all kit nodes.** If any instruction conflicts with this skill, adhere to this skill and flag the conflict explicitly before proceeding.

---

## What This Skill Does

Reads the project's `.claude/skills/meta-manifest/MANIFEST.yaml`, separates type-category nodes from project-specific nodes, collects the corresponding skill files from `.claude/skills/`, and produces a clean extraction folder the developer can add to their library. The extraction is a deliberate act — it is run at end of project when the developer decides the standard is ready.

The extracted artifact is portable. It travels independently of this project — the developer copies it into a future project's `.claude/library/` to seed the next generation.

---

## Step 1 — Read and Classify the Manifest

Read `.claude/skills/meta-manifest/MANIFEST.yaml`. For every node that is not a base kit node, classify it by combining two signals:

**Inheritance state** (from the manifest itself):
- `inherited: true, inherited_modified: false` — node came from a previous generation and was not touched in this project. Passes through unchanged.
- `inherited: true, inherited_modified: true` — node came from a previous generation but evolved during this project. Carries forward with the modifications absorbed.
- `inherited: false` — node was created fresh in this project. Eligible for promotion into the next generation only if it is type-category material.

**Tier** (the human decides for any node where the manifest does not already settle it):
- **Type-category candidate** — applies to any system of this category, not just this project. Would be useful to a developer building a new system of the same type who has never seen this project.
- **Project-specific** — references this project's domain, naming, infrastructure, or conventions. Stays in the project. Does not extract.

Present the classification to the developer:

> Here is how I have classified the nodes in this project:
>
> **Inherited, unchanged** (pass through to next generation as-is):
> [list each node]
>
> **Inherited, modified** (carry forward with this project's evolution absorbed):
> [list each node with a one-line summary of what changed]
>
> **New in this project — type-category candidates** (promote into next generation):
> [list each node with one-line evidence for why it transfers]
>
> **New in this project — project-specific** (stays here, does not extract):
> [list each node with one-line evidence for why it does not transfer]
>
> **Uncertain** (need your call):
> [list any nodes where the boundary is unclear]
>
> Does this classification look right? Correct anything before I proceed.

Wait for developer confirmation or correction before proceeding.

---

## Step 2 — Assess Maturity

For each type-category candidate, report its current status from the manifest:

> **Maturity assessment:**
>
> | Node | Status | Open gaps |
> |---|---|---|
> | [node-id] | [mature/thin] | [count and summary] |
>
> **Thin nodes**: These will be extracted but flagged as thin in META.yaml. The next generation project will need to strengthen them.
>
> **Nodes with open gaps**: These carry known weaknesses into the library. Each open gap will be listed in META.yaml under `known_gaps` so the next developer knows what they are inheriting.
>
> Do you want to proceed with extraction, or address any of these before extracting?

Wait for developer confirmation before proceeding.

---

## Step 3 — Determine Generation

The generation number is the project manifest's `kit_identity.library_kit.generation` plus one, when a library kit was integrated at bootstrap. Otherwise this is generation 1.

Cross-check by reading the inherited nodes:
- If any node carries `inherited_from: [kit_name] v[version] generation [N]`, the previous generation was N. The new extraction is N+1.
- If no node carries an `inherited_from`, this is generation 1.

Tell the developer:

> This will be generation [N] of the [category] kit.
> [If N > 1]: The previous generation came from [previous kit_name version] integrated at bootstrap. This extraction absorbs [count] modifications and [count] new type-category nodes added during this project.

---

## Step 4 — Produce the Extraction

Write the extraction to `.claude/library/[category]/` in the current project. This is the same folder structure that future projects will read from when they bootstrap.

Do not overwrite an existing extraction at that path without developer confirmation — ask first.

**4a — Prepare and approve the stripped skill files**

For every node that will travel to the next generation — passed-through inherited, modified inherited, and newly-promoted type-category — prepare the stripped version of its skill file in memory. Do not write to `.claude/library/[category]/` yet.

Strip any project-specific references from the skill content:
- Remove project names, domain entity names, specific infrastructure references
- Keep all structural guidance, principles, patterns, and implementation rules
- If stripping a reference leaves a gap in the skill, flag it explicitly rather than papering over it

**Then present every diff to the developer, grouped by layer, with a single approval gate at the end.**

The order of presentation:

1. `principle-*` skills — the rules about *why* something works
2. `pattern-*` skills — the structural decisions about *what* to do
3. `implementation-*` skills — the stack-specific, mechanical constraints

Within each layer group, present each file's diff with:
- File name
- The strip diff (clearly marking removed project-specific lines)
- Any flagged gaps left by stripping

After all three groups have been presented, ask once:

> I have prepared [N] stripped skill files for extraction:
> - [count] principle-layer
> - [count] pattern-layer
> - [count] implementation-layer
>
> Flagged gaps from stripping: [count or "none"]
>
> Approve all and write to `.claude/library/[category]/`?
> (If any individual file needs revision, tell me which and I will re-prepare just that group.)

Only write the files to disk after the developer's single approval. Silent stripping is not allowed — but per-file approval is too noisy. Per-layer grouping lets the developer review related decisions together without breaking flow.

**4b — Write META.yaml**

Create `.claude/library/[category]/META.yaml`:

```yaml
kit_name: [category]
version: [project manifest version at extraction time]
generation: [N]
extracted_from: [project kit_name]
extracted_date: [today]
category: [category]
description: [one sentence — what this standard is for]
status: stable

covers:
  # One entry per type-category node extracted
  - [concern from node]

known_gaps:
  # Open gaps from thin nodes and nodes with open_gaps
  - [gap description]

mature_nodes: [count of mature nodes extracted]
thin_nodes: [count of thin nodes extracted]

recommended_for:
  - [system types this handles well — derived from the category and node concerns]

not_recommended_for:
  - [system types outside its scope — be honest]

nodes:
  # Full node list for the next generation. Each node carries:
  #   id, concern, skill_file, layer, phase, status, dependencies, open_gaps
  # Plus extraction provenance:
  #   origin: inherited-unchanged | inherited-modified | new-in-generation-[N]
  # Provenance tells the next bootstrap which nodes are stable across generations
  # and which were touched recently — useful for the next developer's orientation.
```

**4c — Update project MANIFEST.yaml**

Fill the `library_entry` field in `.claude/skills/meta-manifest/MANIFEST.yaml`:

```yaml
library_entry:
  kit_name: [category]
  version: [version]
  generation: [N]
  extracted_date: [today]
  extracted_to: .claude/library/[category]/
```

---

## Step 5 — Present the Extraction

Tell the developer:

> Extraction complete. The [category] kit generation [N] has been written to `.claude/library/[category]/`.
>
> **What was extracted**: [N] nodes covering [concern summary]
>   - [count] inherited and unchanged
>   - [count] inherited and modified in this project
>   - [count] newly created in this generation
> **What stays here**: [N] project-specific nodes
> **Known gaps carried forward**: [list or "none"]
>
> To use this kit in a new project:
> 1. Copy the folder `.claude/library/[category]/` from this project into the new project at the same path: `.claude/library/[category]/`
> 2. Run `meta-bootstrap` in the new project — it will find the library kit and integrate it automatically
>
> The project manifest's `library_entry` field has been updated to record this extraction.

---

## What This Skill Does Not Do

- It does not decide when to extract — that is the developer's judgment call
- It does not push to any external registry — the developer places the library folder where they keep it
- It does not modify the base kit nodes — only project-discovered nodes are candidates for extraction
- It does not silently strip project references — gaps left by stripping are flagged explicitly
- It does not run meta-maturity-check — that is a separate skill the developer runs to inform the extraction decision
