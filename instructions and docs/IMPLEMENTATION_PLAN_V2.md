# AssetHub Implementation Plan — V2 (Post-MVP)

**Created**: 2026-02-08  
**Last Updated**: 2026-02-09  
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
**Status**: ⬜ Not started  
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

#### 2.3 Structured Logging
- Audit current `ILogger` usage for consistency
- Add correlation IDs for request tracing
- Configure log levels per environment (Debug for dev, Warning+ for prod)
- Consider Serilog sinks for structured output (JSON, Seq, Elasticsearch)

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
**Status**: ⬜ Not started  
**Estimate**: 12-20 hours  
**Description**: Establish a frontend testing strategy for the Blazor Server UI.

**Dependencies**: None for bUnit; Docker Compose environment for Playwright

### Scope

#### 3.1 bUnit Component Tests
- Set up `Dam.Ui.Tests` project with bUnit + xUnit
- Mock `AssetHubApiClient`, `IUserFeedbackService`, `IStringLocalizer<T>`, `NavigationManager`
- Priority components:
  - `AssetGrid.razor` — renders, pagination, empty state, delete
  - `CollectionTree.razor` — tree rendering, selection, rename, delete
  - `CreateShareDialog.razor` — validation, password generation, email list
  - `CreateCollectionDialog.razor` — form submission, validation
  - `EditAssetDialog.razor` — pre-populated fields, tag management
  - `LanguageSwitcher.razor` — culture change, cookie set
  - `AssetUpload.razor` — file selection, progress, errors
- Test with both `en` and `sv` cultures

#### 3.2 Playwright E2E Tests
- Set up `Dam.E2E.Tests` project with Playwright for .NET
- Critical user flows:
  - Login → collections → select → view assets
  - Upload → thumbnail → view detail
  - Create share → open URL → enter password → view content
  - Admin: manage users → assign access
  - Language switch: toggle Swedish → verify → toggle back
- Test fixtures with seeded data
- Run against Docker Compose environment

#### 3.3 CI Integration
- bUnit on every build (fast)
- Playwright on PR / nightly (requires running stack)
- Fail build on test failures

#### 3.4 Visual Regression (Optional)
- Playwright screenshot comparison for key pages

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
- [x] All repository tests compile and are discoverable (86 tests)
- [x] Test isolation via per-class databases (Testcontainers)
- [x] 0 build warnings in test project
- [ ] All tests pass (requires Docker running — infrastructure dependency)
- [ ] API integration tests (deferred)
- [ ] Code coverage measurement

---

## 6. Collection ACL Inheritance (Inherited Permissions)

**Priority**: High  
**Status**: ⬜ Not started  
**Estimate**: 4-6 hours  
**Description**: Permissions on a top-level collection grant the same access to all child collections. Users who are granted a role on a parent collection should automatically hold that same role on all descendant collections, without needing explicit ACL entries on each child.

**Dependencies**: None (self-contained change in authorization layer)

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
**Status**: ⬜ Not started  
**Estimate**: 4-8 hours  
**Description**: Managers can manage access to their collections (`CanManageAccess` in `RoleHierarchy`), but the only UI for managing collection access is on the Admin page — which is restricted to the Admin role. This means managers have the *permission* but no *surface* to use it. This needs a design decision before implementation.

**Dependencies**: Should be considered alongside #6 (ACL Inheritance), as both affect how users interact with collection permissions.

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

## 8. Deferred Items (Low Priority)

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

## 9. Known Issues

| Issue | Impact | Status | Workaround |
|-------|--------|--------|------------|
| Worker crashes with exit code 139 | Background jobs not processing | Open | Run Hangfire in API container instead |
| Keycloak `/health/ready` returns 404 | Docker compose health check limited | Minor | Check via admin console |

---

## 10. Phase Completion Summary (Updated 2026-02-09)

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

---

## Priority Order

1. ~~**Deployment Playbooks** (#4)~~ — ✅ Complete (2026-02-08)
2. ~~**Create User via Keycloak** (#1)~~ — ✅ Already implemented (pre-V2)
3. ~~**Backend Integration Testing** (#5)~~ — ✅ Repository & edge case tests done (86 tests); API tests deferred
4. **Collection ACL Inheritance** (#6) — Parent permissions propagate to children
5. **Manager Access Management UX** (#7) — Give managers a way to manage collection access (design decision needed)
6. **Frontend Testing** (#3) — Catches UI regressions
7. **Metrics & Observability** (#2) — Health checks done; structured logging + dashboarding remaining
