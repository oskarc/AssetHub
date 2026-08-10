---
name: pattern-systematic-debug
description: Generic, stack-agnostic debugging workflow — reproduce, isolate, hypothesize in writing, test cheaply, fix, verify. Use when the user reports a bug or unexpected behavior that isn't a one-line fix. Safeguard: stop and zoom out after 3 failed attempts.
---

# Systematic Debugging

When something's broken, the temptation is to skim, guess, and patch. This skill slows that down: write the hypothesis before editing, run the cheapest possible experiment before writing code, and stop guessing after three failures.

Announce in chat whether you're running this skill or skipping it, and why.

## When to run
- User reports: "it's broken", "X doesn't work", "unexpected behavior", a stack trace, a failing test that used to pass.
- Skip for: one-line typos, obvious syntax errors, trivial null checks. When skipping, say so explicitly ("skipping /pattern-systematic-debug — this is a one-liner").

## Steps

### 1. Reproduce
Get a reliable, repeatable repro before anything else. If you can't reproduce it, you can't know you fixed it.
- Ask for: exact steps, inputs, environment, expected vs. actual.
- Confirm the repro in the same environment the user hit it in (or the closest available).
- If the bug is intermittent, note the reproduction rate (e.g., 3/10 attempts) before proceeding.

### 2. Isolate
Shrink the failing case to the smallest thing that still fails.
- What changed recently? Check version control, recent deploys, recent configs.
- Bisect along whichever dimension is cheapest: version, input, config, feature flag.
- The goal: a one-paragraph description of *exactly* what triggers the bug.

### 3. Hypothesize — in writing
Before editing any code, state:
- **What you think is wrong.** One sentence.
- **Why you think so.** Point to specific evidence (log line, code path, recent change).
- **What you'd expect to see if you're right.** This is the prediction your experiment will test.
- **What would prove you wrong.** The counter-evidence to look for.

If you can't write this, you don't understand the bug yet — return to step 2.

### 4. Test the hypothesis — cheapest experiment first
- A log statement is cheaper than a breakpoint, which is cheaper than a code change.
- Read before you write: the answer is often in source you haven't opened yet.
- If the experiment confirms the hypothesis, proceed to step 5.
- If it refutes it, update the hypothesis (back to step 3) — don't just try something else.

### 5. Fix
- The smallest change that addresses the root cause. Not the symptom.
- If the root cause is architectural (wrong layer, wrong abstraction), say so — the fix may need to wait for a design decision.

### 6. Verify
- Original repro no longer fails.
- Adjacent behavior still works (the most common bug-introducing-bug pattern).
- Add a regression test if one doesn't exist.

## The 3-attempt safeguard

If three fix attempts fail to resolve the bug, **stop**. Do not attempt a fourth.

Instead, zoom out:
- Is the mental model wrong? (You've been looking in the wrong layer.)
- Is the repro actually testing what you think? (Stale build, cache, wrong environment.)
- Is this the right problem to solve? (The bug may be a symptom of something larger.)
- Is this above the solo-debugging threshold? (Escalate — ask for a second set of eyes or architectural review.)

Announce the safeguard trigger in chat explicitly: "Hit the 3-attempt safeguard. Stepping back before trying #4."

## Anti-patterns to avoid
- **Shotgun debugging** — changing multiple things at once. You lose the ability to attribute the fix.
- **Symptom-patching** — wrapping the failing call in try/catch and swallowing the error.
- **Skipping step 3.** The written hypothesis is the whole discipline; everything else is scaffolding.
- **Declaring it fixed without re-running the repro** on the patched code.
