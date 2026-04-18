---
description: "General code review against AssetHub conventions. Use before merging, after completing a feature, or when asked to review changes."
mode: "agent"
---
# AssetHub Code Review

Review changed or specified files against AssetHub conventions defined in `copilot-instructions.md`, `CLAUDE.md`, and the layer-specific instruction files.

## Scope

- If the user specifies files, review those.
- Otherwise, identify recently changed files across all layers.

Read each file in full before reviewing.

## Review Dimensions

### Architecture compliance
- Layer dependency rules respected (Domain → nothing, Ui → Application only, etc.).
- No patterns from the "NOT used" list (domain events, value objects, specifications, FluentValidation, etc.).
- Repository interfaces in Application, implementations in Infrastructure.

### Code quality
- `sealed` on service/repository implementations.
- Primary constructors for DI injection.
- `ServiceResult<T>` for business errors — no thrown exceptions.
- Structured logging with named arguments.
- Async/await for I/O-bound operations.
- Nullable reference types: `is null` / `is not null`.

### Security
- `CurrentUser` for identity — never `HttpContext.User` directly.
- `RoleHierarchy` methods — no hardcoded role strings or levels.
- Collection-scoped operations check `ICollectionAuthorizationService`.
- No `FromSqlRaw` / string-built SQL — LINQ only.
- Filenames from user input pass through `FileHelpers.GetSafeFileName`.
- No hardcoded secrets.

### API endpoints (if applicable)
- Group-level `.RequireAuthorization()`.
- `.DisableAntiforgery()` on mutations.
- `{id:guid}` route constraints.
- `ValidationFilter<T>` on input DTOs.
- `.ToHttpResult()` for response mapping.

### Caching (if applicable)
- Keys and TTLs defined in `CacheKeys`.
- Tag-based invalidation after mutations.
- ACL/roles never cached globally.

### Testing (if applicable)
- `MethodName_Condition_ExpectedResult` naming.
- `TestData.Create*()` factory methods.
- `IAsyncLifetime` for setup/teardown.
- Happy path + error cases covered.

### Localization (if applicable)
- Both `.resx` and `.sv.resx` updated together.
- Most-specific `IStringLocalizer<T>` used.
- Key pattern `Area_Context_Element`.

## Report Format

```
## Code review — {scope}

### Issues
- [file.cs:42](…) — **Security** — Missing authorization check before entity access.
- [file.razor:15](…) — **Localization** — Raw string literal for user-visible text.

### Suggestions
- [file.cs:88](…) — Consider using tag-based cache invalidation instead of per-key removal.

### Clean
- Architecture compliance ✓
- Naming conventions ✓
```

Offer to fix issues when safe to do so.
