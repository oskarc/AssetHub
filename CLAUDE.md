# AssetHub — Project Instructions

## Project Overview

AssetHub is a digital asset management system. It uses **C# 13 / .NET 9**, **Blazor Server**, **PostgreSQL**, **MinIO** (S3-compatible storage), **RabbitMQ** (via Wolverine), **Redis** (HybridCache L2), and **Keycloak** (OIDC auth). Do not use C# 14 features.

---

## Architecture (Clean Architecture)

Layers:

| Layer | Purpose | References |
|-------|---------|------------|
| **Domain** | Entities, enums — no base classes, no value objects, no domain events | Nothing |
| **Application** | Service interfaces, repository interfaces, DTOs, `ServiceResult<T>`, configuration, messages | Domain |
| **Infrastructure** | EF Core repos, service implementations, external adapters, Polly resilience | Application + Domain |
| **Api** | Composition root — DI, endpoint mapping, auth config, hosts Blazor Server | All |
| **Worker** | Composition root — Wolverine handlers, `IHostedService` background jobs | All |
| **Ui** | Blazor Server (Razor Class Library) | Application only — never reference Infrastructure or Api |

### Patterns NOT used in this project

Do not generate code using these patterns:
- Domain events (use Wolverine messages instead)
- Value objects (use primitives or simple classes)
- Specifications pattern (use LINQ in repositories)
- Rich domain models (entities are simple; only `Asset` has state methods)
- Aggregate root pattern (entities are standalone)
- Event sourcing (use standard EF Core persistence)
- FluentValidation (use DataAnnotations only)
- Third-party state management (no Fluxor, BlazorState, Blazored.LocalStorage)
- ASP.NET Identity / Microsoft Entra ID (use Keycloak OIDC)

OpenAPI / Swagger **is** used, but only for the curated public integration surface — see the "Public API contract" section below.

### SOLID principles apply

- Single Responsibility — services split by concern (commands, queries, uploads).
- Dependency Inversion — interfaces in Application, implementations in Infrastructure.
- Interface Segregation — separate query and command service interfaces.

---

## C# Conventions

- **Nullable reference types** enabled globally — use `is null` / `is not null`, never `== null` / `!= null` in plain C#. (EF Core LINQ predicates inside `.Where(...)` / `.Count(...)` are the lone exception: `s.RevokedAt == null` translates to SQL; `is null` may not. Use `==`/`!=` only inside expression trees that hit the database.)
- **`sealed` on every service, repository, adapter, and background-service implementation. No exceptions.** Concrete classes that aren't designed for inheritance must say so. If you find yourself adding a `public class Foo : IFoo`, change it to `public sealed class` in the same edit. **Same for private nested classes / records inside Razor `@code` blocks** — `private sealed class FormModel`, `private sealed record JsResult(...)`. Sonar's S3260 catches the rest, but writing it sealed first is cheaper than fixing it later.
- **`private readonly` for fields that aren't reassigned.** Especially `CancellationTokenSource _cts = new()`, `List<X> _items = new()`, dialog `_form` refs, table refs — anything initialized at field declaration and only mutated through methods (Add / Cancel / Dispose) is `readonly`. Sonar's S2933 fires hard on these.
- **`private static` for methods that don't touch `this`.** Pure helpers in Razor / services should be static — Sonar's S2325 catches them otherwise.
- **No empty catch blocks.** A `catch (JSDisconnectedException) { }` or `catch { }` is S108 + a code smell. Either fill it with a one-line comment explaining why the exception is benign (`/* circuit gone — JS module unreachable */`), or delete the catch and let the exception bubble. Empty-with-just-a-comment is fine; empty-with-nothing is not.
- **No nested ternaries.** S3358 catches `a ? b : c ? d : e`. Hoist branches into local variables or use an `if / else if / else` chain. Object-initializer bodies are common offenders — extract the branch above the `new { ... }` block.
- **No floating-point `==`.** S1244. Use `Math.Abs(a - b) < epsilon`, or define an `IsApprox` helper and reuse it.
- **No hardcoded credentials, including "well-known" defaults.** S2068. `?? "guest"` for RabbitMQ Username/Password is the exact regression we just fixed — use `?? string.Empty` and let `ValidateOnStart()` fail loudly. Same shape applies to `?? "admin"` / `?? "postgres"` / any literal that maps to a default credential. UI mask placeholders (`"********"` etc.) are not credentials but require a `[SuppressMessage("...", "S2068", Justification = "...")]` to silence the rule.
- **Primary constructors** preferred for DI injection in services and repositories.
- **DataAnnotations only** for DTO validation — `[Required]`, `[StringLength]`, `[Range]`.
- **File-scoped namespaces**, pattern matching, `nameof`.
- **Async/await** for I/O-bound operations.
- **Structured logging** with named arguments: `logger.LogInformation("Processed {AssetId}", id)`.
- **PascalCase** for types/methods/public members, **camelCase** for locals/fields.
- **`I`** prefix for interfaces.
- Apply `.editorconfig` formatting rules from the repository root.

---

## Domain Entities (`AssetHub.Domain`)

Domain has **zero project references** — only entities, enums, and extension methods. Never add NuGet packages here.

### Entity structure
- No base entity class — each entity is standalone.
- No parameterless constructors — use property initializers for defaults.
- Audit fields: `CreatedAt` (UTC) on all entities. Creator field naming varies:
  - `CreatedByUserId` — standard (Asset, Collection, Share)
  - `AddedByUserId` — join tables (AssetCollection)
  - `ActorUserId` — audit records (AuditEvent, nullable for system events)
  - `RequestedByUserId` — request entities (ZipDownload, nullable for anonymous)
- `UpdatedAt` on `Asset`; new mutable entities should include it.
- No soft delete by default — use status-based lifecycle or hard delete. **Exception**: `Asset` uses soft delete via nullable `DeletedAt` + `DeletedByUserId` (T1-LIFE-01). Repositories filter on `DeletedAt IS NULL` via a global EF query filter; trash and purge paths use `IgnoreQueryFilters()`. The `TrashPurgeBackgroundService` hard-deletes rows older than `AssetLifecycleSettings.TrashRetentionDays`.
- JSONB fields: `List<string>` for tags, `Dictionary<string, object>` for metadata. Initialize with `new()`.
- Navigation properties: `ICollection<T>` with `new List<T>()` default.

### State transitions
Only `Asset` has state transition methods (`MarkReady`, `MarkFailed`, etc.) — never set `Status` directly from services. Other entities have status set directly by services.

### Enums
Stored as strings via extension methods defined alongside the enum:
```csharp
public static string ToDbString(this ExampleStatus s) => s switch { ... };
public static ExampleStatus ToExampleStatus(this string s) => s switch { ... };
```
Never use `int` or `ToString()` for database storage.

---

## Infrastructure Services (`AssetHub.Infrastructure`)

### Class structure
All services are `sealed class` with primary constructors:
```csharp
public sealed class ExampleService(
    IExampleRepository repo,
    CurrentUser currentUser,
    ILogger<ExampleService> logger) : IExampleService
```

### Separation of concern
Split large domains: commands (`AssetService`), queries (`AssetQueryService`), specialized I/O (`AssetUploadService`).

### Polly resilience
Wrap external calls in named pipelines: `"minio"` (retry 3x, circuit breaker 30s), `"clamav"` (retry 2x, circuit breaker 60s), `"smtp"` (retry 2x).

### Return values
Always return `ServiceResult<T>` — never throw for business errors:
```csharp
if (asset is null) return ServiceError.NotFound("Asset not found");
return new AssetDto(asset);
```

### Logging levels
- `Information` — successful operations and summaries.
- `Warning` — non-critical, recoverable failures.
- `Error` — unrecoverable failures.

### Repositories
Primary constructors with `AssetHubDbContext`, `HybridCache`, `ILogger<T>`. Use `HybridCache` for hot-path lookups (see Caching section). Query patterns:
- `.AsNoTracking()` for reads.
- `.Skip().Take()` for pagination (count first).
- `.Select()` projections for minimal data transfer.
- `.ToDictionary(a => a.Id)` to avoid N+1.

### DbContext configuration
- All entity config inline in `OnModelCreating()` — no separate configuration classes.
- JSONB columns: include column type, JSON serialization converter, and a **ValueComparer** (critical for change tracking).
- Enums stored as strings via `ToDbString()` / `ToExampleStatus()` extension methods.
- Index naming: `idx_{entity}_{fields}` with `_unique` suffix for unique.
- Foreign keys: always specify `OnDelete` behavior explicitly.

### DI registration
In `DependencyInjection/InfrastructureServiceExtensions.cs`:
- `AddScoped<IRepo, Repo>()`, `AddScoped<IService, Service>()`.
- Wolverine-handled services: register concrete first, then forward interface.

---

## Error Handling

### ServiceResult — the only way services report errors

Services **never throw** for business errors. All methods return `ServiceResult` or `ServiceResult<T>`.

| Factory | HTTP | Code | When |
|---------|------|------|------|
| `ServiceError.NotFound(msg)` | 404 | `NOT_FOUND` | Entity not found |
| `ServiceError.Forbidden(msg)` | 403 | `FORBIDDEN` | User lacks permission |
| `ServiceError.BadRequest(msg)` | 400 | `BAD_REQUEST` | Invalid input |
| `ServiceError.Conflict(msg)` | 409 | `CONFLICT` | Duplicate or state conflict |
| `ServiceError.Validation(msg, details)` | 400 | `VALIDATION_ERROR` | Field-level errors |
| `ServiceError.Server(msg)` | 500 | `SERVER_ERROR` | Unexpected failure |

### Endpoint layer
Endpoints call `.ToHttpResult()` — never manually inspect `IsSuccess` or map errors:
```csharp
return (await svc.GetByIdAsync(id, ct)).ToHttpResult();
// Custom success: 201 Created
return (await svc.CreateAsync(dto, ct))
    .ToHttpResult(value => Results.Created($"/api/v1/items/{value.Id}", value));
```

### Exceptions (infrastructure only)
Unhandled exceptions caught by global middleware -> `500 + ApiError`. When catching infra exceptions in a service, wrap in `ServiceError.Server()`.

---

## API Endpoints (`AssetHub.Api`)

- One static class per domain: `AssetEndpoints`, `CollectionEndpoints`, etc.
- Extension method: `Map*Endpoints(this WebApplication app)`.
- All endpoints registered in `WebApplicationExtensions.MapAssetHubEndpoints()`.

### Route groups

Every `MapGroup` that hosts at least one POST / PATCH / PUT / DELETE endpoint **must** chain `.RequireAntiforgeryUnlessBearer()`. The filter no-ops on Bearer (JWT/PAT) and anonymous traffic, so it's safe to apply on groups that mix authenticated mutations with public reads. Per-endpoint `.DisableAntiforgery()` then turns off the default ASP.NET antiforgery for the API surface — the filter is the only CSRF gate left, and skipping it on a mutating group means cookie-authed clients (Blazor UI XSS payloads) can hit those endpoints without a token. P-12 / A-7 was specifically about closing this; don't reopen it.

```csharp
var group = app.MapGroup("/api/v1/examples")
    .RequireAuthorization("RequireViewer")
    .RequireAntiforgeryUnlessBearer()
    .WithTags("Examples");
```

### Request binding
- Route: `Guid id` with `{id:guid}` constraint.
- Query strings: `[AsParameters] SearchQueryDto dto`.
- JSON body: automatic by parameter name.
- Form data: `[FromForm]` for file uploads.
- Services: `[FromServices] IAssetService svc`.

### Validation
Apply `ValidationFilter<T>` per-endpoint. DTOs use DataAnnotations. Returns `400` with field-level `ApiError.Details`.

### Authorization
Policies: `RequireViewer`, `RequireContributor`, `RequireManager`, `RequireAdmin`. Prefer group-level. Collection RBAC: inject `ICollectionAuthorizationService`.

### Anti-forgery
`.DisableAntiforgery()` on all API POST/PATCH/DELETE endpoints — that turns off ASP.NET's built-in antiforgery validation. The actual CSRF gate is `.RequireAntiforgeryUnlessBearer()` on the route group (see "Route groups" above): it validates the `X-CSRF-TOKEN` header for cookie principals and skips Bearer / anonymous. **Both must be present** on a mutating group: `DisableAntiforgery` for clean Bearer flow, `RequireAntiforgeryUnlessBearer` for cookie CSRF defense.

### Error response format

All error returns from endpoints flow through `ServiceResult.ToHttpResult()`, which produces:
```json
{ "code": "NOT_FOUND", "message": "Asset not found", "details": {} }
```

When you can't use `ServiceResult` because the validation fires before the service call (e.g., `IFormFile` parameter binding for uploads), use `Results.BadRequest(ApiError.BadRequest("…"))` — the `ApiError` factories in `AssetHub.Application.Dtos` produce the same shape. **Never** return `Results.BadRequest(new { error = "…" })` — that ships an inconsistent shape that breaks SDK consumers reading the OpenAPI schema.

### Public API contract (`[PublicApi]` + OpenAPI)

A curated subset of endpoints forms the stable public REST contract consumed by external integrations, CI scripts, and migration tools. The rest (admin UX, UI-only helpers) remains functional but undocumented.

- Mark public endpoints with `.MarkAsPublicApi()` (in `AssetHub.Api.OpenApi`). Only marked endpoints appear in the generated OpenAPI document at `/swagger/v1/swagger.json`.
- Every public-API endpoint **must** also carry a `RequireScopeFilter` with the appropriate `assets:read`/`assets:write`/`collections:read`/`collections:write`/`search:read` scope — never ship a `[PublicApi]` endpoint without one (see PAT section below).
  - **One documented exception**: PAT self-service endpoints (`/api/v1/me/personal-access-tokens/*`) skip `RequireScopeFilter` because they're guarded by a `pat_id` claim check that *strictly forbids* PAT principals from reaching them at all. No scope (including `admin`) is enough. If you add a new "PAT-can-never-do-this" surface, follow the same pattern: `pat_id` guard inside the handler, no `RequireScopeFilter`, comment in the route mapping pointing back at this rule.
- Changes to `[PublicApi]` endpoints are breaking changes under SemVer. Renames, removals, and type changes need a version bump or a deprecation path.
- Swagger UI lives at `/swagger`. Anonymous in Development; `RequireAdmin` gated in every other environment via middleware in `UseAssetHubMiddleware` + `RequireAuthorization` on the JSON endpoint.

---

## Security & Authorization

### CurrentUser
Inject `CurrentUser` (scoped) — never access `HttpContext.User` directly. `CurrentUser.Anonymous` for background jobs.

### Role hierarchy
`viewer (1) < contributor (2) < manager (3) < admin (4)`. Use `RoleHierarchy` predicate methods (`CanUpload`, `CanDelete`, `HasSufficientLevel`) — never hardcode role levels or string comparisons.

### Collection-scoped RBAC
Per-collection permissions via `CollectionAcl`. Check through `CollectionAuthorizationService`. System admins bypass ACL checks. Check collection access before entity access. Use `PreloadUserRolesAsync()` for batch checks.

### Rules
- Never cache ACL/roles globally — use request-scoped dictionaries.
- Never skip role level checks on role-assigning mutations.
- Never trust client-supplied role values without `HasSufficientLevel()`.
- Never expose other users' IDs without authorization checks.

### Personal Access Tokens (PATs) & scope enforcement

External callers authenticate using either an OIDC JWT **or** a Personal Access Token. The "Smart" authentication scheme selector routes `Authorization: Bearer pat_*` headers to the PAT handler and everything else to JWT / Cookie.

**Token lifecycle.** Users mint PATs on the `/account` page. Plaintext format is `pat_` + 32-char base64url (24 bytes of CSPRNG entropy); only the SHA-256 hash is persisted. Plaintext is returned once in `CreatedPersonalAccessTokenDto.PlaintextToken` and never logged or re-rendered. Idempotent revoke, optional expiry, and `pat.created` / `pat.revoked` audit events.

**Scope enforcement is mandatory on public endpoints.** Allowed scopes are declared in `PersonalAccessTokenDto.AllowedScopes` (`assets:read`, `assets:write`, `collections:read`, `collections:write`, `shares:read`, `shares:write`, `search:read`, `admin`). Enforce with `RequireScopeFilter`:

```csharp
group.MapGet("{id:guid}", GetAsset)
    .AddEndpointFilter(new RequireScopeFilter("assets:read"))
    .MarkAsPublicApi();
```

Filter behaviour: cookie / JWT principals pass through unchanged (no `pat_scope` claims). A PAT with zero scopes is owner-impersonation and passes every check. The `admin` scope is a wildcard. Case-sensitive ordinal comparison.

**Privilege-escalation guard.** A PAT-authenticated principal must **not** be able to mint or revoke PATs. Endpoints that do so (see `PersonalAccessTokenEndpoints`) check for the presence of a `pat_id` claim and return `403` when it's set. Apply the same guard anywhere a caller could otherwise "bootstrap" new long-lived credentials from a compromised token.

**Rules:**
- Every `[PublicApi]` endpoint ships with a `RequireScopeFilter` — no exceptions.
- Never skip the `pat_id` guard when adding mutating self-service endpoints.
- Keycloak realm roles for PAT principals are fetched via `IKeycloakUserService.GetUserRealmRolesAsync` and cached 1 min via `CacheKeys.UserRealmRoles`; do not cache longer.

---

## Blazor UI (`AssetHub.Ui`)

Razor Class Library that depends **only** on Application. Never reference Infrastructure or Api.

### Component library
**MudBlazor 8** exclusively — no raw HTML form elements when a MudBlazor equivalent exists. No other component libraries.

### Pages
- `@attribute [Authorize]` on all pages (except public share pages).
- `@implements IAsyncDisposable` when using event subscriptions or timers.
- Common injections: `AssetHubApiClient`, `NavigationManager`, `IUserFeedbackService`, `IDialogService`, `IStringLocalizer<T>`.

### API communication
Use `AssetHubApiClient` for all HTTP calls — never `HttpClient` directly. Handle `ServiceResult<T>` errors via `IUserFeedbackService`.

### Dialogs
- Named `*Dialog.razor`.
- `MudDialog` with `[CascadingParameter] IMudDialogInstance`.
- Return via `MudDialog.Close(DialogResult.Ok(value))`.

### State management
No third-party libraries. Use scoped services, `CascadingAuthenticationState`, and `MudDialogService`.

### Caching
**HybridCache** (L1 in-memory + L2 Redis) — not `IMemoryCache` or localStorage/sessionStorage.

### Error handling
Use `ErrorBoundary` for UI errors.

### Optimistic UI

Prefer optimistic UI updates for user-initiated mutations (delete, rename, add to collection, toggle, reorder). The goal is to make the UI feel instant — update local state first, then confirm with the server.

**Pattern:**
```razor
@code {
    private async Task DeleteItemAsync(Guid id)
    {
        // 1. Optimistically update local state
        var removedItem = _items.First(i => i.Id == id);
        _items.Remove(removedItem);
        StateHasChanged();

        // 2. Call the API
        var result = await Api.DeleteAsync(id);

        // 3. On failure: roll back and notify
        if (!result.IsSuccess)
        {
            _items.Add(removedItem);
            StateHasChanged();
            Feedback.ShowError(CommonLoc["Error_DeleteFailed"].Value);
        }
    }
}
```

**When to use optimistic UI:**
- Deleting items from lists (assets, collections, ACLs)
- Toggling boolean state (active/inactive, favorite)
- Adding/removing items from collections
- Renaming or updating single fields
- Reordering items

**When NOT to use optimistic UI:**
- File uploads (progress is real, can't fake it)
- Complex multi-step operations (creation wizards, bulk operations)
- Operations where failure is common (validation-heavy forms)
- Navigation after mutation (just await and navigate)

**Rules:**
- Always roll back local state on `ServiceResult` failure and show an error via `IUserFeedbackService`.
- Keep a reference to the removed/changed item before mutating so rollback is trivial.
- Don't optimistically update data that other components depend on (e.g., counts in the sidebar) — let those refresh naturally or refresh after confirmation.
- Success feedback (snackbar) can fire immediately with the optimistic update — the user sees instant confirmation.

### Layouts
- `MainLayout.razor` — authenticated app shell with nav menu.
- `ShareLayout.razor` — separate layout for public share pages (no nav).

---

## Localization

Two languages: English (default `.resx`) and Swedish (`.sv.resx`). Every user-visible string must use `IStringLocalizer<T>`.

### Resource structure
```
src/AssetHub.Ui/Resources/
  ResourceMarkers.cs          <- empty marker classes for IStringLocalizer<T>
  CommonResource.resx / .sv.resx
  AssetsResource.resx / .sv.resx
  CollectionsResource.resx / .sv.resx
  AdminResource.resx / .sv.resx
  ImageEditorResource.resx / .sv.resx
  ...
```

### Key naming
Pattern: **`Area_Context_Element`** in PascalCase with underscores (`Assets_Upload_Title`, `Common_Btn_Cancel`). `Common_` prefix for shared strings.

### Rules
- Add keys to **both** `.resx` and `.sv.resx` together — missing keys fall back to English silently. To audit parity, run `diff <(grep -oE 'data name="[^"]+"' Foo.resx | sort) <(grep -oE 'data name="[^"]+"' Foo.sv.resx | sort)` for each pair; output should be empty.
- Inject the most specific localizer (e.g., `AssetsResource` for asset strings, not `CommonResource`).
- Never use raw string literals for user-visible text.
- Service error messages (`ServiceResult` errors) are not localized — the UI translates them into user-friendly messages.
- When adding a new resource domain, add both the marker class in `ResourceMarkers.cs` and the `.resx` / `.sv.resx` file pair.

---

## Caching

Uses **HybridCache** (L1 in-memory + L2 Redis). All cache config centralized in `Application/CacheKeys.cs`.

### Adding a new cache key
1. Private prefix constant in `CacheKeys`.
2. `public static readonly TimeSpan` TTL field.
3. `public static string` builder method.
4. Tag in `CacheKeys.Tags` if group invalidation is needed.

### Usage
```csharp
var data = await _cache.GetOrCreateAsync(
    CacheKeys.Example(id),
    async ct => await _repo.GetByIdAsync(id, ct),
    new HybridCacheEntryOptions
    {
        Expiration = CacheKeys.ExampleTtl,
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    },
    tags: [CacheKeys.Tags.Example(id)],
    cancellationToken: ct);
```

### Invalidation
Prefer tag-based: `await _cache.RemoveByTagAsync(CacheKeys.Tags.Example(id), ct)`. Always invalidate after create/update/delete.

### Must NOT cache
- Authorization roles/ACL lookups (use request-scoped dictionaries).
- Security tokens or passwords.
- Presigned URLs (already cached in `MinIOAdapter`).

---

## Database Migrations

### Generating
```powershell
dotnet ef migrations add <PascalCaseName> --project src/AssetHub.Infrastructure --startup-project src/AssetHub.Api
```

### Safety rules
Never in the same migration: drop + remove referencing code, rename without data migration, change type without conversion SQL.

Always include:
- `Down()` method that reverses the `Up()`.
- Index names: `idx_{entity}_{fields}` (+ `_unique` for unique).

### JSONB columns
Set column type explicitly in migration (`type: "jsonb"`). Corresponding `OnModelCreating` must include `ValueComparer`.

### pg_trgm indexes
Create via raw SQL: `CREATE INDEX IF NOT EXISTS idx_asset_title_trgm ON "Assets" USING gin ("Title" gin_trgm_ops);`.

### Auto-migration
AssetHub auto-migrates on startup (`Database.MigrateAsync()`). Migrations must be idempotent (`IF NOT EXISTS` in raw SQL). Both Api and Worker run migrations — first to acquire lock applies.

### Pending model changes
`AddSharedInfrastructure` configures EF's `PendingModelChangesWarning` to **throw outside Development** and **log inside Development**. That means CI / staging / production refuse to start if the EF model has drifted from the latest migration — a forgotten `dotnet ef migrations add` fails fast instead of silently shipping a mismatched schema. Don't downgrade this to `Log` globally to "fix" a startup error; generate the missing migration instead.

---

## Worker (`AssetHub.Worker`)

Uses `Host.CreateDefaultBuilder()` with `.UseWolverine()` (no HTTP pipeline).

### Message handlers
Wolverine auto-discovers public `HandleAsync()` methods in `Handlers/`:
```csharp
public sealed class ProcessImageHandler(
    IMediaProcessingService mediaProcessingService,
    ILogger<ProcessImageHandler> logger)
{
    public async Task<object[]> HandleAsync(ProcessImageCommand command, CancellationToken ct)
    {
        // Process, return events
        return [new AssetProcessingCompletedEvent(command.AssetId)];
    }
}
```
- Commands/events defined in `Application/Messages/`.
- Queues: `process-image`, `process-video`, `build-zip`. Auto-provisioned.
- Auto-retry with exponential backoff (1s -> 2s -> 5s -> 10s -> 30s).

### Background services
`BackgroundService` with `PeriodicTimer`. Use `IServiceScopeFactory` — never inject scoped services directly. Create scope per iteration.

### Error handling
- Per-item try/catch in batch loops (one failure doesn't stop the batch).
- `ct.ThrowIfCancellationRequested()` in long-running loops.
- Catch `OperationCanceledException` at top level.

### Logging
- `Information`: start, completion summary.
- `Debug`: per-batch progress, "nothing to do".
- `Warning`: per-item failures, cancellation.
- Always include counts.

---

## Configuration & Secrets

### Settings classes
Live in `Application/Configuration/` with `const string SectionName`:
```csharp
public class ExampleSettings
{
    public const string SectionName = "Example";
    [Required] public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5672;
}
```

### Registration
Critical infra settings use `.ValidateOnStart()` (Keycloak, MinIO, PostgreSQL, RabbitMQ, Redis). Optional features don't (Email, ImageProcessing).

### Secrets
- Never hardcode secrets.
- Environment variables override via `__` -> `:` mapping.
- Production uses Docker file-based secrets, not env vars.

### Existing settings

| Class | Section | ValidateOnStart | Purpose |
|-------|---------|:-:|---------|
| `AppSettings` | `App` | Yes | Base URL, upload limits |
| `KeycloakSettings` | `Keycloak` | Yes | OIDC authority, client ID/secret |
| `MinIOSettings` | `MinIO` | Yes | Endpoint, bucket, credentials |
| `RabbitMQSettings` | `RabbitMQ` | Yes | Host, port, credentials |
| `RedisSettings` | `Redis` | No | Connection string (optional) |
| `EmailSettings` | `Email` | No | SMTP config (optional) |
| `ImageProcessingSettings` | `ImageProcessing` | No | Thumbnail/medium dimensions |
| `OpenTelemetrySettings` | `OpenTelemetry` | No | OTLP endpoint, service name |

---

## Testing

### Frameworks

| Project | Stack |
|---------|-------|
| `AssetHub.Tests` | xUnit + Moq + Testcontainers.PostgreSql |
| `AssetHub.Ui.Tests` | xUnit + bUnit (MudBlazor) |
| `E2E` | Playwright (TypeScript) with Page Object pattern |

### Naming
`MethodName_Condition_ExpectedResult` — e.g., `UpdateAsync_EmptyTitle_ReturnsBadRequest`.

### Fixtures
- **`PostgresFixture`** — real database tests. `[Collection("Database")]`. Call `fixture.CreateDbContextAsync()` per test class.
- **`CustomWebApplicationFactory`** — endpoint/HTTP tests. Real Postgres, mocked externals. `[Collection("Api")]`.
- **`TestAuthHandler`** — fake auth: `TestClaimsProvider.Default()`, `.Admin()`, `.WithUser(id, name, role)`.

### Test data
Use `TestData` factory methods (`CreateAsset()`, `CreateCollection()`, etc.) with optional parameter overrides.

### Lifecycle
`IAsyncLifetime`: seed in `InitializeAsync`, delete in `DisposeAsync`.

### Structure
Mirror source tree: `Services/`, `Repositories/`, `Endpoints/`, `EdgeCases/`.

### E2E
- Page Objects in `tests/E2E/tests/pages/*.ts`.
- Helpers in `tests/E2E/tests/helpers/`.
- Config in `tests/E2E/tests/config/env.ts`.
- Specs numbered: `01-auth.spec.ts`, `02-navigation.spec.ts`, etc.

---

## Docker & Containerization

- Multi-stage builds for all images (build stage + runtime stage).
- Minimal base images (`alpine`, `slim`). Pin versions — no `latest` in production.
- Non-root `USER` in all production images.
- `.dockerignore` to exclude `.git`, `node_modules`, build artifacts, IDE files, test files.
- `HEALTHCHECK` instruction in Dockerfiles.
- No secrets in image layers — use runtime secrets (Docker Secrets, env vars).
- Combine `RUN` commands and clean up temp files in the same layer.
- Resource limits (`cpu_limits`, `memory_limits`) in compose files.
- Logs to `STDOUT`/`STDERR`.

---

## Quality Guardrails (apply on the fly)

Short checklists that trigger by file type. Walk through the relevant block before reporting a task done — these are where regressions from past reviews keep surfacing. For deeper audits use `/implementation-a11y-check`, `/implementation-ux-check`, `/security-review`, or `/review`.

### When editing Blazor UI (`src/AssetHub.Ui/**/*.razor{,.cs,.css}`)

**Accessibility (WCAG 2.2 AA):**
- Every image/thumbnail (`MudCardMedia`, `MudImage`, `<img>`) has a meaningful `alt`, or `aria-hidden="true"` if purely decorative. Asset media: `alt="@($"{asset.Title} ({asset.Type})")"`.
- Every icon-only button has `aria-label` (MudBlazor icons inside meaningful buttons too).
- Every `MudDialog` has an accessible name — `TitleContent` with `id="dialog-title"` + `aria-labelledby` on the wrapper.
- Dynamic status/validation messages are wrapped in `role="status" aria-live="polite"` (or `role="alert"` for errors).
- State is never conveyed by color alone — pair `Color.Success`/`Error`/`Warning` with an icon or text label.
- `<PageTitle>` set on every page.
- Form controls have labels and `For=` expressions when validated.
- Any custom keyboard/mouse interaction (drag, canvas) has a keyboard equivalent (arrow keys, +/-, Delete, Esc).
- `App.razor` → `<html lang>` binds to current culture, never hardcoded.
- `MainLayout` and `ShareLayout` both include a skip-to-main-content link.

**Usability (Nielsen + house rules):**
- List mutations follow **Optimistic UI** (CLAUDE.md § Blazor): update local state first, roll back + `IUserFeedbackService.ShowError` on failure. Don't forget edit/rename.
- Destructive mutations go through `ConfirmDialog`. Bulk permanent delete gets a second confirm with an explicit count.
- Long-running actions (upload, save, zip build, media processing) surface progress — never a frozen button.
- Edit dialogs with non-trivial input track dirty state and warn before discarding (`OnLocationChanging` on full pages, dialog guard on dialogs).
- Every icon-only button has `MudTooltip`; ImageEditor tool tooltips include keyboard shortcuts.
- `EmptyState` components include an action CTA, not just a headline.
- User-visible error text is localized and action-oriented — never raw `ServiceError.Message`.
- Button naming: **Delete** = permanent, **Remove** = unlink from parent, **Discard** = cancel changes. Stay consistent.
- No raw HTML form elements where a MudBlazor equivalent exists.

**Localization:**
- Every user-visible string lives in `.resx`. When you add a key, add it to **both** `.resx` and `.sv.resx` in the same change — missing Swedish falls back to English silently.
- Key pattern `Area_Context_Element`. `Common_` prefix only for genuinely shared strings.
- Inject the most specific `IStringLocalizer<T>` — don't default to `CommonResource`.

**Reliability / Sonar hotspots specific to Razor:**
- **Components that own a `CancellationTokenSource` or `Timer` `@implements IAsyncDisposable`** and dispose it in `DisposeAsync` (`await _cts.CancelAsync(); _cts.Dispose();`). Forgetting this leaves cancellation registrations alive across the Blazor circuit (S2930). Pages that hold a CTS the same way.
- **Component-scoped fields default to `private readonly`** when assigned only at field declaration (`private readonly CancellationTokenSource _cts = new();`, `private readonly List<X> _items = new();`). Sonar's S2933 catches the rest, but writing it readonly first is cheaper than fixing it later. The exception is genuine reassignment patterns (e.g. `AssetCommentsPanel` allocates a new `_cts` when the asset id changes) — keep those mutable.
- **`IBrowserFile.OpenReadStream` always pairs a `file.Size > maxBytes` pre-flight check with a `maxAllowedSize:` argument** before the call. The pre-flight aborts before any buffer is allocated; the cap is the second-line defense. Show `Common.Error_FileTooLarge` via `IUserFeedbackService.ShowError` on rejection. S5693 hotspot is satisfied behaviourally by the explicit Size check.
- **`@ref`-bound and parameter-bound private fields need a `[SuppressMessage("...", "S4487", Justification = "Read by Razor markup binding to <X @ref=\"_field\" />")]`** because Sonar's C# analyser doesn't follow Razor markup back to source. Apply per-field, never globally.
- **Empty `catch (JSDisconnectedException) { }` blocks always carry a one-line comment** like `/* circuit gone — JS module unreachable */`. Empty-with-no-comment is S108.

### When editing API endpoints (`src/AssetHub.Api/Endpoints/`)

- Group has `.RequireAuthorization("Require…")` — never rely on per-endpoint auth alone.
- **Mutating groups (anything with POST/PATCH/PUT/DELETE) chain `.RequireAntiforgeryUnlessBearer()` at the group level.** Per-endpoint `.DisableAntiforgery()` then disables the default ASP.NET pipeline; `RequireAntiforgeryUnlessBearer` is what actually validates cookie-auth requests. Both must be present together. Skipping the filter on the group means cookie-authed callers (e.g., Blazor UI under XSS) can mutate without an antiforgery token (P-12 / A-7 regression).
- Route params use `{id:guid}` constraint.
- Collection-scoped operations call `ICollectionAuthorizationService` before touching entity data.
- Input DTOs apply `ValidationFilter<T>`.
- Return via `.ToHttpResult(...)` — never manually inspect `IsSuccess`.
- **Error shape is `ApiError`, not anonymous types.** When you can't route through `ServiceResult` (typically `IFormFile` parameter validation), return `Results.BadRequest(ApiError.BadRequest("…"))`. Never `Results.BadRequest(new { error = "…" })` — the anonymous shape doesn't match the OpenAPI schema and breaks SDK consumers.
- Every `.MarkAsPublicApi()` line on a chain must also include an `.AddEndpointFilter(scope)` (where `scope` is a pre-built `RequireScopeFilter` for the group). The only documented exception is the `pat_id`-guarded PAT self-service surface.

### When editing services / repositories (`src/AssetHub.Infrastructure/**`)

- **`sealed` on every service, repository, and adapter implementation.** A `public class FooService` slips past quickly; default to `public sealed class`. Inheritance is opt-in by changing it later.
- No `FromSqlRaw` / `FromSqlInterpolated` / string-built SQL — LINQ only. PostgreSQL fuzzy search via `EF.Functions.ILike`.
- Any external process launch uses `ProcessStartInfo.ArgumentList`, never a single command string.
- Any filename derived from user input passes through `FileHelpers.GetSafeFileName`; any ZIP entry uses sanitized names.
- New cache entries go through `CacheKeys` with tags for invalidation. Never cache ACL/roles.
- Background services create a scope per iteration; never inject scoped services into singletons directly.
- Return `ServiceResult<T>` — never throw for business errors. Catch infra exceptions and wrap as `ServiceError.Server(...)`.
- Mutating service methods that emit an audit event wrap action + audit in `IUnitOfWork.ExecuteAsync` so a torn write can't leave the mutation without its trail (A-4). External side-effects (MinIO, webhooks, cache invalidation, mention fan-out) stay outside the transaction.
- Use `is null` / `is not null` in plain C#. The only place `== null` / `!= null` is acceptable is inside an EF Core query expression that gets translated to SQL — patterns like `.Where(s => s.RevokedAt == null)` are load-bearing.
- **Static methods that don't touch `this`.** Pure helpers (validators, mappers, predicate-only-on-args methods) are `private static`. Sonar's S2325 fires on every instance helper that could be static.
- **No `foreach (...) { if (cond) ... }` loops** when the loop body is just filter-then-do. Collapse to `.Where(cond)` or `.Any(cond)` (S3267). The exception is when the loop has multiple branches with side effects.
- **No nested ternaries inside object initialisers.** Hoist branches into local variables above the `new { ... }` block (S3358). Common offender: DTO construction with multiple `string.IsNullOrWhiteSpace(x) ? null : x.Trim()` legs — extract each.
- **Service method parameter count > 7 keeps the `[SuppressMessage("...", "S107", Justification = "Composition root for X: ...")]` template** used across the existing services. Don't bundle into a parameter holder for the sake of the count — the holder relocates the count without solving anything. Do bundle when the cluster is genuinely cohesive (`AssetServiceRepositories`, `CollectionServiceRepositories` are the right shape).

### When editing DTOs (`src/AssetHub.Application/Dtos/`)

- `[Required]`, `[StringLength]`, `[Range]` on every user-bound field.
- Lists: `[MaxLength]` on the list and per-item length validation where it matters (e.g., individual tag length).
- Nullable ref types honored — required inputs are non-nullable; optional inputs are `?`.

### When editing configuration

- New settings class: `const string SectionName`, DataAnnotations on fields, `ValidateOnStart()` for critical infra.
- Never hardcode secrets — placeholders in `appsettings.json`, real values from env / Docker secrets.
- Production `AllowedHosts` must be a specific hostname, not `"*"`.

### When editing Worker handlers / background services (`src/AssetHub.Worker/`)

- Handlers are per-item try/catch in batch loops — one bad message doesn't poison the queue.
- `ct.ThrowIfCancellationRequested()` inside long loops; catch `OperationCanceledException` at the top level.
- Use `IServiceScopeFactory` for scoped dependencies; one scope per iteration.
- Log with counts at `Information` (start/summary), `Debug` (per-batch), `Warning` (per-item failures).
- **No hardcoded credential defaults** — `?? "guest"` for RabbitMQ Username/Password is the regression we just fixed. Use `?? string.Empty`; `RabbitMQSettings.ValidateOnStart()` then catches missing config with a clear error. Same shape for any future config that maps to a default credential.
- **No empty `catch (OperationCanceledException) { }` blocks** — fill with `/* polling cancelled on dispose */` or similar one-liner so S108 doesn't fire and the intent is obvious to the next reader.

### Sonar suppression discipline

Suppressing a Sonar rule is a documented decision, not a way to silence noise. Every existing suppression in the repo (39 of them at the time of writing) has a `Justification = "..."` arg or an inline comment after `// NOSONAR`. New suppressions must do the same — drive-by `// NOSONAR` with no reason will be reverted.

**When suppression is the right answer.** All four conditions must hold:

1. The rule's *behaviour* is satisfied even though the *syntax* isn't (e.g. `exit 1` terminates a bash function but Sonar wants a `return`; a Razor field IS read but the C# analyser can't see Razor markup; a record-struct `Open()` API has no async variant in .NET 9).
2. The fix would introduce worse code (unreachable statements, parameter-holders that just relocate the count, dead `if` branches).
3. The suppression is the **smallest scope possible** — line-level `// NOSONAR` over file-level `#pragma`, attribute on the offending member over project-level `<NoWarn>`.
4. The reason is recorded inline, in the form of a `Justification = "..."` or a one-line comment.

**When suppression is wrong.** If the rule is firing because the *behaviour* is incorrect — a real unread field that nobody uses, a real empty catch that swallows a real exception, a real cognitive-complexity hit that means the method is too long — the answer is to fix the code, not to suppress.

**Existing suppression clusters and their reasoning** (so future-you doesn't relitigate them):

- **`S107` (too many params) on services / Wolverine handlers.** ~20 services. Composition-root shape; bundling into a holder relocates the count without solving anything. Always include the constant `Justification = "Composition root for X: ..."`.
- **`S1200` (class coupled to too many others) on endpoint mappers / `AssetHubApiClient` / DI extensions.** Wiring is the point. Same pattern.
- **`S4487` (unread private field) on Razor `_form` / `@ref` / parameter-bound fields.** False positive — Sonar's C# analyser doesn't follow Razor markup back to source. Apply `[SuppressMessage]` on the field with the markup line in the justification (`Read by Razor @ref binding to <MudForm @ref="_form" />`).
- **`S6966` (sync IO) on `ZipArchiveEntry.Open()`.** No `OpenAsync()` exists in .NET 9. Inline `// NOSONAR S6966` with comment.
- **`S2068` UI password-mask placeholders (`"********"`).** Not credentials. Attribute with explicit `Justification = "UI mask placeholder, not a credential"`.

If a new feature ends up with a suppression cluster that doesn't match one of these, it probably means the design is wrong — push back on the design before suppressing.

### Pre-commit grep sweep

When the changeset is non-trivial (new endpoints, services, repos, resource keys), run these greps against your diff before committing. Each one targets a recurring drift pattern:

| Pattern | What it catches | Acceptable matches |
|---------|-----------------|--------------------|
| `^public class.*(?:Service\|Repository\|Adapter)\(` in `src/AssetHub.Infrastructure/` | Missing `sealed` on a service / repo / adapter (S3260 + house rule) | None — every match is a fix |
| `private (class\|record)` in `src/AssetHub.Ui/` without `sealed` | Nested private types not sealed (S3260) | None |
| `Results\.BadRequest\(new \{ error` in `src/AssetHub.Api/Endpoints/` | Anonymous error shape leaking out | None — convert to `ApiError.BadRequest(...)` |
| `\.MarkAsPublicApi\(\)` lines in `src/AssetHub.Api/Endpoints/` | Public-API endpoint without scope filter | Only the PAT self-service routes (commented exception) |
| `\.RequireAuthorization\(.*\)$` on a `MapGroup` whose body has POST/PATCH/PUT/DELETE — without a sibling `RequireAntiforgeryUnlessBearer()` | Mutating group missing CSRF gate | None |
| ` == null\| != null` outside `.Where(...)` / `.Count(...)` / projection trees | Plain C# nullability drift | EF query expressions only |
| `data name="…"` count in `Foo.resx` vs `Foo.sv.resx` | Missing Swedish translation | Counts must match |
| `class .* : I.*Service` without `sealed` keyword anywhere on the line | Same as the first row, broader | None |
| `catch \([^)]+\)\s*\{\s*\}` (empty catch) in `src/` | S108 — empty exception block | None — fill with one-line reason or delete the catch |
| `private (?!readonly\|const\|static)\s+(List\|Dictionary\|HashSet\|CancellationTokenSource)<` initialised at field declaration | Likely missing `readonly` (S2933) | Only if the field is genuinely reassigned later in the file |
| `\?\? "guest"\|\?\? "admin"\|\?\? "postgres"\|\?\? "root"` | Hardcoded credential default (S2068) | None — use `string.Empty` and let validation fail |
| `\bdouble\b.*== \|\bfloat\b.*==` outside test code | FP equality (S1244) | None — use `Math.Abs(a-b) < ε` |
| `OpenReadStream\(` in Razor without a `file.Size > maxBytes` check above | Missing pre-flight upload guard (S5693 hotspot) | None — every IBrowserFile upload checks Size first |
| `Count\(\) [><=]+ 0` in service / Razor code | Use `.Any()` / `.Count` property (S1155) | None |
| `// NOSONAR\b` without a rule id and reason | Drive-by suppression | Each must include the rule (`// NOSONAR S6966 — ...`) and a one-line why |

A passing sweep doesn't replace the per-area checklists above — it's a final mechanical pass for the things that hide in plain sight.

---

## Task Processing Log

For non-trivial tasks (multi-step changes, new features, bug investigations), maintain a `Claude-Processing.md` file in the workspace root to track progress:

1. **Before starting work**: Create/update `Claude-Processing.md` with the user's request and an action plan broken into granular, trackable items with todo/complete status.
2. **During work**: Update the file as each action item is completed.
3. **When finished**: Add a summary section to the file and inform the user.
4. **Cleanup**: Remind the user to review and delete the file so it is not committed to the repository.

Skip this for simple, single-step tasks (e.g., adding a localization key, a quick rename).
