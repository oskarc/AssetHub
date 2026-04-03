# Copilot Instructions — AssetHub

AssetHub is a self-hosted digital asset management system built with ASP.NET Core 9, Blazor Server (MudBlazor 8), PostgreSQL, MinIO, Keycloak, Wolverine/RabbitMQ, and Redis. It follows Clean Architecture with strict dependency rules.

## Instruction Precedence

When instructions conflict, **project-specific files override generic ones**:
1. This file (`copilot-instructions.md`) — highest priority, defines AssetHub conventions.
2. Scoped instruction files in `.github/instructions/` with `applyTo` targeting `src/AssetHub.*/**` — project-specific layer guidance.
3. Generic instruction files (global user-level or broad `applyTo: '**'`) — general best practices; defer to the above when they disagree.

## Architecture

```
Domain  ←  Application  ←  Infrastructure  ←  Api / Worker
                ↑                                ↑
                Ui (Razor Class Library) ─────────┘
```

| Project | Role |
|---------|------|
| `AssetHub.Domain` | Entities, enums, value objects — zero dependencies |
| `AssetHub.Application` | Service interfaces, DTOs, config, `ServiceResult<T>`, `CurrentUser` |
| `AssetHub.Infrastructure` | EF Core repos, MinIO, SMTP, ClamAV, Keycloak, Polly pipelines |
| `AssetHub.Api` | Composition root — Minimal API endpoints, auth, DI wiring, Blazor host |
| `AssetHub.Ui` | Blazor Server components/pages (Razor Class Library, depends only on Application) |
| `AssetHub.Worker` | Wolverine message consumer — media processing, cleanup jobs |

**Layer rules — never violate:**
- Domain has no project references.
- Application references only Domain.
- Ui references only Application (never Infrastructure).
- Infrastructure references Application + Domain.
- Api and Worker are composition roots that wire everything together.

## Build & Run

```powershell
# Restore & build
dotnet restore
dotnet build --configuration Release    # must pass with zero warnings

# Run API locally (requires docker infra)
dotnet run --project src/AssetHub.Api

# Start infrastructure services
docker compose -f docker/docker-compose.yml up -d

# Full stack including app
docker compose -f docker/docker-compose.yml up --build
```

Hosts file entry required for OIDC: `127.0.0.1 assethub.local keycloak.assethub.local`

## Testing

```powershell
# Unit + integration tests (xUnit, Testcontainers — needs Docker running)
dotnet test --configuration Release

# With coverage
dotnet test --configuration Release --collect:"XPlat Code Coverage"

# Blazor component tests (bUnit) — included in above

# E2E (Playwright, TypeScript — needs app running)
cd tests/E2E
npm install
npx playwright install chromium
npx playwright test
```

### Test conventions
- **Naming**: `MethodName_Condition_ExpectedResult` (e.g., `UpdateAsync_EmptyTitle_ReturnsBadRequest`)
- **Framework**: xUnit + Moq + Testcontainers.PostgreSql
- **Fixtures**: `PostgresFixture` (shared Testcontainer), `CustomWebApplicationFactory` (real DB, mocked externals), `TestAuthHandler` (fake auth with `TestClaimsProvider`)
- **Lifecycle**: `IAsyncLifetime` — `InitializeAsync` seeds, `DisposeAsync` cleans up
- **Test data**: Use `TestData.CreateAsset()`, `TestData.CreateCollection()`, etc.
- **Structure**: Tests mirror source — `Services/`, `Repositories/`, `Endpoints/`, `EdgeCases/`

## Database & Migrations

EF Core 9 + Npgsql. Auto-migrates on startup. To add a migration:

```powershell
$env:EF_CONNECTION = "Host=localhost;Database=assethub;Username=assethub;Password=assethub123"
dotnet ef migrations add <Name> --project src/AssetHub.Infrastructure --startup-project src/AssetHub.Api
```

Notable: JSONB columns for `Tags`/`MetadataJson`, `pg_trgm` for trigram search, string-stored enums via value converters.

## Domain Entities

- **No base entity class** — each entity is independent.
- **Audit fields**: `CreatedAt` (UTC), `CreatedByUserId` on all entities; `UpdatedAt` on mutable ones.
- **No soft delete** — deletion is hard delete or status-based (`AssetStatus.Failed`, `.Uploading`).
- **JSONB fields**: `List<string> Tags`, `Dictionary<string, object> MetadataJson`, `PermissionsJson`.
- **Enums**: `AssetType`, `AssetStatus`, `ShareScopeType`, roles — stored as strings via value converters.

## Code Patterns

### Service Result pattern
Services return `ServiceResult<T>` — never throw for business errors:
```csharp
public async Task<ServiceResult<AssetDto>> GetByIdAsync(Guid id, CancellationToken ct)
{
    var asset = await _repo.GetByIdAsync(id, ct);
    if (asset is null) return ServiceError.NotFound("Asset not found");
    return new AssetDto(asset);
}
```
Error factories: `ServiceError.NotFound()`, `.Forbidden()`, `.BadRequest()`, `.Conflict()`, `.Validation()`, `.Server()`

### Minimal API endpoints
Static extension classes with `Map*Endpoints(this WebApplication app)`, registered in `WebApplicationExtensions.MapAssetHubEndpoints()`. Use `MapGroup()` for route prefixes and `.RequireAuthorization("PolicyName")`.

**ServiceResult → HTTP mapping** — call `.ToHttpResult()` (from `ServiceResultExtensions`):
- Success (void) → `204 No Content`
- Success with value → `200 OK`
- Custom success → pass `onSuccess` callback (e.g., `201 Created`)
- Errors → mapped via `ServiceError.StatusCode` + `ApiError` body; `403` uses `Results.Forbid()`

**Validation** — apply `ValidationFilter<T>` per-endpoint: `.AddEndpointFilter<ValidationFilter<CreateAssetDto>>()`. Uses DataAnnotations — not FluentValidation.

**Request binding**: route `{id:guid}`, query `[AsParameters] QueryDto`, form `[FromForm]` (uploads), JSON body by convention, services via `[FromServices]`.

**Anti-forgery**: `.DisableAntiforgery()` on API endpoints (JWT clients are CSRF-immune). Blazor forms enforce antiforgery via middleware.

### Infrastructure services
- `sealed class` with primary constructors
- Split by concern: `AssetService` (commands), `AssetQueryService` (queries), `AssetUploadService` (uploads)
- Inject repos + service interfaces + `CurrentUser` + `IOptions<T>`
- Wrap external calls in Polly resilience pipelines (`"minio"`, `"clamav"`, `"smtp"`)

### Repositories
- Interface in `Application/Repositories/`, implementation in `Infrastructure/Repositories/`
- No base repository — each is standalone
- Primary constructors with `AssetHubDbContext`, `HybridCache`, `ILogger<T>`
- Async methods with `CancellationToken`, pagination via `skip`/`take`

### Background jobs (Worker)
- Wolverine message consumers handle commands from RabbitMQ queues (`process-image`, `process-video`, `build-zip`)
- Background `IHostedService` classes for scheduled cleanup (stale uploads, orphaned shares, audit retention)
- Primary constructor DI with `IServiceScopeFactory` + `ILogger<T>`
- Create own scope in `ExecuteAsync()` to resolve scoped services
- Batch processing with per-item try/catch for resilience

### DTOs
- Grouped by domain in single files (e.g., `CollectionDtos.cs`)
- Create DTOs: `class` with `[Required]`, `[StringLength]` DataAnnotations
- Update DTOs: `class` with nullable properties (null = don't update)
- Response DTOs: `record` or `class` with `required` properties

### Blazor UI (AssetHub.Ui)
- MudBlazor 8 components throughout
- `AssetHubApiClient` as the single typed HTTP client for all API calls
- `IStringLocalizer<T>` for localization — Swedish (`sv`) and English (default)
- Resource markers in `Resources/ResourceMarkers.cs`, key naming: `Area_Context_Element`
- Dialogs: `*Dialog.razor` components with `MudDialog`
- Pages: `@attribute [Authorize]`, `@implements IAsyncDisposable`

### Configuration
Settings classes in `Application/Configuration/` with `const string SectionName`:
`AppSettings`, `EmailSettings`, `ImageProcessingSettings`, `KeycloakSettings`, `MinIOSettings`, `OpenTelemetrySettings`, `RabbitMQSettings`, `RedisSettings`

## Security

- **Auth**: PolicyScheme routes `Bearer` → JWT, everything else → Cookie/OIDC (Keycloak)
- **Policies**: `RequireViewer`, `RequireContributor`, `RequireManager`, `RequireAdmin` (hierarchical roles)
- **Per-collection RBAC**: ACLs via `CollectionAcl` entity; role checks in `RoleHierarchy.cs`
- **Upload pipeline**: Content-type allowlist → magic byte validation → ClamAV scan (fail-closed) → size/batch limits → filename sanitization
- **Data protection**: Share tokens encrypted via ASP.NET Data Protection; passwords BCrypt-hashed
- **Rate limiting**: Per-user (200/min), SignalR (60 conn/min), anonymous shares (30/min), password attempts (10/5min)

## Error Handling

- Business errors → `ServiceResult` (never throw).
- Unhandled exceptions → global exception middleware in `WebApplicationExtensions` catches for `/api/*` paths:
  - `UnauthorizedAccessException` → 401, `StorageException` → 503, `BadHttpRequestException` → 400, unhandled → 500.
- Error response body is always `ApiError { Code, Message, Details }` (defined in `Application/Dtos/ApiError.cs`).

## DI Registration

- Shared infrastructure registered via `AddSharedInfrastructure()` (used by both Api and Worker).
- API-specific services registered in `ServiceCollectionExtensions.AddAssetHubServices()`.
- **Keyed services**: MinIO has internal + public clients (`AddKeyedSingleton<IMinioClient>("public", ...)`).
- **Service forwarding**: register concrete type first, then interface forwarding: `AddScoped<MediaProcessingService>()` + `AddScoped<IMediaProcessingService>(sp => sp.GetRequiredService<...>())`.
- **Wolverine + RabbitMQ**: commands (`ProcessImageCommand`, `ProcessVideoCommand`, `BuildZipCommand`) published to queues; events (`AssetProcessingCompletedEvent`, `AssetProcessingFailedEvent`) consumed back. Configured in `Program.cs`.
- **HybridCache**: L1 in-memory + L2 Redis, configured via `AddSharedInfrastructure()`.
- **Options validation**: critical settings use `.ValidateOnStart()`; optional settings (Email, ImageProcessing) do not.

## CI Pipeline (`.github/workflows/ci.yml`)

1. **Build** — `dotnet build --configuration Release` must pass with **zero warnings**.
2. **Test** — `dotnet test` with XPlat Code Coverage (Cobertura); results uploaded as artifacts.
3. **Security audit** — `dotnet list package --vulnerable --include-transitive`; fails if any found.
4. **Docker build** (main branch only) — builds API + Worker images, scans with Trivy.
5. **Infra image scan** (main branch only) — builds and scans patched RabbitMQ image with Trivy.

## Conventions

- **C#**: PascalCase for types/methods/properties, camelCase for locals/parameters
- Nullable reference types enabled globally
- `sealed` on service implementations
- Primary constructors preferred
- XML doc comments on interface methods
- One feature/fix per PR; include tests; update docs if user-facing
- Follow existing patterns — look at neighboring files before adding new code

## Key Files

| What | Where |
|------|-------|
| DI wiring | `src/AssetHub.Api/Extensions/ServiceCollectionExtensions.cs` |
| Auth config | `src/AssetHub.Api/Extensions/AuthenticationExtensions.cs` |
| All endpoints | `src/AssetHub.Api/Endpoints/*.cs` |
| Service interfaces | `src/AssetHub.Application/Services/*.cs` |
| Repository interfaces | `src/AssetHub.Application/Repositories/*.cs` |
| EF DbContext | `src/AssetHub.Infrastructure/Data/AssetHubDbContext.cs` |
| Domain entities | `src/AssetHub.Domain/Entities/*.cs` |
| Blazor pages | `src/AssetHub.Ui/Pages/*.razor` |
| Test fixtures | `tests/AssetHub.Tests/Fixtures/*.cs` |
| E2E page objects | `tests/E2E/tests/pages/*.ts` |
| Docker stack | `docker/docker-compose.yml` |
| Credentials | `CREDENTIALS.md` |
