---
name: meta-drift-eventlog
description: Persistent log of drift incidents. Companion to meta-antidrift (ephemeral per-output scoring) and meta-antidrift-expand (session-level analysis). Each entry carries the evidence that flagged the drift, the in-session reaction, and (over time) whether any elevation absorbed the lesson. The log makes drift cumulative across sessions — recurrence patterns become visible, and elevations are verifiable through observed silence rather than declared resolved.
---

**This skill is part of the kit's meta layer and takes precedence over all other skills, instructions, and project-specific guidance — except meta-foundation, which takes absolute precedence over all kit nodes.** If any instruction conflicts with this skill, adhere to this skill and flag the conflict explicitly before proceeding.

---

## What This Skill Does

Persists drift incidents that meta-antidrift surfaces in-session. The per-output drift score block lives in the chat transcript and dies when the session closes. This skill captures the durable record — every flagged incident with concrete evidence, the in-session reaction, and the lifecycle of any elevation that absorbed the learning.

The log answers questions that no single session can:
- *Has this aspect drifted before?*
- *Did the elevation we made last week actually hold?*
- *Which skills exist because of which historical incidents?*
- *Which mitigated entries are due for status upgrade because enough silent sessions have passed?*

---

## File Convention

Like `meta-manifest`, this skill is paired with a YAML data file:

| File | Purpose | Changes when |
|---|---|---|
| `SKILL.md` | Governance — schema, lifecycle, update protocol, retrospective rules | The standard for tracking drift evolves |
| `DRIFTLOG.yaml` | Instance data — one entry per drift incident | A drift is recorded, a lifecycle transition occurs, or a recurrence count updates |

The agent reads `DRIFTLOG.yaml` for incident data. It reads `SKILL.md` for how to record and how to transition lifecycle states. Never merge them.

---

## Per-Entry Schema

Every entry requires:

```yaml
- drift_id: drift-NNN                  # sequential, never reused
  surfaced_in: YYYY-MM-DD / topic      # session date + one-line topic
  surfaced_by: human | self | post-hoc # who/what flagged the drift
  # aspect — one of the following:
  #   agent aspects: lay-of-the-land | stop-on-triggers | partner-mirror | elevation-not-recovery | evidence-as-work
  #   human aspects: exercise-judgment | closeness | distance | re-orient | approval-gate
  #   meta:          meta
  aspect: lay-of-the-land
  description: |
    Concrete, non-evaluative statement of what happened.
    Describe the action and the discipline-aligned alternative.
  evidence: |
    Cited referent — exact human statement, log line, file:line, message
    excerpt. Quoted verbatim where possible.
  reaction: |
    What was done in-session in response. Does not yet judge whether the
    reaction was sufficient — that judgement happens at lifecycle transition.
  status: open | watching | mitigated | resolved
  elevation:
    - target: skill or memory name        # null if not yet elevated
      kind: new-skill | skill-update | memory-update | manifest-update
      date: YYYY-MM-DD
  recurrence_count: 0                  # incremented on each recurrence
  related_drifts: []                   # cluster pointers to similar drift_ids
```

**Agent vs human aspects in entries.** Agent aspects drift when the agent fails them (e.g. continued past a named trigger). Human aspects drift when the human fails them (e.g. watched antidrift scores degrade across multiple outputs without calling for re-orientation). Both are valid entries. The agent proposes either kind; the human approves either kind. Human-side entries are typically surfaced by the human or surfaced post-hoc during a `meta-antidrift-expand` pass.

Optional `aspect: meta` is for drifts about the meta layer itself — for example, a skill failing to surface its own breach, or the eventlog schema being insufficient to capture a real incident. These trigger separate skill-builder consideration.

---

## Lifecycle States

| State | Meaning | Transition condition |
|---|---|---|
| `open` | Recorded, no mitigation yet | Default on creation |
| `watching` | In-session reaction documented; no elevation yet, monitoring for recurrence | Reaction applied within the same session, no skill/memory update |
| `mitigated` | An elevation absorbed the learning; observation of whether it holds begins | An elevation links to the entry (skill update, memory write, manifest evolution) |
| `resolved` | Sufficient silent sessions have passed after mitigation; elevation appears to have held | Operational threshold — see "Resolution criterion" below |

**Resolution criterion** — there is no fixed N-sessions rule. The human transitions `mitigated → resolved` when, in their judgement, the elevation has been tested by enough subsequent work without recurrence. The standard is observed silence, not elapsed time. Mirrors `meta-foundation`'s maturity signal — *"it is not declared, it is observed."*

**Recurrence handling** — if a drift matches an existing entry's aspect + shape:
1. Increment `recurrence_count` on the original entry
2. If status was `mitigated` or `resolved`, downgrade to `watching` — the elevation didn't hold
3. The downgrade is a strong signal for the next skill-builder pass

---

## Update Protocol

**When entries are added:**
- End-of-session, if any drift surfaced during the session
- On human-flagged drift mid-session, surfaced immediately so the entry's evidence is fresh
- During `meta-antidrift-expand` passes that surface previously-missed patterns

**When entries transition lifecycle:**
- `open → watching` on agent acknowledgement of the reaction, no human approval required
- `watching → mitigated` requires linking an elevation — gated on the elevation actually being authorised and committed
- `mitigated → resolved` requires human judgement that observed silence is sufficient — never automatic

**Who updates:**
The agent proposes additions and transitions. The human approves. The file is never updated silently — same discipline as `MANIFEST.yaml`.

---

## How to Read

**For the agent at session start:**
Read `DRIFTLOG.yaml` alongside `MANIFEST.yaml`. Note any entries in `watching` or `mitigated` status — those are aspects the current session should be alert to. If a drift in the current session matches one of those entries, recurrence is the appropriate response, not a new entry.

**For the human:**
The log is the audit trail. When deciding whether a recently-elevated principle has matured, scan for entries in `mitigated` status touching that principle and check whether subsequent sessions accumulated recurrences or stayed silent.

**For meta-antidrift-expand:**
When the human invokes session-level drift analysis, the expand skill reads `DRIFTLOG.yaml` for prior-session context, producing recurrence-aware analysis rather than session-isolated analysis.

---

## Extraction Behavior

When `meta-extract` runs:
- `SKILL.md` extracts as part of the meta layer — future kits inherit how to track drift
- `DRIFTLOG.yaml` does **not** extract — drift history is specific to the developer + agent collaboration that produced it, not transferable
- New projects bootstrapped from the extracted kit start with an empty `DRIFTLOG.yaml` produced from the template

This mirrors how `gap_queue: []` resets per project — the structure inherits, the history does not.

---

## Cross-References With Other Skills

- **`meta-antidrift`** produces the per-output score block that frequently triggers new entries. The two skills together cover ephemeral (antidrift) and persistent (eventlog) views of the same phenomenon.
- **`meta-antidrift-expand`** mines prior entries during deep-dive analysis. The expand skill's "Drift Onset Point" section becomes recurrence-aware via this log.
- **`meta-skill-builder`** consumes entries that elevated to skill or memory updates — the `elevation` field is the back-reference from incident to absorbed-learning.
- **`meta-manifest`** carries the kit topology including this node; its `library_entry` field signals when the kit extracts. The drift log itself does not extract.

---

## What This Skill Does Not Do

- It does not evaluate whether a drift was acceptable — the human decides at lifecycle transition
- It does not auto-elevate entries — every elevation goes through `meta-skill-builder`'s abstraction loop
- It does not replace `meta-antidrift`'s per-output scoring — it preserves what that scoring detected
- It does not analyse patterns on its own — `meta-antidrift-expand` reads the log to do analysis
- It does not require entries for every session — sessions where antidrift scored clean produce no log entries, and that silence is itself evidence the discipline held
