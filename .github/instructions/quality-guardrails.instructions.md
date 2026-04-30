---
applyTo: "src/AssetHub.*/**"
description: "On-the-fly quality guardrails triggered by file type. Walk through the relevant checklist before reporting a task done."
---
# Quality Guardrails

Quick checks by file type — walk through before reporting done. For deep audits, use the dedicated prompts: `/implementation-a11y-check`, `/implementation-ux-check`, `/security-review`, `/review`.

## Blazor UI (`src/AssetHub.Ui/**/*.razor{,.cs,.css}`)

- Images have `alt` (or `aria-hidden="true"`); icon-only buttons have `aria-label` + `MudTooltip`.
- `MudDialog` has accessible name; `<PageTitle>` on every page; form controls have `Label=`/`For=`.
- Status never by color alone — pair `Color.*` with icon or text.
- Optimistic UI on list mutations (update first, roll back on failure + error toast).
- Destructive actions through `ConfirmDialog`; bulk delete shows count.
- Long-running actions show progress; edit dialogs track dirty state.
- Button naming: **Delete** = permanent, **Remove** = unlink, **Discard** = cancel changes.
- No raw HTML form elements — MudBlazor equivalents only.
- Every user-visible string in `.resx` + `.sv.resx` together; `Area_Context_Element` key pattern; most-specific localizer.

## API endpoints (`src/AssetHub.Api/Endpoints/`)

- Group `.RequireAuthorization("Require…")`; POST/PATCH/DELETE `.DisableAntiforgery()`.
- Route params `{id:guid}`; input DTOs use `ValidationFilter<T>`.
- Collection-scoped ops check `ICollectionAuthorizationService` first.
- Return via `.ToHttpResult()` — never inspect `IsSuccess` manually.

## Services / repos (`src/AssetHub.Infrastructure/**`)

- No `FromSqlRaw` / string SQL — LINQ only; fuzzy search via `EF.Functions.ILike`.
- Process launches use `ProcessStartInfo.ArgumentList`; filenames through `FileHelpers.GetSafeFileName`.
- Cache keys from `CacheKeys` with tags; invalidate after mutations; never cache ACL/roles.
- Scoped services via `IServiceScopeFactory` in singletons; `ServiceResult<T>` — never throw.

## DTOs (`src/AssetHub.Application/Dtos/`)

- `[Required]`, `[StringLength]`, `[Range]` on user-bound fields; `[MaxLength]` on lists.
- Nullable ref types: required = non-nullable, optional = `?`.

## Configuration

- Settings class: `const string SectionName`, DataAnnotations, `ValidateOnStart()` for critical infra.
- No hardcoded secrets; production `AllowedHosts` ≠ `"*"`.

## Worker (`src/AssetHub.Worker/**`)

- Per-item try/catch in batch loops; `ct.ThrowIfCancellationRequested()` in long loops.
- `IServiceScopeFactory` per iteration; log counts at `Information`/`Debug`/`Warning`.
