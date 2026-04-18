---
description: 'AssetHub task-to-file maps — which files to read before working on a given area'
applyTo: '**'
---
# Context Maps — AssetHub

Before starting work in any area, read the files listed below to understand existing patterns.

## Authentication & Authorization
- `src/AssetHub.Application/CurrentUser.cs`
- `src/AssetHub.Application/RoleHierarchy.cs`
- `src/AssetHub.Api/Extensions/AuthenticationExtensions.cs`
- `src/AssetHub.Infrastructure/Services/CollectionAuthorizationService.cs`

## Adding an API endpoint
- An existing endpoint in `src/AssetHub.Api/Endpoints/` (e.g., `AssetEndpoints.cs`)
- `src/AssetHub.Api/Extensions/WebApplicationExtensions.cs` (registration)
- `src/AssetHub.Api/Extensions/ServiceResultExtensions.cs` (result mapping)
- `src/AssetHub.Api/Filters/ValidationFilter.cs`

## Adding a service or repository
- An existing service pair (e.g., `AssetService.cs` + `AssetQueryService.cs`)
- `src/AssetHub.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`
- `src/AssetHub.Application/ServiceResult.cs`

## Caching
- `src/AssetHub.Application/CacheKeys.cs`
- An existing repo using `HybridCache` (e.g., `CollectionRepository.cs`)

## Blazor UI
- An existing page in `src/AssetHub.Ui/Pages/` for layout patterns
- `src/AssetHub.Ui/Services/AssetHubApiClient.cs`
- `src/AssetHub.Ui/Resources/ResourceMarkers.cs`
- The matching `.resx` + `.sv.resx` resource files

## Database changes
- `src/AssetHub.Infrastructure/Data/AssetHubDbContext.cs` (entity config)
- `src/AssetHub.Domain/Entities/` (entity definitions)
- Use the `migration` agent for generating migrations

## Worker / background jobs
- An existing handler in `src/AssetHub.Worker/Handlers/`
- `src/AssetHub.Application/Messages/` (commands and events)
- `src/AssetHub.Worker/Program.cs` (Wolverine + RabbitMQ config)

## Tests
- `tests/AssetHub.Tests/Fixtures/` (PostgresFixture, CustomWebApplicationFactory, TestAuthHandler)
- `tests/AssetHub.Tests/Helpers/TestData.cs`
- An existing test class in the same domain area

## Configuration
- `src/AssetHub.Application/Configuration/` (settings classes)
- `src/AssetHub.Api/appsettings.json` + `appsettings.Development.json`
- `src/AssetHub.Api/Extensions/ServiceCollectionExtensions.cs` (options registration)
