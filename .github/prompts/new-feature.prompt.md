---
description: "Scaffold a full vertical-slice feature across all Clean Architecture layers: entity, interface, DTOs, service, repository, endpoint, and tests."
agent: "agent"
argument-hint: "Describe the feature, e.g. 'Add favorites — users can bookmark assets'"
---
Scaffold a complete vertical-slice feature for AssetHub following Clean Architecture. Generate all layers in order:

## 1. Domain Entity (`src/AssetHub.Domain/Entities/`)
- Simple POCO class with `Guid Id` and audit fields (`CreatedAt`, `CreatedByUserId`; add `UpdatedAt` if mutable).
- No base entity class — each entity is independent.
- JSONB-backed fields use `List<string>` or `Dictionary<string, object>`.
- New enums: add `ToDbString()` / reverse extension methods in `Enums.cs`.

## 2. Application Layer (`src/AssetHub.Application/`)
- **Repository interface** in `Repositories/` — async methods with `CancellationToken`.
- **Service interface** in `Services/` — returns `ServiceResult<T>`, XML doc comments on each method.
- **DTOs** in `Dtos/` — grouped in one file per domain:
  - Create DTO: `class` with `[Required]`, `[StringLength]` DataAnnotations.
  - Update DTO: `class` with nullable properties (null = don't update).
  - Response DTO: `record` or `class` with `required` properties.

## 3. Infrastructure Layer (`src/AssetHub.Infrastructure/`)
- **Repository** in `Repositories/` — `sealed class`, primary constructor with `AssetHubDbContext`, `HybridCache`, `ILogger<T>`. Async methods with `CancellationToken`.
- **Service** in `Services/` — `sealed class`, primary constructor, injects repos + `CurrentUser` + `IOptions<T>`. Returns `ServiceResult<T>`, never throws for business errors. Wrap external calls in Polly pipelines.
- **DbContext** — Add `DbSet<T>` and `OnModelCreating` configuration (JSONB with ValueComparer, string enums with `ToDbString()`, named indexes `idx_{entity}_{fields}`).
- **DI registration** — Register in `DependencyInjection/InfrastructureServiceExtensions.cs`. For Wolverine-handled services, register concrete type first then forward interface.

## 4. API Endpoint (`src/AssetHub.Api/Endpoints/`)
- Static class with `Map*Endpoints(this WebApplication app)`.
- Use `MapGroup()` for route prefix, `.RequireAuthorization("PolicyName")`, `.WithTags()`, `.WithName()`.
- Register in `WebApplicationExtensions.MapAssetHubEndpoints()`.
- Use `.ToHttpResult()` for ServiceResult → HTTP mapping; pass `onSuccess` callback for `201 Created`.
- Apply `ValidationFilter<T>` on create/update endpoints. Add `.DisableAntiforgery()` on POST/PATCH/DELETE.

## 5. Cache Keys (`src/AssetHub.Application/CacheKeys.cs`)
- Add private prefix constant, `public static readonly TimeSpan` TTL, `public static string` builder method.
- Add tag in `CacheKeys.Tags` if group invalidation is needed.
- Invalidate after create/update/delete in the service.

## 6. Audit Events
- Emit audit events via `IAuditService.LogAsync()` for all mutations in the service layer.
- Event type format: `{target}.{verb_past_tense}` in snake_case (e.g., `example.created`, `example.deleted`).
- Never audit from endpoints or UI — only from services and worker handlers.

## 7. Tests (`tests/AssetHub.Tests/`)
- Integration tests using `PostgresFixture` or `CustomWebApplicationFactory`.
- Use `TestData.Create*()` factory methods.
- Follow `MethodName_Condition_ExpectedResult` naming.
- Cover: happy path, not found, validation errors, authorization.
- Verify audit events are emitted for the happy path.

## 8. Localization (if UI-facing)
- Add keys to both `.resx` and `.sv.resx` in the most specific resource domain.
- Key pattern: `Area_Context_Element`.
- Add marker class in `ResourceMarkers.cs` if creating a new resource domain.

## Rules
- Respect layer dependency rules — Domain has zero references, Ui never touches Infrastructure.
- Use `sealed` on service/repository implementations.
- Use primary constructors throughout.
- Follow patterns in neighboring files — read existing code before generating.
- Run through the quality guardrails checklist before reporting done.
