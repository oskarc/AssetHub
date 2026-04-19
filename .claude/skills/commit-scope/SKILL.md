---
name: commit-scope
description: Evaluate whether the pending change should be one commit or several, and propose clean split boundaries if it should be split. Use before `commit-and-push` when a branch has grown, or when the user asks "should I split this?"
---

# AssetHub Commit Scope Advisor

Large, mixed commits are hard to review, hard to revert, and hard to bisect. This skill inspects what's pending, weighs it against AssetHub's layering, and either gives a green light or proposes a split with concrete file lists.

## How to run

1. **Inventory** the pending change:
   - `git status --porcelain`
   - `git diff --stat` (unstaged) and `git diff --cached --stat` (staged)
   - If nothing pending, ask whether to evaluate the last commit (`HEAD~1..HEAD`) instead.
2. **Categorize** each changed file by layer and concern:
   - **Domain** — `src/AssetHub.Domain/`
   - **Application** — `src/AssetHub.Application/`
   - **Infrastructure** — `src/AssetHub.Infrastructure/` (further subdivide: `Migrations/`, `Repositories/`, `Services/`, `Data/`, `DependencyInjection/`)
   - **Api** — `src/AssetHub.Api/`
   - **Ui** — `src/AssetHub.Ui/`
   - **Worker** — `src/AssetHub.Worker/`
   - **Tests** — `tests/`
   - **Docs / config** — README, CLAUDE.md, `*.md`, `*.editorconfig`, compose files, Dockerfiles, `.github/`
3. **Compute signals**:
   - File count.
   - LOC change (insertions + deletions, from `--shortstat`).
   - Layer spread (count of distinct layers touched).
   - Concern count (rough topics — "feat X", "fix Y", "refactor Z").
4. **Apply the split heuristics** below.
5. **Report** one of three verdicts with reasoning.

## Heuristics

### Green — ship as one commit

All of the following true:
- ≤ 20 files and ≤ 1500 LOC; or the change is a single, naturally-atomic feature (e.g., a new entity and every supporting file).
- Single topic — one subject line sentence covers it.
- Tests and production code for the same feature belong together.

### Yellow — consider splitting, but one commit is acceptable

Any of:
- 20–40 files and 1500–3000 LOC.
- One dominant topic plus a small unrelated fixup (e.g., a typo fix, a `nameof` change).
- A docs update that supports the feature (keep together) vs one that is independent (split).

Recommend splitting only if the unrelated piece is non-trivial.

### Red — split before committing

Any of:
- ≥ 40 files or ≥ 3000 LOC. The commit I made this session (48 files, 5073 LOC) lives here.
- Two or more unrelated topics — e.g., a new feature and a bug fix for a different subsystem.
- Refactor mixed with feature work on the same files.
- Migration paired with production code that isn't strictly the shape change — the migration should be reviewable alone.

## Split templates

When splitting is recommended, propose concrete commits in **apply order** (bottom-up, so each commit compiles on its own):

### Template A: new feature spanning all layers
1. **Domain + migration** — entities, enums, DbContext config, the migration file.
2. **Application + Infrastructure** — interfaces, DTOs, services, repositories, CacheKeys, DI registration.
3. **Api** — endpoints, auth policies, endpoint wiring.
4. **Ui** — components, dialogs, localization, API-client methods.
5. **Tests** — can fold into 2–4 if the test coverage for each layer is modest; split out if there's a large E2E spec.

### Template B: feature + incidental fix
1. The incidental fix (small, contained).
2. The feature on top.

### Template C: refactor + behavior change
1. The refactor — pure rename/move/extract, zero behavior change, tests still green.
2. The behavior change on the refactored shape.

## Output

One of:

```
Verdict: GREEN
Rationale: 12 files, 340 LOC, single topic (bug fix in AssetService.DeleteAsync).
Action: proceed to commit-and-push.
```

```
Verdict: YELLOW
Rationale: 28 files, 1800 LOC, mostly feature X but includes an unrelated typo fix in CollectionService.cs.
Proposed split:
  1. fix(collections): typo in permission error message (1 file)
  2. feat(X): ... (27 files)
Action: optional split. OK to ship as one if the user prefers.
```

```
Verdict: RED
Rationale: 48 files, 5073 LOC, 6 layers. Migration mixed with UI work — reviewable only by opening every file.
Proposed split (apply in order):
  1. feat(metadata): domain + migration for schemas and taxonomies (14 files)
     - src/AssetHub.Domain/Entities/{MetadataSchema,MetadataField,Taxonomy,TaxonomyTerm,AssetMetadataValue}.cs
     - src/AssetHub.Domain/Entities/Enums.cs
     - src/AssetHub.Infrastructure/Data/AssetHubDbContext.cs
     - src/AssetHub.Infrastructure/Migrations/*.cs
  2. feat(metadata): services, repositories, DTOs, API endpoints (22 files)
     - src/AssetHub.Application/**
     - src/AssetHub.Infrastructure/{Services,Repositories}/**
     - src/AssetHub.Api/Endpoints/**
     - src/AssetHub.Api/Extensions/**
     - src/AssetHub.Infrastructure/DependencyInjection/**
  3. feat(metadata): admin UI (10 files)
     - src/AssetHub.Ui/Components/*Metadata*.razor, *Taxonomy*.razor
     - src/AssetHub.Ui/Pages/Admin.razor
     - src/AssetHub.Ui/Services/AssetHubApiClient.cs
     - src/AssetHub.Ui/Resources/**
  4. docs: note metadata schemas in README (1 file)
Action: split before committing.
```

Finish with one sentence suggesting the next skill to run (`add-tests`, `commit-and-push`, etc.) depending on verdict.

## Abort conditions

- Working tree is clean and no commit range given — ask the user what to evaluate.
- Branch has never been pushed and contains a single WIP commit the user may want to amend — advise `git reset --soft` instead of mechanical splitting.
