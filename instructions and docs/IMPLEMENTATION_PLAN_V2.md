# AssetHub Implementation Plan — V2 (Post-MVP)

**Created**: 2026-02-08  
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
**Status**: ⬜ Not started  
**Estimate**: 6-10 hours  
**Description**: Implement user creation from the Admin UI using the Keycloak Admin REST API. Currently, users must be manually created in the Keycloak admin console.

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

#### 2.4 Health Checks
- `AspNetCore.Diagnostics.HealthChecks` for readiness/liveness probes
- PostgreSQL, MinIO, Keycloak, Hangfire connectivity checks
- Expose `/health` and `/health/ready` endpoints

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
**Status**: ⬜ Not started  
**Estimate**: 10-16 hours  
**Description**: Step-by-step playbooks for cloning the repo and standing up a fully working AssetHub instance.

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
**Status**: ⬜ Not started  
**Estimate**: 8-12 hours  
**Description**: Comprehensive backend test suite for all repositories, endpoints, and edge cases.

### Scope

#### 5.1 Test Project Setup
```xml
<PackageReference Include="xUnit" Version="2.6.6" />
<PackageReference Include="xUnit.runner.visualstudio" Version="2.5.6" />
<PackageReference Include="Moq" Version="4.20.70" />
<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.0" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.0" />
```

#### 5.2 Repository Tests
- **AssetRepository**: GetById, GetByCollection (JOIN), CountByCollection (JOIN), DeleteByCollection, SearchAsync
- **AssetCollectionRepository**: GetCollectionsForAsset, AddToCollection, RemoveFromCollection, BelongsToCollection, GetCollectionIdsForAsset
- **CanAccessAssetAsync**: viewer+ in any collection → true, system admin → true, no access → false, orphaned assets → false

#### 5.3 API Integration Tests
- Asset CRUD (upload, get, update, delete, get all)
- Collection assignment (get, add, remove)
- Rendition endpoints (download, preview, thumb, medium, poster)
- Share endpoints (create, download shared, preview shared)
- Permission scenarios across all endpoints

#### 5.4 Edge Cases
- Asset in 2+ collections: highest role wins, removing from one doesn't affect others
- Orphaned assets (0 collections): cannot access normally, admin can
- Mixed roles: viewer in A + contributor in B → contributor on shared asset
- Collection deletion: cascade on AssetCollections, assets remain if in other collections

#### 5.5 Success Criteria
- [ ] All unit tests pass (100% new repository methods)
- [ ] All integration tests pass (all endpoints)
- [ ] No 500 errors in manual testing
- [ ] Performance < 1s response time
- [ ] Code coverage > 80% for modified code

---

## 6. Deferred Items (Low Priority)

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

## 7. Known Issues

| Issue | Impact | Status | Workaround |
|-------|--------|--------|------------|
| Worker crashes with exit code 139 | Background jobs not processing | Open | Run Hangfire in API container instead |
| Keycloak `/health/ready` returns 404 | Docker compose health check limited | Minor | Check via admin console |

---

## 8. Phase Completion Summary (Updated 2026-02-08)

| Phase | Status | Notes |
|-------|--------|-------|
| Phase 1A: Docker & Database | ✅ COMPLETE | All services running, migrations applied |
| Phase 1B: Collections API | ✅ COMPLETE | Full CRUD + ACL |
| Phase 1C: Authentication | ✅ COMPLETE | Cookie + JWT Bearer, confidential client |
| Phase 2A: Upload & Processing | ✅ COMPLETE | Presigned uploads, thumbnails, progress tracking |
| Phase 2B: Video & Presigned URLs | ✅ COMPLETE | Video metadata, poster frames, presigned downloads |
| Phase 3A: UI - Collections & Grid | ✅ COMPLETE | Blazor pages, search/filter, asset detail, all components |
| Phase 3B: Sharing & Audit | ✅ COMPLETE | Share tokens, public endpoints, full audit logging |
| Phase 3C: Testing | ⬜ NOT STARTED | See #3 and #5 above |
| Phase 3D: Deployment & Docs | 🔄 PARTIAL | Dev Docker Compose working; prod config pending (see #4) |
| Code Audit (25 issues) | ✅ COMPLETE | See AUDIT_IMPLEMENTATION_PLAN.md |
| Post-Audit Review (5 fixes) | ✅ COMPLETE | See AUDIT_IMPLEMENTATION_PLAN.md |
| Build Warnings Cleanup | ✅ COMPLETE | 0 errors, 0 warnings |

---

## Priority Order

1. **Deployment Playbooks** (#4) — Enables others to deploy and use the system
2. **Create User via Keycloak** (#1) — Removes manual Keycloak admin dependency
3. **Backend Integration Testing** (#5) — Validates correctness of all recent refactoring
4. **Frontend Testing** (#3) — Catches UI regressions
5. **Metrics & Observability** (#2) — Production monitoring
