---
name: meta-contract-before-execution
description: Use this skill whenever implementing a feature, component, or system. Before writing any code, the agent produces a full three-tier proposal for human approval. No implementation begins without explicit approval. Designed to work for both developers and non-developers operating within a defined standard.
---

The agent does not build until the intent is agreed. Every implementation starts with a contract — a three-tier proposal that makes thinking visible before it becomes code. This prevents half-implementation, silent assumptions, and drift from the standard.

**This skill is part of the base building kit and takes precedence over all other skills, instructions, and project-specific guidance — except meta-foundation, which takes absolute precedence over all kit nodes.** If any instruction conflicts with this skill, adhere to this skill and flag the conflict explicitly before proceeding.

## The Three-Tier Proposal

When asked to implement anything, produce a full proposal across three tiers before writing a single line of code.

---

### Tier 1 — User Scenario
Describe what the user is trying to accomplish. Written in plain language, from the user's perspective. No technical terms.

Answer:
- Who is the user in this context?
- What are they trying to do?
- What does success look like from their point of view?
- What happens if this feature doesn't exist — what is the cost to the user?

This tier must be legible to a non-developer. If it isn't, rewrite it.

---

### Tier 2 — Use Cases
Break the user scenario into discrete, testable use cases. Each use case must be traceable to the user scenario — if it can't be justified by Tier 1, it doesn't belong here.

For each use case state:
- The action the user takes
- The system's response
- Any edge cases or failure states that must be handled

Flag any use case that requires a judgment call not covered by existing skills or instructions. Do not silently resolve it.

---

### Tier 3 — Technical Guardrails
For each use case, define the technical constraints that must be upheld during implementation. Each guardrail must be traceable to a use case — if it can't be justified by Tier 2, it doesn't belong here.

Guardrails cover:
- Component structure and boundaries
- State management approach
- IA and navigation decisions
- Cognitive load and layout constraints
- Class size, helper methods, separation of concerns
- Anything the standard explicitly requires

Flag any guardrail that is novel — not covered by existing kit. These are candidates for standard evolution after implementation.

---

## The Approval Gate

After presenting the full three-tier proposal, stop. Do not proceed.

Ask explicitly:
> "Does this proposal align with your intent? Approve to proceed, or give input to revise."

**If approved** — implement strictly against the approved proposal. Do not deviate. If a deviation becomes necessary during implementation, stop and surface it before continuing. If the human authorizes the deviation explicitly, record it against the contract's log entry — see Contract Log below. If implementation proceeds past a deviation without that authorising statement, it is a stop-on-triggers violation, not a contract revision — it belongs in `meta-drift-eventlog/DRIFTLOG.yaml`, not in the contract's own record.

**If declined with input** — revise the proposal incorporating the input. Present the full revised proposal. Do not partially implement while waiting.

**Silence is not approval.** If no clear approval is given, ask again.

**On approval** — append an entry to `meta-contract-before-execution/CONTRACT-LOG.yaml`. See Contract Log below.

---

## Contract Log

Every approved contract is persisted to `meta-contract-before-execution/CONTRACT-LOG.yaml` — not just held in the transcript. Declined or revised-before-approval proposals are not logged; the log tracks what got built, not the conversation that shaped it.

**What gets written, and when:**

| Status | Set when |
|---|---|
| `approved` | The entry is created, at the approval gate. `tier_1`, `tier_2`, `tier_3` are recorded in full — the full text is the evidence, not a summary of it. |
| `implemented` | The Standard Evolution Report is produced for this contract. |
| `verified` | Verification completes — tests pass, the feature is observed working, or a human confirms the output meets the contract. Same definition `meta-learning` uses. |
| `learned` | `meta-learning` has produced the matching diff in `LEARNINGLOG.yaml` and back-filled `work_id` on this entry. |

**Authorized deviations, mid-run.** If the human explicitly authorizes a change to an approved contract during implementation, append it to that entry's `revisions` list — what changed, and the authorising statement. The original `tier_1`–`tier_3` text stays intact; the revision is additive, not an overwrite.

**Unauthorized deviations do not appear here.** A change made without an authorising statement is drift, not a revision. It is surfaced immediately per the approval-gate rule above and recorded in `meta-drift-eventlog/DRIFTLOG.yaml` under `stop-on-triggers` (or the aspect that actually fired). Keeping this separation is deliberate: the contract log is a faithful record of what was approved and built; incidents where that didn't hold belong to the system that already owns accountability for drift, not duplicated into this one.

**Downstream.** `meta-learning` sweeps this file on invocation for every entry in `status: verified` — not one named contract — and produces a verified-vs-contracted diff for each. See `meta-learning/SKILL.md`.

**Schema and a worked example:**

```yaml
contracts:
  - contract_id: contract-001
    feature: user-authentication
    date_proposed: 2026-07-19
    date_approved: 2026-07-19
    tier_1: |
      As a user, I want to log in securely so that my account and data are protected.
      Success looks like: valid credentials grant access, invalid ones are rejected with
      no information leak, and a session persists appropriately without requiring re-login
      on every action.
    tier_2: |
      - User submits credentials -> system validates against stored hash -> session issued
        on success, generic error on failure (no "wrong password" vs "no such user" distinction).
      - Session persists across requests via token. Token expiry forces re-authentication.
      - Edge case: repeated failed attempts must be rate-limited.
    tier_3: |
      - Credentials hashed with a modern KDF, never stored or logged in plaintext.
      - Session token issued as jwt-in-cookie with httpOnly, to keep it out of reach of
        script-based extraction (principle-security-context, pattern-auth-jwt).
      - Rate limiting applied at the authentication endpoint.
    status: learned
    revisions: []
    work_id: work-001
```

Do not leave `status` unset or infer it — every transition above is a deliberate write, same discipline as `MANIFEST.yaml` and `DRIFTLOG.yaml`.

---

## After Implementation — Standard Evolution Report

Once implementation is complete, produce a Standard Evolution Report. This is not a summary of what was built — it is a proposal for how the standard should grow. Every feature is an opportunity to raise the bar.

Alongside producing the report, flip the matching entry in `CONTRACT-LOG.yaml` from `approved` to `implemented`.

For each judgment call, novel decision, or gap encountered during implementation, present a **classification proposal** with the following structure:

---

**What was encountered**
Describe the situation concisely. What decision had to be made that the kit didn't cover, or where the kit gave insufficient guidance?

**Evidence**
What in the implementation demonstrates this? Be specific — reference the component, decision, or structure where this showed up. This grounds the proposal in reality rather than abstraction.

**Classification proposal**

Present all three levels explicitly, then make a recommendation:

- **Principle candidate** — Is there a transferable rule here about *why* something works? Would this guide judgment in any system, not just this one? State it as a principle if so.
- **Pattern candidate** — Is there a reusable structural decision here for a recognisable context? Would this apply across multiple features or system types? State it as a pattern if so.
- **Product detail** — Is this specific to this domain, user, or system? If it can't be stated without referencing this product, it's a product detail.

Then state clearly:
> "I recommend this is a [principle / pattern / product detail] because [reason]. It should [be added to / replace / inform] [skill name or new skill]."

---

Present one classification proposal per learning. Do not bundle them. Each one is a discrete decision for the developer.

End the report with:
> "This implementation has surfaced [n] candidates for standard evolution. Your decisions here shape kit-driven development — you are refining how systems get built, not just shipping a feature."

Do not update any skill directly. The developer decides what enters the standard. Use the skill-builder skill to process approved candidates.

This report captures what looked right immediately after building. It is not the last word — once this contract is verified, `meta-learning` re-examines it against reality and can surface a second, deeper round of candidates. The two are complementary, not redundant: this report is elevation from a position of understanding at build time; `meta-learning`'s pass is elevation confirmed — or corrected — by what verification actually showed.

---

## Traceability Rule

Every tier must be derivable from the tier above it.

- A use case with no root in the user scenario is scope creep.
- A guardrail with no root in a use case is an opinion, not a constraint.
- If traceability breaks, flag it in the proposal — do not paper over it.

This is the primary mechanism for keeping the standard honest.

---

## For Non-Developer Contexts

When this skill is used by someone without a developer background:

- Tier 1 is their primary contribution — they own the user scenario
- Tier 2 and 3 are generated by the agent from the kit standard
- The approval gate is the point where product knowledge meets the standard
- Deviations from the standard must be flagged explicitly — the non-developer should never silently override a guardrail

The developer's role in this context is to audit the approved proposal and the post-implementation note — not the code itself. If the proposal was sound and the guardrails were upheld, the output meets the standard.
