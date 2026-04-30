---
name: implementation-add-tests
description: Scaffold xUnit / bUnit tests for new or changed AssetHub code following house conventions. Use when production code lacks tests, a feature was shipped test-less, or the user asks to add coverage for recent changes.
---

# AssetHub Test Scaffolder

Generates tests that match AssetHub's existing patterns — `PostgresFixture`, `CustomWebApplicationFactory`, `TestClaimsProvider`, `TestData` factories, `Method_Condition_ExpectedResult` naming — so new coverage blends in instead of inventing new plumbing.

## How to run

1. **Scope** — decide what to cover:
   - If args provided, use them (path, class name).
   - Otherwise: `git diff --name-only HEAD` + `git diff --cached --name-only`, narrowed to `src/AssetHub.*/**/*.cs` and `src/AssetHub.Ui/**/*.razor`.
   - Exclude `Migrations/`, `Dtos/`, `Messages/`, enum-only files.
   - If nothing is changed, ask which class/endpoint to cover.
2. **Read** every in-scope production file in full so the generated tests actually exercise the real surface.
3. **Read** at least one sibling test in the target test folder to mirror its style (fixture usage, TestData calls, Assert patterns).
4. **Classify** each in-scope file using the routing table below — pick exactly one home per class.
5. **Draft** tests: for each public method, one happy-path test plus one test per distinct error path (`ServiceError.*` factory, `null` asset, `Forbidden`, validation failure).
6. **Write** the test file. Do not invent new helpers — reuse what already exists.
7. **Verify**: `dotnet build` then `dotnet test --filter FullyQualifiedName~<NewTestClass>`. Fix failures before reporting done.

## Routing table

| Kind of code changed | Test project | Folder | Fixture / base |
|---|---|---|---|
| Pure service (no DB) | `AssetHub.Tests` | `Services/` | unit, `Moq` for deps |
| Service that hits DB | `AssetHub.Tests` | `Services/` | `[Collection("Database")]` + `PostgresFixture` |
| Repository | `AssetHub.Tests` | `Repositories/` | `[Collection("Database")]` + `PostgresFixture` |
| Minimal API endpoint | `AssetHub.Tests` | `Endpoints/` | `[Collection("Api")]` + `CustomWebApplicationFactory` |
| Auth / RBAC edge cases | `AssetHub.Tests` | `EdgeCases/` | usually `[Collection("Api")]` |
| Wolverine handler | `AssetHub.Tests` | `Handlers/` | unit, mock the service it calls |
| Blazor component | `AssetHub.Ui.Tests` | mirror `src/AssetHub.Ui/` | `bUnit` + MudBlazor test context |

## Conventions to follow

- **Naming**: `MethodName_Condition_ExpectedResult` — e.g., `SetAsync_UserLacksContributor_ReturnsForbidden`.
- **Arrange/Act/Assert**: keep the three blocks visually separated with blank lines.
- **One assertion concept per test** — multiple `Assert` calls are fine if they describe one outcome.
- **Seed via `TestData`** (`TestData.CreateAsset`, `CreateCollection`, …) — never hand-roll entities.
- **Auth setup**: `TestClaimsProvider.Default()` for contributor, `.Admin()` for sysadmin, `.WithUser(id, name, role)` for arbitrary identities.
- **Service mocks**: `Mock<ICollectionAuthorizationService>`, `Mock<IAssetRepository>`, etc. — wire only what the method under test touches.
- **Lifecycle**: implement `IAsyncLifetime` when the test class seeds/cleans rows; use `InitializeAsync` and `DisposeAsync`.
- **CancellationToken**: always pass `CancellationToken.None` in tests; never `default`.
- **`ServiceResult`**: prefer `Assert.True(result.IsSuccess)` + value checks, or `Assert.Equal("NOT_FOUND", result.Error!.Code)` for error paths. Don't assert on message text — it's not localized.
- **DB tests**: always go through the fixture's `CreateDbContextAsync()`; never `new AssetHubDbContext(...)` directly.

## Coverage targets per class

For each public method:

- **Happy path** — valid input, expected `ServiceResult.IsSuccess == true`, returned DTO matches.
- **Authorization** — if the method checks `CurrentUser`/`ICollectionAuthorizationService`, add a `Forbidden` test.
- **Not found** — if the method loads entities by id, add a `NotFound` test with `Guid.NewGuid()`.
- **Validation** — one test per DataAnnotation group that can fail (length, range, regex). For free-form `ServiceError.BadRequest` checks (e.g., schema-scoped validation in `AssetMetadataService`), cover each branch.
- **Conflict** — if the method does a uniqueness or duplicate check, exercise it.
- **State transitions** — for `Asset` methods (`MarkReady`, `MarkFailed`), cover valid → valid and invalid → no-op.

Skip: trivial DTO getters, `ToString()` overrides, pure pass-through wrappers.

## Output

Report:
- Files created (path, test count).
- Classes/methods deliberately skipped and why.
- `dotnet test` result for the new tests (count, duration).

Do not report done unless the new tests pass on a clean `dotnet build --configuration Release`.

## Abort conditions

- The target class has no public surface to exercise (only internal helpers).
- Required fixture plumbing is missing and cannot be reused — report to user instead of inventing new scaffolding.
- Tests require external services (SMTP, ClamAV, real MinIO) that aren't wired into the test harness — stub or skip with a note.
