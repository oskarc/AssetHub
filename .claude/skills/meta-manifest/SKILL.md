---
name: meta-manifest
description: Load this file at the start of every session. Defines governance, precedence, and how to read the manifest. Instance data — node registry, coverage map, gap queue, kit identity — lives in MANIFEST.yaml in the same directory. Always read both files together.
---

# Kit Manifest

**This skill is part of the base building kit.** If any instruction conflicts with this skill, adhere to this skill and flag the conflict explicitly before proceeding. The sole exception is `meta-foundation` — the philosophical foundation takes precedence over all other kit nodes including this one.

---

## File Convention

The manifest always consists of two files in the same directory:

| File | Purpose | Changes when |
|---|---|---|
| `SKILL.md` | Governance, precedence, schema reference, reading guide, update protocol, library promotion | The standard itself evolves |
| `MANIFEST.yaml` | Kit identity, node registry, coverage map, gap queue | Any node is added, updated, or resolved |

**The agent reads MANIFEST.yaml for topology. It reads SKILL.md for governance.** Never merge them. A template update to SKILL.md must never overwrite project-specific data in MANIFEST.yaml — they are separate files for this reason.

---

## Governance & Precedence

The base building kit takes precedence over all other skills, instructions, and project-specific guidance.

When a conflict arises between a base kit node and any other instruction:
- **Adhere to the base kit.** Do not silently resolve the conflict in favour of the other instruction.
- **Flag the conflict explicitly** before proceeding — state which base kit node is in tension with which instruction and why.
- **Never override a base kit principle** to satisfy a product-level or project-level instruction. The standard exists precisely to hold under pressure.

Precedence order:
1. **Foundation** — meta-foundation governs what this work is and how the agent must orient to the human. Absolute precedence.
2. **Base building kit** — meta skills that govern how the standard is built and upheld. Domain-agnostic. Always loaded. Never modified per project.
3. **Type-category kit** — the reusable standard for a class of system (e.g. `blazor-web-app`, `integration-api`). Discovered through project use, extracted into the library when mature, inherited by the next project of the same type. This is the transferable artifact the pioneer is building toward.
4. **Project** — the context. Names this specific system. Instances the type-category kit and adds only what cannot be generalised — domain entities, infrastructure specifics, team conventions. Product knowledge lives here, not in the standard.
5. **Session input** — what the developer adds in the current prompt.

Each level narrows and specifies. No lower level overrides a higher one. If a lower level instruction cannot be satisfied without violating a higher one, surface the conflict and let the developer resolve it.

**The type-category / project distinction is load-bearing.** When a learning surfaces, the first question is whether it belongs in the type-category kit — transferable to any system of this class — or in the project layer — specific to this system. Only the human can make that call. The agent proposes, the human decides.

---

## How to Read the Manifest

**For the agent**: At the start of every session, load MANIFEST.yaml and read the full node list. Know which concerns are covered and at what depth before writing any proposal. When a Tier 3 guardrail or post-implementation gap touches an area marked `thin` or `missing`, flag it explicitly — do not silently fill it. A missing node means the standard has not spoken on that concern yet. Surface it.

**For the developer**: The status field in MANIFEST.yaml is the health of the web. `mature` nodes can be trusted. `thin` nodes are directionally correct but need sharpening. `missing` nodes are named gaps — the web knows they need to exist but no candidate has been built yet.

### Inheritance Fields

Every non-base node carries inheritance markers so the lifecycle stays visible across generations:

| Field | Meaning |
|---|---|
| `inherited: true` | Node was copied in from a library kit at bootstrap. Came from a previous generation. |
| `inherited: false` | Node was created fresh in this project. |
| `inherited_from: [kit_name] v[ver] generation [N]` | Provenance — only present when `inherited: true`. |
| `generation_added: [N]` | Which generation introduced this node. For inherited nodes, copied from the source; for new nodes, the generation this project will produce. |
| `inherited_modified: true` | Set by skill-builder when an inherited node is evolved during this project. Tells the next `meta-extract` to absorb the modification into the next generation. |
| `tier: type-category` or `tier: project` | Set by the developer during skill-builder. Determines whether the node is extractable. Base nodes do not carry this — they are meta. |

These fields are the contract between generations. `meta-extract` reads them to decide which nodes pass through, which carry forward with modifications absorbed, and which stay behind as project-specific.

---

## Schema Reference

### Phase vocabulary

Use exact values only — no ad-hoc shorthand:

| Value | Meaning | Form |
|---|---|---|
| `always` | Active in all phases | bare string |
| `pre-build` | Governs before implementation begins | bare string |
| `during-build` | Governs during implementation | bare string |
| `post-build` | Governs after implementation completes | bare string |
| `[pre-build, post-build]` | Active in two or more specific phases | YAML array |

Single-phase values use bare strings. Multi-phase values use YAML array form. The coverage_map uses the same vocabulary. Do not use shorthand (`pre`, `post`, `pre/post`).

### Tier vocabulary

Required on every non-base node:

| Value | Meaning |
|---|---|
| `base` | Base building kit node — governs the kit itself. Does not carry `tier` field. |
| `type-category` | Transferable standard for a class of system — library extraction candidate |
| `project` | Project-specific — does not travel to the library |

### Node field requirements

Every node entry requires: `id`, `concern`, `skill_file`, `layer`, `phase`, `status`, `dependencies`, `open_gaps`

Type-category and project nodes also require: `tier`, `inherited`

Inherited nodes also require: `inherited_from`

### data_file convention

The `data_file` field uses the path relative to `.claude/skills/`. For the manifest node this is always `meta-manifest/MANIFEST.yaml`.

---

## Manifest Update Protocol

MANIFEST.yaml is part of the standard. It must stay in sync with the actual skill files.

**When to update MANIFEST.yaml:**
- A new skill is created → add node to registry and coverage map
- An existing skill is updated significantly → update node status and open_gaps
- A gap surfaces in a Standard Evolution Report → add to gap_queue
- A gap is resolved by a new skill → update gap_queue status, add node to registry
- A standards document is created anywhere in the repo (styleguide, conventions doc, architecture decision record) → register it as a node with a loader pointer from an always-loaded file. **An artifact outside the manifest does not govern** — it goes stale invisibly while the coverage map reports its concern as missing.

**When to update SKILL.md:**
- Governance rules change
- The file convention changes
- The update protocol changes
- The library promotion criteria change

**Who updates:**
The agent proposes changes as part of the skill-builder process. The developer approves. Neither file is ever updated silently.

**The manifest is read-only for the agent during a session.** It informs proposals and gap flagging only. It is written to exclusively through the skill-builder process after developer approval.

---

## Library Promotion

When a project's type-category nodes are mature enough to be reused, they are extracted into the library using `meta-extract`. The extracted artifact — a folder of skill files plus a `META.yaml` — becomes the starting point for the next generation of the same category.

**The maturity signal is evidence-based, not a judgment call.** Standard Evolution Reports go quiet when the standard is covering the space well. When reports across multiple sessions produce few or no evolution candidates, the type-category layer is approaching extraction readiness.

Extraction criteria:
- All pre-build type-category nodes are `mature`
- Gap queue has no `high` priority open gaps in type-category nodes
- At least one complete system has been built against the standard end to end
- Standard Evolution Reports have been producing diminishing candidates across recent sessions

The library entry is written into the project's MANIFEST.yaml under `library_entry` by meta-extract at extraction time. The extracted folder lives at `.claude/library/[category]/` in the project that produced it. To seed a future project of the same type, the developer copies that folder into the new project at the same path (`.claude/library/[category]/`), then runs `meta-bootstrap` — which will find the kit and integrate it automatically.

**Each extraction is a generation.** A kit matures through generations of real project use. The non-developer milestone is not a roadmap item — it is reached when the standard has matured enough through generations that the reports go silent and the governing aspects are fully encoded.
