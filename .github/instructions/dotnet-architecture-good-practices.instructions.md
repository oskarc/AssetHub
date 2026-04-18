---
description: "Clean Architecture guidelines for AssetHub"
applyTo: '**/*.cs,**/*.csproj,**/Program.cs,**/*.razor'
---

# Architecture — AssetHub Overrides

These rules override generic DDD/architecture guidance for the AssetHub project.

## AssetHub Architecture: Clean Architecture (not strict DDD)

AssetHub follows **Clean Architecture** with these layers:
- **Domain**: Entities, enums — no base classes, no value objects, no domain events, no specifications.
- **Application**: Service interfaces, **repository interfaces**, DTOs, configuration, `ServiceResult<T>`.
- **Infrastructure**: EF Core implementations, external service adapters, Polly resilience.
- **Api / Worker**: Composition roots — DI wiring, endpoint mapping, auth configuration.
- **Ui**: Blazor Server (Razor Class Library) — depends only on Application.

## What AssetHub Does NOT Use

Do not generate code using these patterns — they are not part of this codebase:
- **Domain events** — use Wolverine messages for async processing instead.
- **Value objects** — use primitive types or simple classes.
- **Specifications pattern** — use LINQ queries in repositories directly.
- **Rich domain models** — entities are simple with minimal behavior (only `Asset` has state methods).
- **Aggregate root pattern** — entities are standalone, no aggregate boundaries.
- **Event sourcing** — use standard EF Core persistence.

## Key Differences from Generic DDD Guidance

| Topic | Generic DDD Guidance | AssetHub Reality |
|-------|---------------------|-----------------|
| Repository interfaces | Domain layer | **Application layer** (`Application/Repositories/`) |
| Business logic | Rich domain models | **Service layer** (`Infrastructure/Services/`) returning `ServiceResult<T>` |
| Error handling | Domain exceptions | **`ServiceResult<T>`** — never throw for business errors |
| Cross-aggregate communication | Domain events | **Wolverine messages** via RabbitMQ |
| Test coverage | 85% minimum | No stated minimum — focus on critical paths |

## SOLID Principles Still Apply

The generic guidance on SOLID principles remains valid:
- **Single Responsibility** — services split by concern (commands, queries, uploads).
- **Dependency Inversion** — interfaces in Application, implementations in Infrastructure.
- **Interface Segregation** — separate query and command service interfaces.

## Testing Standards Still Apply

- **Test naming**: `MethodName_Condition_ExpectedResult`.
- **Unit tests** for service logic and domain rules.
- **Integration tests** for repositories and endpoints.
- **xUnit + Moq + Testcontainers** — not NUnit or MSTest.

## Quality Checklist

Before delivering code, verify these AssetHub-specific items:

### Architecture Validation
- Layer dependency rules respected (Domain → nothing, Application → Domain, Ui → Application only).
- No patterns from the "What AssetHub Does NOT Use" list introduced.
- Repository interfaces in Application, implementations in Infrastructure.
- Services return `ServiceResult<T>` — no thrown exceptions for business errors.

### Implementation Quality
- Tests follow `MethodName_Condition_ExpectedResult` naming.
- `sealed` on service/repository implementations.
- Primary constructors for DI injection.
- Structured logging with named arguments.
- Async/await for all I/O-bound operations.
- Nullable reference types handled correctly (`is null` / `is not null`).

### Security
- Authorization checks use `CurrentUser` and `RoleHierarchy` — no hardcoded role strings.
- Collection-scoped operations check `ICollectionAuthorizationService` before entity access.
- No secrets hardcoded — use configuration/environment variables.
