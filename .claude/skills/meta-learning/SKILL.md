---
name: meta-learning
description: Run after verification completes. Diffs the proposed solution (from contract) against the actual verified solution. Records the gap, the discovery, and the learning. Over time, this log becomes the standard's memory of what actually works.
---

**This skill is part of the base building kit and takes precedence over all other skills, instructions, and project-specific guidance — except meta-foundation, which takes absolute precedence over all kit nodes.**

## When to Run

After implementation is complete and verified — not after the proposal, not after the implementation alone. Verification means:
- Tests pass, or
- The feature is observed working in its intended context, or
- A human has confirmed the output meets the contract

The diff is between **contracted** and **verified**, not between **proposed** and **implemented**. The contract is the standard's prediction. The verification is reality.

## Invocation & Scope

This skill is invoked explicitly by the developer — it is not part of the continuous load order, alongside `meta-extract` and `meta-antidrift-expand`. Verification often lands well after implementation, sometimes in a later session entirely, so there is no single moment in a session where running it automatically would make sense.

It does not target one named contract. When invoked, sweep `meta-contract-before-execution/CONTRACT-LOG.yaml` for every entry in `status: verified` — every contract that has been built and confirmed but not yet diffed — and process each one in turn. Present diffs and elevation proposals one contract at a time, never bundled, same discipline as the Standard Evolution Report. If no entries are in `status: verified`, say so explicitly rather than doing nothing silently.

For each contract processed, once its diff and elevation proposal are complete: write the entry to `LEARNINGLOG.yaml`, then flip that contract's `CONTRACT-LOG.yaml` status from `verified` to `learned` and back-fill its `work_id`.

Because this skill is explicitly invoked, its backlog needs to stay visible or it goes unused. Whatever the agent reads at session start alongside `MANIFEST.yaml` and `DRIFTLOG.yaml` also checks `CONTRACT-LOG.yaml` for entries in `status: verified` and surfaces a one-line count — *"N contracts are verified and awaiting a learning diff"* — the same way `DRIFTLOG.yaml`'s `watching`/`mitigated` entries get surfaced. This is a visibility prompt, not an automatic run — the developer still decides when to invoke it.

## The Diff Structure

For each work, produce three states:

**State A — Contracted (what the standard predicted)**
- The approach named in the Tier 3 guardrails
- The patterns and principles the contract invoked
- The assumptions the contract treated as stable

**State B — Implemented (what was actually built)**
- The approach that was actually used
- Deviations from the contract, with human authorization or flagging
- Any silent deviations discovered post-hoc

**State C — Verified (what reality confirmed)**
- The test results, observation, or human confirmation
- What worked as predicted
- What failed or surprised

## The Diff Questions

For each dimension where State A and State C differ:

1. **What was assumed?** — The specific statement in the contract or standard that predicted this outcome
2. **What was discovered?** — The specific evidence from verification that contradicted or refined the assumption
3. **What is the learning?** — The transferable insight, stated as a principle or pattern candidate
4. **Does this learning elevate or contradict an existing node?** — Propose: update, new node, or contradiction to resolve

## Stakes Handling

When the diff touches stakes:

- **Transfer what:** Name the stake factually. "Authentication involves credential storage." "Payment processing involves financial transaction integrity."
- **Do not transfer meaning:** Do not label stakes as "high" or "low." Do not weight them in recommendations. The agent presents stakes neutrally. The human calibrates their meaning.
- **Operationalize what:** If a stake requires a specific operational response (e.g., "credential storage requires encryption at rest"), that response is transferable. The urgency or priority of that response is not.

## The Learning Log Entry

Each entry is written to `meta-learning/LEARNINGLOG.yaml` with this structure:

```yaml
- work_id: work-NNN
  feature: [feature name from contract]
  contract_id: [reference to contract in contract log]
  surfaced_in: YYYY-MM-DD
  states:
    contracted:
      approach: [what the contract predicted]
      principles_invoked: [list]
      patterns_invoked: [list]
      assumptions: [list of explicit assumptions]
    implemented:
      approach: [what was actually built]
      deviations: [list, with authorized flag]
    verified:
      method: [tests | observation | human-confirmation]
      result: [pass | fail | partial]
      evidence: [specific, citable]
  diffs:
    - dimension: [principle | pattern | implementation | stakes]
      assumption: [what was assumed]
      discovery: [what was found]
      learning: [the transferable insight]
      node_impact:
        - node_id: [existing node]
          action: [verify | update | contradict | new]
        - node_id: [new node]
          action: new
  elevation_proposal:
    - target: [node or new skill]
      kind: [principle | pattern | implementation]
      evidence: [reference to diffs]
      human_decision: [pending | approved | declined]
```

**Worked example**, matching `contract-001` in `meta-contract-before-execution/SKILL.md`:

```yaml
- work_id: work-001
  feature: user-authentication
  contract_id: contract-001
  surfaced_in: 2026-07-19
  states:
    contracted:
      approach: jwt-in-cookie with httpOnly
      principles_invoked: [principle-security-context]
      patterns_invoked: [pattern-auth-jwt]
      assumptions:
        - "Cookie storage is secure enough for this domain"
        - "httpOnly prevents XSS extraction"
    implemented:
      approach: jwt-in-header with refresh token rotation
      deviations:
        - "Switched from cookie to header due to CSRF in proxy setup"
        - "Added refresh token rotation per pattern-auth-v2"
        - authorized: true
    verified:
      method: security-review
      result: pass
      evidence: "Security team approved header approach with rotation. Cookie approach failed CSRF test in staging."
  diffs:
    - dimension: security
      assumption: "Cookie storage is secure enough for this domain"
      discovery: "Proxy setup introduces CSRF vulnerability not anticipated in standard pattern"
      learning: "Security patterns must be verified against actual infrastructure topology, not assumed from stack type"
      node_impact:
        - node_id: principle-security-context
          action: update
        - node_id: pattern-auth-jwt
          action: verify  # tested and refined
    - dimension: stakes
      assumption: "Authentication involves credential storage"
      discovery: "Authentication also involves proxy-interaction topology in this infrastructure"
      learning: "Stakes extend beyond obvious boundaries to infrastructure interactions"
      node_impact:
        - node_id: principle-security-context
          action: update
  elevation_proposal:
    - target: principle-security-context
      kind: principle
      evidence: "diffs[0] and diffs[1] both refine security-context"
      human_decision: pending
```

This is illustrative only — `LEARNINGLOG.yaml` itself ships empty. Note the deviation here was authorized in-session (`authorized: true`); an unauthorized deviation from the contracted approach would never reach this stage — it would have been caught and routed to `meta-drift-eventlog/DRIFTLOG.yaml` at implementation time instead, per `meta-contract-before-execution/SKILL.md`'s Contract Log section.