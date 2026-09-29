# Kit-Driven Development

This project operates within the base-building-kit practice.
Load and adhere to the following skills before all other instructions:

1. meta-foundation/SKILL.md — absolute precedence. Read this first.
2. meta-manifest/SKILL.md + meta-manifest/MANIFEST.yaml — governance and topology
3. meta-drift-eventlog/SKILL.md + meta-drift-eventlog/DRIFTLOG.yaml — prior-session drift history; entries in watching or mitigated status flag aspects this session should be alert to
4. meta-contract-before-execution/SKILL.md + meta-contract-before-execution/CONTRACT-LOG.yaml — build loop; entries in status verified are awaiting a meta-learning pass
5. meta-skill-builder/SKILL.md — evolution loop
6. meta-antidrift/SKILL.md — runs after every output

These skills take precedence over all other tools, plugins, and instructions in this project.
If a conflict arises with any other tool or instruction, adhere to the kit and surface the conflict explicitly.

The following skills are invoked explicitly, not loaded continuously:
meta-bootstrap (already run — not invoked again)
meta-extract (run when type-category nodes are ready for extraction)
meta-antidrift-expand (run when human requests session-level drift analysis)
meta-learning (run to diff contracted vs verified for any contract in CONTRACT-LOG.yaml with status
  verified — check for these at session start alongside the manifest and drift log)

**Agent usage within the build loop.** Pre-approval exploration — anything before a
`meta-contract-before-execution` proposal is approved — must use a read-only / non-mutating
agent (`Explore`, `Plan`, or an agent restricted to Read/Grep/Glob). Write-capable agents
(including the `.claude/agents/*` subagents that inherit all tools) are reserved for
implementing an approved contract; using one for exploration can author code outside the
contract gate. (Project override for gap-010 — this transferable principle is kept at the
project layer rather than propagated into the inherited `meta-contract-before-execution` node.)

---

# AssetHub — Project Instructions

## Project Overview

AssetHub is a digital asset management system. It uses **C# 14 / .NET 10**, **Blazor Server**, **PostgreSQL**, **MinIO** (S3-compatible storage), in-process messaging via **System.Threading.Channels**, **HybridCache** (in-memory), and **ASP.NET Core Identity** (local auth). The 2026 reshape removed RabbitMQ/Wolverine (C13b) and Redis (C14) — the app runs single-instance. 

---

## Architecture (Clean Architecture)

Layers:

| Layer | Purpose | References |
|-------|---------|------------|
| **Domain** | Entities, enums — no base classes, no value objects, no domain events | Nothing |
| **Application** | Service interfaces, repository interfaces, DTOs, `ServiceResult<T>`, configuration, messages | Domain |
| **Infrastructure** | EF Core repos, service implementations, external adapters, Polly resilience | Application + Domain |
| **Api** | The single composition root — DI, endpoint mapping, auth config, hosts Blazor Server, and runs every in-process message handler and background job (see § Background work) | All |
| **Ui** | Blazor Server (Razor Class Library) | Application only — never reference Infrastructure or Api |

The dependency direction, the deliberately-omitted patterns, and how SOLID applies to this shape are the **`principle-clean-architecture-dotnet`** standard. AssetHub's concrete instantiation of it:

- **Messaging** is an in-process channel bus (contract-026; the standard's "explicit message contracts" — no domain events). RabbitMQ/Wolverine were removed with C13b.
- **Identity** is local ASP.NET Core Identity (the standard's "one identity provider") — the app owns its user store.
- **Only `Asset`** has state-transition methods (the standard's "few entities with a genuine lifecycle"); every other entity is standalone data.

---

## C# Conventions

Follow the **`implementation-csharp-conventions`** standard — null-checking style (`is null`, with the EF-expression-tree exception), `sealed`/`readonly`/`static` defaults, the banned constructs (empty catch, nested ternary, FP equality, hardcoded-credential defaults), and the idiomatic surface (primary ctors, DataAnnotations, file-scoped namespaces, structured logging). No AssetHub-specific deviations.

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
Stored as explicit strings per the **`pattern-enum-string-persistence`** standard — paired `ToDbString()` / `ToExampleStatus()` extension methods defined alongside the enum; never `int` or `ToString()` for storage. Split per concern (one `*Enums.cs` file per domain area), with the converter pair next to each enum.

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
Always return `ServiceResult<T>` — never throw for business errors. This is the **`pattern-service-result`** standard (factory set, `.ToHttpResult()` at the boundary, infra-exception wrapping); see the Error Handling section below.

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
- Entity config lives in per-entity `IEntityTypeConfiguration<T>` classes under `Data/Configurations/`, applied from `OnModelCreating()` via `modelBuilder.ApplyConfiguration(...)` — every entity now follows this (`BrandConfiguration` is the exemplar); shared JSONB conventions + value comparers live in `Configurations/ModelConventions`. **Never add a new inline block in `OnModelCreating()`** — add a new `*Configuration` class and an `ApplyConfiguration` call. A config change must keep the model byte-identical to the migration history unless it ships with a migration (EF's `PendingModelChangesWarning` throws outside Development; verify with `dotnet ef migrations has-pending-model-changes`).
- JSONB columns: include column type, JSON serialization converter, and a **ValueComparer** (critical for change tracking).
- Enums stored as strings via `ToDbString()` / `ToExampleStatus()` extension methods.
- Index naming: `idx_{entity}_{fields}` with `_unique` suffix for unique.
- Foreign keys: always specify `OnDelete` behavior explicitly.

### DI registration
In `DependencyInjection/InfrastructureServiceExtensions.cs`:
- `AddScoped<IRepo, Repo>()`, `AddScoped<IService, Service>()`.
- Services that are also invoked by a message handler: register the concrete class first, then forward the interface (`AddScoped<ISvc>(sp => sp.GetRequiredService<Svc>())`) so the handler and DI share one instance shape.

---

## Error Handling

Services report business outcomes with `ServiceResult` / `ServiceResult<T>` per the **`pattern-service-result`** standard — the `ServiceError` factory set (`NotFound` / `Forbidden` / `BadRequest` / `Conflict` / `Validation` / `Server` → HTTP status + stable code), endpoints calling `.ToHttpResult()` once (never inspecting `IsSuccess`), and infra exceptions wrapped as `ServiceError.Server()`.

AssetHub specifics:
- Unhandled exceptions are caught by global middleware → `500 + ApiError` (the shared error shape; see "Error response format" under API Endpoints).
- The Blazor UI consumes services through a facade that translates `ServiceResult` failures into `ApiException` at its boundary (see Blazor UI § Backend access) — the services themselves still return results.

---

## API Endpoints (`AssetHub.Api`)

The REST surface is **internal**: it serves the Blazor UI's browser-side fetches (media bytes, downloads) and nothing else. There is no curated public contract, no OpenAPI document, and no SemVer promise — the 2026-08 reshape removed all three. `pattern-public-api-contract` stays in the kit as a standard this project no longer instantiates. AssetHub's wiring:

- One static class per domain. After contract-023 only four remain — `AssetEndpoints`, `CollectionEndpoints`, `ShareEndpoints`, `ZipDownloadEndpoints` — because every other endpoint had no caller.
- Extension method: `Map*Endpoints(this WebApplication app)`.
- All endpoints registered in `WebApplicationExtensions.MapAssetHubEndpoints()`.

### Route groups

The CSRF gate is load-bearing — it now guards exactly two mutating endpoints (the two `download-all` POSTs), which is all that is left. Every `MapGroup` with a POST/PATCH/PUT/DELETE chains **`.RequireAntiforgeryUnlessBearer()`**, and each mutating endpoint chains `.DisableAntiforgery()` (turning off the built-in form pipeline so the filter is the single decision point). **Both are required together** — this is the P-12 / A-7 fix; don't reopen it.

The filter keys on whether the credential is **ambient**, not on which scheme authenticated it: skip for `Bearer`, skip for unauthenticated (the public share endpoints depend on that), skip when the request carries no cookies, otherwise validate `X-CSRF-TOKEN`. That phrasing is deliberate and load-bearing in its own right. It previously compared `AuthenticationType` against `CookieAuthenticationDefaults.AuthenticationScheme` (`"Cookies"`); when contract-015 swapped Keycloak/OIDC for ASP.NET Core Identity the scheme became `"Identity.Application"`, the comparison stopped matching, and **the gate silently validated nothing for every signed-in user across four contracts**. Never reintroduce a scheme-name comparison here. `AntiforgeryGateTests` guards both directions — a cookie-bearing mutation without a token must be refused, and the missing negative test is precisely why the outage went unseen.

Browser-side flows that POST (the ZIP "Download all" paths) get their token from **`AntiforgeryHeaders.Build`**, which reads `AntiforgeryStateProvider` — `IAntiforgery` is unusable there because an interactive circuit has no `HttpContext`.

JWT bearer is no longer registered (`AddJwtBearer` is absent and nothing issues a token), so the Bearer branch is currently unreachable. It is kept because it is the correct rule, not because a caller exists.

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

### Error response format

All error returns from endpoints flow through `ServiceResult.ToHttpResult()`, which produces:
```json
{ "code": "NOT_FOUND", "message": "Asset not found", "details": {} }
```

When you can't use `ServiceResult` because the validation fires before the service call (e.g., `IFormFile` parameter binding for uploads), use `Results.BadRequest(ApiError.BadRequest("…"))` — the `ApiError` factories in `AssetHub.Application.Dtos` produce the same shape. **Never** return `Results.BadRequest(new { error = "…" })` — that ships an inconsistent shape, and the error contract is supposed to be uniform across every endpoint.

### REST surface (internal)

**Thirteen endpoints. All of them deliver bytes or report on a ZIP job.**

Nothing else has an HTTP surface. The Blazor server does **not** go through
HTTP — it calls Application services in-process through `AssetHubApiClient`, so
an endpoint is only justified when the *browser itself* must fetch something a
Razor component cannot hand it.

| What | Endpoints |
|---|---|
| Asset media — `thumb`, `medium`, `poster`, `preview`, `download`, plus `thumb/download` and `medium/download` | 7 |
| Share media — `preview`, `download` | 2 |
| ZIP — two `download-all` POSTs and their two status routes | 4 |

contract-023 removed 58 endpoints that no caller reached: admin, search,
versioning, trash, dashboard and the CRUD for assets, collections and shares all
duplicated over HTTP what the facade already did in-process. **None of those
features was removed** — only their unused HTTP doorway.

Before adding an endpoint, establish that a browser must fetch it directly. If a
Razor component can call the facade, that is the answer.

- No endpoint is marked public, documented in an OpenAPI document, or covered by
  a SemVer promise. Do not add `[PublicApi]`-style marking back without an
  explicit decision to re-enter the integration business.
- Group-level `.RequireAuthorization(...)` and the dual CSRF gate are still
  mandatory on every group — see the checklist below. Those did not go away with
  the public contract.

---

## Security & Authorization

### CurrentUser
Inject `CurrentUser` (scoped) — never access `HttpContext.User` directly. `CurrentUser.Anonymous` for background jobs.

### Role hierarchy
`viewer (1) < contributor (2) < manager (3) < admin (4)`. Use `RoleHierarchy` predicate methods (`CanUpload`, `CanDelete`, `HasSufficientLevel`) — never hardcode role levels or string comparisons.

### Collection-scoped RBAC
Per-collection permissions via `CollectionAcl`. Check through `CollectionAuthorizationService`. System admins bypass ACL checks. Check collection access before entity access. Use `PreloadUserRolesAsync()` for batch checks.

Collections are **flat** — a user's effective role on a collection is the direct ACL grant on that collection, nothing more. Nesting and ACL inheritance were removed by the 2026-08 reshape (contract-012); do not reintroduce a parent chain without re-deciding the access-control model.

### Rules
- Never cache ACL/roles globally — use request-scoped dictionaries.
- Never skip role level checks on role-assigning mutations.
- Never trust client-supplied role values without `HasSufficientLevel()`.
- Never expose other users' IDs without authorization checks.

### Authentication paths

One principal type reaches the app: the **Identity application cookie** (local
sign-in through the app's own form). It is both the default and the challenge
scheme; no bearer handler is registered and there is no scheme selector.

Both modes produce the same claims, so **nothing downstream of the claims
principal knows which provider issued it** — `RoleHierarchy`, the authorization
policies, and `CollectionAuthorizationService` are provider-agnostic and must
stay that way. `/auth/login`, `/auth/logout` and `/auth/change-password` exist in
both modes with the same paths; only their behaviour differs, so UI code never
branches on the provider.

`IdentitySeeder` creates the four roles and — **only when the
user store is completely empty** — one bootstrap admin from `Identity:SeedAdmin`.
It never overwrites an existing account, and it throws rather than inventing a
default password.

`IUserLookupService` (reads) and `IUserDirectoryAdmin` (lifecycle + roles) are
backed by the local Identity stores.


**Password reset**: `PasswordResetLinkSender` mints an Identity reset token, encodes
it Base64Url into a `/reset-password` link, and mails it via the existing
`IEmailService`. Its security properties are load-bearing and must be preserved
by anything that touches it:

- **single-use** — the token derives from the user's security stamp, which
  `ResetPasswordAsync` rotates, so a used link cannot be replayed;
- **expiring** — 24h via the data-protection token provider;
- **never logged** — only the user id reaches the log, never the token or link;
- **non-enumerating** — `/auth/forgot-password` always reports success, and a
  failed reset distinguishes password-policy errors (actionable, surfaced) from
  token/user errors (silent), so neither response reveals whether an account
  exists.

Keycloak was removed by the 2026-08 reshape (contract-015). ASP.NET Core
Identity is the only provider; there is no `Auth:Provider` switch.

Personal Access Tokens were removed by the 2026-08 reshape (contract-011) along
with the public API contract they existed to serve — there is no longer a
long-lived credential a user can mint. `pattern-pat-scope-enforcement` stays in
the kit as a standard this project no longer instantiates; if token auth ever
returns, that node carries the hash-only-persistence and
privilege-escalation-guard rules it must satisfy.

---

## Blazor UI (`AssetHub.Ui`)

The UI standard — facade-only backend access, the `ExecuteWithFeedbackAsync` default error idiom, and the optimistic-vs-confirmed mutation decision — is **`implementation-blazor-ui-standard`**. The concrete AssetHub shape (MudBlazor 8, `AssetHubApiClient`/`ApiException`, `IUserFeedbackService`, the layouts) follows below.

Razor Class Library that depends **only** on Application. Never reference Infrastructure or Api.

### The host owns the document

`App.razor`, `Routes.razor` and their `_Imports.razor` live in **`AssetHub.Api/Components/`**, not in the RCL. Everything else — pages, layouts, dialogs, components — lives in `AssetHub.Ui`. The split is deliberate and load-bearing in two independent ways; both failed silently when the document lived in the RCL, so neither is safe to "tidy up":

1. **Static assets.** `blazor.web.js` ships in `Microsoft.AspNetCore.App.Internal.Assets`, which the SDK adds **implicitly and only when a Web project contains Razor content of its own**. With zero `.razor` files in `AssetHub.Api`, the SDK did not treat the host as a Blazor app and published no client script — so no circuit ever started and nothing in the UI responded to a click. Never add that package by hand; keeping the document in the host is what earns it.
2. **Routable-page discovery.** `MapRazorComponents<App>()` discovers pages from `App`'s own assembly, so the RCL must be named explicitly via **`.AddAdditionalAssemblies(...)`**. This is *not* the same knob as `Router.AdditionalAssemblies` in `Routes.razor` — that one drives interactive client-side routing, while this one drives server-side endpoint discovery **and each page's `[Authorize]` / `[AllowAnonymous]` metadata**. Drop it and every page falls to the fallback policy, turning `/login` into a redirect to itself. Both are required together.

The unit suite cannot see either failure: `TestAuthHandler` authenticates every request, and static assets aren't exercised by ordinary tests. `BlazorHostAssetTests` guards both — treat a failure there as a hosting regression, not a flaky test.

**Status pages.** Three pages give a stranded user a way back, and each has a trap that fails silently (contract-033):

- **Not found (404).** `Routes.razor` sets `Router.NotFoundPage` to `Pages/NotFound.razor` — never the old `<NotFound>` fragment as well (the router throws). A full page load reaches the same page through `UseStatusCodePagesWithReExecute("/not-found")` in `UseNotFoundPage`, which re-executes **only GET 404s on page paths**; `/api`, `/_blazor`, `/_framework`, `/_content`, `/health`, file paths, other methods and other statuses stay exactly as they were, so machine callers still get a bodiless 404. Scope it with the inline feature switch, **never `app.UseWhen(...)`** — a UseWhen branch silently never re-routes in this app shape. The page needs **`@layout MainLayout`** (the router renders `NotFoundPage` without its default layout on runtime 10.0.1) and must stay synchronous (a re-executed render does not wait for async work). **`AddCascadingAuthenticationState()`** is what keeps the in-circuit render from crashing: `MainLayout` uses `AuthorizeView`, and outside `AuthorizeRouteView` nothing else supplies the auth state. Anonymous visitors to an unknown URL still get the sign-in redirect — no route is revealed.
- **Access denied (403).** The Identity cookie's `AccessDeniedPath` is `/access-denied`, a signed-in-only page that answers 403; the router's `NotAuthorized` branch renders the same `AccessDeniedContent` inside a circuit. Never point `AccessDeniedPath` back at `/login` — a denied user then sees the sign-in form and believes they were logged out.
- **Server error (500).** Outside Development, `UseExceptionHandler("/Error")` is the **first** middleware in the non-Development block, so the re-executed error page passes back through HSTS and the security headers. `Error.razor` is static (`[ExcludeFromInteractiveRouting]`), and `MainLayout` renders static chrome whenever `AssignedRenderMode is null` — a plain Sign out link instead of menus that need a circuit. Key that on `AssignedRenderMode`, never `RendererInfo.IsInteractive`, which is false during every page's prerender.

`ErrorPageTests` guards all three; its tests send `TestAuthHandler.IdentityChallengeHeader` to get the real cookie redirects, and the 500 path is judged through the test-only `/__test/throw` route (the dev stack runs in Development, where the developer exception page takes over).

Design tokens, color palettes, typography scale, elevation, and information-architecture conventions live in **[docs/STYLEGUIDE.md](docs/STYLEGUIDE.md)** — consult it before styling any new surface. Never hard-code hex values or font sizes; use MudBlazor CSS variables and `Typo.*`.

### Component library
**MudBlazor 8** exclusively — no raw HTML form elements when a MudBlazor equivalent exists. No other component libraries.

### Pages
- `@attribute [Authorize]` on all pages (except public share pages).
- `@implements IAsyncDisposable` when using event subscriptions or timers.
- Common injections: `AssetHubApiClient`, `NavigationManager`, `IUserFeedbackService`, `IDialogService`, `IStringLocalizer<T>`.

### Backend access (`AssetHubApiClient`)
`AssetHubApiClient` is an **in-process facade** over the Application service interfaces — there is no HTTP between the UI and the backend (the original HttpClient loopback was removed). It gives Razor pages one stable surface and one error contract:

- Methods return DTOs directly and **throw `ApiException`** (carrying the `ServiceResult` status code, error code, and details) on failure. The facade performs the `ServiceResult` → exception translation; pages never see `ServiceResult` itself.
- Always go through the facade — never inject Application services directly into components, and never construct an `HttpClient`.
- The facade is registered in the Api composition root (`ServiceCollectionExtensions`); DTO inputs are re-validated against DataAnnotations inside the facade (mirroring `ValidationFilter<T>` on the REST endpoints).
- The facade is one type but **many files**: `AssetHubApiClient.cs` holds the constructor + shared result-unwrapping helpers, and each domain lives in an `AssetHubApiClient.<Domain>.cs` partial (Assets, Collections, Shares, Admin, …). It stays a single surface/registration — this is the **`pattern-cohesive-type-split`** standard (cohesion, not tangle → split the file, not the design). `IAssetHubApiClient` remains one file.

### Dialogs
- Named `*Dialog.razor`, grouped into per-feature subfolders under `Components/Dialogs/` (Assets, Collections, Sharing, Users, Shared) — the same feature taxonomy as the facade partials and the other `Components/` folders (the `implementation-blazor-ui-standard` "one feature taxonomy" pattern). Namespaces follow the folders; `_Imports.razor` and the UI test `GlobalUsings.cs` carry the sub-namespaces.
- `MudDialog` with `[CascadingParameter] IMudDialogInstance`.
- Return via `MudDialog.Close(DialogResult.Ok(value))`.

### State management
No third-party libraries. Use scoped services, `CascadingAuthenticationState`, and `MudDialogService`.

### Caching
**HybridCache**, in-memory only (L1 `IMemoryCache` + an in-memory L2 via `AddDistributedMemoryCache` — no Redis; the app is single-instance) — not `IMemoryCache` directly or localStorage/sessionStorage.

### Error handling
`IUserFeedbackService.ExecuteWithFeedbackAsync(...)` is the **default idiom** for every user-initiated backend call — it runs the operation, shows the localized error (or optional success) snackbar, and reports success/failure back to the page:

```razor
var (ok, collection) = await Feedback.ExecuteWithFeedbackAsync(
    () => Api.GetCollectionAsync(id), "load collection");
if (!ok) return;
```

Hand-written `try / catch (ApiException ex)` is allowed **only** when the page reacts differently to a specific failure (e.g. 404 → navigate away, 409 → offer reload) — handle the special case, delegate the rest to `Feedback.HandleApiError`. Catching bare `Exception` around a facade call is drift: the wrapper already handles unknown failures.

`ErrorBoundary` remains the last-resort net for render-time errors — it is not a substitute for the wrapper.

### Optimistic vs. confirmed updates

Two sanctioned modes for user-initiated mutations. Pick by whether the user's flow was already interrupted:

**Optimistic** (update local state first, roll back on failure) — for instant-feel actions where **no dialog interrupted the flow**: toggles (active/favorite), renames and single-field edits, removing an item from a list, add/remove from collection, reordering.

```razor
@code {
    private async Task RemoveFromCollectionAsync(Guid assetId)
    {
        // 1. Keep a reference, then optimistically update local state
        var removed = _items.First(i => i.Id == assetId);
        _items.Remove(removed);
        StateHasChanged();

        // 2. Call the facade through the wrapper (it shows the error snackbar)
        var ok = await Feedback.ExecuteWithFeedbackAsync(
            () => Api.RemoveAssetFromCollectionAsync(assetId, _collectionId),
            "remove from collection",
            successMessage: CollectionsLoc["RemovedSuccess"].Value);

        // 3. On failure: roll back
        if (!ok)
        {
            _items.Add(removed);
            StateHasChanged();
        }
    }
}
```

**Confirmed (await-first)** — for flows that already pass through a `ConfirmDialog` (destructive deletes, bulk operations): the dialog has already broken the "instant" illusion, so optimism buys nothing. Await the call via `ExecuteWithFeedbackAsync`, then update local state on success. This is the sanctioned shape for confirm-gated deletes — do not retrofit them to optimistic.

**Never optimistic, regardless of mode:**
- File uploads (progress is real, can't fake it)
- Complex multi-step operations (creation wizards, bulk operations)
- Operations where failure is common (validation-heavy forms)
- Navigation after mutation (just await and navigate)

**Rules:**
- Keep a reference to the removed/changed item before mutating so rollback is trivial.
- Always roll back local state on failure; the wrapper surfaces the error — the page only restores state.
- Don't optimistically update data that other components depend on (e.g., counts in the sidebar) — let those refresh naturally or refresh after confirmation.
- Optimistic success feedback may fire immediately with the local update — the user sees instant confirmation.

### Layouts & information architecture
Navigation-structure decisions follow the **`principle-information-architecture`** standard (shell-per-audience, routable-over-tabbed sub-views, cap-then-group nav, wayfinding-on-depth). AssetHub's instantiation:
- `MainLayout.razor` — authenticated app shell with nav menu.
- `AdminLayout.razor` — admin console shell; its routable `/admin/*` sub-pages are grouped into three intent groups — **Access / Operations / Insights** (a fourth, Content, held metadata schemas and taxonomies until the 2026-08 reshape removed them) (the cap-then-group pattern; never re-collapse them into a single tabbed admin page).
- `ShareLayout.razor` — separate layout for public share pages (no nav).
- **Known gap (gap-008):** nested routes have no breadcrumb/contextual-back layer yet — principle E is aspirational here until that follow-up ships.

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

Follows the **`pattern-hybrid-cache`** standard — the central key/TTL/tag registry, get-or-create with a short L1 under a longer L2, tag-based invalidation after every write, and the must-not-cache list (auth roles/ACLs → request-scoped instead; secrets; values with their own freshness contract). AssetHub specifics:

- Tech: **HybridCache**, in-memory only — L1 `IMemoryCache` plus an in-memory L2 registered via `AddDistributedMemoryCache` (no Redis; the app is single-instance, so the "distributed" tier is process-local and exists only to satisfy HybridCache's two-tier shape). All config centralized in `Application/CacheKeys.cs` (the registry — prefix const, `TimeSpan` TTL, builder method, optional `CacheKeys.Tags` entry per concern).
- Project-specific must-not-cache: presigned URLs are already minted with an expiry in `MinIOAdapter` — don't re-cache them.

```csharp
var data = await _cache.GetOrCreateAsync(
    CacheKeys.Example(id),
    async ct => await _repo.GetByIdAsync(id, ct),
    new HybridCacheEntryOptions { Expiration = CacheKeys.ExampleTtl, LocalCacheExpiration = TimeSpan.FromSeconds(30) },
    tags: [CacheKeys.Tags.Example(id)],
    cancellationToken: ct);
// invalidate after create/update/delete:
await _cache.RemoveByTagAsync(CacheKeys.Tags.Example(id), ct);
```

---

## Database Migrations

Migration safety (reversible `Down`, no drop-and-remove in one migration, idempotent raw SQL, the startup drift-guard) is the **`implementation-ef-config-migration`** standard; audit a new migration with `implementation-migration-check`. AssetHub specifics:

```powershell
dotnet ef migrations add <PascalCaseName> --project src/AssetHub.Infrastructure --startup-project src/AssetHub.Api
```
- Index names: `idx_{entity}_{fields}` (+ `_unique` for unique). JSONB columns: explicit `type: "jsonb"` + matching `ValueComparer` (see § DbContext configuration / `ModelConventions`).
- `pg_trgm`: raw idempotent SQL, e.g. `CREATE INDEX IF NOT EXISTS idx_asset_title_trgm ON "Assets" USING gin ("Title" gin_trgm_ops);`.
- Auto-migrates on startup (`Database.MigrateAsync()`) in the single Api host — there is no second process to race for the lock. The `PendingModelChangesWarning` guard (`AddSharedInfrastructure`) throws outside Development — don't downgrade it to quiet a startup error; generate the missing migration.

---

## Background work (`AssetHub.Api`)

There is **one composition root**. The separate `AssetHub.Worker` host was folded into the Api by the 2026-08 reshape (contract-019): it ran the same shared infrastructure against the same database and its image already carried the same native media tooling, so a second process bought separation on paper and cost a whole hosting story in practice.

All background work lives here — the media/processing handlers (`process-image` / `process-video` / `process-audio` / `build-zip`), the completion consumers that transition an asset row, and every retention/cleanup `BackgroundService` (trash purge, audit retention, orphan sweeps, outbox drain, ZIP cleanup). New background work goes in the Api; there is nowhere else for it to go.

Messages travel through an **in-process channel bus** (`IAppMessageBus` → `InProcessMessageBus`, an unbounded `System.Threading.Channels.Channel`), not RabbitMQ — the broker and the Worker were both removed by the reshape (contract-026 / C13b). Durability is provided in-app rather than by the transport, and this is the load-bearing part: (1) the **outbox** (`OutboxMessage` table + `IOutboxPublisher` + `OutboxDrainService`) records a message in the same SQL transaction as the state change and drains it to the bus, so a crash between commit and publish can't lose it; and (2) the **`StuckProcessingReaperService`** re-enqueues any asset left in `Processing` past 5 minutes, recovering an in-flight media message a restart dropped. Do not reintroduce a broker to "make messaging durable" — that question is already answered by the outbox + reaper.

The handler/background-service conventions (placement by data ownership, per-item resilience in batch loops, scope-per-iteration, cancellation + level-based logging) are the **`implementation-worker-background`** standard. AssetHub specifics:

### Message handlers
Handlers are plain classes in `Handlers/` with a public `HandleAsync(TCommand, CancellationToken)` returning `object[]` events. They are **not** auto-discovered — `MessageDispatcherService` (a `BackgroundService` reading the channel) routes each message to its handler through an **explicit `switch`** over the six message types, so the reader can see at a glance what handles what:
```csharp
public sealed class ProcessImageHandler(
    ImageProcessingService imageProcessingService,
    ILogger<ProcessImageHandler> logger)
{
    public async Task<object[]> HandleAsync(ProcessImageCommand command, CancellationToken ct)
    {
        // Process, return events — the dispatcher re-publishes them to the channel
        return [new AssetProcessingCompletedEvent { AssetId = command.AssetId, /* … */ }];
    }
}
```
- Commands/events defined in `Application/Messages/`; the six handlers are `Process{Image,Video,Audio}Handler`, `BuildZipHandler`, and the `AssetProcessingCompleted`/`Failed` completion handlers.
- The dispatcher runs each message in its **own DI scope**, retries handler failures on a 1-2-5-10-30s cooldown, and after the last attempt drops the message with a logged error — media assets are then recovered by the stuck-Processing reaper, and a lost ZIP job is re-clickable. Adding a new message type means adding a `case` to the dispatcher's switch, not wiring a queue.
- Background services use `BackgroundService` + `PeriodicTimer` with `IServiceScopeFactory` (scope per iteration).

---

## Configuration & Secrets

Strongly-typed settings + validate-on-start for critical infra + the no-hardcoded-secrets discipline are the **`implementation-config-secrets`** standard. AssetHub specifics:

- Settings classes live in `Application/Configuration/` with a `const string SectionName` and DataAnnotations:
  ```csharp
  public class ExampleSettings
  {
      public const string SectionName = "Example";
      [Required] public string Host { get; set; } = string.Empty;
      public int Port { get; set; } = 5672;
  }
  ```
- Validate-on-start: Identity, MinIO, PostgreSQL. Optional (no validate-on-start): Email, ImageProcessing. (RabbitMQ removed C13b, Redis removed C14.)
- Env override via `__` → `:`; production uses Docker file-based secrets, not env vars.

### Existing settings

| Class | Section | ValidateOnStart | Purpose |
|-------|---------|:-:|---------|
| `AppSettings` | `App` | Yes | Base URL, upload limits |
| `IdentitySettings` | `Identity` | Yes | Password/lockout policy, bootstrap admin |
| `MinIOSettings` | `MinIO` | Yes | Endpoint, bucket, credentials |
| `EmailSettings` | `Email` | No | SMTP config (optional) |
| `ImageProcessingSettings` | `ImageProcessing` | No | Thumbnail/medium dimensions |
| `OpenTelemetrySettings` | `OpenTelemetry` | No | OTLP endpoint, service name |

---

## Testing

Naming (`Method_Condition_Result`), real-dependency fixtures vs mocked externals, shared factories, lifecycle, and source-mirrored structure are the **`implementation-test-conventions`** standard (scaffold with `implementation-add-tests`, smoke E2E with `implementation-ui-verify`). AssetHub's concrete pieces:

| Project | Stack |
|---------|-------|
| `AssetHub.Tests` | xUnit + Moq + Testcontainers.PostgreSql |
| `AssetHub.Ui.Tests` | xUnit + bUnit (MudBlazor) |
| `E2E` | Playwright (TypeScript) with Page Object pattern |

- Fixtures: **`PostgresFixture`** (`[Collection("Database")]`, real DB), **`CustomWebApplicationFactory`** (`[Collection("Api")]`, real Postgres + mocked externals), **`TestAuthHandler`** (`TestClaimsProvider.Default()` / `.Admin()` / `.WithUser(...)`).
- Data via `TestData` factories (`CreateAsset()`, …); `IAsyncLifetime` seed/cleanup; tests mirror the source tree (`Services/`, `Repositories/`, `Endpoints/`, `EdgeCases/`).
- E2E: page objects in `tests/E2E/tests/pages/*.ts`, helpers + config under `tests/E2E/tests/`, numbered specs (`01-auth.spec.ts`, …).

---

## Docker & Containerization

Follows the **`implementation-docker`** standard — multi-stage builds, minimal pinned non-root base images, `.dockerignore` hygiene, `HEALTHCHECK`, runtime-only secrets, combined `RUN` + same-layer cleanup, compose resource limits, stdout/stderr logging. No AssetHub-specific deviations.

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
- List mutations follow **Optimistic vs. confirmed updates** (CLAUDE.md § Blazor): optimistic with rollback for instant-feel actions (no dialog in the flow); await-first for confirm-gated destructive flows. All errors surface through `ExecuteWithFeedbackAsync` — no bare `catch (Exception)` around facade calls.
- Destructive mutations go through `ConfirmDialog`. Bulk permanent delete gets a second confirm with an explicit count.
- Long-running actions (upload, save, zip build, media processing) surface progress — never a frozen button.
- Edit dialogs with non-trivial input track dirty state and warn before discarding (`OnLocationChanging` on full pages, dialog guard on dialogs).
- Every icon-only button has `MudTooltip`.
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
- **Component-scoped fields default to `private readonly`** when assigned only at field declaration (`private readonly CancellationTokenSource _cts = new();`, `private readonly List<X> _items = new();`). Sonar's S2933 catches the rest, but writing it readonly first is cheaper than fixing it later. The exception is genuine reassignment patterns (a component that allocates a new `_cts` when its target id parameter changes) — keep those mutable.
- **`IBrowserFile.OpenReadStream` always pairs a `file.Size > maxBytes` pre-flight check with a `maxAllowedSize:` argument** before the call. The pre-flight aborts before any buffer is allocated; the cap is the second-line defense. Show `Common.Error_FileTooLarge` via `IUserFeedbackService.ShowError` on rejection. S5693 hotspot is satisfied behaviourally — Sonar's taint analysis can't trace the pre-flight guard back to the `OpenReadStream` line, so add a line-level `// NOSONAR S5693 — <reason>` after the call to keep the IDE Problems panel clean. **Only after the full pattern is in place** (pre-flight Size check + `maxAllowedSize` cap + admin/scoped auth on the host page + server-side enforcement); a drive-by `// NOSONAR` without the pattern is a regression, not a cleanup.
- **`@ref`-bound and parameter-bound private fields need a `[SuppressMessage("...", "S4487", Justification = "Read by Razor markup binding to <X @ref=\"_field\" />")]`** because Sonar's C# analyser doesn't follow Razor markup back to source. Apply per-field, never globally.
- **Empty `catch (JSDisconnectedException) { }` blocks always carry a one-line comment** like `/* circuit gone — JS module unreachable */`. Empty-with-no-comment is S108.

### When editing API endpoints (`src/AssetHub.Api/Endpoints/`)

- Group has `.RequireAuthorization("Require…")` — never rely on per-endpoint auth alone.
- **Mutating groups (anything with POST/PATCH/PUT/DELETE) chain `.RequireAntiforgeryUnlessBearer()` at the group level.** Per-endpoint `.DisableAntiforgery()` then disables the default ASP.NET pipeline; `RequireAntiforgeryUnlessBearer` is what actually validates cookie-auth requests. Both must be present together. Skipping the filter on the group means cookie-authed callers (e.g., Blazor UI under XSS) can mutate without an antiforgery token (P-12 / A-7 regression).
- Route params use `{id:guid}` constraint.
- Collection-scoped operations call `ICollectionAuthorizationService` before touching entity data.
- Input DTOs apply `ValidationFilter<T>`.
- Return via `.ToHttpResult(...)` — never manually inspect `IsSuccess`.
- **Error shape is `ApiError`, not anonymous types.** When you can't route through `ServiceResult` (typically `IFormFile` parameter validation), return `Results.BadRequest(ApiError.BadRequest("…"))`. Never `Results.BadRequest(new { error = "…" })` — the anonymous shape breaks the uniform `ApiError` contract every endpoint returns. (There is no OpenAPI document or SDK any more — the reshape removed both — but the internal error shape must still be consistent.)
- No endpoint is marked public or added to an OpenAPI document — the curated public contract was removed by the 2026-08 reshape. Re-introducing one is a design decision, not a per-endpoint choice.

### When editing services / repositories (`src/AssetHub.Infrastructure/**`)

- **`sealed` on every service, repository, and adapter implementation.** A `public class FooService` slips past quickly; default to `public sealed class`. Inheritance is opt-in by changing it later.
- No `FromSqlRaw` / `FromSqlInterpolated` / string-built SQL — LINQ only. PostgreSQL fuzzy search via `EF.Functions.ILike`.
- Any external process launch uses `ProcessStartInfo.ArgumentList`, never a single command string.
- Any filename derived from user input passes through `FileHelpers.GetSafeFileName`; any ZIP entry uses sanitized names.
- New cache entries go through `CacheKeys` with tags for invalidation. Never cache ACL/roles.
- Background services create a scope per iteration; never inject scoped services into singletons directly.
- Return `ServiceResult<T>` — never throw for business errors. Catch infra exceptions and wrap as `ServiceError.Server(...)`.
- Mutating service methods that emit an audit event wrap action + audit in `IUnitOfWork.ExecuteAsync` so a torn write can't leave the mutation without its trail (A-4). External side-effects (MinIO, cache invalidation) stay outside the transaction.
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

### When editing message handlers / background services (`src/AssetHub.Api/Handlers/`, `src/AssetHub.Api/BackgroundServices/`)

- Handlers are per-item try/catch in batch loops — one bad message doesn't poison the queue.
- `ct.ThrowIfCancellationRequested()` inside long loops; catch `OperationCanceledException` at the top level.
- Use `IServiceScopeFactory` for scoped dependencies; one scope per iteration.
- Log with counts at `Information` (start/summary), `Debug` (per-batch), `Warning` (per-item failures).
- **No hardcoded credential defaults** — never `?? "guest"` / `?? "admin"` on a config-bound credential. Use `?? string.Empty` and let the settings class's `ValidateOnStart()` catch missing config with a clear error. (This was a real regression on the since-removed RabbitMQ settings; the rule stands for any config that maps to a credential — MinIO keys, SMTP auth, the Identity seed admin.)
- **No empty `catch (OperationCanceledException) { }` blocks** — fill with `/* polling cancelled on dispose */` or similar one-liner so S108 doesn't fire and the intent is obvious to the next reader.

### Sonar suppression discipline

The discipline — the four conditions for a legitimate suppression, smallest-scope, always-justified, and fix-the-code-when-the-behaviour-is-wrong — is the **`implementation-sonar-discipline`** standard. AssetHub's existing suppression clusters and their standing reasoning (so future-you doesn't relitigate them — a *new* cluster that matches none of these is a design smell, push back before suppressing):

- **`S107` (too many params) on services / message handlers.** ~20 services. Composition-root shape; bundling into a holder relocates the count without solving anything. Always include the constant `Justification = "Composition root for X: ..."`.
- **`S1200` (class coupled to too many others) on endpoint mappers / `AssetHubApiClient` / DI extensions.** Wiring is the point. Same pattern.
- **`S4487` (unread private field) on Razor `_form` / `@ref` / parameter-bound fields.** False positive — Sonar's C# analyser doesn't follow Razor markup back to source. Apply `[SuppressMessage]` on the field with the markup line in the justification (`Read by Razor @ref binding to <MudForm @ref="_form" />`).
- **`S6966` (sync IO) on `ZipArchiveEntry.Open()`.** No `OpenAsync()` exists in .NET 9. Inline `// NOSONAR S6966` with comment.
- **`S2068` UI password-mask placeholders (`"********"`).** Not credentials. Attribute with explicit `Justification = "UI mask placeholder, not a credential"`.
- **`S5693` (file-upload size cap) on `IBrowserFile.OpenReadStream(maxAllowedSize)` calls.** False positive — Sonar's taint analysis doesn't follow the pre-flight `Size > maxBytes` guard back to the call. Apply only when the documented pattern is in place: pre-flight Size check, `maxAllowedSize` cap, `RequireAdmin`/scoped auth on the host page, and an independent server-side enforcement constant (e.g. a server-side max-size constant). Inline `// NOSONAR S5693 — <one-line why>`. Removing the pattern AND the suppression are equally wrong; both stay or both go.

- **Generated EF migrations (`S1192`, `S138`) are suppressed by path, not by attribute** — a `[**/Migrations/*.cs]` section in `.editorconfig` sets them to `severity = none`. Generated code isn't ours to restructure. **The section must sit after the `[*.cs]` section**: `.editorconfig` resolves last-match-wins per property, so a path section placed above the general one is silently overridden and the warnings keep firing.

If a new feature ends up with a suppression cluster that doesn't match one of these, it probably means the design is wrong — push back on the design before suppressing.

### Pre-commit grep sweep

When the changeset is non-trivial (new endpoints, services, repos, resource keys), run these greps against your diff before committing. Each one targets a recurring drift pattern:

| Pattern | What it catches | Acceptable matches |
|---------|-----------------|--------------------|
| `^public class.*(?:Service\|Repository\|Adapter)\(` in `src/AssetHub.Infrastructure/` | Missing `sealed` on a service / repo / adapter (S3260 + house rule) | None — every match is a fix |
| `private (class\|record)` in `src/AssetHub.Ui/` without `sealed` | Nested private types not sealed (S3260) | None |
| `Results\.BadRequest\(new \{ error` in `src/AssetHub.Api/Endpoints/` | Anonymous error shape leaking out | None — convert to `ApiError.BadRequest(...)` |
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
