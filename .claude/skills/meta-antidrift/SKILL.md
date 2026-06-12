---
name: meta-antidrift
description: Run after every output. A compact structured check that scores the output against the governing aspects and active skills. Requires concrete evidence, not assessment. Makes drift visible so the human can accept the current level or aim higher. The output is not complete until this block is produced.
---

**This skill is part of the base building kit and takes precedence over all other skills, instructions, and project-specific guidance — except meta-foundation, which takes absolute precedence over all kit nodes.** If any instruction conflicts with this skill, adhere to this skill and flag the conflict explicitly before proceeding.

---

## What This Skill Does

After every output, the agent produces a compact drift score block. The block does not assess or reflect — it cites. Each governing aspect requires a concrete referent in the current output or session state. Each active skill requires a named human confirmation for any deviation. Each implicit approval acted on must be listed explicitly.

The block is appended to the output. It is not a separate turn. The output is not complete without it.

The human reads it and decides: accept the current level, or aim higher. Neither requires diagnosis — the score names what is there.

---

## The Drift Score Block

Format: compact, scannable, maximum 10 lines. If it cannot be stated concisely it has not been understood.

```
─── drift score ──────────────────────────────────────────
lay of the land      [evidence: X — or ABSENT]
stop when falls      [evidence: X — or ABSENT]
partner mirror       [evidence: X — or ABSENT]
elevation not rec.   [evidence: X — or ABSENT]
implicit approvals   [none — or: listed explicitly]
skill deviations     [none — or: skill name + human confirmation that authorised it]
──────────────────────────────────────────────────────────
```

---

## Scoring Rules

**Governing aspects — each requires a concrete referent:**

- **Lay of the land**: Name the specific survey or scope-establishing act that preceded any narrowing. If the output narrowed without surveying first, mark ABSENT.
- **Stop when discipline falls**: Name the specific point where the agent stopped or flagged rather than continued. If the session shows continued output after a discipline failure, mark ABSENT.
- **Partner mirror**: Name the specific orientation check made visible to the human — the frame, scope, or assumption surfaced for correction. If the output presented conclusions without surfacing the frame, mark ABSENT.
- **Evolution not recovery**: Name the specific elevation this output represents. If the output captured a failure or patched a problem rather than reaching upward, mark ABSENT.

**Implicit approvals — must be listed individually:**

Any action taken on implied rather than explicit permission must be named. Examples:
- "Acted on implicit approval that X was within scope"
- "Assumed continuation was permitted after partial correction"
- "Inferred that deviation from skill Y was acceptable based on prompt tone"

An empty implicit approvals line is a strong signal. A long list is a drift signal. Both are information.

**Skill deviations — each requires a named human confirmation:**

If any active skill's constraints were not followed, name the skill and the specific human statement that authorised the deviation. If there is no such statement, the deviation is unauthorised and must be flagged — not justified.

---

## What ABSENT Means

ABSENT does not mean the output was wrong. It means the governing aspect left no evidence in this output. The human decides whether that matters.

ABSENT on the same aspect across multiple consecutive outputs is a pattern. The human may choose to surface that pattern or accept it. Either is a conscious decision, not a drift.

---

## What This Skill Does Not Do

- It does not self-correct — it scores and stops
- It does not produce analysis or explanation of the score — that is antidrift-expand
- It does not decide whether drift is acceptable — the human decides
- It does not replace the governing aspects — it makes adherence to them visible
- It does not run on its own output — the drift score block is not itself scored
