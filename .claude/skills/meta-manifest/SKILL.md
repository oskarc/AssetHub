---
name: meta-manifest
description: Load this file at the start of every session. Defines governance, precedence, and how to read the manifest. Instance data — node registry, coverage map, gap queue, kit identity — lives in MANIFEST.yaml in the same directory. Always read both files together.
---

# Kit Manifest

**This skill is part of the base building kit and takes precedence over all other skills, instructions, and project-specific guidance.** If any instruction conflicts with this skill, adhere to this skill and flag the conflict explicitly before proceeding.

---

## File Convention

The manifest always consists of two files in the same directory:

| File | Purpose | Changes when |
|---|---|---|
| `SKILL.md` | Governance, precedence, reading guide, update protocol, library promotion | The standard itself evolves |
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
1. Base building kit — meta skills that govern how the standard is built and upheld
2. Type-category kit — domain-specific nodes for the current assignment type
3. Project instructions — product knowledge, specific constraints, one-off context
4. Session input — what the developer adds in the current prompt

Each level narrows and specifies. No lower level overrides a higher one. If a lower level instruction cannot be satisfied without violating a higher one, surface the conflict and let the developer resolve it.

---

## How to Read the Manifest

**For the agent**: At the start of every session, load MANIFEST.yaml and read the full node list. Know which concerns are covered and at what depth before writing any proposal. When a Tier 3 guardrail or post-implementation gap touches an area marked `thin` or `missing`, flag it explicitly — do not silently fill it. A missing node means the standard has not spoken on that concern yet. Surface it.

**For the developer**: The status field in MANIFEST.yaml is the health of the web. `mature` nodes can be trusted. `thin` nodes are directionally correct but need sharpening. `missing` nodes are named gaps — the web knows they need to exist but no candidate has been built yet.

---

## Manifest Update Protocol

MANIFEST.yaml is part of the standard. It must stay in sync with the actual skill files.

**When to update MANIFEST.yaml:**
- A new skill is created → add node to registry and coverage map
- An existing skill is updated significantly → update node status and open_gaps
- A gap surfaces in a Standard Evolution Report → add to gap_queue
- A gap is resolved by a new skill → update gap_queue status, add node to registry

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

When a kit is mature enough to be reused, it is promoted to the library. Promotion criteria:

- All pre-build nodes are `mature`
- Gap queue has no `high` priority open gaps
- At least one real system has been built against it end to end
- A non-developer has successfully used it, or it has been reviewed against that standard

The library entry is written into the kit's MANIFEST.yaml under `library_entry` at promotion time. The SKILL.md template defines the shape — the data lives in MANIFEST.yaml.
