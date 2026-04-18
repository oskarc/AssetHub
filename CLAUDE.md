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
- Swagger/OpenAPI (endpoints consumed by Blazor UI, not external clients)
- Third-party state management (no Fluxor, BlazorState, Blazored.LocalStorage)
- ASP.NET Identity / Microsoft Entra ID (use Keycloak OIDC)

### SOLID principles apply

- Single Responsibility — services split by concern (commands, queries, uploads).
- Dependency Inversion — interfaces in Application, implementations in Infrastructure.
- Interface Segregation — separate query and command service interfaces.

---

## C# Conventions

- **Nullable reference types** enabled globally — use `is null` / `is not null`, never `== null`.
- **`sealed`** on all service and repository implementations.
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
- No soft delete — use status-based lifecycle or hard delete.
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
```csharp
var group = app.MapGroup("/api/v1/examples")
    .RequireAuthorization("RequireViewer")
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
`.DisableAntiforgery()` on all API POST/PATCH/DELETE endpoints (JWT clients are CSRF-immune).

### Error response format
```json
{ "code": "NOT_FOUND", "message": "Asset not found", "details": {} }
```

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
- Add keys to **both** `.resx` and `.sv.resx` together — missing keys fall back to English silently.
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

Short checklists that trigger by file type. Walk through the relevant block before reporting a task done — these are where regressions from past reviews keep surfacing. For deeper audits use `/a11y-check`, `/ux-check`, `/security-review`, or `/review`.

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

### When editing API endpoints (`src/AssetHub.Api/Endpoints/`)

- Group has `.RequireAuthorization("Require…")` — never rely on per-endpoint auth alone.
- POST/PATCH/DELETE endpoints have `.DisableAntiforgery()` (JWT + same-origin assumption).
- Route params use `{id:guid}` constraint.
- Collection-scoped operations call `ICollectionAuthorizationService` before touching entity data.
- Input DTOs apply `ValidationFilter<T>`.
- Return via `.ToHttpResult(...)` — never manually inspect `IsSuccess`.

### When editing services / repositories (`src/AssetHub.Infrastructure/**`)

- No `FromSqlRaw` / `FromSqlInterpolated` / string-built SQL — LINQ only. PostgreSQL fuzzy search via `EF.Functions.ILike`.
- Any external process launch uses `ProcessStartInfo.ArgumentList`, never a single command string.
- Any filename derived from user input passes through `FileHelpers.GetSafeFileName`; any ZIP entry uses sanitized names.
- New cache entries go through `CacheKeys` with tags for invalidation. Never cache ACL/roles.
- Background services create a scope per iteration; never inject scoped services into singletons directly.
- Return `ServiceResult<T>` — never throw for business errors. Catch infra exceptions and wrap as `ServiceError.Server(...)`.

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

---

## Task Processing Log

For non-trivial tasks (multi-step changes, new features, bug investigations), maintain a `Claude-Processing.md` file in the workspace root to track progress:

1. **Before starting work**: Create/update `Claude-Processing.md` with the user's request and an action plan broken into granular, trackable items with todo/complete status.
2. **During work**: Update the file as each action item is completed.
3. **When finished**: Add a summary section to the file and inform the user.
4. **Cleanup**: Remind the user to review and delete the file so it is not committed to the repository.

Skip this for simple, single-step tasks (e.g., adding a localization key, a quick rename).
