---
name: commit-and-push
description: "Thoroughly review all staged/unstaged changes against AssetHub conventions, run build and tests (fixing failures), compose a why-focused commit message, and push. Use when the user asks to commit, push, or ship changes."
---

# AssetHub Commit & Push

End-to-end quality gate: review → build → test → fix → commit → push. Nothing ships without passing every phase.

## How to run

Execute phases 1–8 in order. **Stop and fix** when a phase fails — do not skip ahead. After fixing, re-run the failed phase before continuing.

---

## Phase 1: Inventory changed files

1. Run `git status --porcelain` to list all modified, added, and deleted files.
2. Run `git diff --name-only` (unstaged) and `git diff --cached --name-only` (staged).
3. If nothing is staged, stage all changes with `git add -A` after confirming with the user.
4. Group files by layer:
   - **Domain** — `src/AssetHub.Domain/`
   - **Application** — `src/AssetHub.Application/`
   - **Infrastructure** — `src/AssetHub.Infrastructure/`
   - **API** — `src/AssetHub.Api/`
   - **UI** — `src/AssetHub.Ui/`
   - **Worker** — `src/AssetHub.Worker/`
   - **Tests** — `tests/`
   - **Config/Docs/Other** — everything else
5. Read every changed file in full — do not skim.

---

## Phase 2: Quality guardrails review

Walk through `.github/instructions/quality-guardrails.instructions.md` for each changed file, applying only the checks relevant to that file's layer:

- **Blazor UI** — a11y (alt, aria-label, PageTitle, color+icon), optimistic UI, ConfirmDialog, localization parity, MudBlazor-only.
- **API endpoints** — auth policy, antiforgery, route constraints, ValidationFilter, `.ToHttpResult()`.
- **Services / repos** — no raw SQL, sanitized filenames, CacheKeys+tags, ServiceResult, scoped services.
- **DTOs** — DataAnnotations on all user-bound fields, nullable ref types.
- **Configuration** — SectionName, no hardcoded secrets, ValidateOnStart.
- **Worker** — per-item try/catch, CancellationToken, IServiceScopeFactory, log counts.

If findings exist, fix them now. Re-read the file after fixing to confirm.

---

## Phase 3: Security review

For every changed file, check against the security conventions in `.github/prompts/security-review.prompt.md`:

- No hardcoded secrets or credentials anywhere (grep for patterns: `password`, `secret`, `apikey`, `connectionstring` in literals).
- No `FromSqlRaw` / `FromSqlInterpolated` / string-built SQL.
- Authorization policies present on new/changed endpoints.
- Input validation on new/changed DTOs.
- File paths sanitized if derived from user input.
- `CurrentUser` used — never `HttpContext.User` directly.
- `RoleHierarchy` methods — no hardcoded role strings.

If findings exist, fix them now.

---

## Phase 4: Architecture compliance

Verify layer dependency rules are not violated:

- Domain references nothing.
- Application references only Domain.
- UI references only Application — never Infrastructure or Api.
- Infrastructure references Application + Domain.
- Api and Worker are composition roots.

Check for patterns that are **not used** in AssetHub:
- Domain events, value objects, specifications, FluentValidation, Swagger/OpenAPI, third-party state management.

If violations found, fix them now.

---

## Phase 5: Self-check validation

Run through `.github/instructions/self-check.instructions.md` for the types of changes present:

- **C# modified?** → Verify types exist (grep), namespace imports correct.
- **Tests modified?** → Verify naming convention, fixture usage.
- **Localization keys added?** → Verify both `.resx` and `.sv.resx` updated, key pattern correct.
- **Cache keys added?** → Verify in `CacheKeys.cs`, tags defined, invalidation wired.
- **Endpoints modified?** → Verify registration in `WebApplicationExtensions`.
- **Entities/DbContext modified?** → Flag if migration is needed.
- **DI registration modified?** → Verify service registered correctly.

If findings exist, fix them now.

---

## Phase 6: Build

```powershell
dotnet build --configuration Release
```

- Must pass with **zero warnings** (CI enforces this).
- If build fails, diagnose the errors, fix them, and re-run until clean.
- After fixing, return to Phase 2 for any newly modified files.

---

## Phase 7: Tests

```powershell
dotnet test --configuration Release --no-build
```

- All tests must pass.
- If tests fail:
  1. Read the failing test and the code it tests.
  2. Determine if the test or the production code is wrong.
  3. Fix the root cause (prefer fixing production code if the test expectation is correct).
  4. Re-run `dotnet build --configuration Release` then `dotnet test --configuration Release --no-build`.
  5. Repeat until all tests pass.
- After fixing, return to Phase 2 for any newly modified files.

---

## Phase 8: Commit & push

### Compose the commit message

Follow **Conventional Commits** with a **why-focused** body:

```
<type>(<scope>): <short summary in imperative mood>

<WHY this change was made — the motivation, problem, or user need>

- <bullet points of notable implementation decisions if needed>
```

**Rules:**
- **Subject line**: imperative mood, ≤72 chars, no period at the end.
- **Type**: `feat`, `fix`, `refactor`, `style`, `test`, `docs`, `chore`, `perf`, `ci`.
- **Scope**: the affected domain area (e.g., `assets`, `collections`, `auth`, `ui`, `worker`, `infra`).
- **Body**: explain **why** the change was made, not what. The diff shows what; the message explains the motivation.
  - Bad: "Added a null check to GetByIdAsync"
  - Good: "Prevent 500 errors when clients request a deleted asset that is still referenced in collection views"
- **Do NOT include**: file lists, line numbers, generated boilerplate.
- **Co-authored-by**: if the user has a name/email configured in git, do not add an AI co-author trailer.

### Multi-scope changes

If changes span multiple unrelated concerns, ask the user whether to:
1. Commit as a single commit with the broadest scope.
2. Split into multiple focused commits (preferred when changes are truly independent).

### Execute

```powershell
git add -A
git commit -m "<message>"
git push
```

- If push fails due to remote changes, run `git pull --rebase` and re-push.
- If rebase has conflicts, report them to the user — do not auto-resolve merge conflicts.

---

## Abort conditions

Stop the entire flow and report to the user if:
- Build fails after 3 fix attempts on the same error.
- Tests fail after 3 fix attempts on the same test.
- A security finding cannot be resolved without architectural changes.
- Merge conflicts arise during push that require manual resolution.

---

## Summary

After successful push, report:
- Branch and remote pushed to.
- Commit hash and message.
- Count of files changed, insertions, deletions (`git diff --stat HEAD~1`).
- Any warnings or notes from the review phases.
