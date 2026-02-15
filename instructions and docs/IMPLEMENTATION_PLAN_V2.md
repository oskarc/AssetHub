# AssetHub Implementation Plan — V2 (Post-MVP)

**Created**: 2026-02-08  
**Last Updated**: 2026-02-12  
**Context**: All MVP features, audit fixes, and code quality work are complete (see V1). This document tracks remaining and future work.

---

## Status Legend

| Status | Meaning |
|--------|---------|
| ⬜ | Not started |
| 🔄 | In progress |
| ✅ | Completed |
| ⏭️ | Skipped / Deferred |

---

## 1. Create User via Keycloak Admin API

**Priority**: High  
**Status**: ✅ Complete (pre-existing)  
**Completed**: Pre-V2 (implemented during MVP)  
**Estimate**: 6-10 hours  
**Description**: Implement user creation from the Admin UI using the Keycloak Admin REST API.

> **Note**: This feature was already fully implemented before V2 was created. The endpoint (`POST /api/admin/users`), `KeycloakUserService`, `UserProvisioningService`, `CreateUserDialog.razor`, and Admin page integration all exist and work.

**Dependencies**: Keycloak admin API access configured, SMTP server (optional, for email notifications)

### Scope

#### 1.1 Keycloak Admin API Integration
- Install `Keycloak.AuthServices.Sdk` or use HttpClient for Keycloak Admin REST API
- Configure Keycloak admin client credentials in appsettings.json
- Implement `IKeycloakUserService` for user management operations
- Methods: CreateUser, UpdateUser, ResetPassword, EnableUser, DisableUser
- Handle Keycloak API authentication (service account or admin user)

#### 1.2 Backend API Endpoint
- `POST /api/admin/users` — Create new user
- Request DTO: `CreateUserDto`
  ```csharp
  public class CreateUserDto
  {
      public required string Username { get; set; }
      public required string Email { get; set; }
      public required string FirstName { get; set; }
      public required string LastName { get; set; }
      public required string Password { get; set; }
      public bool EmailVerified { get; set; } = false;
      public bool RequirePasswordChange { get; set; } = true;
      public List<string> InitialCollectionIds { get; set; } = new();
      public string InitialRole { get; set; } = "viewer";
  }
  ```
- Authorization: Admin role required
- Create user in Keycloak, optionally assign to collections via CollectionAcl
- Return user details or error

#### 1.3 UI Components
- **CreateUserDialog.razor** component with:
  - Username, Email, First Name, Last Name fields
  - Password options: generate temporary (recommended) or manual entry
  - "Require password change on first login" checkbox (default: checked)
  - Optional initial collection access with role selector
- "Create User" button on Admin page Users tab
- Success message + temporary password display with clipboard copy
- Error handling for duplicate username/email

#### 1.4 Validation
- Username: 3-50 chars, alphanumeric + underscore + hyphen, no spaces, unique in realm
- Email: Valid format, unique
- Password: Min 8 chars, uppercase, number, special character (per Keycloak policy)
- Client-side + server-side validation

#### 1.5 Password Handling
- **Option 1 (Recommended)**: Generate secure 16-char password, display once, mark as temporary
- **Option 2**: Admin enters password with strength indicator + confirm field

#### 1.6 Email Notification (Optional)
- Welcome email with application URL, username, temporary password
- Requires SMTP configuration in appsettings.json

#### 1.7 Keycloak Configuration
- Dedicated service account with `manage-users` + `view-realm` permissions
- Credentials in appsettings.json (or Key Vault)
- Token endpoint + admin API base URL configured

#### 1.8 Keycloak Admin API Example
```csharp
public async Task<string> CreateUserAsync(CreateUserDto dto)
{
    var user = new
    {
        username = dto.Username,
        email = dto.Email,
        firstName = dto.FirstName,
        lastName = dto.LastName,
        enabled = true,
        emailVerified = dto.EmailVerified,
        credentials = new[]
        {
            new { type = "password", value = dto.Password, temporary = dto.RequirePasswordChange }
        }
    };
    var response = await _httpClient.PostAsJsonAsync(
        $"{_keycloakBaseUrl}/admin/realms/{_realm}/users", user);
    response.EnsureSuccessStatusCode();
    var location = response.Headers.Location?.ToString();
    return location?.Split('/').Last() ?? "";
}
```

#### 1.9 Testing Checklist
- [ ] Create user with generated password
- [ ] Create user with manual password
- [ ] Verify login with temporary password
- [ ] Verify password change required on first login
- [ ] Duplicate username/email rejection
- [ ] Invalid email/weak password rejection
- [ ] Non-admin cannot create users
- [ ] Collection assignment during creation
- [ ] Keycloak API error handling

---

## 2. Metrics & Observability

**Priority**: Medium  
**Status**: 🔄 Partial (structured logging done, dashboarding deferred)  
**Estimate**: 8-12 hours  
**Description**: Monitor application health, performance, and usage in production.

**Dependencies**: Tooling decision; Docker Compose updated for new services

### Scope

#### 2.1 Evaluate Tooling Options
- **OpenTelemetry** (.NET native) — vendor-neutral, exports to multiple backends
- **Prometheus + Grafana** — pull-based metrics, mature dashboarding
- **Seq** — structured log aggregation (lightweight, self-hosted)
- **Elastic APM / ELK Stack** — full observability suite
- Decision criteria: self-hosted vs cloud, cost, complexity, team familiarity

#### 2.2 Metrics to Capture
- **HTTP**: Request rate, latency (p50/p95/p99), error rate per endpoint
- **Business**: Uploads/day, shares created, active users, assets processed
- **Infrastructure**: CPU/memory usage, DB connection pool, MinIO latency
- **Background Jobs**: Hangfire queue depth, processing time, failure rate
- **Cache**: Hit/miss ratio

#### 2.3 Structured Logging — ✅ DONE (2026-02-15)
- Serilog.AspNetCore configured with `UseSerilog()` bootstrap logger + host builder integration
- Console sink with human-readable template (dev) and CompactJsonFormatter (prod)
- Enrichers: `FromLogContext`, `WithEnvironmentName`, `WithMachineName`, `WithThreadId`, `Application="AssetHub"`
- `UseSerilogRequestLogging()` middleware with `RequestHost`, `UserAgent`, `UserId` enrichment
- Per-environment overrides via `Serilog` config section (replaces `Logging` section)
- Dev: `Debug` default, `Information` for ASP.NET Core + EF Core commands
- Prod: `Warning` default, CompactJSON output for log aggregation
- Global `try/catch` with `Log.Fatal` + `Log.CloseAndFlush` for startup error capture

#### 2.4 Health Checks — ✅ DONE (2026-02-08)
- `AspNetCore.HealthChecks.NpgSql` for PostgreSQL, custom checks for MinIO + Keycloak
- `/health` (liveness) and `/health/ready` (readiness) endpoints implemented
- Auto-migration on startup, auto-bucket creation, Docker health check in compose

#### 2.5 Dashboarding
- Set up Grafana dashboards (or equivalent) for key metrics
- Define alerting rules (error rate spike, job queue backlog, disk usage)

---

## 3. Frontend Testing

**Priority**: Medium  
**Status**: ✅ Complete  
**Completed**: 2026-02-09  
**Estimate**: 12-20 hours  
**Description**: Establish a frontend testing strategy for the Blazor Server UI.

**Dependencies**: None for bUnit; Docker Compose environment for Playwright

### Deliverables

#### 3.1 bUnit Component Tests — ✅ Complete
- Created `tests/Dam.Ui.Tests/` project with bUnit v2, xUnit, Moq, MudBlazor 8
- Made all `AssetHubApiClient` methods `virtual` for Moq mocking
- Base class `BunitTestBase` with pre-configured services:
  - Mock `AssetHubApiClient`, `IUserFeedbackService`, `IDialogService`
  - `StubStringLocalizer<T>` for all 5 resource types (CommonResource, AssetsResource, CollectionsResource, SharesResource, AdminResource)
  - MudBlazor services, MudPopoverProvider, JSInterop Loose mode
  - `IAsyncLifetime` for proper async disposal of MudBlazor services
  - `ShowDialogAsync<T>()` helper for dialog component testing
- **210 tests across 13 test files**, all passing:
  - **Components** (10 files): EmptyState (9), AssetGrid (12), CollectionTree (10), CreateCollectionDialog (8), EditAssetDialog (12), CreateShareDialog (12), AddToCollectionDialog (8), ManageAccessDialog (17), AssetUpload (10), LanguageSwitcher (5)
  - **Services** (3 files): RolePermissions (8 theories), AssetDisplayHelpers (15+ theories), UserFeedbackService (12)
- Test coverage includes: rendering, role-based visibility, form validation, dialog workflows, API mock verification, error handling, localization key usage, MudBlazor component interactions (MudSelect dropdowns via PopoverProvider, icon SVG assertions)

#### 3.2 Playwright E2E Tests — ✅ Complete (pre-existing + additions)
- **14 spec files, ~173 tests** covering:
  - Auth (login/logout, Keycloak OIDC, protected routes)
  - Navigation & Layout (drawer, dark mode, direct URLs)
  - Collections CRUD (create, rename, sub-collections, breadcrumbs)
  - Asset operations (upload, browse, search, filter, sort, detail, edit, share, delete)
  - Sharing (create, public access with password, revocation, invalid token)
  - Admin (shares, collection access, user management)
  - All Assets page (search, filters, pagination, card actions)
  - API integration (30+ endpoint tests, auth guards)
  - ACL/permissions (grant, upgrade, revoke, role visibility)
  - Viewer role restrictions
  - Edge cases (404s, rapid navigation, debounce, browser history)
  - Responsive design & basic accessibility
  - Full workflow scenarios (create→upload→share→admin→cleanup)
  - **Language switching** (new: spec 14 — switcher visibility, dropdown options, culture switching, cookie persistence, round-trip English↔Svenska)
- Page Object Model for all app pages (8 POMs)
- ApiHelper for test data setup/teardown
- Sequential execution with auth state reuse

#### 3.3 CI Integration
- bUnit runs on every build via `dotnet test` (fast, <1s for 210 tests)
- Playwright configured for CI (retries, JUnit reporter, trace/screenshot/video on failure)

---

## 4. Deployment Playbooks & Onboarding Guide

**Priority**: High  
**Status**: ✅ Complete  
**Completed**: 2026-02-08  
**Estimate**: 10-16 hours  
**Description**: Step-by-step playbooks for cloning the repo and standing up a fully working AssetHub instance.

**Deliverables**:
- `.env.template` — documented environment variable template
- `docker-compose.prod.yml` — production Docker Compose (all services, memory limits, no exposed internal ports)
- `docs/DEPLOYMENT.md` — comprehensive 14-section deployment guide covering quickstart, Keycloak/MinIO/DB setup, reverse proxy / TLS (Caddy/Nginx/Traefik), backup & restore, upgrades, security hardening checklist, troubleshooting, and env var reference

**Dependencies**: Stable configuration schema

### Scope

#### 4.1 Infrastructure Playbook
- **Docker Compose (Self-Hosted)**
  - Production-ready `docker-compose.prod.yml` (app, worker, PostgreSQL, MinIO, Keycloak)
  - `.env.template` with every variable documented
  - Volume mounts, networking, TLS/SSL (Nginx/Traefik + Let's Encrypt)
  - Resource limits and restart policies
- **Kubernetes (Optional)**: Helm chart or Kustomize manifests
- **Cloud Guides (Optional)**: AWS (ECS/RDS/S3), Azure (App Service/Blob), bare metal
- **Backup & Restore**: pg_dump schedule, MinIO replication, Keycloak export/import

#### 4.2 Keycloak Setup Playbook
- Realm creation script or importable `realm-export.json`
- OIDC client with correct redirect URIs, scopes, mappers
- Role definitions, user federation (LDAP/AD)
- SMTP for email verification/password reset
- Admin service account for Create User API (#1)
- Checklist: verify token/userinfo/JWKS endpoints

#### 4.3 MinIO Setup Playbook
- Bucket creation script + access policy
- CORS configuration for presigned uploads
- Lifecycle rules (auto-delete incomplete multipart)
- Migration guide: MinIO → AWS S3 / Azure Blob

#### 4.4 Application Configuration Playbook
- `appsettings.Production.json` template with all sections
- Environment variable override reference
- CORS, logging, feature flags

#### 4.5 Database Setup Playbook
- EF Core migrations: how to apply
- Seed data, connection string with SSL, performance tuning (`pg_trgm`)

#### 4.6 First-Run Quickstart ("5-minute setup")
1. Clone repo
2. Copy `.env.template` → `.env`, fill in values
3. `docker compose -f docker-compose.prod.yml up -d`
4. Run database migrations
5. Import Keycloak realm
6. Create first admin user in Keycloak
7. Open browser → login → create collection → upload asset
- Troubleshooting FAQ
- Health check verification

#### 4.7 Upgrade & Migration Guide
- Pull, migrate, restart procedure
- Breaking change policy + changelog format
- Database migration safety

#### 4.8 Security Hardening Checklist
- Change all default passwords
- Enable HTTPS everywhere
- Restrict Hangfire dashboard access
- Review Keycloak client settings
- `ASPNETCORE_ENVIRONMENT=Production`
- Firewall rules

---

## 5. Backend Integration Testing

**Priority**: High  
**Status**: ✅ Repository & Edge Case tests complete (2026-02-09). API integration tests deferred.  
**Estimate**: 8-12 hours (repository tests done ~6h; API tests remaining ~4h)  
**Description**: Comprehensive backend test suite for all repositories, endpoints, and edge cases.

### Implementation (2026-02-09)

Test project: `tests/Dam.Tests/` — added to solution, builds with 0 errors, 0 warnings.

**Stack**: xUnit 2.9.3, Moq 4.20, Testcontainers.PostgreSql 4.x (real PostgreSQL 16-alpine in Docker), Microsoft.AspNetCore.Mvc.Testing 9.0.

**Architecture**: Shared PostgreSQL container via `PostgresFixture` (xUnit Collection Fixture). Each test class gets an isolated database via `CreateDbContextAsync()` which creates a unique DB, runs `EnsureCreatedAsync()`, and creates `pg_trgm` extension. `CustomWebApplicationFactory` ready for future API integration tests with mocked external services and `TestAuthHandler` for fake auth.

#### 5.1 Test Project Setup — ✅ Done
- `tests/Dam.Tests/Dam.Tests.csproj` with all packages
- `tests/Dam.Tests/GlobalUsings.cs` (global using Xunit)
- `tests/Dam.Tests/Fixtures/PostgresFixture.cs` — shared container + DB factory
- `tests/Dam.Tests/Fixtures/CustomWebApplicationFactory.cs` — WebApplicationFactory for API tests
- `tests/Dam.Tests/Fixtures/TestAuthHandler.cs` — fake auth handler + claims providers
- `tests/Dam.Tests/Helpers/TestData.cs` — entity factory methods

#### 5.2 Repository Tests — ✅ Done (76 tests)
- **AssetRepositoryTests** (21 tests): GetById, GetByCollection (JOIN + pagination), CountByCollection, Create, Update, Delete, DeleteByCollection, Search (title, description, case-insensitive, filter by type, sort), SearchAll (ACL filtering, excludes non-ready), GetByOriginalKey, GetByType, GetByStatus
- **AssetCollectionRepositoryTests** (12 tests): GetCollectionsForAsset, AddToCollection (creates/duplicate/missing asset/missing collection), RemoveFromCollection, BelongsToCollection, GetCollectionIdsForAsset (cached), GetCollectionIdsForAssets (batch)
- **CollectionRepositoryTests** (17 tests): GetById (exists/not-exists/includes-acls/no-acls-default/includes-children), GetRootCollections (only-roots/orders-by-name), GetChildren, GetAccessibleCollections, Create (persists/sets-created-at), Update, Delete (removes/cascades-children/cascades-acls), Exists, GetAllWithAcls
- **CollectionAclRepositoryTests** (11 tests): GetByCollection, GetByPrincipal, SetAccess (creates/updates), RevokeAccess (removes/no-op), RevokeAllAccess, GetByUser, GetAll
- **ShareRepositoryTests** (15 tests): GetById, GetByTokenHash, GetByScope, Create, Update, Delete, IncrementAccess (single/multiple), GetByUser (filtered/paginated), GetAll (includes asset/collection navigation)

#### 5.3 API Integration Tests — ⬜ Not started
- Asset CRUD (upload, get, update, delete, get all)
- Collection assignment (get, add, remove)
- Rendition endpoints (download, preview, thumb, medium, poster)
- Share endpoints (create, download shared, preview shared)
- Permission scenarios across all endpoints

#### 5.4 Edge Cases — ✅ Done (10 tests)
- `MultiCollectionAccessTests` (10 tests): Asset in multiple collections (found-from-each, all-collection-ids), RemoveFromOneCollection (doesn't-affect-others, becomes-orphaned), CollectionDeletion (cascades-asset-collections-not-assets, asset-in-other-still-accessible), MixedRoles (different-roles-different-collections), OrphanedAsset (not-visible-in-search-all, still-accessible-by-id), ACL cascade on deletion, HierarchicalDeletion (cascades 3 levels deep)

#### 5.5 Success Criteria
- [x] All repository tests compile and are discoverable (107 tests)
- [x] Test isolation via per-class databases (Testcontainers)
- [x] 0 build warnings in test project
- [x] All 107 tests pass (2026-02-12) — fixed `ManyServiceProvidersCreatedWarning` + change-tracker bleed
- [ ] API integration tests (deferred)
- [ ] Code coverage measurement

---

## 6. Collection ACL Inheritance (Inherited Permissions)

**Priority**: High  
**Status**: ✅ Complete  
**Completed**: 2026-02-09  
**Estimate**: 4-6 hours  
**Description**: Permissions on a top-level collection grant the same access to all child collections. Users who are granted a role on a parent collection should automatically hold that same role on all descendant collections, without needing explicit ACL entries on each child.

**Dependencies**: None (self-contained change in authorization layer)

### Implementation (2026-02-09)

**Changes made:**

1. **`CollectionAuthorizationService.GetUserRoleAsync`** — After direct ACL lookup returns null, walks up the `ParentId` chain until a role is found or root is reached. Leverages the existing request-scoped `_roleCache` to avoid redundant lookups, including caching parent roles discovered during the walk.

2. **`ICollectionAuthorizationService.IsRoleInheritedAsync`** — New method that checks whether the user's effective role comes from a parent (no direct ACL on the collection itself). Used by endpoints to populate the `IsRoleInherited` flag on DTOs.

3. **`CollectionRepository.GetAccessibleCollectionsAsync`** — Replaced simple direct-ACL LINQ query with a PostgreSQL recursive CTE that finds all collections with a direct ACL for the user PLUS all their descendant collections.

4. **`CollectionResponseDto.IsRoleInherited`** — New boolean field indicating whether the user's role on a collection is inherited from a parent.

5. **Endpoints updated** — `GetRootCollections`, `GetCollectionById`, and `GetChildren` now populate `UserRole` and `IsRoleInherited` on every response. `GetAllAssets` now uses the authorization service (with inheritance) instead of direct ACL lookup.

6. **Tests** — 18 new tests in `CollectionAclInheritanceTests.cs` covering: parent→child inheritance, grandparent→grandchild, 5-level deep hierarchy, direct ACL overrides inherited, IsRoleInherited true/false, no ACL → null, revoke parent → child loses access, CanCreateSubCollection with inherited role, CanManageAcl with inherited role, multiple users with different inherited roles, GetAccessibleCollections includes descendants.

### Current Behavior

Today every collection requires its own explicit ACL entry. If a user has `contributor` on "Marketing" but "Marketing / Campaigns / Q1" has no ACL for that user, they get **403 Forbidden** when trying to open the child — even though they can *see* it listed as a child.

### Desired Behavior

| Scenario | Expected result |
|----------|----------------|
| User has `contributor` on parent | Effective role on all children/grandchildren is `contributor` |
| User has `viewer` on parent, `manager` on child (explicit) | Effective role on the child is `manager` (explicit wins / highest wins) |
| User has no ACL anywhere in the ancestor chain | 403 as today |
| Admin role on root collection | Full access to entire subtree |

### Scope

#### 6.1 `CollectionAuthorizationService.GetUserRoleAsync` — parent-chain walk
After the direct ACL lookup returns `null`, walk up the `ParentId` chain until a role is found or the root is reached:

```csharp
// Pseudocode — after direct lookup returns null:
var currentId = collectionId;
while (role == null)
{
    var parentId = await dbContext.Collections
        .Where(c => c.Id == currentId)
        .Select(c => c.ParentId)
        .FirstOrDefaultAsync(ct);
    if (parentId == null) break;

    var parentAcl = await dbContext.CollectionAcls
        .FirstOrDefaultAsync(a =>
            a.CollectionId == parentId &&
            a.PrincipalType == "user" &&
            a.PrincipalId == userId, ct);
    role = parentAcl?.Role;
    currentId = parentId.Value;
}
```

The existing per-request `_roleCache` in `CollectionAuthorizationService` already prevents redundant lookups within the same request.

#### 6.2 `CollectionRepository.GetAccessibleCollectionsAsync` — include inherited access
Currently filters by `c.Acls.Any(a => ... userId)` (direct only). Must also include collections whose **ancestors** have an ACL for the user. Options:
- **Recursive CTE** (best performance): single SQL query walking `ParentId`
- **In-memory**: load all collection `Id` + `ParentId` pairs, resolve in C# (acceptable for < 10k collections)

#### 6.3 UI — show effective role
- Collection tree & asset grid should display the user's *effective* role (direct or inherited)
- Consider a visual indicator (e.g., italic or icon) when a role is inherited vs. directly assigned
- Admin "Collection Access" view should distinguish inherited vs. explicit entries

#### 6.4 Edge cases to handle
- Revoking access on a parent should cascade: children that had *only* inherited access lose it
- Granting explicit access on a child that *differs* from the inherited role: explicit should take priority (highest role wins)
- Deleting a parent collection already cascades children — no change needed there
- Moving a collection to a new parent should recalculate inherited ACLs

#### 6.5 Performance considerations
- Parent-chain walk is O(depth), typically 3-5 levels. Acceptable with the request-scoped role cache.
- For `GetAccessibleCollectionsAsync`, a recursive CTE avoids N+1. Example:
  ```sql
  WITH RECURSIVE ancestors AS (
      SELECT c."Id", c."ParentId"
      FROM "Collections" c
      INNER JOIN "CollectionAcls" a ON a."CollectionId" = c."Id"
      WHERE a."PrincipalId" = @userId AND a."PrincipalType" = 'user'
      UNION ALL
      SELECT child."Id", child."ParentId"
      FROM "Collections" child
      INNER JOIN ancestors a ON child."ParentId" = a."Id"
  )
  SELECT DISTINCT "Id" FROM ancestors;
  ```

#### 6.6 Testing
- Add repository tests: parent ACL → child accessible, grandchild accessible
- Add tests: explicit child role overrides inherited, no ACL anywhere → 403
- Add tests: revoke parent → children lose inherited access
- Update existing `MultiCollectionAccessTests` for inheritance scenarios

---

## 7. Manager Access Management UX

**Priority**: High  
**Status**: ✅ Complete (2026-02-09)  
**Estimate**: 4-8 hours  
**Description**: Managers can manage access to their collections (`CanManageAccess` in `RoleHierarchy`), but the only UI for managing collection access is on the Admin page — which is restricted to the Admin role. This means managers have the *permission* but no *surface* to use it.

**Design Decision**: Option A — In-context access panel on the collection detail view.

**Implementation Details**:
- **Chosen approach**: ManageAccessDialog component opened from the Assets page action bar
- **Backend enhancements**:
  - Enhanced `GetCollectionAcls` endpoint to populate `PrincipalName` via `IUserLookupService`
  - Added `SearchUsersForAcl` endpoint (`GET /api/collections/{id}/acl/users/search?q=...`) — returns users not already in ACL, filtered by query, limited to 50 results
  - Both endpoints enforce `CanManageAclAsync` (manager+ check)
- **API client**: Added `GetCollectionAclsAsync`, `SetCollectionAccessAsync`, `RevokeCollectionAccessAsync`, `SearchUsersForAclAsync` methods using manager-facing endpoints (`/api/collections/{id}/acl`)
- **New DTO**: `UserSearchResultDto` (Id, Username, Email) for lightweight user search results
- **New component**: `ManageAccessDialog.razor` — MudBlazor dialog with:
  - Collection info header showing current user's role and inherited indicator
  - User autocomplete search with debounce for granting access
  - Role selector (viewer/contributor/manager — managers can't grant above their level)
  - ACL table with edit/revoke actions, role escalation guard on both grant and revoke
  - Visual distinction: role chips, "Higher role" label for entries the user can't edit
- **Assets page**: "Manage Access" button appears when `CanManageAccess` (manager+), opens ManageAccessDialog
- **Localization**: EN + SV keys added for all dialog strings

### The Problem

Today the Admin page (`/admin`) has three tabs:
1. **Shares** — manage all share links (admin-only concern)
2. **Collection Access** — grant/revoke per-collection roles (relevant to managers)
3. **User Management** — create Keycloak users, view all users (admin-only concern)

Managers should be able to manage access on collections where they hold the manager role, but they should **not** see share management for other users' shares, user management, or access entries for collections they don't manage.

### Design Options

#### Option A: In-context access panel on the collection page
Add an "Access" tab or panel directly to the collection detail view. When a user with `manager+` role views a collection, they see a list of current ACL entries and can grant/revoke access — scoped to that specific collection only.

**Pros**: Discoverable, contextual, no admin page changes needed  
**Cons**: Adds UI complexity to the collection view

#### Option B: Filtered admin page for managers
Allow managers to access `/admin` but only show the "Collection Access" tab, filtered to collections where they are `manager+`. Hide the Shares and User Management tabs entirely.

**Pros**: Reuses existing UI  
**Cons**: The admin page name/framing implies system-wide access; may confuse managers about their scope

#### Option C: Separate "My Collections" management page
A new page (e.g., `/manage`) that shows only the collections the current user manages, with inline access management. Distinct from the admin page.

**Pros**: Clean separation, clear scope for the user  
**Cons**: More pages to maintain, potential duplication with admin page

### Scope (once design is chosen)

#### 7.1 Backend
- The API endpoints already exist and enforce role checks (`POST /api/collections/{id}/acl`, `DELETE /api/collections/{id}/acl/...`) — managers can call them today via API. No backend changes needed unless we add a "list collections I manage" endpoint.
- Consider adding: `GET /api/collections/managed` — returns collections where the current user has `manager+` role

#### 7.2 UI
- Implement the chosen design (A, B, or C)
- Ensure the access management UI shows:
  - Current ACL entries for the collection
  - User search/picker for granting access
  - Role selector (viewer / contributor / manager — managers should not be able to grant admin)
  - Revoke button per entry
- Managers should only be able to grant roles **up to their own level** (a manager cannot create admins)

#### 7.3 Constraints
- **Managers cannot**: see the Shares tab (unless scoped to their own shares), manage users in Keycloak, view access for collections they don't manage
- **Managers can**: grant viewer/contributor/manager on collections they manage, revoke access on those same collections
- **Admins**: continue to see everything on the admin page as today

#### 7.4 Testing
- Manager can grant/revoke access on managed collection
- Manager cannot grant admin role
- Manager cannot see access entries for unmanaged collections
- Manager cannot access user management or share management for other users
- Admin retains full access to everything

---

## 8. Authentication & OIDC Hardening

**Priority**: Critical  
**Status**: ✅ Complete  
**Completed**: 2026-02-12  
**Estimate**: 4-6 hours  
**Description**: Fixed OIDC login loop caused by SameSite=Strict cookies not surviving cross-site redirects after Keycloak DB wipe. Implemented same-site subdomain architecture as the architecturally correct solution. Cleaned up all debugging artifacts and dead code.

### Problem

After a Keycloak database wipe (fresh realm import), the login flow entered an infinite redirect loop. Root cause: the app cookie (`__Host.assethub.auth`) uses `SameSite=Strict`, which means browsers do **not** send it on cross-site navigation responses. When Keycloak (`localhost:8443`) redirected back to the app (`localhost:7252`), the browser treated this as a cross-site redirect and stripped the correlation cookie, causing OpenID Connect to fail with "Correlation failed."

### Solution: Same-Site Subdomain Architecture

Instead of weakening `SameSite` to `Lax` or adding bounce pages, the app and Keycloak now share the same registrable domain:

| Environment | App URL | Keycloak URL |
|-------------|---------|-------------|
| Development | `https://assethub.local:7252` | `https://keycloak.assethub.local:8443` |
| Production | `https://assethub.com` | `https://auth.assethub.com` |

Because both are subdomains of the same registrable domain, `SameSite=Strict` cookies are sent on redirects between them.

### Changes Made

#### 8.1 Certificate & DNS
- Generated self-signed cert (`certs/dev-cert.pfx`, password `devpass123`) with SANs: `assethub.local`, `keycloak.assethub.local`, `localhost`
- Cert trusted in `CurrentUser\Root` store
- Hosts file: `127.0.0.1 assethub.local keycloak.assethub.local`

#### 8.2 Docker Compose
- `KC_HOSTNAME: keycloak.assethub.local` for Keycloak
- Docker network alias `keycloak.assethub.local` on Keycloak service (so the API container can resolve it internally)
- API Authority → `https://keycloak.assethub.local:8443/realms/media`
- API BaseUrl → `https://assethub.local:7252`
- Removed deprecated `version: '3.8'`

#### 8.3 App Configuration
- `appsettings.Development.json`: Authority only (`https://keycloak.assethub.local:8443/realms/media`), no BrowserAuthority/ValidIssuer needed
- Removed `BrowserAuthority` / `ValidIssuer` settings and all associated rewrite logic from `Program.cs` (`OnRedirectToIdentityProvider`, `OnRedirectToIdentityProviderForSignOut` handlers)
- Removed PAR (Pushed Authorization Request) disable workaround
- `ValidIssuer` now uses `keycloakAuthority` directly (single source of truth)

#### 8.4 Keycloak Realm
- `media-realm.json`: redirectUris → `https://assethub.local:7252/signin-oidc`, webOrigins → `https://assethub.local:7252`
- Live Keycloak client updated via Admin API to match

#### 8.5 Code Cleanup
- Removed all `[OIDC-DIAG]` diagnostic logging (14 occurrences)
- Removed `OidcBackchannelLoggingPostConfigure.cs` and its DI registration
- Removed `/debug/ping` and `/debug/keycloak/userinfo-probe` endpoints
- Removed `UserInfoProbeRequest` record
- Deleted 6 deprecated files: `_iconcheck.csx`, `apply_nullable_migration.sql`, `fix_shares.sql`, `test-upload.txt`, `test-image.png`, `OidcBackchannelLoggingPostConfigure.cs`
- OIDC events retained: `OnRemoteFailure`, `OnAuthenticationFailed` (functional error handlers), `OnTokenValidated` (role mapping)
- Build: **0 warnings, 0 errors**

### Production Notes
- In production, use `auth.assethub.com` (or similar subdomain) for Keycloak with a proper TLS certificate
- The same-site subdomain approach means `SameSite=Strict` works correctly without any workarounds
- No `BrowserAuthority` / issuer rewriting needed when Keycloak is on a subdomain of the app domain

---

## 9. Deferred Items (Low Priority)

These items were identified during development but intentionally deferred:

| Item | Source | Notes |
|------|--------|-------|
| API Localization | Feature #15 | API error messages remain in English |
| Date/Time & Number Formatting | Feature #15 | Uses default culture formatting |
| Distributed Cache (Redis) | Feature #17 | Only needed for multi-instance deployments |
| Response Caching / Output Caching | Feature #17 | Cache-Control headers for renditions |
| ETag / Conditional Requests | Feature #17 | 304 Not Modified for renditions |
| `[JsonPropertyName]` consistency | Audit R7 | `AssetCollectionDto` has attributes, others don't (cosmetic) |
| Share.razor 401 empty body handling | Audit R6 | Fragile but functional; empty response on wrong password could improve UX |

---

## 10. Known Issues & Open Tasks

| Issue | Impact | Status | Workaround |
|-------|--------|--------|------------|
| Worker crashes with exit code 139 | Background jobs not processing | Open | Run Hangfire in API container instead |
| Keycloak `/health/ready` returns 404 | Docker compose health check limited | Minor | Check via admin console |
| ~~Change language button doesn't work~~ | ~~Users cannot switch UI language~~ | ✅ Fixed | .resx files renamed from `sv-SE` to `sv` to match registered culture code |
| Reset password not implemented | Users cannot reset their own password | ✅ Fixed | Keycloak forgot-password flow (via SMTP/Mailpit), in-app Change Password via `kc_action=UPDATE_PASSWORD` |
| Keycloak data not persisted outside container | DB wipe on container recreation loses all users/config | ✅ Fixed | Keycloak now uses separate `keycloak` DB + named `keycloakdata` volume in both dev & prod compose |

### Open Tasks

| # | Task | Priority | Notes |
|---|------|----------|-------|
| 1 | ~~**Fix language switcher**~~ | ~~Medium~~ | ✅ Fixed (2026-02-15): Resource files were named `*.sv-SE.resx` but registered culture was `"sv"`. .NET looked for `*.sv.resx`, didn't find it, fell back to English. Renamed all 5 `.sv-SE.resx` → `.sv.resx`. |
| 2 | ~~**Implement password reset**~~ | ~~Medium~~ | ✅ Fixed (2026-02-15): Added Mailpit dev SMTP to docker-compose, Keycloak realm SMTP config, `Forgot password?` link on Login page, `/auth/change-password` endpoint with `kc_action=UPDATE_PASSWORD`, account menu with Change Password option in MainLayout. |
| 3 | ~~**Run full test suite**~~ | ~~High~~ | ✅ Fixed (2026-02-12): 107 backend tests + 210 bUnit tests all pass. Fixed `ManyServiceProvidersCreatedWarning` in `PostgresFixture` and change-tracker bleed in `GetByIdAsync_DoesNotIncludeAcls_ByDefault`. E2E tests (173) require full Docker environment. |
| 4 | ~~**Persist Keycloak data**~~ | ~~High~~ | ✅ Fixed (2026-02-12): Separate `keycloak` DB in prod compose, `keycloakdata` named volume in both compose files, `init-keycloak-db.sh` mounted in prod |
| 5 | ~~**Define user deletion policy**~~ | ~~Medium~~ | ✅ Fixed (2026-02-15): Policy confirmed — collections/assets retained, ACLs removed, shares revoked. `DELETE /users/{userId}` now resilient to already-deleted Keycloak users (cleans up app data even if user gone). All user-name fallbacks changed from raw GUID to `"Deleted User (abc12345)"` label in Admin shares, Admin users, CollectionTree, and ManageAccessDialog. `UserSyncService` Hangfire job handles ghost cleanup. |
| 6 | ~~**Smart asset deletion (multi-collection)**~~ | ~~High~~ | ✅ Fixed (2026-02-15): Implemented multi-collection-aware deletion. `DeleteByCollectionAsync` now distinguishes exclusive vs shared assets. `DeleteAsset` endpoint accepts `fromCollectionId`/`permanent` params. New `GetAssetDeletionContext` endpoint. `DeleteAssetDialog` component shows Remove/Delete options for multi-collection assets. `RemoveAssetFromCollection` cleans up orphans. `DeleteCollection` cleans up MinIO for exclusive assets. |
| 7 | ~~**Backend test gaps**~~ | ~~Medium~~ | ✅ Fixed (2026-02-15): P1 → Added `SmartDeletionTests.cs` (6), `AuthorizationEdgeCaseTests.cs` (18). P2 → Added SearchAsync sort/pagination/combined (8), UpdateAsync concurrent (1), ShareRepository sort (4). Total backend: 120 tests. API HTTP tests deferred. |
| 8 | ~~**bUnit / UI test gaps**~~ | ~~Medium~~ | ✅ Fixed (2026-02-15): P1 → `EditAssetDialog` (2), `CreateShareDialog` (2), `ManageAccessDialog` (2). P2 → `AddToCollectionDialog` submit (2), `AssetGrid` share/delete chains (2), `LanguageSwitcher` culture-switch (1). Total bUnit: 221 tests. CollectionTree DnD and AssetUpload pipeline deferred (P3). |

### 10.6 Smart Asset Deletion (Multi-Collection Logic)

When a user deletes an asset from a collection, the behaviour must account for the asset's presence in other collections and the user's authority across them.

**Rules:**

| Scenario | Behaviour |
|----------|----------|
| Asset exists in **only 1 collection** | Delete the asset outright (no prompt needed). |
| Asset exists in **multiple collections** and the user has **contributor+ on ALL** of them | Prompt the user: *"Remove from this collection"* or *"Delete permanently"*. **Remove** → detach from this collection only. **Delete** → destroy the asset and all join records. |
| Asset exists in a collection where the user **does NOT have contributor+** | The asset can only be **removed from the collections where the user has contributor+**. Even if the prompt says "Delete", the system silently removes the asset from the user's authorized collections only — the asset continues to exist in the unauthorized collection(s). The user is not informed that they lack authority to fully delete; from their perspective the operation succeeded. |

**Implementation checklist:**
- [x] Backend: new endpoint or extended `DELETE` that checks collection memberships + user authority per collection
- [x] Backend: if removing from last authorized collection and unauthorized collections remain, asset survives
- [x] UI: detect multi-collection scenario and show Remove/Delete prompt
- [x] UI: single-collection scenario skips the prompt and deletes directly
- [ ] Tests: asset in 1 collection → deleted
- [ ] Tests: asset in 2 collections, user has access to both → prompt, remove vs delete
- [ ] Tests: asset in 2 collections, user lacks access to one → only removed from authorized collection
- [ ] Tests: verify asset still accessible from unauthorized collection after "delete"

### 10.7 Backend Test Gaps

Identified 2026-02-13. All items are additive — existing tests pass.

| Area | Gap | Priority |
|------|-----|----------|
| `CollectionAuthorizationService` | ~~`CanCreateRootCollectionAsync` — zero test coverage~~ | ~~P1~~ | ✅ Added in `AuthorizationEdgeCaseTests.cs` (4 tests: valid, empty, null, whitespace) |
| `CollectionAuthorizationService` | ~~`CanManageAclAsync` / `CanCreateSubCollectionAsync` — no negative cases~~ | ~~P1~~ | ✅ Added in `AuthorizationEdgeCaseTests.cs` (8 tests: viewer/contributor/no-ACL denied, manager/admin/contributor allowed) |
| `CollectionAuthorizationService` | ~~`CheckAccessAsync` — no test for direct ACL hit or non-existent collection~~ | ~~P1~~ | ✅ Added in `AuthorizationEdgeCaseTests.cs` (4 tests: direct hit, insufficient role, non-existent, no ACL) |
| `CollectionAuthorizationService` | ~~`GetUserRoleAsync` — no multi-user same-chain test~~ | ~~P2~~ | ✅ Added in `AuthorizationEdgeCaseTests.cs` (2 tests: 3 users independent roles, direct ACL priority over inherited) |
| `AssetRepository` | ~~`DeleteByCollectionAsync` — no test for shared-asset scenario~~ | ~~P0~~ | ✅ Added in `SmartDeletionTests.cs` (6 tests: exclusive deleted, shared preserved, mixed, 3-collection, empty, multiple exclusive) |
| `AssetRepository` | ~~`SearchAsync` — shallow: no sort-order, pagination, or combined-filter tests~~ | ~~P2~~ | ✅ Added 8 tests: sort by title/size/created (asc+desc), default sort, pagination with total count, combined query+type filter, combined query+type+sort |
| `AssetRepository` | ~~`UpdateAsync` — no concurrent-update / conflict test~~ | ~~P2~~ | ✅ Added 1 test: concurrent modification with two DbContexts verifying last-write-wins |
| `ShareRepository` | ~~`SearchAllAsync` — no sort/pagination tests~~ | ~~P2~~ | ✅ Added 4 tests: GetAllAsync sort order, GetByUserAsync sort+empty, GetByScopeAsync sort order |
| API integration tests | All endpoints untested at HTTP level (deferred from §5.3) | P2 |

### 10.8 bUnit / UI Test Gaps

Identified 2026-02-13. Pattern: components are well-tested for static rendering and role-based visibility, but weak on interactive mutation flows (submit → API → close).

| Component | Gap | Priority |
|-----------|-----|----------|
| **EditAssetDialog** | ~~No `SaveAsync` submission test~~ | ~~P1~~ | ✅ Added: Save + error handling tests (2 tests) |
| **CreateShareDialog** | ~~No `CreateShare` submission test~~ | ~~P1~~ | ✅ Added: CreateShare + error handling tests (2 tests) |
| **AssetUpload** | No actual file-upload pipeline test (InputFile → progress → completion) | P2 |
| **ManageAccessDialog** | ~~No grant / revoke / edit-role flow tests~~ | ~~P1~~ | ✅ Added: RevokeAccess + error handling tests (2 tests) |
| **AddToCollectionDialog** | ~~No submission test — collections are selected but confirm never clicked~~ | ~~P2~~ | ✅ Added 2 tests: Submit_Calls_Api_And_Shows_Success, Submit_Handles_Api_Error_On_Add |
| **AssetGrid** | ~~No delete-chain or share-chain tests (button → confirm dialog → API call)~~ | ~~P2~~ | ✅ Added 2 tests in AssetGridInteractionTests: Share_Button_Click_Opens_CreateShareDialog, Delete_Button_Click_Opens_DeleteAssetDialog |
| **CollectionTree** | No drag-and-drop or context-menu interaction tests | P3 | Deferred — requires complex MudTreeView DnD simulation |
| **LanguageSwitcher** | ~~Tests only check rendering; no culture-switch-via-navigation test~~ | ~~P2~~ | ✅ Added 1 test: Changing_Culture_Sets_Cookie_Via_JsInterop (module import + setCookie verification) |
| ~~General~~ | ~~Several near-no-op tests use `Times.AtMostOnce()`~~ | ~~P1~~ | ✅ Verified: no `AtMostOnce()` usages found in test suite |

---

## 11. Phase Completion Summary (Updated 2026-02-12)

| Phase | Status | Notes |
|-------|--------|-------|
| Phase 1A: Docker & Database | ✅ COMPLETE | All services running, migrations applied |
| Phase 1B: Collections API | ✅ COMPLETE | Full CRUD + ACL |
| Phase 1C: Authentication | ✅ COMPLETE | Cookie + JWT Bearer, confidential client |
| Phase 2A: Upload & Processing | ✅ COMPLETE | Presigned uploads, thumbnails, progress tracking |
| Phase 2B: Video & Presigned URLs | ✅ COMPLETE | Video metadata, poster frames, presigned downloads |
| Phase 3A: UI - Collections & Grid | ✅ COMPLETE | Blazor pages, search/filter, asset detail, all components |
| Phase 3B: Sharing & Audit | ✅ COMPLETE | Share tokens, public endpoints, full audit logging |
| Phase 3C: Testing | ✅ COMPLETE | 86 repository + edge case tests (2026-02-09); API integration & frontend tests deferred |
| Phase 3D: Deployment & Docs | ✅ COMPLETE | Production compose, .env.template, full deployment guide, one-click install |
| Code Audit (25 issues) | ✅ COMPLETE | See AUDIT_IMPLEMENTATION_PLAN.md |
| Post-Audit Review (5 fixes) | ✅ COMPLETE | See AUDIT_IMPLEMENTATION_PLAN.md |
| Build Warnings Cleanup | ✅ COMPLETE | 0 errors, 0 warnings |
| Auth & OIDC Hardening | ✅ COMPLETE | Same-site subdomain architecture, SameSite=Strict fix, code cleanup (2026-02-12) |

---

## Priority Order

1. ~~**Deployment Playbooks** (#4)~~ — ✅ Complete (2026-02-08)
2. ~~**Create User via Keycloak** (#1)~~ — ✅ Already implemented (pre-V2)
3. ~~**Backend Integration Testing** (#5)~~ — ✅ Repository & edge case tests done (86 tests); API tests deferred
4. ~~**Collection ACL Inheritance** (#6)~~ — ✅ Complete (2026-02-09)
5. ~~**Manager Access Management UX** (#7)~~ — ✅ Complete (2026-02-09): Option A — in-context ManageAccessDialog
6. ~~**Auth & OIDC Hardening** (#8)~~ — ✅ Complete (2026-02-12): Same-site subdomain architecture
7. ~~**Persist Keycloak data** (#10.4)~~ — ✅ Fixed (2026-02-12)
8. ~~**Run full test suite** (#10.3)~~ — ✅ Complete (2026-02-12): 107 backend + 210 bUnit = 317 tests passing
9. ~~**Fix language switcher** (#10.1)~~ — ✅ Fixed (2026-02-15): renamed `.sv-SE.resx` → `.sv.resx`
10. ~~**Implement password reset** (#10.2)~~ — ✅ Fixed (2026-02-15): Keycloak forgot-password + in-app change-password
11. ~~**Smart asset deletion** (#10.6)~~ — ✅ Fixed (2026-02-15): Multi-collection-aware delete/remove logic
12. ~~**Define user deletion policy** (#10.5)~~ — ✅ Fixed (2026-02-15): Resilient delete endpoint + "Deleted User" display labels
13. ~~**Backend test gaps** (#10.7)~~ — ✅ Complete: Added 13 tests — SearchAsync sort/pagination/combined-filter (8), UpdateAsync concurrent modification (1), ShareRepository sort-order (4). Total backend: 107 → 120.
14. ~~**bUnit / UI test gaps** (#10.8)~~ — ✅ Complete: Added 5 tests — AddToCollectionDialog submit+error (2), AssetGrid share/delete chains (2), LanguageSwitcher culture-switch via JS interop (1). Total bUnit: 216 → 221.
15. **Frontend Testing** (#3) — Catches UI regressions
16. ~~**Metrics & Observability** (#2)~~ — 🔄 Partial (2026-02-15): Serilog structured logging + request enrichment done. Prometheus/Grafana dashboarding deferred.
