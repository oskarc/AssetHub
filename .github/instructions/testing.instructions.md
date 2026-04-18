---
applyTo: "tests/**"
description: "Use when writing or editing unit tests, integration tests, bUnit component tests, or E2E Playwright tests for AssetHub."
---
# Testing Conventions

## Reference files — read before writing tests
- `tests/AssetHub.Tests/Fixtures/PostgresFixture.cs` — shared Testcontainer DB fixture
- `tests/AssetHub.Tests/Fixtures/CustomWebApplicationFactory.cs` — HTTP integration test factory
- `tests/AssetHub.Tests/Fixtures/TestAuthHandler.cs` + `TestClaimsProvider.cs` — fake auth
- `tests/AssetHub.Tests/Helpers/TestData.cs` — factory methods for test entities
- An existing test in the same domain (e.g., `Services/AssetServiceTests.cs`) — follow the same pattern

## Naming
`MethodName_Condition_ExpectedResult` — e.g., `UpdateAsync_EmptyTitle_ReturnsBadRequest`.

## Frameworks
| Project | Stack |
|---------|-------|
| `AssetHub.Tests` | xUnit + Moq + Testcontainers.PostgreSql |
| `AssetHub.Ui.Tests` | xUnit + bUnit (MudBlazor) |
| `E2E` | Playwright (TypeScript) with Page Object pattern |

## Fixture Selection
- **`PostgresFixture`** — for repository and service tests needing a real database. Uses `[Collection("Database")]` + `ICollectionFixture<PostgresFixture>`. Call `fixture.CreateDbContextAsync()` for a fresh DB per test class.
- **`CustomWebApplicationFactory`** — for endpoint/HTTP integration tests. Real Postgres, mocked externals (MinIO, Keycloak, Email, ClamAV). Uses `[Collection("Api")]`.
- **`TestAuthHandler`** — fake auth with `TestClaimsProvider`:
  ```csharp
  TestClaimsProvider.Default()           // viewer role
  TestClaimsProvider.Admin()             // admin role
  TestClaimsProvider.WithUser(id, name, role)  // custom
  ```

## Test Data
Use `TestData` factory methods with sensible defaults:
```csharp
TestData.CreateAsset()
TestData.CreateCollection()
TestData.CreateAcl()
TestData.CreateAssetCollection()
```
Override specific properties via optional parameters — don't construct entities manually.

## Lifecycle
Implement `IAsyncLifetime`:
- `InitializeAsync` — create DB context, seed test data.
- `DisposeAsync` — call `dbContext.Database.EnsureDeletedAsync()`.

## Structure
Mirror the source tree: `Services/`, `Repositories/`, `Endpoints/`, `EdgeCases/`.

## E2E (Playwright)
- Page Objects in `tests/E2E/tests/pages/*.ts`.
- Helpers in `tests/E2E/tests/helpers/` (`api-helper.ts`, `dialog-helper.ts`, `test-fixtures.ts`).
- Config/credentials in `tests/E2E/tests/config/env.ts`.
- Specs numbered for ordering: `01-auth.spec.ts`, `02-navigation.spec.ts`, etc.
