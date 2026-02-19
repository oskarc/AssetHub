# AssetHub — Implementation Plan (2026-02-19)

Based on the comprehensive code review findings and decisions made.
Updated: 2026-02-20 — Phases 1–5 completed, Phase 5.4 (Dashboard) completed, Phase 3 enum migration fully completed, Phase 6 pending.

---

## Phase 1: Critical Fixes (Non-breaking, immediate impact) ✅ COMPLETED

### 1.1 Remove `UseAntiforgery()` entirely ✅
- **Why:** Breaks JWT Bearer clients on POST/PUT/DELETE. Blazor Server uses SignalR (not forms).
- **Files:** `WebApplicationExtensions.cs`

### 1.2 Register `IMemoryCache` in Worker ✅
- **Why:** Worker resolves `AssetCollectionRepository` (depends on `IMemoryCache`) but never registers it → runtime crash
- **Files:** `Dam.Worker/Program.cs`
- **Note:** Comment updated to "required by repository layer" (both `AssetCollectionRepository` and `AssetRepository` depend on it)

### 1.3 Fix cache invalidation on collection delete ✅
- **Why:** `AssetRepository.DeleteByCollectionAsync` bypasses caching repository → stale auth for 2 minutes
- **Approach:** Routed deletion through `AssetCollectionRepository` so its cache invalidation runs
- **Files:** `AssetRepository.cs`, `AssetCollectionRepository.cs`

### 1.4 Fix Blazor memory leaks (3 components) ✅
- **Why:** `DotNetObjectReference` and `IJSObjectReference` never disposed
- **Files:** `Assets.razor`, `Share.razor`, `LanguageSwitcher.razor`

### 1.5 Fix `Dictionary<string, object>` deserialization ⏸️ DEFERRED
- **Why:** System.Text.Json deserializes values as `JsonElement`, not primitives → `InvalidCastException`
- **Status:** Deferred — requires deeper analysis of all consumer call sites; low runtime impact currently

---

## Phase 2: Security Fixes ✅ COMPLETED

### 2.1 Open redirect — URL allowlist ✅
- **Why:** `AssetDetail.razor` uses `ReturnUrl` from query string without validation; `/auth/login` has edge cases
- **Approach:** Created `UrlValidator` helper with known internal route allowlist
- **Files:** New `UrlValidator.cs`, `WebApplicationExtensions.cs`, `AssetDetail.razor`

### 2.2 Docker security fixes ✅
- **Why:** Containers run as root, no `.dockerignore`, `Directory.Build.props` not in restore layer, `minio:latest` in prod
- **Files:** `Dockerfile`, `Dockerfile.Worker`, new `.dockerignore`, `docker-compose.prod.yml`

---

## Phase 3: Domain Model Refactor (Full DDD) ✅ COMPLETED (partial scope)

### 3.1 Replace string-typed enums with proper enums + EF value converters ✅
- **Files:** All entities in `Dam.Domain/Entities/`, `AssetHubDbContext.cs`, new `Enums.cs`
- **Scope:** All string-typed enums (`AssetStatus`, `AssetType`, `ShareScopeType`, `PrincipalType`, `AclRole`, `ZipDownloadStatus`) replaced with proper C# enums + EF `.HasConversion<string>()` value converters
- **Service layer migration:** All 11 service files + 2 endpoint files + 1 worker job updated to use enum values instead of string constants. `.ToDbString()` used where repository/DTO interfaces still expect strings. (Completed 2026-02-20)

### 3.2 Add private setters + constructors with validation ⏸️ DEFERRED
- **Status:** Deferred — too high risk relative to value; would require updating every entity creation site across the entire codebase

### 3.3 Add state-machine methods (MarkReady, MarkFailed, Revoke, etc.) ✅
- **Files:** `Asset.cs`, `Share.cs`, `ZipDownload.cs`

### 3.4 Add transaction boundaries to multi-step operations ⏸️ DEFERRED
- **Status:** Deferred — EF Core's `SaveChangesAsync` already provides implicit transaction per call; explicit `IDbContextTransaction` adds complexity without clear benefit for current usage patterns

### 3.5 Remove presentation/infrastructure from domain ✅
- Moved `GetHumanReadableSize()` to UI helper
- Moved `ToDbString()`/`FromString()` to infrastructure (replaced by EF value converters in 3.1)
- `HangfireJobId` on `ZipDownload` kept as pragmatic compromise

---

## Phase 4: HTTP Verbs + Config Validation ✅ COMPLETED

### 4.1 Fix HTTP verb issues ✅
- `download-all` → `MapPost` (both Collection + Share)
- Admin `revoke` → `MapDelete`
- `UpdateSharePassword` → `MapPatch`
- **Files:** `CollectionEndpoints.cs`, `ShareEndpoints.cs`, `AdminEndpoints.cs`

### 4.2 Add configuration validation ✅
- Added `[Required]` attributes to all settings classes
- Called `.ValidateOnStart()` in DI registration
- **Files:** All 5 settings classes in `Configuration/`, `InfrastructureServiceExtensions.cs`, `ServiceCollectionExtensions.cs`

---

## Phase 5: Cleanup + Localization ✅ COMPLETED

### 5.1 Remove template pages ✅
- Deleted `Counter.razor`, `Weather.razor`
- `Home.razor` converted to role-based dashboard (see Phase 5.4)
- `Error.razor` localized with MudBlazor-compatible resource strings

### 5.2 Full localization pass ✅
- Added ~16 new resource keys to `CommonResource.resx` / `CommonResource.sv.resx`
- Localized 15 files across `Dam.Ui/` (Pages + Components):
  `Routes.razor`, `Error.razor`, `AssetDetail.razor`, `Share.razor`, `Assets.razor`,
  `AssetGrid.razor`, `CreateUserDialog.razor`, `ShareLinkDialog.razor`,
  `SharePasswordDialog.razor`, `CreateShareDialog.razor`, `CollectionTree.razor`,
  `AddToCollectionDialog.razor`, `ManageUserAccessDialog.razor`, `UserAccessDialog.razor`,
  `MainLayout.razor`

### 5.3 Localization review fixes (post-review) ✅
- **Critical fix:** Removed `static` from `MainLayout.DisplayName()` (was referencing instance `CommonLoc`)
- **Critical fix:** Moved `Assets.razor` `_breadcrumbs` field initializer to `OnInitializedAsync()` (localizer is null during construction)
- **Consistency:** Added `.Value` to 17 `LocalizedString` usages in C# code across 6 files (`Assets.razor`, `Share.razor`, `AssetDetail.razor`, `CollectionTree.razor`, `SharePasswordDialog.razor`, `CreateShareDialog.razor`)
- **Cleanup:** Removed redundant `@using` directives from `Routes.razor` and `Error.razor`
- **Verified:** Zero compile errors after all changes

### 5.4 Role-based Dashboard ✅
- **Why:** `Home.razor` was a plain redirect to `/collections` — user chose to add a proper dashboard (Q&A: option 1 + 3)
- **Backend:**
  - New `DashboardDto`, `DashboardStatsDto`, `DashboardShareDto`, `AuditEventDto` in `Dam.Application/Dtos/DashboardDtos.cs`
  - New `IDashboardService` interface + `DashboardService` implementation with role-scoped queries
  - New `GET /api/dashboard` endpoint (`DashboardEndpoints.cs`)
  - Registered in DI + endpoint mapper
  - Added `GetDashboardAsync()` to `AssetHubApiClient`
- **Frontend:** `Home.razor` rewritten (~270 lines) with MudBlazor layout:
  - **Admin:** Platform stats cards (total assets, storage, collections, users, shares), recent assets grid, active shares sidebar, recent activity timeline
  - **Manager:** Recent assets (own), active shares (own), recent activity (own)
  - **Contributor/Viewer:** Recent assets, quick access to collections
- **Localization:** ~22 `Dashboard_*` resource keys added to both `CommonResource.resx` and `CommonResource.sv.resx`

---

## Phase 6: Testing Plan ⬜ NOT STARTED

### 6.1 Fix silent-pass tests
- Backend: Replace `// Should not throw` with `Record.ExceptionAsync`
- E2E: Replace `if (isVisible)` guards with `expect().toBeVisible()` assertions
- **Files:** `AssetRepositoryTests.cs`, `CollectionAclRepositoryTests.cs`, `ShareRepositoryTests.cs`, E2E specs

### 6.2 Write service-layer integration tests (top priority)
- Focus: `AssetService`, `ShareAccessService`, `CollectionService`, `AssetDeletionService`, `CollectionAclService`, `AdminService`
- Use: Testcontainers + mocks for MinIO/Keycloak

### 6.3 Write API endpoint integration tests
- Use existing `CustomWebApplicationFactory`
- Cover all 5 endpoint files

### 6.4 Write tests for untested UI components (8 components)

---

## Full Worker Cleanup ⏸️ PARTIALLY DONE

- [Phase 1] Register `IMemoryCache` ✅
- [Phase 1] Comment updated to "required by repository layer" ✅
- Add Serilog integration (match API logging format) ⬜
- Add `appsettings.json` for Worker ⬜
- Swap `Hangfire.AspNetCore` → `Hangfire.NetCore` ⬜
- Add `CancellationToken` support to `StaleUploadCleanupJob` ⬜
- Fix `ASPNETCORE_ENVIRONMENT` → `DOTNET_ENVIRONMENT` in compose ⬜
- Remove unused `IMinIOAdapter` resolution in cleanup job ⬜

---

## Deferred Items Summary

| Item | Reason |
|------|--------|
| 1.5 `Dictionary<string, object>` JsonElement | Needs deeper consumer analysis; low runtime impact |
| 3.2 Private setters + constructors | Too high risk vs. value; touches every entity creation site |
| 3.4 Explicit `IDbContextTransaction` | EF Core implicit transactions sufficient for current patterns |
| Worker full cleanup (Serilog, appsettings, etc.) | Lower priority; worker functions correctly |

---

## Verification Protocol

For each deliverable:
1. Read the target file(s) before editing
2. Make the change
3. Check for compile errors
4. Verify no downstream breakage (grep for usages)
5. Run relevant tests if applicable
