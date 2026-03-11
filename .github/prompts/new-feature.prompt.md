---
description: "Scaffold a full vertical-slice feature across all Clean Architecture layers: entity, interface, DTOs, service, repository, endpoint, and tests."
agent: "agent"
argument-hint: "Describe the feature, e.g. 'Add favorites — users can bookmark assets'"
---
Scaffold a complete vertical-slice feature for AssetHub following Clean Architecture. Generate all layers in order:

## 1. Domain Entity (`src/AssetHub.Domain/Entities/`)
- Simple POCO class with `Guid Id` and audit fields (`CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`).

## 2. Application Layer (`src/AssetHub.Application/`)
- **Repository interface** in `Repositories/` — async methods with `CancellationToken`.
- **Service interface** in `Services/` — returns `ServiceResult<T>`, XML doc comments on each method.
- **DTOs** in `Dtos/` — grouped in one file per domain:
  - Create DTO: `class` with `[Required]`, `[StringLength]` DataAnnotations.
  - Update DTO: `class` with nullable properties (null = don't update).
  - Response DTO: `record` or `class` with `required` properties.

## 3. Infrastructure Layer (`src/AssetHub.Infrastructure/`)
- **Repository** in `Repositories/` — `sealed class`, primary constructor with `AssetHubDbContext`, `IMemoryCache`, `ILogger<T>`.
- **Service** in `Services/` — `sealed class`, primary constructor, injects repos + `CurrentUser` + `IOptions<T>`. Returns `ServiceResult<T>`, never throws for business errors.
- **DbContext** — Add `DbSet<T>` and any `OnModelCreating` configuration.
- **DI registration** — Register in `DependencyInjection/`.

## 4. API Endpoint (`src/AssetHub.Api/Endpoints/`)
- Static class with `Map*Endpoints(this WebApplication app)`.
- Use `MapGroup()` for route prefix, `.RequireAuthorization("PolicyName")`.
- Register in `WebApplicationExtensions.MapAssetHubEndpoints()`.
- Map `ServiceResult` errors to appropriate HTTP status codes.

## 5. Tests (`tests/AssetHub.Tests/`)
- Integration tests using `PostgresFixture` or `CustomWebApplicationFactory`.
- Use `TestData.Create*()` factory methods.
- Follow `MethodName_Condition_ExpectedResult` naming.
- Cover: happy path, not found, validation errors, authorization.

## Rules
- Respect layer dependency rules — Domain has zero references, Ui never touches Infrastructure.
- Use `sealed` on service/repository implementations.
- Use primary constructors throughout.
- Follow patterns in neighboring files — read existing code before generating.
