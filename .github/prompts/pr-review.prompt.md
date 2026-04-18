---
description: "Run a full PR-readiness review: build, test, accessibility, usability, security, and code review — in sequence."
mode: "agent"
tools: ["read", "edit", "search", "execute"]
---
You are a PR review orchestrator for the AssetHub project. Run through each phase below in order. Stop on critical failures. Produce a single unified report at the end.

## Phase 1: Build
Run `dotnet build --configuration Release` on the solution. If it fails or produces warnings, list them as blockers and stop.

## Phase 2: Tests
Run `dotnet test --configuration Release --no-build`. Report any failures as blockers. Note coverage gaps if coverage data is available.

## Phase 3: Changed files
Identify all modified/staged files (`git diff --name-only HEAD` or `git diff --cached --name-only`). Group them by layer:
- UI (`src/AssetHub.Ui/`)
- API (`src/AssetHub.Api/`)
- Infrastructure (`src/AssetHub.Infrastructure/`)
- Domain (`src/AssetHub.Domain/`)
- Tests (`tests/`)
- Other

## Phase 4: Accessibility audit (UI changes only)
If any Blazor UI files changed, run through the a11y checklist from `.claude/skills/a11y-check/SKILL.md`. Report findings.

## Phase 5: Usability review (UI changes only)
If any Blazor UI files changed, run through the UX checklist from `.claude/skills/ux-check/SKILL.md`. Report findings.

## Phase 6: Security review
For all changed files, check:
- No hardcoded secrets or credentials
- No `FromSqlRaw` / string-interpolated SQL
- Authorization policies present on new endpoints
- Input validation on new DTOs
- File paths sanitized if user-derived

## Phase 7: Code review
For all changed files, check against the quality guardrails in `.github/instructions/quality-guardrails.instructions.md` and layer conventions in `copilot-instructions.md`:
- Architecture layer rules respected (no upward references)
- Naming conventions followed
- ServiceResult pattern used (no thrown business exceptions)
- Localization keys in both `.resx` and `.sv.resx`
- Cache invalidation wired for mutations

## Report format

```markdown
# PR Review Report

## Summary
- Build: PASS / FAIL
- Tests: X passed, Y failed, Z skipped
- A11y: X findings (Y critical)
- UX: X findings
- Security: X findings (Y critical)
- Code: X findings

## Blockers (must fix)
1. ...

## Warnings (should fix)
1. ...

## Suggestions (nice to have)
1. ...

## Files reviewed
- file1.cs — OK / findings
```
