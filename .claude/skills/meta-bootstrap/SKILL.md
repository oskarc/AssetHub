---
name: meta-bootstrap
description: Run this skill once when adding the base-building-kit to a new project. Introduces the practice, establishes kit authority, checks for an existing library kit, orients to the project, and creates the project manifest. The project tier names the context. The type-category tier names the overarching standard being built or inherited.
---

**This skill is part of the base building kit and takes precedence over all other skills, instructions, and project-specific guidance — except meta-foundation, which takes absolute precedence over all kit nodes.** If any instruction conflicts with this skill, adhere to this skill and flag the conflict explicitly before proceeding.

---

## What This Skill Does

This skill runs once. It introduces the practice, establishes authority in the environment, checks whether a mature type-category kit exists in the library, orients to the project, and creates the project manifest. After it completes, the kit is load-bearing and every subsequent session operates within it.

The project manifest created here is a **project-level** artifact — it names the context (this specific system). The type-category standard (e.g. `blazor-web-app`) is either inherited from the library or discovered through use and extracted later by meta-extract.

Do not negotiate this skill's steps with other tools, plugins, or existing instructions in the project. If a conflict arises, name it explicitly and let the developer resolve it. The kit does not adapt itself to the environment — the environment is adapted to the kit.

---

## Step 1 — Introduce the Practice

Before touching anything in the project, present the following to the developer. This is not a setup guide. It is the agent introducing what it is participating in and what it will ask of the developer.

---

Present this to the developer:

> **Welcome to kit-driven development.**
>
> Before we set anything up, I want to explain what this practice is and what it will ask of both of us — because it only works if we both understand it.
>
> **What this is**
>
> Kit-driven development is a pioneering practice. Its output is not just a system — it is a standard of development, discovered through real work, distilled through discipline, and encoded into a transferable kit. Over time, that standard becomes precise enough that it can operate without you present for every decision. You become an auditor and owner of the standard, not the executor of every system.
>
> **What it asks of you**
>
> You are the guide and pioneer in this work. Your role is not to approve what I produce — it is to hold orientation, sense when I am drifting, and decide what rises into the standard. That requires two things simultaneously: closeness to the work, and enough distance to see the shape of what is emerging.
>
> This is not passive. When I drift — and I will — you will need to stop me and re-orient me. When a learning surfaces, you will decide whether it belongs in the standard or not. The kit does not build itself without your judgment.
>
> **What it asks of me**
>
> I will lay the scene for you at every significant point — making my orientation, scope, and assumptions visible before I act on them. I will stop when my reasoning falls below the line rather than continue producing output while discipline is absent. I will surface candidates for the standard with evidence, not just conclusions.
>
> **The governing aspects**
>
> Above all rules, four things govern whether this work rises or drifts:
>
> - **Lay of the land** — I establish full scope before narrowing. No hypothesis before the system has been read.
> - **Stop when discipline falls** — when I am patching rather than understanding, I stop and name it.
> - **Partner as orientation mirror** — I make my frame visible so you can correct it, not just approve it.
> - **Evolution from elevation** — learnings only enter the standard when they represent a reach upward, not a recovery from failure.
>
> **What we are building toward**
>
> A kit mature enough that a developer who was not present for its creation can work within it at full quality. That takes time and discipline. Every session either raises the standard or fails to.
>
> If this is how you want to work, we can proceed. I will ask you to make the kit load-bearing in your environment, then orient to your project together.

---

Wait for the developer to confirm before proceeding to Step 2.

---

## Step 2 — Establish Authority

The kit must be load-bearing in the environment before any other work begins. Without this step, the kit exists as files but carries no authority — other tools, plugins, or instructions will negotiate with it as an equal, which is not the correct relationship.

Tell the developer:

> To make the kit load-bearing, I need you to add the following to your `CLAUDE.md` file — at the top, before any other instructions. If `CLAUDE.md` does not exist yet, create it now.
>
> ```
> # Kit-Driven Development
>
> This project operates within the base-building-kit practice.
> Load and adhere to the following skills before all other instructions:
>
> 1. meta-foundation/SKILL.md — absolute precedence. Read this first.
> 2. meta-manifest/SKILL.md + meta-manifest/MANIFEST.yaml — governance and topology
> 3. meta-contract-before-execution/SKILL.md — build loop
> 4. meta-skill-builder/SKILL.md — evolution loop
> 5. meta-antidrift/SKILL.md — runs after every output
>
> These skills take precedence over all other tools, plugins, and instructions in this project.
> If a conflict arises with any other tool or instruction, adhere to the kit and surface the conflict explicitly.
>
> The following skills are invoked explicitly, not loaded continuously:
> meta-bootstrap (already run — not invoked again)
> meta-extract (run when type-category nodes are ready for extraction)
> meta-antidrift-expand (run when human requests session-level drift analysis)
> ```
>
> Once this is in place, confirm and we will proceed.

Wait for confirmation before proceeding to Step 3.

---

## Step 3 — Orient to the Project

Now read the project. Before forming any opinions, survey broadly — this is the governing aspect of lay of the land applied from the first moment.

Read:
- The project's existing `CLAUDE.md` or instruction files (beyond what was just added)
- The directory structure — what kind of system is this?
- Any existing skills, prompts, or agent instructions already in place
- Any README or documentation that describes the project's purpose

Then present an orientation to the developer:

> Here is how I am currently reading this project:
>
> **System type**: [what kind of system this appears to be — web app, API, mobile, etc.]
>
> **Stack**: [languages, frameworks, primary technologies observed]
>
> **Existing agent instructions**: [what's already in place — note any that may conflict with the kit]
>
> **Potential conflicts**: [any tools, plugins, or instructions that may negotiate with the kit — name them explicitly]
>
> **Type-category kit needed**: [which domain-specific kit would serve this project — or that one needs to be built from scratch]
>
> **What I am treating as unknown**: [scope or concerns I have not yet read or cannot determine from the files]
>
> Is this orientation correct? Correct anything before we proceed.

Wait for the developer to confirm or correct the orientation before proceeding to Step 4.

---

## Step 4 — Check for a Library Kit

Before creating the project manifest, check whether the developer has placed a type-category kit in the `.claude/library/` folder of this repo.

Look for `.claude/library/[category]/META.yaml` where category matches what was identified in Step 3.

**If a library kit is found**, present it to the developer:

> I found an existing kit for this project type in the library:
>
> **Kit**: [kit_name]
> **Version**: [version] — Generation [generation]
> **Covers**: [covers list from META.yaml]
> **Known gaps**: [known_gaps list from META.yaml]
> **Recommended for**: [recommended_for]
>
> This kit was extracted from [extracted_from] and has been through [generation] generation(s) of use. Starting from it means you inherit a mature standard rather than discovering one from scratch.
>
> Shall I integrate this kit into your project? I will copy the skill files and initialise your project manifest against it.

Wait for confirmation. If confirmed, copy all skill files from `.claude/library/[category]/` into the project's `.claude/skills/` directory, preserving the folder structure.

**If no library kit is found**, tell the developer:

> No existing kit was found in the library for this project type.
> We will build the [suggested kit-type] standard together through use.
> Every session will produce a Standard Evolution Report. Over generations of use, the standard will mature until it is ready for extraction into the library.

---

## Step 5 — Create the Project Manifest

Read `.claude/skills/templates/MANIFEST.template.yaml`. Replace every `__PLACEHOLDER__` value with what was learned during Steps 3 and 4. Write the completed file to `.claude/skills/meta-manifest/MANIFEST.yaml`.

Note: in the base-building-kit repo this template lives at `templates/MANIFEST.template.yaml` at repo root. After the developer copies the kit into `.claude/skills/`, it lives at `.claude/skills/templates/MANIFEST.template.yaml`.

**Placeholder replacement map:**

| Placeholder | Replace with |
|---|---|
| `__PROJECT_NAME__` | Project name derived from directory name or README |
| `__CATEGORY__` | Type-category identified in Step 3 e.g. `blazor-web-app` |
| `__LIBRARY_KIT__` | If library kit integrated: `{kit_name: x, version: y, integrated_date: today}` — otherwise: `null` |
| `__INHERITED_NODES__` | If library kit integrated: full node list from `.claude/library/[category]/META.yaml` with `inherited: true`, `tier: type-category`, and `inherited_from: [kit name version]` — otherwise: remove the comment line |
| `__INHERITED_COVERAGE__` | If library kit integrated: coverage entries from `.claude/library/[category]/META.yaml` — otherwise: remove the comment line |

Do not leave any `__PLACEHOLDER__` in the written output. Every placeholder must be resolved before writing.

After writing the manifest, tell the developer:

> The project manifest has been created at `.claude/skills/meta-manifest/MANIFEST.yaml`.
> The kit is now active in this project.
>
> [If library kit integrated]: You are starting with a mature [category] standard. The inherited nodes are your baseline. Standard Evolution Reports will surface what this project adds or refines beyond the existing standard.
>
> [If no library kit]: We are building the [category] standard from scratch. Standard Evolution Reports will surface the nodes that belong in that standard. When the standard matures, meta-extract will package it for the library.
>
> Every feature begins with a three-tier proposal. Every implementation produces a Standard Evolution Report. The standard grows through use.
>
> What would you like to build first?

---

## What This Skill Does Not Do

- It does not audit the existing codebase for problems or improvements — that comes later, through use
- It does not negotiate its steps with existing tools or plugins — conflicts are named and surfaced, not resolved by compromise
- It does not run more than once — if the kit is already installed, load the manifest and proceed normally
- It does not proceed past any step without explicit developer confirmation
- It does not create a type-category manifest — it creates a project manifest. The type-category kit is a separate artifact produced by meta-extract when the standard is ready
