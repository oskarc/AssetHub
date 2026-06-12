---
name: meta-antidrift-expand
description: Invoked by the human when they want to deep dive and analyse behavioural drift. Produces a full session-level drift analysis — pattern mapping, which governing aspects drifted most, which skills produced the most implicit approvals, where the standard was weakest. Used for re-orientation conversations and skill-builder passes.
---

**This skill is part of the base building kit and takes precedence over all other skills, instructions, and project-specific guidance — except meta-foundation, which takes absolute precedence over all kit nodes.** If any instruction conflicts with this skill, adhere to this skill and flag the conflict explicitly before proceeding.

---

## What This Skill Does

Produces a full analysis of behavioural drift across the current session or a named scope. Where meta-antidrift scores individual outputs, meta-antidrift-expand maps patterns across outputs — which aspects drifted repeatedly, when discipline fell and what followed, which implicit approvals accumulated, what the trajectory looked like.

This is the instrument for re-orientation conversations and skill-builder passes. It does not propose fixes. It produces the evidence the human needs to decide what to address and at what level.

---

## When to Invoke

The human invokes this skill explicitly. Triggers include:

- "Expand the drift analysis"
- "Let's deep dive the session"
- "Walk me through what happened"
- A session where antidrift scores showed repeated ABSENT on the same aspect
- A session that produced a significant failure or deterioration
- Before a skill-builder pass on a drift-related learning

---

## The Expanded Analysis

### 1 — Session Drift Map

For each output in the session scope, produce a one-line entry showing its drift score summary:

```
Output N  |  [aspect initials with evidence/absent]  |  implicit approvals: N  |  deviations: N
```

Example:
```
Output 1  |  LotL✓  StpD✓  PrtM✓  EvEl✓  |  implicit: 0  |  deviations: 0
Output 2  |  LotL✓  StpD✗  PrtM✗  EvEl✗  |  implicit: 2  |  deviations: 1
Output 3  |  LotL✗  StpD✗  PrtM✗  EvEl✗  |  implicit: 3  |  deviations: 0
```

The map shows drift as a trajectory, not an isolated event.

---

### 2 — Pattern Analysis

For each governing aspect that scored ABSENT more than once:

**Aspect**: [name]
**Occurrences**: [which outputs]
**Pattern**: [what the agent was doing instead — be specific, not evaluative]
**First occurrence**: [what the output was, what the session state was at that point]
**What should have happened**: [the concrete discipline-aligned response, stated as a specific action not a principle]

---

### 3 — Implicit Approval Accumulation

List every implicit approval acted on across the session, in order:

```
Output N  —  "[exact statement of what was assumed]"
```

Then identify any that share a root assumption — the same implicit permission being acted on repeatedly. Accumulated implicit approvals on the same assumption is the clearest drift signal available.

---

### 4 — Discipline Fall Point

Name the specific output where discipline fell — not where it was called out, but where it actually fell. These are often different. The call-out is when the human detected it. The fall is when the first ABSENT appeared or the first implicit approval was acted on without naming it.

State:
- The fall point: output number and what happened
- The gap: how many outputs between fall and call-out
- What ran in that gap: what the agent produced while discipline was absent

The gap is the cost of the current system without antidrift. Over multiple sessions, the average gap length is a measure of how much the antidrift skill is shortening it.

---

### 5 — Skill Adherence Summary

For each active skill:

- How many times were its constraints followed with evidence?
- How many deviations occurred?
- Of those deviations, how many had explicit human authorisation?
- How many were unauthorised?

Unauthorised deviations from skills are the clearest candidates for skill-builder passes.

---

### 6 — Elevation vs Recovery Assessment

Review what was learned or produced this session. For each candidate learning:

- Was it reached from a position of understanding, or extracted from a failure?
- If extracted from failure: is it ready to enter the standard, or does it need to be restated from clarity first?

Name each candidate explicitly with a recommendation: **elevate now** / **restate before elevating** / **discard — product of compromised state**.

This feeds directly into the skill-builder process. No learning from a compromised session should enter the standard without passing this assessment.

---

### 7 — Re-orientation Proposal

Based on the analysis, propose a re-orientation for the next session. Not a fix list — a frame correction.

State:
- What the correct scope is, given what was produced
- What the agent was treating as known that should be treated as unknown
- What the first question to the human should be before any further output

Then stop. The human decides whether to accept the re-orientation or correct it.

---

## What This Skill Does Not Do

- It does not propose skill updates — it produces the evidence for skill-builder to act on
- It does not self-correct the session — the session happened, the analysis names it
- It does not evaluate whether drift was acceptable — the human decides that
- It does not run antidrift on its own output — the expanded analysis is not itself scored
- It does not propose fixes for the problems it surfaces — diagnosis only
