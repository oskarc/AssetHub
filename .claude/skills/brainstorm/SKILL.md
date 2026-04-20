---
name: brainstorm
description: Socratic brainstorming — interrogate a feature request with targeted questions to surface the real problem, constraints, and alternatives before writing code. Use when the user asks to build/implement/add something non-trivial, or when the request feels like a solution masquerading as a problem.
---

# Socratic Brainstorming

Most "implement X" requests ship with the solution already baked in. This skill pauses that, asks what the actual problem is, and produces a short, agreed-upon brief before any code is written.

Announce in chat whether you're running this skill or skipping it, and why.

## When to run
- User says "implement", "build", "add", "create", "let's do" — and the scope is more than a line or two.
- The request describes a solution ("add a Redis cache for user sessions") without stating the problem ("sessions load slowly").
- The request is vague ("improve the upload flow").
- Skip for: trivial changes, clear bug fixes, tasks with an existing plan or ticket that already answers these questions. When skipping, say so explicitly ("skipping /brainstorm — the request is unambiguous").

## The six questions

Ask them conversationally, not as a checklist dump. One or two at a time, tightest-first. Stop when you have enough to write the brief.

### 1. Problem
"What's the actual problem you're trying to solve?"
- Listen for the *solution* being described instead of the *problem*. Reflect it back: "So the problem is X, and the proposed solution is Y — is that right?"
- Ask "why" until you hit a real constraint, deadline, user pain, or business driver.

### 2. Users / callers
"Who hits this today, and what do they do?"
- Real users, internal users, or other code. If "users" stays abstract, ask for a concrete example.
- What's the current workaround? (Often reveals the real pain threshold.)

### 3. Success
"How do we know it worked?"
- A metric, a user behavior, a disappeared complaint — something observable.
- "Done" for v1, not "done" for the long-term vision.

### 4. Constraints
"What can't move?"
- Deadlines, budgets, compatibility, performance, security, team capacity.
- Stated *and* unstated — probe the ones users often forget (rollback, migration, on-call).

### 5. Alternatives
"What else could solve this? Why not those?"
- If the user has no alternatives in mind, offer two obvious ones and ask why they're unsuitable.
- The point isn't to pick the alternative — it's to make the rejection reasons explicit. They inform edge-case handling later.

### 6. Smallest version
"What's the smallest thing that would deliver value?"
- What can be cut from v1? Features that feel load-bearing often aren't.
- What's the escape hatch if v1 doesn't work?

## Output — the brief

Once the six questions have useful answers, write a short brief (≤10 lines) back to the user:

```
Problem: <one sentence>
Users: <who hits this, what they do today>
Success: <observable criterion>
Constraints: <what can't move>
Rejected alternatives: <list with reasons>
V1 scope: <smallest shippable version>
Out of scope (for now): <explicitly cut items>
```

End with: "Proceed with implementation?" Wait for confirmation before touching code.

## Anti-patterns to avoid
- **Wall-of-text interrogation.** Asking all six at once feels like a deposition; the user disengages.
- **Accepting the framing.** "Add a cache" is not a problem statement. Probe before accepting.
- **Skipping the written brief.** Verbal alignment rots — the artifact is the whole point.
- **Running this on tasks it wasn't meant for** (trivial edits, obvious bug fixes). Announce the skip instead.
