# AssetHub Implementation Plan (2-3 Week MVP)

**Target Launch**: 2-3 weeks  
**Scope**: Single-tenant, Keycloak auth, Blazor Server UI, MinIO + Hangfire backend  
**Success Criteria**: Collections → Upload → Process → Share → Download (end-to-end)

---

## Executive Overview

### What We're Building (MVP)
- **Collections**: Hierarchical folders with role-based access (viewer/contributor/manager/admin)
- **Upload**: Multi-file drag-and-drop with progress tracking
- **Media Processing**: Image thumbnails (ImageMagick), video metadata + poster frames (ffmpeg)
- **Grid UI**: Fast, virtualized asset browser with search/filter
- **Sharing**: Time-limited public share tokens with optional password
- **Audit**: Log upload, processing, share creation, downloads
- **Full-text Search**: PostgreSQL trigram search on asset metadata

### Tech Stack (Fixed)
- **API**: ASP.NET Core 9 (minimal APIs)
- **Frontend**: Blazor Server + MudBlazor
- **Auth**: Keycloak (OIDC) - already integrated
- **Database**: PostgreSQL (EF Core)
- **Storage**: MinIO (S3-compatible, Docker)
- **Background Jobs**: Hangfire with Postgres storage
- **Media Tools**: ImageMagick CLI (container), ffmpeg (container)
- **Deployment**: Docker Compose (local + prod)
- **Testing**: xUnit + Moq (unit), Integration tests, E2E (Selenium/Playwright optional)

### What We're Skipping (Phase 2+)
- Document preview (PDF/PPTX)
- Video transcoding (HLS/DASH)
- Advanced DLP/watermarking
- Mobile responsiveness
- Groups/batch user management
- Advanced analytics

### Planned Features (Next Phase) 🔜

The following features have been identified as high-priority improvements and should be implemented after MVP completion:

#### 1. Multi-Collection Asset Assignment ✅ COMPLETE
**Priority**: High  
**Status**: Implemented on 2026-02-04  
**Description**: Allow a single image/asset to belong to multiple collections simultaneously.
- [x] Add many-to-many relationship between Assets and Collections
  - Created `AssetCollection` join entity with AssetId, CollectionId, AddedAt, AddedByUserId
  - EF migration: `20260204185835_AddAssetCollections`
  - Unique index on (AssetId, CollectionId)
- [x] Create repository layer
  - `IAssetCollectionRepository` interface with GetCollectionsForAsset, AddToCollection, RemoveFromCollection
  - `AssetCollectionRepository` implementation
- [x] API endpoints
  - `GET /api/assets/{id}/collections` - Get all collections for an asset
  - `POST /api/assets/{id}/collections/{collectionId}` - Add asset to collection
  - `DELETE /api/assets/{id}/collections/{collectionId}` - Remove asset from collection
- [x] Updated UI (AssetDetail.razor)
  - Shows all collections with primary indicator
  - Add to collection dialog (AddToCollectionDialog.razor)
  - Remove from collection with confirmation
  - Role-based visibility (contributor+ can manage)
- **Note**: Primary collection (CollectionId) is preserved for backwards compatibility. Additional collections are stored in the join table.

#### 2. All Assets View Page ✅ COMPLETE
**Priority**: High  
**Status**: Implemented on 2026-01-30  
**Description**: A dedicated page showing all assets regardless of collection membership.
- [x] New `/all-assets` page with full asset grid
- [x] Filter/search across entire asset library (by type, search query, sort order)
- [x] Shows collection name for each asset
- [x] Quick navigation to asset's collection
- [x] API endpoint: `GET /api/assets/all` with search/filter support

#### 3. Asset Metadata Editing ✅ COMPLETE
**Priority**: High  
**Status**: Implemented on 2026-01-30  
**Description**: Implement UI and API for editing asset metadata.
- [x] Edit dialog for asset name, description, tags
- [x] API endpoint: `PATCH /api/assets/{id}` with UpdateAssetDto
- [x] EditAssetDialog component with tag management
- [x] Integration in AssetDetail.razor page

#### 4. Login Page (Authentication Gate) ✅ COMPLETE
**Priority**: Critical  
**Status**: Implemented on 2026-01-30  
**Description**: Show a login page before displaying any menus or content.
- [x] Login.razor page with branded sign-in UI
- [x] RedirectToLogin component for automatic redirect
- [x] Routes.razor updated with NotAuthorized handling
- [x] Return URL support for post-login redirect
- [x] Hide all navigation/menus until authenticated
- [x] Application branding updated (AssetHub name, English text)

#### 5. Admin Page for Share Management ✅ COMPLETE
**Priority**: High  
**Status**: Implemented on 2026-02-01  
**Description**: Administrative interface for managing share links.
- [x] View all active share links across the system
- [x] Display: link URL, creator (user), creation date, expiration
- [x] Revoke/disable individual shares
- [x] Filter by user, collection, or status
- [x] Three tabs: Shares, Collection Access, Users
- [x] Username display (instead of subject IDs) via UserLookupService

#### 6. Application Branding/Renaming ✅ COMPLETE
**Priority**: Medium  
**Status**: Implemented on 2026-01-30  
**Description**: Change the application name from generic "application" references.
- [x] Update page titles, headers, and branding
- [x] Configure application display name in appsettings
- [x] English language throughout

#### 7. Asset Collection Membership Display ✅ COMPLETE
**Priority**: Medium  
**Status**: Implemented on 2026-02-04  
**Description**: On the asset detail view, show a list of all collections the asset belongs to.
- [x] Display collection badges/chips on asset detail
- [x] Click to navigate to parent collection
- [ ] Quick add/remove from collections - *Deferred: Requires Multi-Collection Asset Assignment (#1)*
- **Note**: Currently assets belong to a single collection. Multi-collection support is planned for Phase 2.

#### 8. Role-Based UI Visibility ✅ COMPLETE
**Priority**: High  
**Status**: Implemented on 2026-02-01  
**Description**: Hide/show UI elements based on user's role on a collection/asset.
- [x] Viewers cannot see Share, Delete, or Upload buttons
- [x] Contributors can see Upload, Share, Edit
- [x] Managers can see Delete, manage ACL
- [x] All Assets page restricted to admin only
- [x] Centralized RolePermissions class for consistent role checks

#### 9. Empty State Messages ✅ COMPLETE
**Priority**: Medium  
**Status**: Implemented on 2026-02-04  
**Description**: Show friendly messages when there is no data to display.
- [x] Display "There is nothing to show here" or similar when lists/grids are empty
- [x] Consistent empty state styling across all data views (assets, collections, shares, users)
- [x] Provide helpful actions (e.g., "Create your first collection")
- [x] EmptyState component with Title, Description, Icon, ActionText, ActionIcon, OnAction parameters
- [x] Used in: Admin.razor (3 tabs), AllAssets.razor, Assets.razor, AssetGrid.razor, CollectionTree.razor, AssetDetail.razor, Share.razor

#### 10. Error Handling & User Feedback ✅ COMPLETE
**Priority**: High  
**Status**: Implemented on 2026-02-04  
**Description**: Improve error handling with user-friendly messages.
- [x] Display polite error messages for 401/500 errors (e.g., "Something went wrong while fetching users")
- [x] Never expose technical error details to users
- [x] Log API errors server-side for debugging
- [x] Consistent error toast notifications via MudBlazor Snackbar
- [x] IUserFeedbackService with HandleError, ShowSuccess, ShowWarning methods
- [x] ApiException class with status code and user-friendly message extraction
- [x] All pages/components use Feedback.HandleError() for consistent error handling
- [x] ShareLinkDialog updated to use IUserFeedbackService for consistency
- [x] Share.razor updated to use ILogger instead of Console.WriteLine

#### 11. API Error Logging ✅ COMPLETE
**Priority**: Medium  
**Status**: Implemented on 2026-02-04 (as part of Error Handling)  
**Description**: Add comprehensive logging for API errors.
- [x] Log all exceptions with stack traces via UserFeedbackService
- [x] Log request context (operation name) in error messages
- [x] Structured logging ready (Microsoft.Extensions.Logging)
- [x] Share.razor uses ILogger<Share> for proper logging

#### 12. User Access Details Modal ✅ COMPLETE
**Priority**: Medium  
**Status**: Implemented on 2026-02-04  
**Description**: On Admin page Users tab, "View Access" should open a modal showing the user's collection access.
- [x] Display list of collections the user has access to
- [x] Show role per collection (viewer, contributor, manager, admin)
- [ ] Show when access was granted (CreatedAt date) - *Deferred: requires API update*
- [x] Include "Revoke Access" button per collection
- [x] Quick navigation to collection (click collection name)
- [x] Auto-refresh users list after revoking access
- [x] UserAccessDialog.razor component created

#### 13. Role Permissions Documentation ✅ COMPLETE
**Priority**: Low  
**Status**: Implemented on 2026-02-04  
**Description**: Document the permission model for clarity.
- [x] Clarify: Who can do what and when?
- [x] Question answered: If a contributor uploads an image, who owns it? (The asset is owned by the collection, not the user)
- [x] Documented in README.md with:
  - Role hierarchy table (Viewer → Contributor → Manager → Admin)
  - Permission matrix showing actions per role
  - Key concepts section
  - Code reference to RoleHierarchy.cs

#### 14. Add CancellationToken Support ✅ COMPLETE
**Priority**: Low  
**Status**: Completed on 2026-02-07  
**Description**: Add CancellationToken to repository methods, service interfaces, endpoints, and UI API client for proper request cancellation.
- [x] IAssetRepository methods already have CancellationToken
- [x] ICollectionRepository methods already have CancellationToken
- [x] ICollectionAclRepository methods already have CancellationToken
- [x] Key Asset endpoints updated (GetAssets, GetAllAssets, GetAsset, GetAssetsByCollection)
- [x] ICollectionAuthorizationService — 4 methods updated with CancellationToken parameter
- [x] CollectionAuthorizationService — 4 methods updated, forwarded to EF Core calls
- [x] IMediaProcessingService — 2 methods updated with CancellationToken parameter
- [x] MediaProcessingService — 2 public + 2 private methods updated, all inner calls forwarded; Hangfire Enqueue passes `CancellationToken.None`
- [x] AssetEndpoints.cs — ~15 call sites updated (auth checks, helper methods, ScheduleProcessing, renditions)
- [x] CollectionEndpoints.cs — 10+ call sites updated
- [x] ShareEndpoints.cs — 2 call sites updated
- [x] AdminEndpoints.cs — 6+ call sites updated
- [x] AssetHubApiClient.cs — all 29 HTTP methods updated with CancellationToken parameter
- **Total**: ~63 gaps fixed across the entire stack. Build verified: 0 errors.

#### 15. Localization (Swedish & English) ✅ COMPLETE
**Priority**: Medium  
**Status**: Completed on 2026-02-07  
**Description**: Full multi-language support for all user-facing text in Swedish and English.

- [x] **Resource Files Setup** (10 .resx files)
  - `Resources/ResourceMarkers.cs` — 5 marker classes: `CommonResource`, `AssetsResource`, `CollectionsResource`, `AdminResource`, `SharesResource`
  - File structure:
    ```
    Resources/
      ├── ResourceMarkers.cs
      ├── CommonResource.resx          (English, ~170+ keys)
      ├── CommonResource.sv-SE.resx    (Swedish)
      ├── AssetsResource.resx          (English, ~35 keys)
      ├── AssetsResource.sv-SE.resx    (Swedish)
      ├── CollectionsResource.resx     (English, ~10 keys)
      ├── CollectionsResource.sv-SE.resx (Swedish)
      ├── AdminResource.resx           (English, ~40 keys)
      ├── AdminResource.sv-SE.resx     (Swedish)
      ├── SharesResource.resx          (English, ~30 keys)
      └── SharesResource.sv-SE.resx   (Swedish)
    ```
  - Key naming convention: `Category_Name` (e.g. `Btn_Delete`, `Nav_Home`, `Role_Viewer`, `Filter_AllTypes`, `Sort_NewestFirst`, `Expiry_1Day`, `Label_Size`, `Text_NoMetadata`, `Success_AccessGranted`, `Confirm_Revoke`, `Validation_EnterPassword`)

- [x] **Blazor Localization Configuration**
  - `AddLocalization()` in Program.cs
  - `RequestLocalizationOptions` with `en` (default) and `sv` supported cultures
  - `CookieRequestCultureProvider` at position 0 for culture persistence
  - `UseRequestLocalization()` middleware after `UseStaticFiles()`, before `UseAuthentication()`
  - `_Imports.razor` updated with `@using Microsoft.Extensions.Localization` and `@using Dam.Ui.Resources`

- [x] **All UI Files Localized** (22 files)
  - **Layouts (2):** MainLayout.razor, NavMenu.razor
  - **Pages (7):** Login.razor, Home.razor, Assets.razor, AssetDetail.razor, AllAssets.razor, Admin.razor, Share.razor
  - **Components (13):** LanguageSwitcher.razor, AssetUpload.razor, AssetGrid.razor, CollectionTree.razor, CreateCollectionDialog.razor, CreateShareDialog.razor, ShareLinkDialog.razor, SharePasswordDialog.razor, EditAssetDialog.razor, AddToCollectionDialog.razor, ManageUserAccessDialog.razor, CreateUserDialog.razor, UserAccessDialog.razor
  - Localizer naming convention: `CommonLoc`, `AssetLoc`, `AdminLoc`, `ShareLoc`, `CollectionLoc` (descriptive `{Domain}Loc` pattern)

- [x] **Language Switcher Component**
  - `LanguageSwitcher.razor` — MudSelect-based dropdown in AppBar
  - Reads `CultureInfo.CurrentUICulture.TwoLetterISOLanguageName` on init
  - Sets `.AspNetCore.Culture` cookie via `CookieRequestCultureProvider`
  - Forces full page reload via `NavigationManager.NavigateTo(uri, forceLoad: true)`

- [ ] **API Localization** — Deferred (API error messages remain in English)
- [ ] **Date/Time & Number Formatting** — Deferred (uses default culture formatting)

**Dependencies**: None

#### 16. Create User Functionality ✅ COMPLETE
**Priority**: High  
**Status**: Implemented on 2026-02-07  
**Description**: Implement user creation functionality through the Admin interface, integrated with Keycloak.

**Current Limitation**: Users must be manually created in Keycloak admin console. The application can only manage existing users' collection access.

**Scope**:
- [ ] **Keycloak Admin API Integration**
  - Install `Keycloak.AuthServices.Sdk` or use HttpClient for Keycloak Admin REST API
  - Configure Keycloak admin client credentials in appsettings.json
  - Implement `IKeycloakUserService` for user management operations
  - Methods: CreateUser, UpdateUser, ResetPassword, EnableUser, DisableUser
  - Handle Keycloak API authentication (service account or admin user)

- [ ] **Backend API Endpoint**
  - `POST /api/admin/users` - Create new user
  - Request DTO: `CreateUserDto`
    ```csharp
    public class CreateUserDto
    {
        public required string Username { get; set; }
        public required string Email { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public required string Password { get; set; } // Or generate temporary password
        public bool EmailVerified { get; set; } = false;
        public bool RequirePasswordChange { get; set; } = true;
        public List<string> InitialCollectionIds { get; set; } = new(); // Optional
        public string InitialRole { get; set; } = "viewer"; // Default role for initial collections
    }
    ```
  - Authorization: Admin role required
  - Create user in Keycloak
  - Optionally assign to collections via CollectionAcl
  - Return user details or error

- [ ] **UI Components**
  - **CreateUserDialog.razor** component with form:
    - Username (required, unique validation)
    - Email (required, format validation)
    - First Name (required)
    - Last Name (required)
    - Password options:
      - Generate temporary password (recommended)
      - Manual password entry (with strength indicator)
    - "Require password change on first login" checkbox (default: checked)
    - Initial collection access (optional):
      - Multi-select collection picker
      - Role selector per collection (or single role for all)
  - Add "Create User" button to Admin page Users tab
  - Success message with created username
  - Error handling for duplicate username/email

- [ ] **Validation**
  - Username requirements:
    - 3-50 characters
    - Alphanumeric, underscore, hyphen allowed
    - No spaces
    - Unique across Keycloak realm
  - Email: Valid email format, unique
  - Password requirements (Keycloak policy):
    - Minimum 8 characters
    - At least one uppercase letter
    - At least one number
    - At least one special character
  - Client-side validation with immediate feedback
  - Server-side validation before Keycloak API call

- [ ] **Password Handling**
  - **Option 1: Generated Password** (Recommended)
    - Generate secure random password (16 chars, mixed case, numbers, symbols)
    - Display to admin once (copy to clipboard)
    - Mark as temporary (user must change on first login)
    - Optionally send via email (requires SMTP configuration)
  
  - **Option 2: Manual Password**
    - Admin enters password
    - Show password strength indicator
    - Confirm password field
    - Option to force change on first login

- [ ] **Email Notification (Optional)**
  - Send welcome email to new user
  - Include:
    - Application URL
    - Username
    - Temporary password (if generated)
    - Instructions to log in and change password
  - Requires SMTP configuration in appsettings.json
  - Template-based email with AssetHub branding

- [ ] **Keycloak Configuration**
  - Create dedicated service account or admin user for API access
  - Required Keycloak admin API permissions:
    - `manage-users` (create, update users)
    - `view-realm` (read realm settings)
  - Store credentials securely (appsettings.json or Azure Key Vault)
  - Configure token endpoint and admin API base URL

- [ ] **User Feedback**
  - Success toast: "User [username] created successfully"
  - Display temporary password in dialog (if generated)
  - Copy password to clipboard button
  - Error messages for:
    - Duplicate username/email
    - Keycloak API errors
    - Network/connection issues
    - Validation failures

- [ ] **Security Considerations**
  - Admin-only access (check role in endpoint)
  - Log user creation events (audit trail)
  - Don't log passwords
  - Secure password transmission (HTTPS only)
  - Rate limiting for user creation endpoint
  - CAPTCHA consideration for production

- [ ] **Post-Creation Actions**
  - Option to immediately assign to collections
  - Redirect to user access management
  - Refresh users list to show new user
  - Option to create another user (stay in dialog)

- [ ] **Testing Checklist**
  - [ ] Create user with generated password
  - [ ] Create user with manual password
  - [ ] Verify user can log in with temporary password
  - [ ] Verify password change is required on first login
  - [ ] Duplicate username rejection
  - [ ] Duplicate email rejection
  - [ ] Invalid email format rejection
  - [ ] Weak password rejection
  - [ ] Admin authorization check (non-admin cannot create users)
  - [ ] Collection assignment during creation
  - [ ] Email notification delivery (if implemented)
  - [ ] Keycloak API error handling

**Keycloak Admin API Example**:
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
            new
            {
                type = "password",
                value = dto.Password,
                temporary = dto.RequirePasswordChange
            }
        }
    };
    
    var response = await _httpClient.PostAsJsonAsync(
        $"{_keycloakBaseUrl}/admin/realms/{_realm}/users", 
        user);
    
    response.EnsureSuccessStatusCode();
    
    // Extract user ID from Location header
    var location = response.Headers.Location?.ToString();
    var userId = location?.Split('/').Last();
    
    return userId;
}
```

**Alternative Approach**: 
If Keycloak Admin API is too complex, consider:
- User self-registration page (publicly accessible)
- Admin approval workflow
- Email verification required

**Time Estimate**: 6-10 hours (Keycloak integration + UI + testing)

**Dependencies**: 
- Keycloak admin API access configured
- SMTP server (optional, for email notifications)

#### 17. Caching Strategy ✅ COMPLETE
**Priority**: Medium  
**Status**: Completed on 2026-02-07  
**Description**: In-memory caching (`IMemoryCache`) for hot paths to reduce database load and improve response times.

- [x] **Audit Current Performance**
  - Profiled hot paths: `GetUserRoleAsync` (2 DB queries/call, called in loops), `GetCollectionIdsForAssetAsync` (called per auth check), `GetUserNamesAsync` (raw SQL per call)
  - Identified O(N×M) query patterns in `GetAllAssets` and rendition endpoints
  - `GetCollectionById` was calling `CheckAccessAsync` + `GetUserRoleAsync` for the same pair (4 DB queries → 2 with cache)

- [x] **CacheKeys Static Class** (`Dam.Application/CacheKeys.cs`)
  - Centralized key patterns, TTLs, and invalidation helpers
  - Key builders: `AuthRole(userId, collectionId)`, `AssetCollectionIds(assetId)`, `UserName(userId)`, `AccessibleCollections(userId)`, `AllUsers()`
  - TTL configuration: Auth roles = 2 min, Asset-collection IDs = 2 min, Usernames = 10 min, All users = 30 sec
  - Invalidation helpers: `InvalidateAuthRole`, `InvalidateAssetCollectionIds`, `InvalidateAclChange` (composite)

- [x] **CollectionAuthorizationService** — Request-scoped cache (security-safe)
  - `GetUserRoleAsync(userId, collectionId)` cached in a private `Dictionary` scoped to the HTTP request
  - Since the service is registered as `Scoped`, the dictionary is discarded after each request — **zero stale-permission window**
  - Eliminates redundant DB queries within the same request (e.g., `GetCollectionById` calling both `CheckAccessAsync` and `GetUserRoleAsync` for the same pair)
  - Does **not** use `IMemoryCache` — avoids security risk of cross-request cached roles surviving after ACL revocation

- [x] **AssetCollectionRepository** — P0 Cache
  - `GetCollectionIdsForAssetAsync(assetId)` cached with 2-min TTL
  - Eliminates 1 DB query per `CanAccessAssetAsync` call (used by 5 rendition endpoints + all asset mutations)
  - Automatic invalidation on `AddToCollectionAsync` and `RemoveFromCollectionAsync`

- [x] **UserLookupService** — P2 Cache
  - `GetUserNamesAsync` checks per-user cache before DB query; only fetches uncached user IDs
  - Individual usernames cached with 10-min TTL (usernames rarely change)
  - `GetAllUsersAsync` cached with 30-sec TTL; also populates individual username cache entries

- [x] **Cache Invalidation on Writes**
  - `AssetCollectionRepository.AddToCollectionAsync` → invalidates asset collection IDs (IMemoryCache)
  - `AssetCollectionRepository.RemoveFromCollectionAsync` → invalidates asset collection IDs (IMemoryCache)
  - Auth roles: no invalidation needed (request-scoped, automatically fresh each request)

- [x] **DI Registration**
  - `builder.Services.AddMemoryCache()` in Program.cs
  - `IMemoryCache` injected via primary constructors (no new interfaces needed)

- [ ] **Distributed Cache** — Deferred (Redis for multi-instance deployments)
- [ ] **Response Caching / Output Caching** — Deferred (Cache-Control headers for renditions)
- [ ] **ETag / Conditional Requests** — Deferred

**Files Created**: `src/Dam.Application/CacheKeys.cs`  
**Files Modified**: `Program.cs`, `CollectionAuthorizationService.cs`, `AssetCollectionRepository.cs`, `UserLookupService.cs`  
**Build**: 0 errors, 0 warnings  
**Dependencies**: None  
**Security**: Auth roles use request-scoped caching only (no cross-request persistence)

#### 18. Metrics & Observability ⏳ PLANNED
**Priority**: Medium  
**Status**: Not started  
**Description**: Select and integrate a metrics/observability tool to monitor application health, performance, and usage in production.

**Scope**:
- [ ] **Evaluate Tooling Options**
  - **OpenTelemetry** (.NET native support) — vendor-neutral, exports to multiple backends
  - **Prometheus + Grafana** — pull-based metrics, mature dashboarding
  - **Application Insights** (Azure) — if Azure-hosted, zero-config .NET integration
  - **Seq** — structured log aggregation (lightweight, self-hosted)
  - **Elastic APM / ELK Stack** — full observability suite
  - Decision criteria: self-hosted vs cloud, cost, complexity, team familiarity

- [ ] **Metrics to Capture**
  - **HTTP**: Request rate, latency (p50/p95/p99), error rate per endpoint
  - **Business**: Uploads/day, shares created, active users, assets processed
  - **Infrastructure**: CPU/memory usage, DB connection pool, MinIO latency
  - **Background Jobs**: Hangfire queue depth, processing time, failure rate
  - **Cache**: Hit/miss ratio (ties into #17)

- [ ] **Structured Logging**
  - Audit current `ILogger` usage for consistency
  - Add correlation IDs for request tracing
  - Configure log levels per environment (Debug for dev, Warning+ for prod)
  - Consider Serilog sinks for structured output (JSON, Seq, Elasticsearch)

- [ ] **Health Checks**
  - `AspNetCore.Diagnostics.HealthChecks` for readiness/liveness probes
  - PostgreSQL connectivity check
  - MinIO connectivity check
  - Keycloak connectivity check
  - Hangfire server status
  - Expose `/health` and `/health/ready` endpoints

- [ ] **Dashboarding**
  - Set up Grafana dashboards (or equivalent) for key metrics
  - Define alerting rules (error rate spike, job queue backlog, disk usage)

**Time Estimate**: 8-12 hours  
**Dependencies**: Tooling decision must be made first; Docker Compose updated for any new services (Prometheus, Grafana, Seq)

#### 19. Frontend Testing ⏳ PLANNED
**Priority**: Medium  
**Status**: Not started  
**Description**: Establish a frontend testing strategy for the Blazor Server UI to catch regressions and validate component behavior.

**Scope**:
- [ ] **Evaluate Testing Approaches**
  - **bUnit** — Unit/component testing for Blazor (in-process, fast, mocks services)
  - **Playwright** — E2E browser testing (real browser, full user flows)
  - **Both** — bUnit for component logic, Playwright for critical user journeys
  - Decision: bUnit as primary, Playwright for smoke tests

- [ ] **bUnit Component Tests**
  - Set up `Dam.Ui.Tests` project with bUnit + xUnit
  - Mock `AssetHubApiClient`, `IUserFeedbackService`, `IStringLocalizer<T>`, `NavigationManager`
  - Priority components to test:
    - `AssetGrid.razor` — renders assets, pagination, empty state, delete confirmation
    - `CollectionTree.razor` — tree rendering, selection, rename, delete
    - `CreateShareDialog.razor` — form validation, password generation, email list
    - `CreateCollectionDialog.razor` — form submission, validation
    - `EditAssetDialog.razor` — pre-populated fields, tag management, save
    - `LanguageSwitcher.razor` — culture change, cookie set
    - `AssetUpload.razor` — file selection, progress tracking, error states
  - Test localization: verify components render with both `en` and `sv` cultures

- [ ] **Playwright E2E Tests**
  - Set up `Dam.E2E.Tests` project with Playwright for .NET
  - Critical user flows to cover:
    - Login → navigate to collections → select collection → view assets
    - Upload asset → verify thumbnail appears → view detail
    - Create share link → open share URL → enter password → view content
    - Admin: manage users → create user → assign collection access
    - Language switch: toggle to Swedish → verify nav/buttons change → toggle back
  - Configure test fixtures for seeded data (test collection, test assets)
  - Run against Docker Compose environment

- [ ] **CI Integration**
  - bUnit tests run on every build (fast, no infrastructure needed)
  - Playwright tests run on PR / nightly (requires running app + services)
  - Fail build on test failures

- [ ] **Visual Regression (Optional)**
  - Playwright screenshot comparison for key pages
  - Detect unintended layout/style changes

**Time Estimate**: 12-20 hours (bUnit setup + core tests + Playwright setup + critical flows)  
**Dependencies**: None for bUnit; Docker Compose environment for Playwright

#### 20. Deployment Playbooks & Onboarding Guide ⏳ PLANNED
**Priority**: High  
**Status**: Not started  
**Description**: Create step-by-step playbooks that allow any organisation to clone the repo from GitHub and stand up a fully working AssetHub instance — covering both infrastructure provisioning and application configuration.

**Scope**:

- [ ] **Infrastructure Playbook**
  - **Docker Compose (Self-Hosted)**
    - Production-ready `docker-compose.prod.yml` with all services (app, worker, PostgreSQL, MinIO, Keycloak, Hangfire)
    - `.env.template` file with every required variable documented (descriptions, defaults, examples)
    - Volume mount strategy for persistent data (DB, MinIO buckets, Keycloak H2/Postgres)
    - Networking configuration (internal service mesh, exposed ports)
    - TLS/SSL termination setup (reverse proxy with Nginx/Traefik, Let's Encrypt)
    - Resource limits and restart policies per container
  - **Kubernetes (Optional)**
    - Helm chart or Kustomize manifests for k8s deployment
    - ConfigMap/Secret templates for environment configuration
    - Ingress configuration with TLS
    - PersistentVolumeClaim definitions for stateful services
  - **Cloud-Specific Guides** (optional appendices)
    - AWS: ECS/Fargate or EC2 + RDS + S3 (instead of MinIO)
    - Azure: App Service + Azure Database for PostgreSQL + Blob Storage
    - Bare metal / VPS: systemd units or Docker Compose on a single server
  - **Backup & Restore**
    - PostgreSQL backup script (`pg_dump` schedule, retention policy)
    - MinIO bucket replication or backup strategy
    - Keycloak realm export/import for disaster recovery
    - Documented restore procedure with verification steps

- [ ] **Keycloak Setup Playbook**
  - Realm creation script or importable `realm-export.json` with all required configuration
  - Client registration: OIDC client for AssetHub with correct redirect URIs, scopes, mappers
  - Role definitions (if using Keycloak realm roles)
  - User federation options (LDAP/AD integration guide)
  - SMTP configuration for Keycloak email verification/password reset
  - Admin service account creation for the Create User API (#16)
  - Identity provider federation (optional: Google, Azure AD, SAML)
  - Checklist: verify token endpoint, userinfo endpoint, JWKS URI all reachable from app

- [ ] **MinIO Setup Playbook**
  - Bucket creation script (asset storage bucket, naming convention)
  - Access policy configuration (application service account, read/write permissions)
  - CORS configuration for direct browser uploads (if applicable)
  - Lifecycle rules (e.g., auto-delete incomplete multipart uploads)
  - Optional: migration guide from MinIO to AWS S3 / Azure Blob (S3-compatible API)

- [ ] **Application Configuration Playbook**
  - `appsettings.Production.json` template with all sections explained:
    - `ConnectionStrings:DefaultConnection` — PostgreSQL
    - `StorageConfig` — MinIO endpoint, bucket, access key, secret key
    - `Authentication` — Keycloak authority, client ID, client secret, audience
    - `HangfireConfig` — dashboard credentials, worker count
    - `MediaProcessing` — ImageMagick/ffmpeg paths (or container paths)
  - Environment variable override reference (`ASPNETCORE_*`, `ConnectionStrings__*`, etc.)
  - CORS and allowed origins configuration
  - Logging level configuration per environment
  - Feature flags / toggles (if any)

- [ ] **Database Setup Playbook**
  - EF Core migrations: how to apply (`dotnet ef database update` or migration bundle)
  - Initial seed data (default admin user, root collection — if applicable)
  - Connection string format with SSL mode for production
  - Performance tuning recommendations (connection pooling, `pg_trgm` extension for search)

- [ ] **First-Run Quickstart**
  - Single-page "5-minute setup" guide:
    1. Clone repo
    2. Copy `.env.template` → `.env`, fill in values
    3. `docker compose -f docker-compose.prod.yml up -d`
    4. Run database migrations
    5. Import Keycloak realm (or run setup script)
    6. Create first admin user in Keycloak
    7. Open browser → login → create first collection → upload an asset
  - Troubleshooting FAQ (common issues: Keycloak redirect mismatch, MinIO connection refused, migration failures)
  - Health check verification: `curl /health` returns healthy for all dependencies

- [ ] **Upgrade & Migration Guide**
  - How to upgrade to a new version (pull, migrate, restart)
  - Breaking change policy and changelog format
  - Database migration safety (always backup before `ef database update`)
  - Configuration diff tool or changelog for new settings between versions

- [ ] **Security Hardening Checklist**
  - Change all default passwords (Keycloak admin, MinIO root, Hangfire dashboard, PostgreSQL)
  - Enable HTTPS everywhere (app, Keycloak, MinIO)
  - Restrict Hangfire dashboard access (IP whitelist or auth)
  - Review Keycloak client settings (confidential client, PKCE, token lifetimes)
  - Set `ASPNETCORE_ENVIRONMENT=Production` (disables dev-only features)
  - Firewall rules: only expose ports 443 (app) and 8443 (Keycloak) externally

**Time Estimate**: 10-16 hours  
**Dependencies**: Production Docker Compose (#17–#18 inform monitoring additions), stable configuration schema

---

## Session Notes

### 2026-02-04 Session: Removal of Primary Collection Architecture

**Focus**: Complete removal of the "primary collection" concept and CollectionId field

#### Problem Identified
User identified a fundamental architectural flaw: "An asset does not have a primary or secondary collection. It has collections. Meaning there isn't hierarchy in the relationship."

The system was incorrectly treating one collection as "primary" (via `Asset.CollectionId`) while others were "secondary" (via `AssetCollections` join table). This created unnecessary complexity and inconsistent behavior.

#### Completed Work

**1. Domain Model Changes**
- **Asset.cs**: Removed `CollectionId` field (Guid?) and `Collection` navigation property
- **Collection.cs**: Removed `Assets` navigation property (one-to-many relationship)
- Both now exclusively use `AssetCollections` for many-to-many relationships

**2. Database Schema Updates**
- **Migration**: `20260204213956_RemoveAssetCollectionId`
  - Dropped `CollectionId` column from Assets table
  - Dropped foreign key constraint `FK_Assets_Collections_CollectionId`
  - Dropped index `idx_assets_collection_id`
- Migration applied successfully to database

**3. Repository Layer Refactoring**
- **AssetRepository.cs**: 
  - Removed all `.Include(a => a.Collection)` from queries
  - `CountByCollectionAsync` now queries via AssetCollections join table
  - `DeleteByCollectionAsync` now queries via AssetCollections join table
  - `SearchAsync` no longer includes Collection navigation
  - `GetByIdAsync` no longer includes Collection navigation

- **AssetCollectionRepository.cs**:
  - `GetCollectionsForAssetAsync` returns only from join table (no primary collection logic)
  - `AddToCollectionAsync` no longer checks for primary collection
  - `BelongsToCollectionAsync` only checks join table
  - All methods exclusively use AssetCollections table

**4. API Endpoints Refactoring**
- **AssetEndpoints.cs**:
  - Created `CanAccessAssetAsync()` helper method for consistent permission checks
  - Updated all endpoints to check access via **any** collection (not just primary):
    - `GetAsset`, `UpdateAsset`, `DeleteAsset`
    - `AddAssetToCollection`, `RemoveAssetFromCollection`, `GetAssetCollections`
  - All rendition endpoints updated to use helper:
    - `DownloadOriginal`, `PreviewOriginal`, `GetThumbnail`, `GetMedium`, `GetPoster`
  - `GetAllAssets`: Now determines user's highest role across all asset collections
  - `UploadAsset`: Creates only AssetCollections entry (no CollectionId assignment)
  - `MapToDto`: Removed CollectionId and CollectionName from response

- **ShareEndpoints.cs**:
  - `CreateShare`: Uses join table to check if asset belongs to any collection
  - `DownloadSharedAsset`: Verifies asset-collection membership via `BelongsToCollectionAsync`
  - `PreviewSharedAsset`: Verifies asset-collection membership via `BelongsToCollectionAsync`

**5. DTO Updates**
- **AssetResponseDto**: Removed `CollectionId` and `CollectionName` properties

**6. UI Updates**
- **AssetDetail.razor**: 
  - Removed navigation to specific collection after delete (navigates to /assets)
  - Removed fallback to CollectionId in `NavigateBackToAssets()`
- **AllAssets.razor**: 
  - Removed collection name display from asset cards
  - Removed "Go to collection" button (assets can belong to multiple collections)

**7. DbContext Configuration**
- **AssetHubDbContext.cs**:
  - Removed Collection navigation configuration
  - Removed CollectionId index
  - Removed foreign key relationship definition

#### Files Modified
- `src/Dam.Domain/Entities/Asset.cs`
- `src/Dam.Domain/Entities/Collection.cs`
- `src/Dam.Application/Dtos/AssetResponseDto.cs`
- `src/Dam.Infrastructure/Repositories/AssetRepository.cs`
- `src/Dam.Infrastructure/Repositories/AssetCollectionRepository.cs`
- `src/Dam.Infrastructure/Data/AssetHubDbContext.cs`
- `Endpoints/AssetEndpoints.cs`
- `Endpoints/ShareEndpoints.cs`
- `src/Dam.Ui/Pages/AssetDetail.razor`
- `src/Dam.Ui/Pages/AllAssets.razor`

#### Migration Created & Applied
- `src/Dam.Infrastructure/Migrations/20260204213956_RemoveAssetCollectionId.cs`
- Successfully applied to database via `dotnet ef database update`

#### Build & Deployment Status
✅ All changes compile successfully with no errors
✅ Docker container rebuilt and deployed: `docker compose build api && docker compose up -d api`
✅ System fully operational with new architecture

#### Architecture Benefits
- **Simplified Model**: Single source of truth for asset-collection relationships
- **Consistent Permissions**: All permission checks use the same logic (any collection)
- **No Hierarchy**: All collections have equal relationship with assets
- **Better Scalability**: Easier to implement features requiring multi-collection assets

#### Testing Required ⚠️
**Status**: Testing deferred to future session (see Testing Plan section below)

---

### 2026-02-04 Session: Error Handling & User Feedback

**Focus**: Consistent error handling and user feedback across the application

#### Completed Work

**1. Error Handling Infrastructure Review**
- Verified `IUserFeedbackService` implementation is comprehensive
- `HandleError()` logs full exception and shows user-friendly message
- `HandleApiError()` extracts API error messages by status code
- `ExecuteWithFeedbackAsync()` wraps operations with automatic feedback

**2. Consistent Feedback Service Usage**
- **ShareLinkDialog.razor**: Updated to use `IUserFeedbackService` instead of `ISnackbar` directly
- All clipboard operations now use `Feedback.ShowSuccess/ShowWarning`

**3. Proper Logging**
- **Share.razor**: Replaced `Console.WriteLine` with `ILogger<Share>`
- Errors logged with structured logging including token context

**4. Verified Existing Implementation**
- All pages already use `Feedback.HandleError()` consistently
- Admin, AllAssets, Assets, AssetDetail, CollectionTree all properly wrapped
- Delete/revoke actions use `ExecuteWithFeedbackAsync` with success messages

#### Files Modified
- `src/Dam.Ui/Components/ShareLinkDialog.razor`
- `src/Dam.Ui/Pages/Share.razor`

#### Build Status
✅ All changes compile successfully

---

### 2026-02-04 Session: Empty State Messages

**Focus**: Consistent empty state messaging across the application

#### Completed Work

**1. EmptyState Component**
- Already well-designed with Title, Description, Icon, ActionText, ActionIcon, OnAction parameters
- Supports optional child content for additional customization

**2. Standardized Empty States**
- **CollectionTree.razor**: Converted inline empty state to use EmptyState component
- **AssetDetail.razor**: Improved "not found" message to use EmptyState with action button
- **Share.razor**: Added empty state when shared collection has no assets

**3. Verified Existing Usage**
- Admin.razor: 4 empty states (shares, collections, collection selection, users)
- AllAssets.razor: 2 empty states (no results, no assets)
- Assets.razor: "Select a collection" empty state
- AssetGrid.razor: "No assets yet" empty state

#### Files Modified
- `src/Dam.Ui/Components/CollectionTree.razor`
- `src/Dam.Ui/Pages/AssetDetail.razor`
- `src/Dam.Ui/Pages/Share.razor`

#### Build Status
✅ All changes compile successfully

---

### 2026-02-04 Session: Role Permissions Documentation & CancellationToken

**Focus**: Documentation and request cancellation support

#### Completed Work

**1. Role Permissions Documentation (README.md)**
- Added comprehensive RBAC section with:
  - Role hierarchy table (Viewer → Contributor → Manager → Admin)
  - Permission matrix showing what each role can do
  - Key concepts explanation
  - Code reference to RoleHierarchy.cs

**2. CancellationToken Support (Partial)**
- Repository interfaces already support CancellationToken
- Updated key Asset endpoints:
  - `GetAssets` - now passes ct to repository
  - `GetAllAssets` - now passes ct to repository
  - `GetAsset` - now passes ct to repository
  - `GetAssetsByCollection` - now passes ct to repository

#### Files Modified
- `README.md` - Added Role-Based Access Control section
- `Endpoints/AssetEndpoints.cs` - Added CancellationToken parameters

#### Deferred
- ICollectionAuthorizationService CancellationToken (requires interface change)
- Remaining endpoints (low priority)

#### Build Status
✅ All changes compile successfully

---

### 2026-02-04 Session: Multi-Collection Asset Assignment

**Focus**: Allow assets to belong to multiple collections simultaneously

#### Completed Work

**1. Database Schema**
- Created `AssetCollection` join entity (`src/Dam.Domain/Entities/AssetCollection.cs`)
  - Fields: Id, AssetId, CollectionId, AddedAt, AddedByUserId
- EF configuration with unique index on (AssetId, CollectionId)
- Migration: `20260204185835_AddAssetCollections`

**2. Domain Layer Updates**
- Added `AssetCollections` navigation property to `Asset` entity
- Added `AssetCollections` navigation property to `Collection` entity
- Preserved `CollectionId` as primary collection for backwards compatibility

**3. Repository Layer**
- Created `IAssetCollectionRepository` interface
- Created `AssetCollectionRepository` implementation
  - `GetCollectionsForAssetAsync` - Returns primary + linked collections
  - `AddToCollectionAsync` - Creates link (validates asset/collection exist)
  - `RemoveFromCollectionAsync` - Removes link
  - `BelongsToCollectionAsync` - Checks membership

**4. API Endpoints (AssetEndpoints.cs)**
- `GET /api/assets/{id}/collections` - Get all collections for asset
- `POST /api/assets/{id}/collections/{collectionId}` - Add to collection
- `DELETE /api/assets/{id}/collections/{collectionId}` - Remove from collection
- Authorization: Contributor+ on source collection to add/remove

**5. UI Components**
- `AddToCollectionDialog.razor` - Modal for selecting collection to add asset to
- `AssetDetail.razor` - Updated to show all collections with:
  - Primary collection indicator
  - Add button (contributors+)
  - Remove button per linked collection
  - Confirmation before removing

**6. API Client (AssetHubApiClient.cs)**
- Added `GetAssetCollectionsAsync`
- Added `AddAssetToCollectionAsync`
- Added `RemoveAssetFromCollectionAsync`

**7. DTO**
- Created `AssetCollectionDto` for API responses

#### Files Created
- `src/Dam.Domain/Entities/AssetCollection.cs`
- `src/Dam.Application/Repositories/IAssetCollectionRepository.cs`
- `src/Dam.Infrastructure/Repositories/AssetCollectionRepository.cs`
- `src/Dam.Application/Dtos/AssetCollectionDto.cs`
- `src/Dam.Ui/Components/AddToCollectionDialog.razor`
- `src/Dam.Infrastructure/Migrations/20260204185835_AddAssetCollections.cs`

#### Files Modified
- `src/Dam.Domain/Entities/Asset.cs`
- `src/Dam.Domain/Entities/Collection.cs`
- `src/Dam.Infrastructure/Data/AssetHubDbContext.cs`
- `Endpoints/AssetEndpoints.cs`
- `src/Dam.Ui/Services/AssetHubApiClient.cs`
- `src/Dam.Ui/Pages/AssetDetail.razor`
- `Program.cs` (DI registration)

#### Build Status
✅ All changes compile successfully

#### Migration Pending
Run `dotnet ef database update` to apply the new AssetCollections table

---

### 2026-02-04 Session: Asset Collection Membership Display

**Focus**: Show collection membership on asset detail page

#### Completed Work

**1. AssetDetail.razor Enhancement**
- Added "Collection" section with clickable chip
- Shows collection name with folder icon
- Click navigates to collection in Assets page

**2. Verified Existing Features**
- AllAssets.razor already shows collection name on asset cards
- CollectionName is returned by API endpoints

#### Files Modified
- `src/Dam.Ui/Pages/AssetDetail.razor`

#### Deferred
- Add/remove from collections (requires Multi-Collection Asset Assignment, Phase 2)

#### Build Status
✅ All changes compile successfully

---

### 2026-02-04 Session: User Access Details Modal

**Focus**: Implement detailed user access modal for Admin page

#### Completed Work

**1. UserAccessDialog Component**
- Created `src/Dam.Ui/Components/UserAccessDialog.razor`
- Shows user info (username, user ID, highest role)
- Lists all collections with roles in a table
- Each collection row has:
  - Clickable collection name (navigates to collection)
  - Role chip with color coding
  - "Revoke" button to remove access

**2. Revoke Access Functionality**
- Removes user's ACL via `Api.RemoveCollectionAclAsync()`
- Shows success toast on completion
- Updates local list immediately
- Closes dialog if no collections remain

**3. Admin.razor Integration**
- Updated `ShowUserDetails()` to open dialog instead of toast
- Auto-refreshes users list when dialog closes after changes

#### Files Created
- `src/Dam.Ui/Components/UserAccessDialog.razor`

#### Files Modified
- `src/Dam.Ui/Pages/Admin.razor`

#### Deferred
- "CreatedAt" date for when access was granted (requires API/DTO update)

#### Build Status
✅ All changes compile successfully

---

### 2026-02-01/02 Session: Code Quality & Security Review

**Focus**: Security fixes, code consolidation, architecture improvements

#### Completed Work

**1. Critical Security Fix: CreateShare Authorization**
- **Issue**: Any authenticated user could create share links for ANY asset/collection
- **Fix**: Added authorization check requiring `contributor+` role on the collection
- **File**: `Endpoints/ShareEndpoints.cs`

**2. Security: BCrypt Password Hashing**
- **Issue**: Share passwords were hashed with SHA256 (weak for passwords)
- **Fix**: Replaced with BCrypt.Net-Next for proper password hashing
- **Files**: `AssetHub.csproj`, `Endpoints/ShareEndpoints.cs`

**3. Code Consolidation: Centralized RoleHierarchy**
- **Issue**: Role hierarchy dictionary duplicated in 3+ locations
- **Fix**: Created `Dam.Application/RoleHierarchy.cs` with constants and helper methods
- **Updated Files**:
  - `src/Dam.Ui/Services/RolePermissions.cs` - delegates to RoleHierarchy
  - `src/Dam.Infrastructure/Services/AuthorizationService.cs` - uses RoleHierarchy.MeetsRequirement()
  - `Endpoints/AssetEndpoints.cs` - uses RoleHierarchy.Roles.* constants
  - `Endpoints/CollectionEndpoints.cs` - uses RoleHierarchy.Roles.* constants
  - `Endpoints/AdminEndpoints.cs` - uses RoleHierarchy.AllRoles and GetLevel()

**4. Standardized API Error Responses**
- **Issue**: Inconsistent error response formats across endpoints
- **Fix**: Created `Dam.Application/Dtos/ApiError.cs` with factory methods
- **Updated**: AdminEndpoints, ShareEndpoints to use ApiError

**5. Role-Based UI Visibility**
- Viewers cannot see Share/Delete/Upload buttons
- All Assets page restricted to admin only
- Centralized permission checks in RolePermissions class

**6. Username Display**
- Created UserLookupService to query Keycloak's user_entity table
- Admin page shows usernames instead of subject IDs
- User validation when adding collection access

**7. Architecture Improvements**
- Moved IUserLookupService interface to Application layer (Clean Architecture)
- Removed duplicate using directives

#### Files Created
- `src/Dam.Application/RoleHierarchy.cs`
- `src/Dam.Application/Dtos/ApiError.cs`
- `src/Dam.Application/Services/IUserLookupService.cs`

#### Build Status
✅ All changes compile successfully, API running

---

### 2026-02-07 Session: Caching Strategy Implementation

**Focus**: In-memory caching for hot paths (authorization, asset-collection lookups, username resolution)

#### Problem Identified
Every API request triggered multiple redundant database queries:
- `GetUserRoleAsync` = 2 DB queries per call, called in loops for multi-collection assets
- `CanAccessAssetAsync` = 1 + 2N queries (N = number of collections an asset belongs to)
- `GetCollectionById` = 4 DB queries (same user+collection queried twice)
- Rendition endpoints (thumb, medium, poster, download, preview) all re-ran full auth checks
- Username lookups opened fresh Npgsql connections every call

#### Completed Work

**1. CacheKeys Static Class** (`src/Dam.Application/CacheKeys.cs`)
- Centralized cache key patterns, TTL constants, and invalidation helpers
- Keys: `auth:role:{userId}:{collectionId}`, `asset:colls:{assetId}`, `user:name:{userId}`, `accessible:colls:{userId}`, `users:all`
- TTLs: Auth roles 2 min, Asset-collection IDs 2 min, Usernames 10 min, All users 30 sec
- Composite invalidation: `InvalidateAclChange()` clears auth role + accessible collections

**2. CollectionAuthorizationService** — Request-scoped caching (NOT IMemoryCache)
- Uses a private `Dictionary<string, string?>` instead of `IMemoryCache`
- Since the service is Scoped (one instance per HTTP request), the dictionary is automatically discarded after each request
- **Security rationale**: Caching auth roles in `IMemoryCache` creates a stale-permission window — if an admin revokes access, the old role persists for the TTL duration across subsequent requests. Request-scoped caching eliminates this risk entirely while still deduplicating DB calls within a single request.

**3. AssetCollectionRepository** — `IMemoryCache` + `ILogger` injected
- `GetCollectionIdsForAssetAsync` cached with 2-min TTL
- `AddToCollectionAsync` → invalidates cache on write
- `RemoveFromCollectionAsync` → invalidates cache on write

**4. UserLookupService** — `IMemoryCache` injected
- `GetUserNamesAsync` checks per-user cache first, only queries DB for uncached IDs
- Individual usernames cached with 10-min TTL
- `GetAllUsersAsync` cached with 30-sec TTL, also populates individual username cache

**5. CollectionAclRepository** — No cache invalidation needed
- Auth roles use request-scoped caching (automatically fresh each request)
- ACL writes take effect immediately on the next request

**6. Program.cs** — `builder.Services.AddMemoryCache()` registered

#### Files Created
- `src/Dam.Application/CacheKeys.cs`

#### Files Modified
- `Program.cs`
- `src/Dam.Infrastructure/Services/CollectionAuthorizationService.cs`
- `src/Dam.Infrastructure/Repositories/AssetCollectionRepository.cs`
- `src/Dam.Infrastructure/Services/UserLookupService.cs`
- `src/Dam.Infrastructure/Repositories/CollectionRepository.cs` (CollectionAclRepository)

#### Build Status
✅ 0 errors, 0 warnings

#### Impact Estimate
- **Auth checks**: Deduplicated within each request (e.g., `GetCollectionById` 4→2 queries). Fresh on every new request — no stale-permission risk.
- **Asset rendition endpoints**: Collection IDs cached in IMemoryCache (2-min TTL, invalidated on writes)
- **Username display**: Per-user IMemoryCache (10-min TTL) — only fetches uncached IDs
- **Security**: Auth roles never persist beyond a single HTTP request

---

### 2026-02-07 Session: Security Hardening

**Focus**: Role escalation prevention, admin-only asset endpoints, user ID claim safety

#### Problems Identified (from full auth flow audit)
1. **Role escalation**: `SetCollectionAccess` allowed a manager to grant admin roles — no check that the caller's role >= target role
2. **Revoke escalation**: `RevokeCollectionAccess` allowed a manager to revoke an admin's access
3. **Unfiltered asset listing**: `GET /api/assets` and `GET /api/assets/all` returned assets to any authenticated user without collection-based filtering
4. **Silent auth failure**: `GetUserIdOrDefault()` returned `"unknown"` when the user ID claim was missing, allowing operations to proceed under a fake identity instead of failing with 401

#### Completed Work

**1. Role Escalation Guards** (`src/Dam.Application/RoleHierarchy.cs`)
- Added `CanGrantRole(callerRole, targetRole)` — returns `true` only if caller's level >= target level
- Added `CanRevokeRole(callerRole, targetRole)` — same level check for revocation

**2. SetCollectionAccess Guard** (`Endpoints/CollectionEndpoints.cs`)
- After the existing `canManage` check, now fetches caller's role via `authService.GetUserRoleAsync`
- Calls `RoleHierarchy.CanGrantRole(callerRole, dto.Role)` — returns 400 with descriptive message if escalation attempted

**3. RevokeCollectionAccess Guard** (`Endpoints/CollectionEndpoints.cs`)
- Fetches caller's role and target's current role via `aclRepo.GetByPrincipalAsync`
- Calls `RoleHierarchy.CanRevokeRole(callerRole, targetAcl.Role)` — returns 400 if caller's level < target's level

**4. Admin-Only Asset Listings** (`Endpoints/AssetEndpoints.cs`)
- `GET /api/assets` (GetAssets) — added `.RequireAuthorization("RequireAdmin")` (unused by UI, returns unfiltered data)
- `GET /api/assets/all` (GetAllAssets) — added `.RequireAuthorization("RequireAdmin")` (UI already gated by `<AuthorizeView Policy="RequireAdmin">` and page-level `[Authorize(Policy = "RequireAdmin")]`)

**5. GetRequiredUserId** (`Endpoints/ClaimsPrincipalExtensions.cs`)
- Added `GetRequiredUserId()` — returns user ID or throws `UnauthorizedAccessException` if claim is missing
- Marked `GetUserIdOrDefault()` as `[Obsolete]` with message directing to `GetRequiredUserId()`
- Replaced all 19 `GetUserIdOrDefault()` calls in `AssetEndpoints.cs` and `ShareEndpoints.cs` with `GetRequiredUserId()`

#### Files Modified
- `src/Dam.Application/RoleHierarchy.cs` — Added CanGrantRole, CanRevokeRole
- `Endpoints/CollectionEndpoints.cs` — Role escalation guards on SetCollectionAccess and RevokeCollectionAccess
- `Endpoints/AssetEndpoints.cs` — Admin-only on GetAssets and GetAllAssets, GetRequiredUserId migration
- `Endpoints/ShareEndpoints.cs` — GetRequiredUserId migration
- `Endpoints/ClaimsPrincipalExtensions.cs` — Added GetRequiredUserId, deprecated GetUserIdOrDefault

#### Build Status
✅ 0 errors, 32 warnings (all pre-existing)

#### Remaining Suggestions (not yet implemented)
~~All suggestions implemented in the follow-up session below.~~

---

### 2026-02-07 Session: Security Hardening (Part 2)

**Focus**: Implement all remaining security suggestions from the audit — cookie hardening, audience validation, PKCE, exception sanitization, admin role guard, and generic role-level helper.

#### Completed Work

**1. Generic Role-Level Helper** (`src/Dam.Application/RoleHierarchy.cs`)
- Added `HasSufficientLevel(callerRole, targetRole)` — generic guard that returns `true` when caller's level >= target's level
- Refactored `CanGrantRole` and `CanRevokeRole` to delegate to `HasSufficientLevel` (thin convenience wrappers)
- Any future level-based check can reuse `HasSufficientLevel` directly

**2. Auth Cookie Hardened** (`Program.cs`)
- `SameSite` changed from `Lax` to `Strict` — prevents the cookie from being sent on cross-site requests, mitigating CSRF for cookie-authenticated Blazor Server requests
- Added `HttpOnly = true` — prevents JavaScript access to the auth cookie
- Added `SecurePolicy = CookieSecurePolicy.SameAsRequest` — ensures HTTPS in production

**3. JWT Audience Validation Enabled** (`Program.cs`)
- `ValidateAudience` changed from `false` to `true`
- `ValidAudiences` set to `["assethub-app", "account"]` — Keycloak audience mapper must be configured to include `assethub-app` in token audience claims
- Note: If tokens start getting rejected, add an audience mapper in Keycloak client config

**4. PKCE Re-enabled** (`Program.cs`)
- `UsePkce` changed from `false` to `true`
- Defense-in-depth against authorization code interception, even with confidential client flow

**5. Exception Message Sanitization** (`Endpoints/AssetEndpoints.cs`, `Program.cs`)
- Removed all 8 try/catch blocks from `AssetEndpoints.cs` that leaked `ex.Message` to API responses
- Added global API exception handler middleware in `Program.cs`:
  - Catches `UnauthorizedAccessException` → returns 401 JSON
  - Catches any exception on `/api/*` paths → logs full exception server-side via `ILogger`, returns sanitized ProblemDetails JSON (no stack traces, no internal messages)
- Endpoints now let exceptions bubble up naturally; middleware handles logging + sanitized response

**6. AdminSetCollectionAccess Role Guard** (`Endpoints/AdminEndpoints.cs`)
- Added role validation with a `targetRole` local variable (normalized to lowercase)
- Validates against `RoleHierarchy.AllRoles` — returns 400 for invalid role names
- Removed duplicate role validation that was done earlier in the same method
- Defense-in-depth: even though callers must be system admins, the same validation pattern applies

#### Files Modified
- `src/Dam.Application/RoleHierarchy.cs` — Added `HasSufficientLevel`, refactored `CanGrantRole`/`CanRevokeRole`
- `Program.cs` — SameSite=Strict, HttpOnly, SecurePolicy, ValidateAudience=true, UsePkce=true, global API exception handler
- `Endpoints/AssetEndpoints.cs` — Removed all 8 try/catch blocks (exception handling delegated to global middleware)
- `Endpoints/AdminEndpoints.cs` — Consolidated role validation in `AdminSetCollectionAccess`

#### Build Status
✅ 0 errors, 32 warnings (all pre-existing)

---

## Phase Breakdown (2-3 Weeks)

| Phase | Week | Focus | Output |
|-------|------|-------|--------|
| Phase 1 | Week 1 (Days 1-3) | Foundation & Infrastructure | Running Docker Compose, DB schema, EF migrations |
| Phase 1 | Week 1 (Days 4-5) | Collections & ACL | Collection CRUD, role-based access checks, API working |
| Phase 2 | Week 1-2 (Days 6-8) | Upload & Processing | Upload endpoints, Hangfire jobs, thumbnail generation |
| Phase 2 | Week 2 (Days 9-10) | Video & Presigned URLs | Video metadata extraction, poster frame, presigned URL generation |
| Phase 3 | Week 2 (Days 11-12) | UI & Search | Blazor grid, search/filter, asset detail page |
| Phase 3 | Week 2-3 (Days 13-14) | Sharing & Audit | Share tokens, public endpoints, audit logging |
| Phase 3 | Week 3 (Days 15) | Testing & Hardening | Unit + integration tests, security review, E2E scenarios |
| Phase 3 | Week 3 (Days 16) | Deployment & Docs | Docker Compose production config, README, runbook |

---

## Phase 1: Foundation & Infrastructure (Days 1-5)

### Phase 1A: Docker Compose & Database (Days 1-3)

**STATUS: ✅ COMPLETE**

> All infrastructure in place and running. All services (PostgreSQL, MinIO, Keycloak, Hangfire) successfully initialized and healthchecked. Database migrations applied. API boots successfully and connects to all services.

#### Deliverables
- [x] Docker Compose file with PostgreSQL, MinIO, Keycloak, Hangfire Dashboard
- [x] PostgreSQL migrations (EF Core)
- [x] MinIO bucket setup (terraform or script)
- [x] Appsettings for each environment
- [x] API boots and connects to all services

#### Modules Involved
- **Dam.Infrastructure**: DbContext, migrations, MinIO client setup
- **Program.cs**: Dependency injection for EF, MinIO, Hangfire

#### Tasks

**1.1 Create Docker Compose (docker-compose.yml)**
```yaml
services:
  postgres:
    image: postgres:16-alpine
    env: POSTGRES_DB=asethub, POSTGRES_PASSWORD=...
    volumes: pgdata:/var/lib/postgresql/data
    ports: 5432:5432

  minio:
    image: minio/minio
    env: MINIO_ROOT_USER=..., MINIO_ROOT_PASSWORD=...
    volumes: miniodata:/data
    ports: 9000:9000, 9001:9001
    command: server /data --console-address ":9001"

  keycloak:
    # Already in your instructions/keycloak folder
    image: quay.io/keycloak/keycloak:24.0.1
    env: KC_DB=postgres, KC_DB_URL=..., KC_ADMIN=admin, KC_ADMIN_PASSWORD=...
    ports: 8080:8080
    depends_on: postgres

  api:
    build: .
    dockerfile: Dockerfile
    env: ConnectionStrings__Postgres=..., Minio__Endpoint=minio:9000, ...
    ports: 7252:7252
    depends_on: postgres, minio, keycloak

  hangfire-worker:
    build: .
    dockerfile: Dockerfile.Worker
    env: (same as api + HANGFIRE_STORAGE=postgres)
    depends_on: postgres, minio

  hangfire-dashboard:
    # Runs on api via /hangfire endpoint (or separate container if needed)
```

**1.2 EF Core DbContext & Migrations**

Create `Dam.Infrastructure/Data/AssetHubDbContext.cs`:
```csharp
public class AssetHubDbContext : DbContext
{
    public DbSet<Collection> Collections { get; set; }
    public DbSet<Asset> Assets { get; set; }
    public DbSet<CollectionAcl> CollectionAcls { get; set; }
    public DbSet<Share> Shares { get; set; }
    public DbSet<AuditEvent> AuditEvents { get; set; }
    // Hangfire stores its own tables
}
```

Create migration:
```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

**1.3 MinIO Initialization**
- Create bucket `asethub-dev` (or via IaC script)
- Store credentials in appsettings + Docker env

**1.4 Update Program.cs**
- Add EF Core: `builder.Services.AddDbContext<AssetHubDbContext>(...)`
- Add Hangfire: `builder.Services.AddHangfire(x => x.UsePostgreSqlStorage(...))`
- Add MinIO client wrapper

#### Success Criteria
- `docker compose up` starts all services without errors
- API connects to PostgreSQL and MinIO
- Migrations run successfully
- Hangfire dashboard accessible at `/hangfire`

#### Testing
- Unit: N/A (infrastructure setup)
- Integration: Smoke test that DbContext can query/insert
- E2E: N/A

#### Effort Estimate
- **3 days** (1 dev, familiar with Docker/EF)

---

### Phase 1B: Collections & Role-Based Access Control (Days 4-5)

**STATUS: ✅ COMPLETE**

> All endpoints implemented and fully functional. Collections CRUD with hierarchical parent_id support. Role-based access control (viewer/contributor/manager/admin) fully integrated with Keycloak claims. All 10 endpoints tested and working. Collections API is the primary active endpoint in current deployment.

#### Deliverables
- [x] Collection entity and repository
- [x] CollectionAcl entity and repository
- [x] RBAC authorization policies in API
- [x] Collection CRUD endpoints (GET, POST, PATCH, DELETE)
- [x] ACL assignment endpoint (POST /api/collections/{id}/acl)
- [x] Keycloak claims mapping (tenant_id, user_id, group_ids) - already done

#### Modules Involved
- **Dam.Domain**: Collection, CollectionAcl entities, IAclService
- **Dam.Application**: GetCollectionsHandler, CreateCollectionHandler, AssignAclHandler
- **Dam.Infrastructure**: CollectionRepository, CollectionAclRepository
- **Dam.Api**: Collection endpoints, AuthZ policies

#### Tasks

**1.5 Domain Layer**

Create `Dam.Domain/Entities/Collection.cs`:
```csharp
public class Collection
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; }

    public Collection? Parent { get; set; }
    public ICollection<Collection> Children { get; set; }
    public ICollection<CollectionAcl> Acls { get; set; }
    public ICollection<Asset> Assets { get; set; }
}
```

Create `Dam.Domain/Entities/CollectionAcl.cs`:
```csharp
public class CollectionAcl
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public string PrincipalType { get; set; } // "user" or "group"
    public string PrincipalId { get; set; }
    public string Role { get; set; } // viewer|contributor|manager|admin
    public DateTime CreatedAt { get; set; }

    public Collection Collection { get; set; }
}

public enum CollectionRole
{
    Viewer,
    Contributor,
    Manager,
    Admin
}
```

Create `Dam.Domain/Services/IAuthorizationService.cs`:
```csharp
public interface IAuthorizationService
{
    Task<bool> CanViewCollectionAsync(string userId, List<string> groupIds, Guid collectionId);
    Task<bool> CanUploadToCollectionAsync(string userId, List<string> groupIds, Guid collectionId);
    Task<bool> CanManageCollectionAsync(string userId, List<string> groupIds, Guid collectionId);
    Task<string> GetUserRoleInCollectionAsync(string userId, List<string> groupIds, Guid collectionId);
}
```

**1.6 Application Layer (Use Cases)**

Create `Dam.Application/Collections/GetCollectionsHandler.cs`:
```csharp
public class GetCollectionsHandler
{
    private readonly ICollectionRepository _repo;
    private readonly IAuthorizationService _authz;

    public async Task<List<CollectionDto>> HandleAsync(string userId, List<string> groupIds, Guid? parentId)
    {
        var userCollections = await _repo.GetCollectionsByParentAsync(parentId);
        
        // Filter: only collections user has access to
        var filtered = new List<CollectionDto>();
        foreach (var col in userCollections)
        {
            if (await _authz.CanViewCollectionAsync(userId, groupIds, col.Id))
                filtered.Add(MapToDto(col));
        }
        
        return filtered;
    }
}

public class CreateCollectionHandler
{
    private readonly ICollectionRepository _repo;
    private readonly IAuthorizationService _authz;

    public async Task<CollectionDto> HandleAsync(string userId, List<string> groupIds, CreateCollectionRequest req)
    {
        // Check: can user create in parent collection (or root)?
        if (req.ParentId.HasValue && !await _authz.CanManageCollectionAsync(userId, groupIds, req.ParentId.Value))
            throw new UnauthorizedAccessException();

        var collection = new Collection 
        { 
            Id = Guid.NewGuid(),
            ParentId = req.ParentId,
            Name = req.Name,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId
        };

        await _repo.CreateAsync(collection);

        // Create initial ACL: creator = manager
        await _aclRepo.CreateAsync(new CollectionAcl 
        { 
            CollectionId = collection.Id,
            PrincipalType = "user",
            PrincipalId = userId,
            Role = "manager"
        });

        return MapToDto(collection);
    }
}
```

**1.7 Infrastructure Layer (Repositories)**

Create `Dam.Infrastructure/Repositories/CollectionRepository.cs`:
```csharp
public class CollectionRepository : ICollectionRepository
{
    private readonly AssetHubDbContext _db;

    public async Task<Collection> GetByIdAsync(Guid id)
        => await _db.Collections.Include(c => c.Acls).FirstOrDefaultAsync(c => c.Id == id);

    public async Task<List<Collection>> GetCollectionsByParentAsync(Guid? parentId)
        => await _db.Collections.Where(c => c.ParentId == parentId).ToListAsync();

    public async Task CreateAsync(Collection collection)
    {
        _db.Collections.Add(collection);
        await _db.SaveChangesAsync();
    }

    // ... UPDATE, DELETE
}
```

Create `Dam.Infrastructure/Services/AuthorizationService.cs`:
```csharp
public class AuthorizationService : IAuthorizationService
{
    private readonly AssetHubDbContext _db;

    public async Task<bool> CanViewCollectionAsync(string userId, List<string> groupIds, Guid collectionId)
    {
        var acl = await _db.CollectionAcls
            .FirstOrDefaultAsync(a => a.CollectionId == collectionId &&
                ((a.PrincipalType == "user" && a.PrincipalId == userId) ||
                 (a.PrincipalType == "group" && groupIds.Contains(a.PrincipalId))));

        return acl != null && new[] { "viewer", "contributor", "manager", "admin" }.Contains(acl.Role);
    }

    public async Task<bool> CanUploadToCollectionAsync(string userId, List<string> groupIds, Guid collectionId)
    {
        var role = await GetUserRoleInCollectionAsync(userId, groupIds, collectionId);
        return role == "contributor" || role == "manager" || role == "admin";
    }

    public async Task<bool> CanManageCollectionAsync(string userId, List<string> groupIds, Guid collectionId)
    {
        var role = await GetUserRoleInCollectionAsync(userId, groupIds, collectionId);
        return role == "manager" || role == "admin";
    }

    public async Task<string> GetUserRoleInCollectionAsync(string userId, List<string> groupIds, Guid collectionId)
    {
        var acl = await _db.CollectionAcls
            .Where(a => a.CollectionId == collectionId &&
                ((a.PrincipalType == "user" && a.PrincipalId == userId) ||
                 (a.PrincipalType == "group" && groupIds.Contains(a.PrincipalId))))
            .OrderByDescending(a => new[] { "admin", "manager", "contributor", "viewer" }.IndexOf(a.Role))
            .FirstOrDefaultAsync();

        return acl?.Role ?? "none";
    }
}
```

**1.8 API Endpoints**

Create `Dam.Api/Endpoints/CollectionsEndpoints.cs`:
```csharp
public static void MapCollectionEndpoints(this WebApplication app)
{
    var group = app.MapGroup("/api/collections")
        .WithName("Collections")
        .RequireAuthorization();

    group.MapGet("/", GetCollections)
        .WithName("GetCollections");

    group.MapPost("/", CreateCollection)
        .WithName("CreateCollection");

    group.MapPatch("/{id}", UpdateCollection)
        .WithName("UpdateCollection");

    group.MapDelete("/{id}", DeleteCollection)
        .WithName("DeleteCollection");

    group.MapPost("/{id}/acl", AssignAcl)
        .WithName("AssignAcl");
}

// Handlers extract userId/groupIds from HttpContext.User
// Use the ClaimsPrincipalExtensions.GetUserId() extension for consistent claim extraction
private static async Task<IResult> GetCollections(
    HttpContext http,
    IMediator mediator,
    Guid? parentId = null)
{
    var userId = http.User.GetUserId(); // checks "sub" then ClaimTypes.NameIdentifier
    var groupIds = http.User.FindAll("groups").Select(c => c.Value).ToList();

    var result = await mediator.Send(new GetCollectionsQuery { ParentId = parentId });
    return Results.Ok(result);
}

// Similar for POST, PATCH, DELETE...
```

**1.9 DTOs**

Create `Dam.Application/Dtos/CollectionDto.cs`:
```csharp
public class CollectionDto
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; }
    public string UserRole { get; set; } // what the current user has
}
```

#### Success Criteria
- GET `/api/collections` returns user's accessible collections
- POST `/api/collections` creates new collection with user as manager
- POST `/api/collections/{id}/acl` assigns roles to users/groups
- Unauthorized users cannot access/modify collections
- API responds with DTOs (not raw entities)

#### Testing
- **Unit**: AuthorizationService with mocked repo
  - Test CanViewCollection for different roles
  - Test CanUploadToCollection edge cases
- **Integration**: Create collection, assign ACL, verify query filters by user
- **E2E**: Manual API calls (Postman or curl)

#### Effort Estimate
- **2 days** (1-2 devs)

---

### Phase 1C: Keycloak OIDC Integration & Authentication (Concurrent with 1A-1B)

**STATUS: ✅ COMPLETE**

> Keycloak 24.0.1 fully configured with media realm. Users created and tested. OIDC authentication integrated into ASP.NET Core 9 Minimal APIs. JWT tokens validated. Browser and container-side networking resolved (Authority vs MetadataAddress configuration). User successfully logs in via browser.
>
> **Key Achievement**: Solved complex Windows Docker Desktop DNS resolution issue where browser needs `host.docker.internal:8080` for metadata fetch while server uses container network `keycloak:8080` for authority validation.

#### Current Implementation Details

**Keycloak Setup (docker-compose.yml)**
- Service: keycloak:24.0.1 with PostgreSQL backend
- Environment: 
  - `KEYCLOAK_ADMIN=admin`, `KEYCLOAK_ADMIN_PASSWORD=admin123`
  - `KC_DB=postgres`, `KC_DB_URL=...`, `KC_PROXY=edge`
- Realm: `media` (created manually)
- Users created:
  - admin / admin123 (system admin)
  - testuser / testuser123 (test user - viewer)
  - mediaadmin / mediaadmin123 (media realm admin)
- Client: assethub-app (configured as confidential client with client secret)

**Dual Authentication Configuration (Program.cs)**

The application supports **two authentication methods**:

| Method | Use Case | How It Works |
|--------|----------|--------------|
| **Cookie + OIDC** | Blazor Server UI (browser) | Browser redirects to Keycloak login, session maintained via cookie |
| **JWT Bearer** | API clients (curl, Postman, mobile apps) | Client sends `Authorization: Bearer <token>` header |

A **policy scheme** automatically selects the appropriate method based on the presence of an `Authorization: Bearer` header.

```csharp
// Dual auth: Cookie for browser UI, JWT Bearer for API clients
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "DualAuth";
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddPolicyScheme("DualAuth", "Cookie or Bearer", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ") == true)
            return JwtBearerDefaults.AuthenticationScheme;
        return CookieAuthenticationDefaults.AuthenticationScheme;
    };
})
.AddCookie()
.AddJwtBearer(options =>
{
    options.Authority = keycloakAuthority; // "http://keycloak:8080/realms/media"
    options.Audience = "assethub-app";
    options.RequireHttpsMetadata = false;
})
.AddOpenIdConnect(options =>
{
    options.Authority = keycloakAuthority;
    options.ClientId = "assethub-app";
    options.ClientSecret = clientSecret;
    options.ResponseType = "code";
    options.UsePkce = true;
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.SaveTokens = true;
    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
});
```

**API Usage with JWT Bearer**
```bash
# Get token from Keycloak
TOKEN=$(curl -s -X POST "http://keycloak:8080/realms/media/protocol/openid-connect/token" \
  -d "grant_type=password" \
  -d "client_id=assethub-app" \
  -d "client_secret=VxBiV29QVchYHFzD5N62l43fTXbTMzSl" \
  -d "username=admin" \
  -d "password=admin123" | jq -r '.access_token')

# Call API with Bearer token
curl -H "Authorization: Bearer $TOKEN" http://localhost:7252/api/assets
```

**JWT Token Processing**
- Tokens validated against Keycloak public key (JWKS endpoint)
- Claims extracted: `sub` (user ID), `preferred_username`, `email`, `groups`
- Role mapping: Keycloak realm roles → ASP.NET Core role claims
- Authorization policies enforce `RequireAuthorization()` on protected endpoints

**User ID Extraction Pattern**

Use the `ClaimsPrincipalExtensions` class in `AssetHub.Endpoints` namespace for consistent user ID extraction:

```csharp
// Extension methods (in Endpoints/ClaimsPrincipalExtensions.cs)
public static class ClaimsPrincipalExtensions
{
    // Returns null if user ID not found
    public static string? GetUserId(this ClaimsPrincipal user)
        => user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    // Returns fallback value if user ID not found
    public static string GetUserIdOrDefault(this ClaimsPrincipal user, string fallback = "unknown")
        => user.GetUserId() ?? fallback;
}

// Usage in endpoint handlers:
var userId = user.GetUserId();
if (userId == null) return Results.Unauthorized();

// For non-critical paths (e.g., logging):
var userId = user.GetUserIdOrDefault();
```

This ensures consistent claim lookup order: first `"sub"` (standard OIDC), then `ClaimTypes.NameIdentifier` as fallback.

#### Issues Resolved
1. **Missing KEYCLOAK_ADMIN variables**: Updated from deprecated KC_ADMIN format
2. **Browser login redirect failed**: Keycloak hostname not resolvable from browser
3. **MetadataAddress unreachable**: Container cannot resolve localhost; needed host.docker.internal
4. **Solution**: Dual configuration with fallback MetadataAddress for Windows Docker Desktop

#### Security Considerations - COMPLETED
✅ **Client is now configured as confidential** (with client secret). 

**Implemented Configuration**:
1. ✅ assethub-app client is confidential (requires client secret)
2. ✅ Program.cs requires ClientSecret - throws `InvalidOperationException` if missing
3. ✅ Secret stored in appsettings.json / environment variables
4. ✅ CREDENTIALS.md updated with configuration details
5. ✅ Role hierarchy defined: viewer → contributor → manager → admin

**Authorization Policies** (defined in Program.cs):
- `RequireViewer` - viewer, contributor, manager, or admin
- `RequireContributor` - contributor, manager, or admin  
- `RequireManager` - manager or admin
- `RequireAdmin` - admin only

#### Success Criteria
- [x] Keycloak realm (media) accessible at http://keycloak:8080/admin
- [x] Test users can log in via browser
- [x] API validates tokens from Keycloak
- [x] Claims extracted and available in HttpContext.User
- [x] Protected endpoints return 401 when unauthenticated
- [x] Client secret configured and required

---

## Phase 2: Upload & Media Processing (Days 6-10)

### Phase 2A: Upload Flow & Hangfire Jobs (Days 6-8)

**STATUS: ✅ ENABLED**

> All endpoint code implemented and compiling without errors. Asset CRUD operations ready. MinIO adapter for S3-compatible storage configured. Media processing service structure in place. Hangfire job scheduling integrated.
> 
> **Current State**: Endpoints enabled in Program.cs (`app.MapAssetEndpoints();`). Authentication integration with Keycloak verified working.

#### Deliverables
- [x] Asset entity and repository
- [x] Upload init/complete endpoints
- [x] Job queue integration (Hangfire)
- [x] Worker service for thumbnail generation (ImageMagick)
- [x] Presigned URL generation (MinIO)
- [x] Asset detail endpoint with metadata

#### Modules Involved
- **Dam.Domain**: Asset, Rendition entities, IMediaProcessor interface
- **Dam.Application**: InitUploadHandler, CompleteUploadHandler
- **Dam.Infrastructure**: AssetRepository, MinIOAdapter, ImageMagickProcessor, HangfireJobService
- **Dam.Worker**: Hangfire job handlers
- **Dam.Api**: Upload endpoints, presigned URL endpoint

#### Tasks

**2.1 Domain: Asset Entity**

Create `Dam.Domain/Entities/Asset.cs`:
```csharp
public class Asset
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public string AssetType { get; set; } // image|video|document
    public string Status { get; set; } // processing|ready|failed
    public string Title { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, object> MetadataJson { get; set; } = new();
    
    public string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    
    public string OriginalObjectKey { get; set; } // tenant/{id}/assets/{assetId}/original
    public string? ThumbObjectKey { get; set; } // .../thumb.jpg
    public string? MediumObjectKey { get; set; } // .../medium.jpg
    public string? PosterObjectKey { get; set; } // .../poster.jpg (video)
    
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Collection Collection { get; set; }
}
```

**2.2 Application: Upload Use Cases**

Create `Dam.Application/Assets/InitUploadHandler.cs`:
```csharp
public record InitUploadRequest
{
    public Guid CollectionId { get; set; }
    public string Filename { get; set; }
    public string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public string AssetType { get; set; } // image|video|document
}

public record InitUploadResponse
{
    public Guid AssetId { get; set; }
    public string PresignedUploadUrl { get; set; }
    public string ObjectKey { get; set; }
}

public class InitUploadHandler
{
    private readonly IAssetRepository _assetRepo;
    private readonly IAuthorizationService _authz;
    private readonly IObjectStorageService _storage;

    public async Task<InitUploadResponse> HandleAsync(
        string userId, Guid collectionId, InitUploadRequest req)
    {
        // 1. Verify user can upload to this collection
        if (!await _authz.CanUploadToCollectionAsync(userId, [], collectionId))
            throw new UnauthorizedAccessException();

        // 2. Validate content type & size
        if (!IsAllowedContentType(req.ContentType))
            throw new BadRequestException("Content type not allowed");

        if (req.SizeBytes > GetMaxSizeForType(req.AssetType))
            throw new BadRequestException("File too large");

        // 3. Create asset in DB with status=processing
        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            CollectionId = collectionId,
            AssetType = req.AssetType,
            Status = "processing",
            Title = Path.GetFileNameWithoutExtension(req.Filename),
            ContentType = req.ContentType,
            SizeBytes = req.SizeBytes,
            OriginalObjectKey = $"assets/{asset.Id}/original",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
            UpdatedAt = DateTime.UtcNow
        };

        await _assetRepo.CreateAsync(asset);

        // 4. Generate presigned PUT URL (15 min)
        var uploadUrl = await _storage.GetPresignedUploadUrlAsync(
            asset.OriginalObjectKey, 
            TimeSpan.FromMinutes(15));

        return new InitUploadResponse
        {
            AssetId = asset.Id,
            PresignedUploadUrl = uploadUrl,
            ObjectKey = asset.OriginalObjectKey
        };
    }

    private bool IsAllowedContentType(string ct)
        => ct switch
        {
            "image/jpeg" or "image/png" or "image/webp" => true,
            "video/mp4" => true,
            "application/pdf" => true,
            _ => false
        };

    private long GetMaxSizeForType(string type)
        => type switch
        {
            "image" => 500 * 1024 * 1024,      // 500 MB
            "video" => 2 * 1024 * 1024 * 1024, // 2 GB
            "document" => 100 * 1024 * 1024,   // 100 MB
            _ => throw new InvalidOperationException()
        };
}
```

Create `Dam.Application/Assets/CompleteUploadHandler.cs`:
```csharp
public record CompleteUploadRequest
{
    public Guid AssetId { get; set; }
    public string ObjectKey { get; set; }
}

public class CompleteUploadHandler
{
    private readonly IAssetRepository _assetRepo;
    private readonly IJobQueueService _jobQueue;

    public async Task HandleAsync(string userId, CompleteUploadRequest req)
    {
        var asset = await _assetRepo.GetByIdAsync(req.AssetId);
        if (asset == null)
            throw new NotFoundException("Asset not found");

        // Enqueue processing job based on asset type
        if (asset.AssetType == "image")
        {
            await _jobQueue.EnqueueAsync("ProcessImage", new { AssetId = asset.Id });
        }
        else if (asset.AssetType == "video")
        {
            await _jobQueue.EnqueueAsync("ProcessVideo", new { AssetId = asset.Id });
        }
        else if (asset.AssetType == "document")
        {
            // Just mark as ready
            asset.Status = "ready";
            await _assetRepo.UpdateAsync(asset);
        }
    }
}
```

**2.3 Infrastructure: MinIO Adapter**

Create `Dam.Infrastructure/ObjectStorage/MinIOAdapter.cs`:
```csharp
public interface IObjectStorageService
{
    Task<string> GetPresignedUploadUrlAsync(string objectKey, TimeSpan expiry);
    Task<string> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan expiry);
    Task<Stream> GetObjectAsync(string objectKey);
    Task PutObjectAsync(string objectKey, Stream data, string contentType);
    Task DeleteObjectAsync(string objectKey);
    Task<bool> ObjectExistsAsync(string objectKey);
}

public class MinIOAdapter : IObjectStorageService
{
    private readonly IMinioClient _client;
    private readonly string _bucket;

    public MinIOAdapter(IMinioClient client, IOptions<MinIOOptions> options)
    {
        _client = client;
        _bucket = options.Value.BucketName;
    }

    public async Task<string> GetPresignedUploadUrlAsync(string objectKey, TimeSpan expiry)
    {
        var url = await _client.PresignedPutObjectAsync(
            new PresignedPutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(objectKey)
                .WithExpiry((int)expiry.TotalSeconds));
        
        return url;
    }

    public async Task<string> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan expiry)
    {
        var url = await _client.PresignedGetObjectAsync(
            new PresignedGetObjectArgs()
                .WithBucket(_bucket)
                .WithObject(objectKey)
                .WithExpiry((int)expiry.TotalSeconds));
        
        return url;
    }

    // ... implement GET, PUT, DELETE, EXISTS
}
```

**2.4 Infrastructure: Media Processor (ImageMagick)**

Create `Dam.Infrastructure/MediaProcessing/ImageMagickProcessor.cs`:
```csharp
public interface IMediaProcessor
{
    Task ProcessImageAsync(string originalPath, string outputDir);
    Task<Dictionary<string, string>> ProcessVideoAsync(string originalPath, string outputDir);
}

public class ImageMagickProcessor : IMediaProcessor
{
    private readonly IObjectStorageService _storage;

    public async Task ProcessImageAsync(string originalPath, string outputDir)
    {
        // 1. Download from MinIO
        using var original = await _storage.GetObjectAsync(originalPath);
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "original.jpg");
        
        using (var file = File.Create(tempFile))
            await original.CopyToAsync(file);

        // 2. Generate thumb (300x300)
        var thumbPath = Path.Combine(tempDir, "thumb.jpg");
        await RunImageMagickAsync($"convert {tempFile} -resize 300x300 {thumbPath}");

        // 3. Generate medium (1200x1200)
        var mediumPath = Path.Combine(tempDir, "medium.jpg");
        await RunImageMagickAsync($"convert {tempFile} -resize 1200x1200 {mediumPath}");

        // 4. Extract EXIF (optional for metadata)
        // var metadata = ExtractExif(tempFile);

        // 5. Upload renditions back to MinIO
        using (var thumbFile = File.OpenRead(thumbPath))
            await _storage.PutObjectAsync(
                originalPath.Replace("/original", "/thumb.jpg"),
                thumbFile,
                "image/jpeg");

        using (var mediumFile = File.OpenRead(mediumPath))
            await _storage.PutObjectAsync(
                originalPath.Replace("/original", "/medium.jpg"),
                mediumFile,
                "image/jpeg");

        // 6. Cleanup
        Directory.Delete(tempDir, true);
    }

    public async Task<Dictionary<string, string>> ProcessVideoAsync(string originalPath, string outputDir)
    {
        // Similar flow: download, ffprobe for metadata, ffmpeg for poster, upload, cleanup
        var metadata = new Dictionary<string, string>();
        
        // Use ffprobe to get duration, dimensions, codec
        var result = await RunCommandAsync($"ffprobe -v quiet -print_json -show_format -show_streams {videoPath}");
        
        // Parse JSON result, extract duration/width/height
        
        // Generate poster at t=1s
        var posterPath = Path.Combine(tempDir, "poster.jpg");
        await RunCommandAsync($"ffmpeg -i {videoPath} -ss 00:00:01 -vframes 1 {posterPath}");
        
        // Upload poster
        
        return metadata;
    }

    private async Task RunImageMagickAsync(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        await process.WaitForExitAsync();
        
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ImageMagick failed: {command}");
    }

    private async Task<string> RunCommandAsync(string command)
    {
        // Similar to above, return stdout
    }
}
```

**2.5 Worker: Hangfire Job Handlers**

Create `Dam.Worker/Jobs/ProcessImageJob.cs`:
```csharp
public class ProcessImageJob
{
    private readonly IAssetRepository _assetRepo;
    private readonly IMediaProcessor _processor;
    private readonly IObjectStorageService _storage;
    private readonly ILogger<ProcessImageJob> _logger;

    public async Task ExecuteAsync(Guid assetId)
    {
        try
        {
            _logger.LogInformation($"Processing image asset {assetId}");

            var asset = await _assetRepo.GetByIdAsync(assetId);
            if (asset == null)
                throw new NotFoundException($"Asset {assetId} not found");

            // Process image
            await _processor.ProcessImageAsync(asset.OriginalObjectKey, "/tmp/output");

            // Update asset
            asset.ThumbObjectKey = asset.OriginalObjectKey.Replace("/original", "/thumb.jpg");
            asset.MediumObjectKey = asset.OriginalObjectKey.Replace("/original", "/medium.jpg");
            asset.Status = "ready";
            asset.UpdatedAt = DateTime.UtcNow;

            await _assetRepo.UpdateAsync(asset);

            _logger.LogInformation($"Processed image asset {assetId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to process image {assetId}");
            
            var asset = await _assetRepo.GetByIdAsync(assetId);
            if (asset != null)
            {
                asset.Status = "failed";
                asset.MetadataJson["error"] = ex.Message;
                await _assetRepo.UpdateAsync(asset);
            }

            throw; // Let Hangfire retry
        }
    }
}

public class ProcessVideoJob
{
    // Similar but also extract metadata and generate poster
    public async Task ExecuteAsync(Guid assetId)
    {
        // ... implementation similar to image
    }
}
```

Register in Program.cs:
```csharp
RecurringJob.AddOrUpdate<ProcessImageJob>(
    "process-images",
    j => j.ExecuteAsync(Guid.Empty),
    Cron.MinuteInterval(1));
```

**2.6 API Endpoints**

Create `Dam.Api/Endpoints/UploadEndpoints.cs`:
```csharp
public static void MapUploadEndpoints(this WebApplication app)
{
    var group = app.MapGroup("/api/upload")
        .RequireAuthorization();

    group.MapPost("/init", InitUpload)
        .WithName("InitUpload");

    group.MapPost("/complete", CompleteUpload)
        .WithName("CompleteUpload");

    group.MapGet("/presigned-download/{assetId}", GetPresignedDownloadUrl)
        .WithName("GetPresignedDownloadUrl");
}

private static async Task<IResult> InitUpload(
    HttpContext http,
    IMediator mediator,
    InitUploadRequest req)
{
    var userId = http.User.GetUserId();
    var response = await mediator.Send(new InitUploadCommand { Request = req, UserId = userId });
    return Results.Ok(response);
}

private static async Task<IResult> CompleteUpload(
    HttpContext http,
    IMediator mediator,
    CompleteUploadRequest req)
{
    var userId = http.User.GetUserId();
    await mediator.Send(new CompleteUploadCommand { Request = req, UserId = userId });
    return Results.NoContent();
}

private static async Task<IResult> GetPresignedDownloadUrl(
    HttpContext http,
    IMediator mediator,
    Guid assetId)
{
    var userId = http.User.GetUserId();
    var url = await mediator.Send(new GetPresignedUrlQuery { AssetId = assetId, UserId = userId });
    return Results.Ok(new { url });
}
```

#### Success Criteria
- POST `/api/upload/init` returns presigned upload URL
- Client uploads file directly to MinIO via presigned URL
- POST `/api/upload/complete` enqueues Hangfire job
- Hangfire job processes image (generates thumb/medium) and updates asset status to "ready"
- GET `/api/upload/presigned-download/{assetId}` returns presigned download URL (TTL 5 min)
- Asset appears in grid after processing completes

#### Testing
- **Unit**:
  - InitUploadHandler validates content type and size
  - ProcessImageJob updates asset status correctly
  - AuthZ prevents upload to unauthorized collections
- **Integration**:
  - Full upload flow: init → presign → client upload → complete → Hangfire job → asset ready
  - Mock MinIO and IMediaProcessor
- **E2E**:
  - Upload image via UI → watch grid update

#### Effort Estimate
- **3 days** (1-2 devs, familiar with Hangfire)

---

### Phase 2B: Video Processing & Presigned URLs (Days 9-10)

**STATUS: 🔄 PARTIALLY COMPLETE**

> FFmpeg and ffprobe integration implemented. Video metadata extraction service ready. Presigned URL generation integrated into Asset endpoints. Full-text search foundation structure in place but full implementation deferred.
>
> **Note**: Phase 2B depends on Phase 2A being enabled. Currently both Asset endpoints (2A+2B) are disabled. Enable 2A first for image processing, then enable 2B for video support.

#### Deliverables
- [x] Video metadata extraction (ffprobe)
- [x] Poster frame generation (ffmpeg)
- [x] Asset detail API with presigned URLs
- [ ] Full-text search foundation (Postgres GIN) - code structure only, not fully integrated

#### Tasks

**2.7 Video Processing**

Create `Dam.Infrastructure/MediaProcessing/FFmpegProcessor.cs`:
```csharp
public class FFmpegProcessor : IMediaProcessor
{
    public async Task<Dictionary<string, object>> ProcessVideoAsync(string originalKey)
    {
        // 1. Download video from MinIO
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "video.mp4");
        using var stream = await _storage.GetObjectAsync(originalKey);
        using (var file = File.Create(tempFile))
            await stream.CopyToAsync(file);

        try
        {
            // 2. Extract metadata with ffprobe
            var metadata = await ExtractMetadataAsync(tempFile);

            // 3. Generate poster frame at t=1s
            var posterPath = Path.Combine(Path.GetDirectoryName(tempFile)!, "poster.jpg");
            await RunFFmpegAsync($"-i {tempFile} -ss 00:00:01 -vframes 1 {posterPath}");

            // 4. Upload poster to MinIO
            using (var posterFile = File.OpenRead(posterPath))
                await _storage.PutObjectAsync(
                    originalKey.Replace("/original", "/poster.jpg"),
                    posterFile,
                    "image/jpeg");

            // 5. Return metadata
            return metadata;
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private async Task<Dictionary<string, object>> ExtractMetadataAsync(string filePath)
    {
        var json = await RunCommandAsync($"ffprobe -v quiet -print_json -show_format -show_streams {filePath}");
        var doc = JsonDocument.Parse(json);
        
        var metadata = new Dictionary<string, object>();
        
        if (doc.RootElement.TryGetProperty("format", out var format))
        {
            if (format.TryGetProperty("duration", out var duration))
                metadata["duration_seconds"] = duration.GetDouble();
        }

        if (doc.RootElement.TryGetProperty("streams", out var streams))
        {
            var videoStream = streams.EnumerateArray().FirstOrDefault();
            if (videoStream.ValueKind != JsonValueKind.Undefined)
            {
                if (videoStream.TryGetProperty("width", out var width))
                    metadata["width"] = width.GetInt32();
                if (videoStream.TryGetProperty("height", out var height))
                    metadata["height"] = height.GetInt32();
                if (videoStream.TryGetProperty("codec_name", out var codec))
                    metadata["codec"] = codec.GetString();
            }
        }

        return metadata;
    }
}
```

**2.8 Asset Detail Endpoint**

Create handler and endpoint:
```csharp
public record AssetDetailDto
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string AssetType { get; set; }
    public string Status { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    public string? OriginalUrl { get; set; } // Presigned
    public string? ThumbUrl { get; set; }
    public string? MediumUrl { get; set; }
    public string? PosterUrl { get; set; }
    public List<string> Tags { get; set; }
}

group.MapGet("/{id}", GetAssetDetail)
    .RequireAuthorization()
    .WithName("GetAssetDetail");

private static async Task<IResult> GetAssetDetail(
    HttpContext http,
    IMediator mediator,
    Guid id)
{
    var userId = http.User.GetUserId();
    var result = await mediator.Send(new GetAssetDetailQuery { AssetId = id, UserId = userId });
    return Results.Ok(result);
}
```

Handler:
```csharp
public class GetAssetDetailHandler
{
    private readonly IAssetRepository _assetRepo;
    private readonly IAuthorizationService _authz;
    private readonly IObjectStorageService _storage;

    public async Task<AssetDetailDto> HandleAsync(string userId, Guid assetId)
    {
        var asset = await _assetRepo.GetByIdAsync(assetId);
        if (asset == null)
            throw new NotFoundException();

        // Check auth
        if (!await _authz.CanViewCollectionAsync(userId, [], asset.CollectionId))
            throw new UnauthorizedAccessException();

        // Generate presigned URLs (TTL 5 min)
        var presignTtl = TimeSpan.FromMinutes(5);
        
        var dto = new AssetDetailDto
        {
            Id = asset.Id,
            Title = asset.Title,
            Description = asset.Description,
            AssetType = asset.AssetType,
            Status = asset.Status,
            SizeBytes = asset.SizeBytes,
            CreatedAt = asset.CreatedAt,
            Metadata = asset.MetadataJson,
            Tags = asset.Tags,
            OriginalUrl = asset.Status == "ready" 
                ? await _storage.GetPresignedDownloadUrlAsync(asset.OriginalObjectKey, presignTtl)
                : null,
            ThumbUrl = !string.IsNullOrEmpty(asset.ThumbObjectKey)
                ? await _storage.GetPresignedDownloadUrlAsync(asset.ThumbObjectKey, presignTtl)
                : null,
            MediumUrl = !string.IsNullOrEmpty(asset.MediumObjectKey)
                ? await _storage.GetPresignedDownloadUrlAsync(asset.MediumObjectKey, presignTtl)
                : null,
            PosterUrl = !string.IsNullOrEmpty(asset.PosterObjectKey)
                ? await _storage.GetPresignedDownloadUrlAsync(asset.PosterObjectKey, presignTtl)
                : null
        };

        return dto;
    }
}
```

**2.9 Full-Text Search Foundation**

Add to migrations:
```sql
-- Enable pg_trgm for trigram search
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Index on title + description
CREATE INDEX idx_assets_fulltext ON assets 
USING gin (to_tsvector('english', title || ' ' || COALESCE(description, '')));

-- For case-insensitive trigram search
CREATE INDEX idx_assets_title_trgm ON assets USING gin (title gin_trgm_ops);
```

Create search handler:
```csharp
public record SearchAssetsRequest
{
    public Guid CollectionId { get; set; }
    public string? Query { get; set; }
    public string? AssetType { get; set; }
    public DateTime? CreatedAfter { get; set; }
    public DateTime? CreatedBefore { get; set; }
    public int Page { get; set; } = 0;
    public int PageSize { get; set; } = 20;
}

public class SearchAssetsHandler
{
    private readonly IAssetRepository _assetRepo;
    private readonly IAuthorizationService _authz;

    public async Task<(List<AssetGridDto>, int Total)> HandleAsync(
        string userId,
        SearchAssetsRequest req)
    {
        if (!await _authz.CanViewCollectionAsync(userId, [], req.CollectionId))
            throw new UnauthorizedAccessException();

        var query = _assetRepo.Query()
            .Where(a => a.CollectionId == req.CollectionId)
            .Where(a => a.Status == "ready");

        // Full-text search
        if (!string.IsNullOrWhiteSpace(req.Query))
        {
            var searchTerm = req.Query.Trim();
            // Using EF.Functions.Like for trigram or custom SQL
            query = query.Where(a => 
                EF.Functions.Like(a.Title, $"%{searchTerm}%") ||
                EF.Functions.Like(a.Description ?? "", $"%{searchTerm}%"));
        }

        if (!string.IsNullOrEmpty(req.AssetType))
            query = query.Where(a => a.AssetType == req.AssetType);

        if (req.CreatedAfter.HasValue)
            query = query.Where(a => a.CreatedAt >= req.CreatedAfter);

        if (req.CreatedBefore.HasValue)
            query = query.Where(a => a.CreatedAt <= req.CreatedBefore);

        var total = await query.CountAsync();

        var assets = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip(req.Page * req.PageSize)
            .Take(req.PageSize)
            .Select(a => new AssetGridDto
            {
                Id = a.Id,
                Title = a.Title,
                AssetType = a.AssetType,
                ThumbUrl = a.ThumbObjectKey, // We'll generate presigned in response
                PosterUrl = a.PosterObjectKey,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return (assets, total);
    }
}
```

#### Success Criteria
- Video metadata extracted correctly (duration, resolution, codec)
- Poster frame generated and accessible
- Asset detail endpoint returns presigned URLs for all renditions
- Search/filter works on title, description, type, date
- Full-text search fast with Postgres index

#### Testing
- **Unit**: FFmpegProcessor extracts metadata correctly
- **Integration**: Upload video → process → metadata populated → search finds it
- **E2E**: Upload video, see poster in grid, click for details

#### Effort Estimate
- **2 days** (1 dev)

---

## Phase 3: UI, Sharing & Testing (Days 11-16)

### Phase 3A: Blazor UI - Collections & Asset Grid (Days 11-12)

**STATUS: ✅ COMPLETE**

> UI development complete. All core components implemented and functional. MudBlazor 8.x integrated with updated APIs.
>
> **Completed Components**:
> - `Services/AssetHubApiClient.cs` - HTTP client for API calls
> - `Components/CollectionTree.razor` - Collection navigation sidebar
> - `Components/AssetGrid.razor` - Asset card grid with thumbnails
> - `Components/AssetUpload.razor` - Drag-and-drop file upload
> - `Components/CreateCollectionDialog.razor` - Create collection modal
> - `Components/ShareLinkDialog.razor` - Share URL display modal
> - `Pages/Assets.razor` - Main asset browser page with Download All button
> - `Pages/AssetDetail.razor` - Asset detail/preview page with 2-column layout
> - `Pages/Share.razor` - Public share page with consistent layout
>
> **Recent Enhancements (January 2026)**:
> - Download All button for collections (ZIP archive streaming)
> - Download All button for shared collections
> - Clickable assets in shared collections with detail view
> - Two-column layout for shared asset pages (matching authenticated view)
> - Advanced MetaData display on shared asset pages
> - Navigation menu hidden for non-authenticated share page visitors
> - Timezone configured to Europe/Stockholm
> - DateTime.UtcNow fix for PostgreSQL timestamptz compatibility

#### Deliverables
- [x] Collections tree/breadcrumb navigation
- [x] Asset grid with virtualization
- [x] Search/filter UI
- [x] Asset detail modal/page
- [x] Upload UI (drag-and-drop, progress)
- [x] Download All for collections (ZIP streaming)
- [x] Shared collection asset detail view with metadata

#### Modules Involved
- **Dam.Ui**: Blazor Razor Class Library (pages, layouts, components)

**Current Dam.Ui Structure:**
```
src/Dam.Ui/
├── Dam.Ui.csproj       # Razor Class Library (Microsoft.NET.Sdk.Razor)
├── _Imports.razor      # Global usings for Razor components
├── App.razor           # Root app component
├── Routes.razor        # Router configuration
├── Layout/             # MainLayout.razor, NavMenu.razor
├── Pages/              # Home.razor, Counter.razor, etc.
└── Components/         # (future) Reusable components like CollectionTree, AssetUpload
```

#### Tasks (UI Implementation - Full Component List)

**3.1 Services**

Create `Dam.Ui/Services/ApiClient.cs`:
```csharp
public class ApiClient
{
    private readonly HttpClient _http;
    private readonly NavigationManager _nav;

    public async Task<List<CollectionDto>> GetCollectionsAsync(Guid? parentId = null)
    {
        var url = "/api/collections" + (parentId.HasValue ? $"?parentId={parentId}" : "");
        return await _http.GetFromJsonAsync<List<CollectionDto>>(url);
    }

    public async Task<AssetDetailDto> GetAssetAsync(Guid id)
        => await _http.GetFromJsonAsync<AssetDetailDto>($"/api/assets/{id}");

    public async Task<(List<AssetGridDto> Assets, int Total)> SearchAssetsAsync(SearchAssetsRequest req)
        => await _http.GetFromJsonAsync<(List<AssetGridDto>, int)>("/api/assets/search", req);

    public async Task<InitUploadResponse> InitUploadAsync(InitUploadRequest req)
        => await _http.PostAsJsonAsync<InitUploadResponse>("/api/upload/init", req);

    // ... etc
}
```

Register in Program.cs:
```csharp
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<CollectionService>();
builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<UploadService>();
```

**3.2 Pages & Components**

Create `Dam.Ui/Pages/Assets.razor` (main browse page):
```razor
@page "/assets"
@using Dam.Ui.Services

@inject ApiClient Api
@inject NavigationManager Nav

<MudContainer MaxWidth="MaxWidth.ExtraLarge" Class="pt-4">
    <MudGrid>
        <MudItem xs="12" sm="3">
            <MudPaper Class="pa-4">
                <MudText Typo="Typo.h6" GutterBottom="true">Collections</MudText>
                <CollectionTree ParentId="null" OnSelect="OnCollectionSelected" />
            </MudPaper>
        </MudItem>

        <MudItem xs="12" sm="9">
            <MudPaper Class="pa-4">
                <!-- Search / Filter -->
                <MudGrid Class="mb-4">
                    <MudItem xs="12">
                        <MudTextField @bind-Value="_searchQuery" 
                                      Placeholder="Search..." 
                                      Variant="Variant.Outlined"
                                      Adornment="Adornment.End"
                                      AdornmentIcon="@Icons.Material.Filled.Search" />
                    </MudItem>
                    <MudItem xs="12" sm="6">
                        <MudSelect @bind-Value="_selectedType" 
                                   Label="Asset Type" 
                                   Variant="Variant.Outlined">
                            <MudSelectItem Value="@((string)null)">All</MudSelectItem>
                            <MudSelectItem Value="image">Images</MudSelectItem>
                            <MudSelectItem Value="video">Videos</MudSelectItem>
                            <MudSelectItem Value="document">Documents</MudSelectItem>
                        </MudSelect>
                    </MudItem>
                    <MudItem xs="12" sm="6">
                        <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="SearchAsync">
                            Search
                        </MudButton>
                    </MudItem>
                </MudGrid>

                <!-- Upload -->
                <AssetUpload OnUploadComplete="OnUploadComplete" />

                <!-- Grid -->
                @if (_assets == null)
                {
                    <MudProgressCircular Color="Color.Default" Indeterminate="true" />
                }
                else if (_assets.Count == 0)
                {
                    <MudText>No assets found.</MudText>
                }
                else
                {
                    <MudTable Items="_assets" Hover="true" Virtualize="true" Height="600px">
                        <HeaderContent>
                            <MudTh>Thumbnail</MudTh>
                            <MudTh>Title</MudTh>
                            <MudTh>Type</MudTh>
                            <MudTh>Created</MudTh>
                            <MudTh>Actions</MudTh>
                        </HeaderContent>
                        <RowTemplate>
                            <MudTd>
                                <MudImage Src="@context.ThumbUrl" Style="width:60px;height:60px;object-fit:cover;" />
                            </MudTd>
                            <MudTd>@context.Title</MudTd>
                            <MudTd>@context.AssetType</MudTd>
                            <MudTd>@context.CreatedAt.ToShortDateString()</MudTd>
                            <MudTd>
                                <MudButton Size="Size.Small" Variant="Variant.Text" 
                                           OnClick="() => ViewAssetAsync(context.Id)">
                                    View
                                </MudButton>
                            </MudTd>
                        </RowTemplate>
                    </MudTable>
                }
            </MudPaper>
        </MudItem>
    </MudGrid>
</MudContainer>

@code {
    private Guid? _selectedCollectionId;
    private string _searchQuery = "";
    private string _selectedType;
    private List<AssetGridDto> _assets;

    private async Task OnCollectionSelected(Guid collectionId)
    {
        _selectedCollectionId = collectionId;
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (!_selectedCollectionId.HasValue)
            return;

        var (assets, total) = await Api.SearchAssetsAsync(new SearchAssetsRequest
        {
            CollectionId = _selectedCollectionId.Value,
            Query = _searchQuery,
            AssetType = _selectedType,
            Page = 0,
            PageSize = 50
        });

        _assets = assets;
    }

    private async Task ViewAssetAsync(Guid assetId)
    {
        Nav.NavigateTo($"/assets/{assetId}");
    }

    private async Task OnUploadComplete()
    {
        await SearchAsync();
    }

    protected override async Task OnInitializedAsync()
    {
        // Load initial collection
    }
}
```

Create `Dam.Ui/Components/CollectionTree.razor`:
```razor
@using Dam.Ui.Services

@inject ApiClient Api

<MudList>
    @foreach (var collection in _collections)
    {
        <MudListItem>
            <MudButton OnClick="() => OnSelect(collection.Id)" 
                       Variant="Variant.Text" 
                       Color="Color.Inherit">
                @collection.Name
            </MudButton>
            @if (collection.HasChildren)
            {
                <CollectionTree ParentId="collection.Id" OnSelect="OnSelect" />
            }
        </MudListItem>
    }
</MudList>

@code {
    [Parameter]
    public Guid? ParentId { get; set; }

    [Parameter]
    public EventCallback<Guid> OnSelect { get; set; }

    private List<CollectionDto> _collections = new();

    protected override async Task OnInitializedAsync()
    {
        _collections = await Api.GetCollectionsAsync(ParentId);
    }
}
```

Create `Dam.Ui/Components/AssetUpload.razor`:
```razor
@using Dam.Ui.Services

@inject ApiClient Api
@inject SnackbarService Snackbar

<MudPaper Class="pa-4 mb-4" Style="border: 2px dashed #ccc;">
    <MudFileUpload T="IReadOnlyList<IBrowserFile>" OnFilesChanged="OnFilesSelected" Multiple>
        <ChildContent>
            <MudButton HtmlTag="label" Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Filled.CloudUpload">
                Upload Assets
            </MudButton>
        </ChildContent>
    </MudFileUpload>
</MudPaper>

@if (_uploads.Any())
{
    <MudPaper Class="pa-4">
        <MudText Typo="Typo.h6" GutterBottom="true">Uploads</MudText>
        <MudTable Items="_uploads">
            <HeaderContent>
                <MudTh>File</MudTh>
                <MudTh>Status</MudTh>
                <MudTh>Progress</MudTh>
            </HeaderContent>
            <RowTemplate>
                <MudTd>@context.FileName</MudTd>
                <MudTd>@context.Status</MudTd>
                <MudTd>
                    <MudProgressLinear Value="@context.Progress" />
                </MudTd>
            </RowTemplate>
        </MudTable>
    </MudPaper>
}

@code {
    [Parameter]
    public Guid SelectedCollectionId { get; set; }

    [Parameter]
    public EventCallback OnUploadComplete { get; set; }

    private List<UploadProgress> _uploads = new();

    private class UploadProgress
    {
        public string FileName { get; set; }
        public string Status { get; set; }
        public int Progress { get; set; }
    }

    private async Task OnFilesSelected(IReadOnlyList<IBrowserFile> files)
    {
        foreach (var file in files)
        {
            var upload = new UploadProgress { FileName = file.Name, Status = "Initializing", Progress = 0 };
            _uploads.Add(upload);

            try
            {
                // Init upload
                upload.Status = "Getting upload URL...";
                var initResp = await Api.InitUploadAsync(new InitUploadRequest
                {
                    CollectionId = SelectedCollectionId,
                    Filename = file.Name,
                    ContentType = file.ContentType,
                    SizeBytes = file.Size,
                    AssetType = DetermineAssetType(file.ContentType)
                });

                // Upload file directly to MinIO via presigned URL
                upload.Status = "Uploading...";
                using var stream = file.OpenReadStream(long.MaxValue);
                var request = new HttpRequestMessage(HttpMethod.Put, initResp.PresignedUploadUrl);
                request.Content = new StreamContent(stream);
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
                
                // Track progress
                var totalBytes = file.Size;
                var uploadedBytes = 0;
                var reportingStream = new ReportingStream(stream, (bytes) =>
                {
                    uploadedBytes += bytes;
                    upload.Progress = (int)((uploadedBytes * 100) / totalBytes);
                    StateHasChanged();
                });

                // TODO: actually upload with progress tracking

                upload.Status = "Completing upload...";
                await Api.CompleteUploadAsync(new CompleteUploadRequest
                {
                    AssetId = initResp.AssetId,
                    ObjectKey = initResp.ObjectKey
                });

                upload.Status = "Processing...";
            }
            catch (Exception ex)
            {
                upload.Status = $"Error: {ex.Message}";
                Snackbar.Add($"Upload failed: {ex.Message}", Severity.Error);
            }
        }

        await OnUploadComplete.InvokeAsync();
    }

    private string DetermineAssetType(string contentType)
        => contentType.StartsWith("image/") ? "image"
         : contentType.StartsWith("video/") ? "video"
         : "document";
}
```

Create `Dam.Ui/Pages/AssetDetail.razor`:
```razor
@page "/assets/{AssetId:guid}"

@inject ApiClient Api
@inject NavigationManager Nav

@if (_asset == null)
{
    <MudProgressCircular Color="Color.Default" Indeterminate="true" />
}
else
{
    <MudContainer MaxWidth="MaxWidth.Large" Class="pt-4">
        <MudGrid>
            <MudItem xs="12" sm="8">
                <MudPaper Class="pa-4">
                    @if (_asset.AssetType == "image")
                    {
                        <MudImage Src="@_asset.MediumUrl" Style="width:100%;max-height:600px;object-fit:contain;" />
                    }
                    else if (_asset.AssetType == "video")
                    {
                        <video width="100%" height="600" controls>
                            <source src="@_asset.OriginalUrl" type="video/mp4">
                            Your browser does not support the video tag.
                        </video>
                    }
                </MudPaper>
            </MudItem>

            <MudItem xs="12" sm="4">
                <MudPaper Class="pa-4">
                    <MudText Typo="Typo.h5">@_asset.Title</MudText>
                    <MudText Typo="Typo.body2" Color="Color.TextSecondary">@_asset.AssetType.ToUpper()</MudText>
                    
                    <MudDivider Class="my-4" />

                    <MudText Typo="Typo.h6">Details</MudText>
                    <MudText Typo="Typo.body2">Created: @_asset.CreatedAt.ToString("g")</MudText>
                    <MudText Typo="Typo.body2">Size: @FormatBytes(_asset.SizeBytes)</MudText>

                    @if (_asset.AssetType == "video" && _asset.Metadata.TryGetValue("duration_seconds", out var duration))
                    {
                        <MudText Typo="Typo.body2">Duration: @TimeSpan.FromSeconds((double)duration).ToString(@"hh\:mm\:ss")</MudText>
                    }

                    <MudDivider Class="my-4" />

                    <MudButton Variant="Variant.Filled" Color="Color.Primary" 
                               Href="@_asset.OriginalUrl" Target="_blank" FullWidth>
                        Download
                    </MudButton>

                    <MudButton Variant="Variant.Outlined" Color="Color.Default" 
                               OnClick="() => Nav.NavigateTo('/assets')" FullWidth Class="mt-2">
                        Back
                    </MudButton>
                </MudPaper>
            </MudItem>
        </MudGrid>
    </MudContainer>
}

@code {
    [Parameter]
    public Guid AssetId { get; set; }

    private AssetDetailDto _asset;

    protected override async Task OnInitializedAsync()
    {
        _asset = await Api.GetAssetAsync(AssetId);
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
```

#### Success Criteria
- Collections tree loads and navigates
- Asset grid displays thumbnails and metadata
- Search filters by query, type, date
- Upload shows progress and moves asset to grid after processing
- Asset detail shows preview (image/video) and download link
- Full-text search works

#### Testing
- **E2E**: Navigate → Upload → Search → View → Download

#### Effort Estimate
- **2 days** (1 dev familiar with Blazor)

---

### Phase 3B: Sharing & Audit (Days 13-14)

**STATUS: ✅ COMPLETE**

> All Share endpoint code implemented and fully functional. Share token generation with SHA256 hashing. Public share access with password protection. Share revocation with audit trail. Access tracking (LastAccessedAt, AccessCount) implemented.
>
> **Current State**: Endpoints enabled in Program.cs (`app.MapShareEndpoints();`). Full share workflow tested and working:
> - `POST /api/shares` - Create share with token generation
> - `GET /api/shares/{token}` - Public access with password validation
> - `GET /api/shares/{token}/download` - Download with presigned URL redirect
> - `GET /api/shares/{token}/download-all` - Download all assets as ZIP (for shared collections)
> - `DELETE /api/shares/{id}` - Revoke share (soft delete for audit)
>
> **Recent Enhancements (January 2026)**:
> - Download All endpoint for shared collections (`GET /api/shares/{token}/download-all`)
> - Clickable assets in shared collections with View button
> - Selected asset detail view with File Information and Advanced MetaData
> - Two-column layout matching authenticated AssetDetail.razor
> - Fixed DateTime.UtcNow for PostgreSQL timestamptz compatibility (Npgsql 6+ requirement)
> - Asset type chips styled with `width: fit-content` to prevent expansion

#### Deliverables
- [x] Share creation endpoint & UI
- [x] Public share page (anonymous access)
- [x] Download audit logging
- [x] Revoke shares

#### Tasks

**3.10 Domain: Share Entity**

Create `Dam.Domain/Entities/Share.cs`:
```csharp
public class Share
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } // SHA256(token)
    public string ScopeType { get; set; } // "asset" or "collection"
    public Guid ScopeId { get; set; }
    public Dictionary<string, bool> PermissionsJson { get; set; } // {view: true, download: true}
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? PasswordHash { get; set; } // bcrypt
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }

    public Asset? Asset { get; set; }
    public Collection? Collection { get; set; }
}
```

**3.11 Share Creation Handler**

Create `Dam.Application/Shares/CreateShareHandler.cs`:
```csharp
public record CreateShareRequest
{
    public string ScopeType { get; set; } // "asset" or "collection"
    public Guid ScopeId { get; set; }
    public int ExpiresInHours { get; set; }
    public bool CanDownload { get; set; }
    public string? Password { get; set; }
}

public record CreateShareResponse
{
    public Guid ShareId { get; set; }
    public string ShareUrl { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class CreateShareHandler
{
    private readonly IShareRepository _repo;
    private readonly IAuthorizationService _authz;
    private readonly IConfiguration _config;

    public async Task<CreateShareResponse> HandleAsync(
        string userId, CreateShareRequest req)
    {
        // 1. Verify user can share
        if (req.ScopeType == "asset")
        {
            var asset = await _assetRepo.GetByIdAsync(req.ScopeId);
            if (!await _authz.CanShareCollectionAsync(userId, asset.CollectionId))
                throw new UnauthorizedAccessException();
        }
        else if (req.ScopeType == "collection")
        {
            if (!await _authz.CanShareCollectionAsync(userId, req.ScopeId))
                throw new UnauthorizedAccessException();
        }

        // 2. Generate token
        var token = GenerateSecureToken(32);
        var tokenHash = SHA256Hash(token);

        // 3. Create share record
        var share = new Share
        {
            Id = Guid.NewGuid(),
            TokenHash = tokenHash,
            ScopeType = req.ScopeType,
            ScopeId = req.ScopeId,
            PermissionsJson = new Dictionary<string, bool>
            {
                { "view", true },
                { "download", req.CanDownload }
            },
            ExpiresAt = DateTime.UtcNow.AddHours(req.ExpiresInHours),
            PasswordHash = req.Password != null ? BCrypt.HashPassword(req.Password) : null,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
            AccessCount = 0
        };

        await _repo.CreateAsync(share);

        // 4. Return share URL with token
        var baseUrl = _config["App:BaseUrl"];
        var shareUrl = $"{baseUrl}/share/{Uri.EscapeDataString(token)}";

        return new CreateShareResponse
        {
            ShareId = share.Id,
            ShareUrl = shareUrl,
            ExpiresAt = share.ExpiresAt
        };
    }

    private string GenerateSecureToken(int length)
    {
        using var rng = new RNGCryptoServiceProvider();
        var tokenData = new byte[length];
        rng.GetBytes(tokenData);
        return Convert.ToBase64String(tokenData);
    }

    private string SHA256Hash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hashedBytes);
    }
}
```

**3.12 Public Share Endpoint**

Create `Dam.Api/Endpoints/PublicShareEndpoints.cs`:
```csharp
public static void MapPublicShareEndpoints(this WebApplication app)
{
    var group = app.MapGroup("/api/public/shares");

    group.MapGet("/{token}/asset/{assetId}", GetSharedAsset)
        .WithName("GetSharedAsset");

    group.MapPost("/{token}/validate-password", ValidatePassword)
        .WithName("ValidatePassword");

    group.MapPost("/{token}/download/{assetId}", LogDownload)
        .WithName("LogDownload");
}

private static async Task<IResult> GetSharedAsset(
    HttpContext http,
    IMediator mediator,
    string token,
    Guid assetId)
{
    // Validate token, check expiry, check revoked
    var asset = await mediator.Send(new GetSharedAssetQuery { Token = token, AssetId = assetId });
    return Results.Ok(asset);
}

private static async Task<IResult> ValidatePassword(
    HttpContext http,
    IMediator mediator,
    string token,
    [FromBody] ValidatePasswordRequest req)
{
    var isValid = await mediator.Send(new ValidateSharePasswordQuery { Token = token, Password = req.Password });
    return Results.Ok(new { valid = isValid });
}

private static async Task<IResult> LogDownload(
    HttpContext http,
    IMediator mediator,
    string token,
    Guid assetId)
{
    // Log download in audit
    var ip = http.Connection.RemoteIpAddress?.ToString();
    var userAgent = http.Request.Headers["User-Agent"].ToString();

    await mediator.Send(new LogShareDownloadCommand
    {
        Token = token,
        AssetId = assetId,
        IP = ip,
        UserAgent = userAgent
    });

    return Results.NoContent();
}
```

**3.13 Audit Events**

Create `Dam.Domain/Entities/AuditEvent.cs`:
```csharp
public class AuditEvent
{
    public Guid Id { get; set; }
    public string EventType { get; set; } // UPLOAD, DOWNLOAD, SHARE_CREATED, SHARE_REVOKED, SHARE_ACCESSED
    public string? ActorUserId { get; set; } // null for anonymous
    public string? IP { get; set; }
    public string? UserAgent { get; set; }
    public string TargetType { get; set; } // asset, collection, share
    public Guid? TargetId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, object> DetailsJson { get; set; }
}
```

Create audit service:
```csharp
public interface IAuditService
{
    Task LogAsync(string eventType, string? userId, string? ip, string? userAgent, 
                 string targetType, Guid targetId, Dictionary<string, object> details = null);
}

public class AuditService : IAuditService
{
    private readonly AssetHubDbContext _db;

    public async Task LogAsync(string eventType, string? userId, string? ip, string? userAgent,
                              string targetType, Guid targetId, Dictionary<string, object> details = null)
    {
        var evt = new AuditEvent
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            ActorUserId = userId,
            IP = ip,
            UserAgent = userAgent,
            TargetType = targetType,
            TargetId = targetId,
            CreatedAt = DateTime.UtcNow,
            DetailsJson = details ?? new Dictionary<string, object>()
        };

        _db.AuditEvents.Add(evt);
        await _db.SaveChangesAsync();
    }
}
```

**3.14 UI: Share Component**

Create `Dam.Ui/Components/AssetShare.razor`:
```razor
@using Dam.Ui.Services

@inject ApiClient Api
@inject SnackbarService Snackbar

<MudDialog>
    <TitleContent>
        <MudText Typo="Typo.h6">Share Asset</MudText>
    </TitleContent>
    <DialogContent>
        <MudForm @ref="_form">
            <MudTextField @bind-Value="_expiresInHours" Label="Expires In (hours)" />
            <MudCheckBox @bind-Checked="_canDownload" Label="Allow Download" />
            <MudTextField @bind-Value="_password" Label="Password (optional)" InputType="InputType.Password" />
        </MudForm>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">Cancel</MudButton>
        <MudButton Color="Color.Primary" OnClick="CreateShare">Create Share</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] MudDialogInstance MudDialog { get; set; }
    [Parameter] public Guid AssetId { get; set; }

    private MudForm _form;
    private int _expiresInHours = 24;
    private bool _canDownload = true;
    private string _password;

    private async Task CreateShare()
    {
        var resp = await Api.CreateShareAsync(new CreateShareRequest
        {
            ScopeType = "asset",
            ScopeId = AssetId,
            ExpiresInHours = _expiresInHours,
            CanDownload = _canDownload,
            Password = _password
        });

        Snackbar.Add($"Share URL: {resp.ShareUrl}", Severity.Success);
        MudDialog.Close(DialogResult.Ok(resp));
    }

    private void Cancel() => MudDialog.Cancel();
}
```

**3.15 Public Share Page**

Create `Dam.Ui/Pages/Share.razor`:
```razor
@page "/share/{Token}"

@inject ApiClient Api
@inject NavigationManager Nav
@inject SnackbarService Snackbar

@if (_loading)
{
    <MudProgressCircular Color="Color.Default" Indeterminate="true" />
}
else if (_share == null)
{
    <MudAlert Severity="Severity.Error">Invalid or expired share link</MudAlert>
}
else
{
    <MudContainer MaxWidth="MaxWidth.Large" Class="pt-4">
        @if (_share.PasswordRequired && !_passwordValidated)
        {
            <MudPaper Class="pa-4">
                <MudText Typo="Typo.h6">This share is password protected</MudText>
                <MudTextField @bind-Value="_password" Label="Password" InputType="InputType.Password" />
                <MudButton Variant="Variant.Filled" Color="Color.Primary" OnClick="ValidatePassword">
                    Unlock
                </MudButton>
            </MudPaper>
        }
        else
        {
            <MudGrid>
                <MudItem xs="12" sm="8">
                    @if (_share.AssetType == "image")
                    {
                        <MudImage Src="@_share.MediumUrl" />
                    }
                    else if (_share.AssetType == "video")
                    {
                        <video width="100%" height="600" controls>
                            <source src="@_share.OriginalUrl" type="video/mp4">
                        </video>
                    }
                </MudItem>
                <MudItem xs="12" sm="4">
                    <MudPaper Class="pa-4">
                        <MudText Typo="Typo.h5">@_share.Title</MudText>
                        <MudText Typo="Typo.body2">Expires: @_share.ExpiresAt</MudText>

                        @if (_share.CanDownload)
                        {
                            <MudButton Variant="Variant.Filled" Color="Color.Primary" 
                                      Href="@_share.OriginalUrl" Target="_blank" FullWidth Class="mt-4">
                                Download
                            </MudButton>
                        }
                    </MudPaper>
                </MudItem>
            </MudGrid>
        }
    </MudContainer>
}

@code {
    [Parameter]
    public string Token { get; set; }

    private bool _loading = true;
    private bool _passwordValidated = false;
    private string _password;
    private SharedAssetDto _share;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _share = await Api.GetSharedAssetAsync(Token);
            _passwordValidated = !_share.PasswordRequired;
            _loading = false;
        }
        catch
        {
            _share = null;
            _loading = false;
            Snackbar.Add("Invalid or expired share", Severity.Error);
        }
    }

    private async Task ValidatePassword()
    {
        var isValid = await Api.ValidateSharePasswordAsync(Token, _password);
        if (isValid)
        {
            _passwordValidated = true;
            Snackbar.Add("Unlocked", Severity.Success);
        }
        else
        {
            Snackbar.Add("Invalid password", Severity.Error);
        }
    }
}
```

#### Success Criteria
- Create share endpoint generates token and returns share URL
- Public share page accessible without authentication
- Password protection works
- Download logging recorded in audit
- Shares can be revoked and become inaccessible
- Share TTL enforced (expired shares return 410)

#### Testing
- **Unit**: Token generation, SHA256 hashing, TTL logic
- **Integration**: Create share → access with token → expired token rejected
- **E2E**: Asset detail → create share → copy URL → open in incognito → download → verify audit log

#### Effort Estimate
- **2 days** (1 dev)

---

### Phase 3C: Testing & Hardening (Days 15)

**STATUS: ❌ NOT STARTED**

> Deferred - Testing will be prioritized after Asset and Share endpoints are enabled and working. Unit test structure can be scaffolded quickly. Focus currently on backend stability and feature enablement.

#### Deliverables
- [ ] Unit tests (70%+ coverage on domain/application)
- [ ] Integration tests (API endpoints + DB)
- [ ] Security review (token hashing, authz checks, input validation)
- [ ] E2E test script (manual or Playwright)

#### Tasks

**3.16 Unit Tests (xUnit + Moq)**

Create `Dam.Tests/Domain/AuthorizationServiceTests.cs`:
```csharp
public class AuthorizationServiceTests
{
    private readonly Mock<AssetHubDbContext> _mockDb = new();
    private readonly AuthorizationService _service;

    public AuthorizationServiceTests()
    {
        _service = new AuthorizationService(_mockDb.Object);
    }

    [Fact]
    public async Task CanViewCollection_UserWithViewerRole_ReturnsTrue()
    {
        // Arrange
        var userId = "user1";
        var collectionId = Guid.NewGuid();
        var acl = new CollectionAcl 
        { 
            CollectionId = collectionId, 
            PrincipalType = "user", 
            PrincipalId = userId, 
            Role = "viewer" 
        };

        _mockDb.Setup(db => db.CollectionAcls)
            .ReturnsDbSet(new[] { acl });

        // Act
        var result = await _service.CanViewCollectionAsync(userId, new(), collectionId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task CanUploadToCollection_ViewerRole_ReturnsFalse()
    {
        // Arrange
        var userId = "user1";
        var collectionId = Guid.NewGuid();
        var acl = new CollectionAcl 
        { 
            CollectionId = collectionId, 
            PrincipalType = "user", 
            PrincipalId = userId, 
            Role = "viewer" 
        };

        _mockDb.Setup(db => db.CollectionAcls)
            .ReturnsDbSet(new[] { acl });

        // Act
        var result = await _service.CanUploadToCollectionAsync(userId, new(), collectionId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CanUploadToCollection_ContributorRole_ReturnsTrue()
    {
        // Arrange
        var userId = "user1";
        var collectionId = Guid.NewGuid();
        var acl = new CollectionAcl 
        { 
            CollectionId = collectionId, 
            PrincipalType = "user", 
            PrincipalId = userId, 
            Role = "contributor" 
        };

        _mockDb.Setup(db => db.CollectionAcls)
            .ReturnsDbSet(new[] { acl });

        // Act
        var result = await _service.CanUploadToCollectionAsync(userId, new(), collectionId);

        // Assert
        Assert.True(result);
    }
}
```

Create `Dam.Tests/Application/CreateShareHandlerTests.cs`:
```csharp
public class CreateShareHandlerTests
{
    private readonly Mock<IShareRepository> _mockShareRepo = new();
    private readonly Mock<IAssetRepository> _mockAssetRepo = new();
    private readonly Mock<IAuthorizationService> _mockAuthz = new();
    private readonly CreateShareHandler _handler;

    public CreateShareHandlerTests()
    {
        var mockConfig = new Mock<IConfiguration>();
        mockConfig.Setup(x => x["App:BaseUrl"]).Returns("http://localhost:3000");
        
        _handler = new CreateShareHandler(_mockShareRepo.Object, _mockAssetRepo.Object, _mockAuthz.Object, mockConfig.Object);
    }

    [Fact]
    public async Task CreateShare_UnauthorizedUser_ThrowsException()
    {
        // Arrange
        var userId = "user1";
        var assetId = Guid.NewGuid();
        var asset = new Asset { CollectionId = Guid.NewGuid() };

        _mockAssetRepo.Setup(r => r.GetByIdAsync(assetId))
            .ReturnsAsync(asset);

        _mockAuthz.Setup(a => a.CanShareCollectionAsync(userId, asset.CollectionId))
            .ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => 
            _handler.HandleAsync(userId, new CreateShareRequest { ScopeType = "asset", ScopeId = assetId }));
    }

    [Fact]
    public async Task CreateShare_TokenIsUnique()
    {
        // Arrange
        var userId = "user1";
        var assetId = Guid.NewGuid();
        var asset = new Asset { CollectionId = Guid.NewGuid() };

        _mockAssetRepo.Setup(r => r.GetByIdAsync(assetId))
            .ReturnsAsync(asset);

        _mockAuthz.Setup(a => a.CanShareCollectionAsync(userId, asset.CollectionId))
            .ReturnsAsync(true);

        // Act
        var resp1 = await _handler.HandleAsync(userId, new CreateShareRequest 
        { 
            ScopeType = "asset", 
            ScopeId = assetId,
            ExpiresInHours = 24 
        });

        var resp2 = await _handler.HandleAsync(userId, new CreateShareRequest 
        { 
            ScopeType = "asset", 
            ScopeId = assetId,
            ExpiresInHours = 24 
        });

        // Assert
        Assert.NotEqual(resp1.ShareUrl, resp2.ShareUrl);
    }

    [Fact]
    public async Task CreateShare_WithPassword_PasswordIsHashed()
    {
        // Arrange
        var userId = "user1";
        var assetId = Guid.NewGuid();
        var password = "SecurePassword123";
        var asset = new Asset { CollectionId = Guid.NewGuid() };

        _mockAssetRepo.Setup(r => r.GetByIdAsync(assetId))
            .ReturnsAsync(asset);

        _mockAuthz.Setup(a => a.CanShareCollectionAsync(userId, asset.CollectionId))
            .ReturnsAsync(true);

        Share capturedShare = null;
        _mockShareRepo.Setup(r => r.CreateAsync(It.IsAny<Share>()))
            .Callback<Share>(s => capturedShare = s)
            .Returns(Task.CompletedTask);

        // Act
        await _handler.HandleAsync(userId, new CreateShareRequest 
        { 
            ScopeType = "asset", 
            ScopeId = assetId,
            Password = password 
        });

        // Assert
        Assert.NotNull(capturedShare.PasswordHash);
        Assert.NotEqual(password, capturedShare.PasswordHash);
        Assert.True(BCrypt.Verify(password, capturedShare.PasswordHash));
    }
}
```

**3.17 Integration Tests**

Create `Dam.Tests/Integration/CollectionApiTests.cs`:
```csharp
public class CollectionApiTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory;
    private HttpClient _client;
    private AssetHubDbContext _db;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AssetHubDbContext>));
                    if (descriptor != null) services.Remove(descriptor);

                    // Use in-memory DB for tests
                    services.AddDbContext<AssetHubDbContext>(options =>
                        options.UseInMemoryDatabase("TestDb"));
                });
            });

        _client = _factory.CreateClient();
        _db = _factory.Services.GetRequiredService<AssetHubDbContext>();
        
        await _db.Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task GetCollections_Unauthenticated_Returns401()
    {
        // Act
        var response = await _client.GetAsync("/api/collections");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateCollection_Authenticated_Returns201()
    {
        // Arrange
        var token = GenerateTestToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new { name = "Test Collection", description = "Test" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/collections", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadAsAsync<CollectionDto>();
        Assert.NotNull(content.Id);
        Assert.Equal("Test Collection", content.Name);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_db != null)
        {
            await _db.Database.EnsureDeletedAsync();
            _db.Dispose();
        }
    }

    private string GenerateTestToken()
    {
        // Generate JWT with test claims
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Name, "Test User")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-key-at-least-32-chars"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(claims: claims, signingCredentials: creds, expires: DateTime.UtcNow.AddHours(1));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

**3.18 E2E Test Script**

Create `Dam.Tests/E2E/UploadAndShareFlow.cs`:
```csharp
public class UploadAndShareFlowTests
{
    private readonly HttpClient _client;
    private readonly string _baseUrl = "http://localhost:5000";

    public UploadAndShareFlowTests()
    {
        _client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
    }

    [Fact]
    public async Task FullUploadAndShareFlow()
    {
        // 1. Authenticate
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { username = "testuser", password = "password" });
        var token = await loginResponse.Content.ReadAsAsync<dynamic>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.accessToken);

        // 2. Create collection
        var collResponse = await _client.PostAsJsonAsync("/api/collections", new { name = "Test" });
        var collection = await collResponse.Content.ReadAsAsync<CollectionDto>();

        // 3. Upload image
        var imageContent = new ByteArrayContent(File.ReadAllBytes("test-image.jpg"));
        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

        var uploadInitResponse = await _client.PostAsJsonAsync("/api/upload/init", new
        {
            collectionId = collection.Id,
            filename = "test-image.jpg",
            contentType = "image/jpeg",
            sizeBytes = new FileInfo("test-image.jpg").Length,
            assetType = "image"
        });

        var uploadInit = await uploadInitResponse.Content.ReadAsAsync<dynamic>();
        var assetId = (Guid)uploadInit.assetId;

        // Upload to presigned URL
        var minioClient = new HttpClient();
        await minioClient.PutAsync(uploadInit.presignedUploadUrl, imageContent);

        // Complete upload
        await _client.PostAsJsonAsync("/api/upload/complete", new
        {
            assetId = assetId,
            objectKey = uploadInit.objectKey
        });

        // Wait for processing
        await Task.Delay(5000);

        // 4. Get asset
        var assetResponse = await _client.GetAsync($"/api/assets/{assetId}");
        var asset = await assetResponse.Content.ReadAsAsync<AssetDetailDto>();
        Assert.NotNull(asset.ThumbUrl);

        // 5. Create share
        var shareResponse = await _client.PostAsJsonAsync("/api/shares", new
        {
            scopeType = "asset",
            scopeId = assetId,
            expiresInHours = 24,
            canDownload = true
        });

        var share = await shareResponse.Content.ReadAsAsync<CreateShareResponse>();

        // 6. Access share (anonymous)
        var anonClient = new HttpClient();
        var sharePageResponse = await anonClient.GetAsync(share.ShareUrl);
        Assert.True(sharePageResponse.IsSuccessStatusCode);
    }
}
```

#### Success Criteria
- Unit test coverage > 70% for Domain + Application
- All API endpoints have integration tests
- Security review passes (token hashing, authz enforced, input validated)
- E2E flow works end-to-end

#### Testing
- Run: `dotnet test`

#### Effort Estimate
- **1 day** (1 dev)

---

### Phase 3D: Deployment & Documentation (Day 16)

**STATUS: 🔄 PARTIALLY COMPLETE**

> Docker Compose operational and documented. Development environment fully working. CREDENTIALS.md created with all credentials and troubleshooting. Production configuration and environment-based settings deferred pending Phase 2 completion.
>
> **Completed**: Docker Compose (dev), CREDENTIALS documentation, API running and tested
>
> **Remaining**: Production Compose variant, staging environment, comprehensive README update

#### Deliverables
- [x] Development Docker Compose with Postgres backups, MinIO persistence (working)
- [ ] Production Docker Compose configuration
- [ ] Environment configuration (dev/staging/prod templates)
- [x] CREDENTIALS.md with setup/troubleshooting
- [ ] Comprehensive README with setup/run instructions
- [ ] Deployment runbook

#### Tasks

**3.19 Production Docker Compose**

Create `docker-compose.prod.yml`:
```yaml
version: '3.8'

services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: asethub
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data
      - ./backups:/backups
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5
    networks:
      - asethub

  minio:
    image: minio/minio:latest
    environment:
      MINIO_ROOT_USER: ${MINIO_USER}
      MINIO_ROOT_PASSWORD: ${MINIO_PASSWORD}
    volumes:
      - miniodata:/minio
    command: server /minio --console-address ":9001"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:9000/minio/health/live"]
      interval: 30s
      timeout: 20s
      retries: 3
    networks:
      - asethub

  api:
    build:
      context: .
      dockerfile: Dockerfile
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__Postgres: ${DB_CONNECTION_STRING}
      Keycloak__Authority: ${KEYCLOAK_AUTHORITY}
      Keycloak__ClientId: ${KEYCLOAK_CLIENT_ID}
      Keycloak__ClientSecret: ${KEYCLOAK_CLIENT_SECRET}
      MinIO__Endpoint: minio:9000
      MinIO__AccessKey: ${MINIO_USER}
      MinIO__SecretKey: ${MINIO_PASSWORD}
      MinIO__BucketName: asethub-prod
      App__BaseUrl: ${APP_BASE_URL}
    ports:
      - "7252:7252"
    depends_on:
      postgres:
        condition: service_healthy
      minio:
        condition: service_healthy
    networks:
      - asethub
    restart: unless-stopped

  worker:
    build:
      context: .
      dockerfile: Dockerfile.Worker
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__Postgres: ${DB_CONNECTION_STRING}
      MinIO__Endpoint: minio:9000
      MinIO__AccessKey: ${MINIO_USER}
      MinIO__SecretKey: ${MINIO_PASSWORD}
    depends_on:
      postgres:
        condition: service_healthy
      minio:
        condition: service_healthy
    networks:
      - asethub
    restart: unless-stopped
    deploy:
      replicas: 2

volumes:
  pgdata:
  miniodata:

networks:
  asethub:
    driver: bridge
```

**3.20 Environment Configuration**

Create `.env.example`:
```env
# Database
DB_PASSWORD=your-secure-password
DB_CONNECTION_STRING=Server=postgres;Port=5432;Database=asethub;User Id=postgres;Password=your-secure-password;

# Keycloak
KEYCLOAK_AUTHORITY=https://keycloak.example.com/realms/media
KEYCLOAK_CLIENT_ID=assethub-app
KEYCLOAK_CLIENT_SECRET=your-client-secret

# MinIO
MINIO_USER=minioadmin
MINIO_PASSWORD=your-minio-password

# App
APP_BASE_URL=https://asethub.example.com
```

**3.21 README.md**

Create comprehensive README:
```markdown
# AssetHub - Lightweight DAM System

A self-hosted, permission-based digital asset management system built with .NET, Blazor, and PostgreSQL.

## Features

- Collections with hierarchical navigation
- Role-based access control (Viewer/Contributor/Manager/Admin)
- Image/Video/Document upload with automatic thumbnail generation
- Full-text search with Postgres
- Time-limited share links with optional password protection
- Audit logging for all access and downloads
- Keycloak OIDC authentication
- Docker Compose deployment

## Quick Start (Development)

### Prerequisites
- Docker & Docker Compose
- .NET 9 SDK (if developing locally)

### Start Services
```bash
docker compose up -d
```

This starts:
- PostgreSQL (port 5432)
- MinIO (ports 9000, 9001)
- Keycloak (port 8080)
- API (port 7252)
- Worker (background processing)

### Access
- **API**: http://localhost:7252/api
- **Keycloak**: http://keycloak:8080 (admin / admin123)
- **MinIO Console**: http://localhost:9001 (minioadmin / minioadmin)

### Seed Initial Data
```bash
dotnet run -- --seed
```

## Production Deployment

### Using Docker Compose
```bash
cp .env.example .env
# Edit .env with your production values
docker compose -f docker-compose.prod.yml up -d
```

### Backup Strategy
- Postgres: Use pg_dump
- MinIO: S3 replication or daily snapshots

```bash
# Backup PostgreSQL
docker exec assethub_postgres pg_dump -U postgres asethub > backup_$(date +%s).sql

# Restore
docker exec -i assethub_postgres psql -U postgres asethub < backup.sql
```

## API Endpoints

### Collections
- `GET /api/collections` - List collections user can access
- `POST /api/collections` - Create collection
- `PATCH /api/collections/{id}` - Update collection
- `POST /api/collections/{id}/acl` - Assign roles

### Assets
- `GET /api/assets?collectionId=...&q=...` - Search assets
- `GET /api/assets/{id}` - Get asset details with presigned URLs
- `POST /api/upload/init` - Initiate upload
- `POST /api/upload/complete` - Complete upload (triggers processing)

### Shares
- `POST /api/shares` - Create share link
- `GET /api/public/shares/{token}/asset/{assetId}` - Access shared asset (anonymous)
- `POST /api/shares/{id}/revoke` - Revoke share

## Architecture

```
AssetHub/
├── AssetHub.csproj         # Main host project (Blazor Server + API endpoints)
├── AssetHub.sln
├── Program.cs              # Application entry point
├── Endpoints/              # Minimal API endpoint definitions
├── Dockerfile              # API container
├── Dockerfile.Worker       # Worker container
├── docker-compose.yml      # Full stack orchestration
└── src/
    ├── Dam.Domain          # Entities, domain logic, interfaces
    ├── Dam.Application     # Use cases, DTOs, handlers, BuildInfo, DebugGuard
    ├── Dam.Infrastructure  # EF Core, MinIO, Hangfire
    ├── Dam.Worker          # Background job processing
    └── Dam.Ui              # Blazor Razor Class Library (pages, layouts, components)
```

**Note**: The main `AssetHub` project at the root serves as the host application. It references all `Dam.*` projects under `src/`. The `Dam.Ui` project is a Razor Class Library containing all Blazor components, pages, and layouts. Utility classes like `BuildInfo` and `DebugGuard` live in `Dam.Application`.

## Security

- Share tokens are hashed with SHA256 before storage
- Presigned URLs have short TTL (5 min for download, 15 min for upload)
- All access is logged in audit table
- Keycloak OIDC for authentication
- Rate limiting on public endpoints

## Development Notes

- Use `docker compose logs -f api` to tail API logs
- Use `docker compose logs -f worker` for job processing logs
- Hangfire dashboard at `http://localhost:7252/hangfire`

## Contributing

1. Fork the repo
2. Create feature branch (`git checkout -b feature/amazing-feature`)
3. Commit changes (`git commit -m 'Add amazing feature'`)
4. Push to branch (`git push origin feature/amazing-feature`)
5. Open Pull Request

## License

MIT

## Support

For issues, questions, or feature requests, open a GitHub issue.
```

**3.22 Deployment Runbook**

Create `DEPLOYMENT.md`:
```markdown
# AssetHub Deployment Runbook

## Pre-Deployment Checklist

- [ ] All tests passing locally
- [ ] Environment variables configured in `.env`
- [ ] Database backups enabled
- [ ] Keycloak realm/client configured
- [ ] MinIO bucket created
- [ ] SSL certificates in place

## Deployment Steps

### 1. Prepare Environment
```bash
# Clone repo
git clone https://github.com/yourorg/asethub.git
cd asethub

# Copy and configure
cp .env.example .env
nano .env  # Edit values
```

### 2. Initialize Database
```bash
docker compose -f docker-compose.prod.yml up postgres -d
docker compose -f docker-compose.prod.yml run --rm api dotnet ef database update
```

### 3. Start Services
```bash
docker compose -f docker-compose.prod.yml up -d
```

### 4. Verify
```bash
curl http://localhost:7252/health
docker compose -f docker-compose.prod.yml logs api
```

## Rollback

If something goes wrong:
```bash
# Stop services
docker compose -f docker-compose.prod.yml down

# Restore database from backup
docker exec -i assethub_postgres psql -U postgres asethub < backup.sql

# Start again
docker compose -f docker-compose.prod.yml up -d
```

## Monitoring

### Health Checks
- API health: `curl http://localhost:7252/health`
- DB connection: Check Postgres logs
- MinIO: Check S3 endpoint connectivity

### Logs
```bash
docker compose logs -f api
docker compose logs -f worker
docker compose logs -f postgres
```

## Upgrade

1. Back up database
2. Pull new code: `git pull`
3. Rebuild images: `docker compose build`
4. Run migrations: `docker compose run --rm api dotnet ef database update`
5. Restart: `docker compose up -d`

## Troubleshooting

### API can't connect to Postgres
- Check Postgres is running: `docker compose ps`
- Check connection string in `.env`
- Check network: `docker network ls`

### Upload fails
- Check MinIO is running
- Check bucket exists: `docker compose logs minio`
- Check disk space: `docker system df`

### Jobs not processing
- Check worker is running: `docker compose ps worker`
- Check job queue in Postgres: `SELECT * FROM hangfire_job;`
- Check logs: `docker compose logs worker`
```

#### Success Criteria
- Docker Compose prod config works end-to-end
- README covers setup, usage, deployment
- Deployment runbook covers common scenarios

#### Effort Estimate
- **1 day** (0.5 dev)

---

## Summary Timeline

| Week | Days | Phase | Output |
|------|------|-------|--------|
| Week 1 | 1-3 | 1A: Docker + DB | All services running, migrations done |
| Week 1 | 4-5 | 1B: Collections + ACL | Full collection CRUD + auth working |
| Week 1-2 | 6-8 | 2A: Upload + Jobs | End-to-end upload → thumbnail pipeline |
| Week 2 | 9-10 | 2B: Video + Search | Video metadata, presigned URLs, full-text search |
| Week 2 | 11-12 | 3A: UI | Blazor grid, search, upload, asset detail |
| Week 2 | 13-14 | 3B: Sharing + Audit | Share links, public endpoints, logging |
| Week 3 | 15 | 3C: Testing | Unit + integration + E2E tests |
| Week 3 | 16 | 3D: Deployment | Docker Compose prod, docs, runbook |

---

## Tech Stack Recap

| Layer | Technology |
|-------|-----------|
| Frontend | Blazor Server, MudBlazor |
| API | ASP.NET Core 9 (minimal APIs) |
| Database | PostgreSQL + EF Core |
| Auth | Keycloak OIDC |
| Storage | MinIO (S3-compatible) |
| Jobs | Hangfire + Postgres |
| Media | ImageMagick (images), ffmpeg (video) |
| Search | PostgreSQL (full-text, trigram) |
| Testing | xUnit, Moq, WebApplicationFactory |
| Deployment | Docker Compose |

---

## Key Principles to Remember

1. **Authorization First**: Every API call checks ACL before proceeding
2. **Presigned URLs**: Never proxy files through API; let MinIO serve directly
3. **Async Processing**: All heavy work (thumbnails, video metadata) done in Worker via Hangfire
4. **Audit Everything**: Share creation, access, download all logged
5. **Short TTLs**: Presigned URLs expire after 5-15 minutes
6. **Token Security**: Share tokens hashed with SHA256, never stored plaintext
7. **Single Tenant**: All data scoped to implicit tenant (can expand later)

---

## Success Metrics (by end of week 3)

- [ ] All Docker services start without errors
- [ ] User can create collections and assign roles
- [ ] User can upload images/videos with progress tracking
- [ ] Thumbnails auto-generate within 5 seconds
- [ ] Asset grid loads 50 assets in <300ms
- [ ] Full-text search returns results instantly
- [ ] Share links work with password protection and expiry
- [ ] Audit log records all significant events
- [ ] Unit test coverage > 70%
- [ ] Production deployment checklist complete

---

## Questions to Revisit

1. **Scalability**: Worker can be scaled with `docker compose deploy replicas: N`
2. **Search**: Postgres full-text is fast for <1M assets; OpenSearch later if needed

---

## Immediate Next Steps (Priority Order)

### ✅ COMPLETED: Keycloak Client Secret Migration (Phase 1C Follow-up)

**Status**: Implemented. The assethub-app client is now configured as a confidential client with client secret required.

**Completed Steps**:
1. ✅ assethub-app configured as confidential client in Keycloak
2. ✅ Client secret stored in media-realm.json: `VxBiV29QVchYHFzD5N62l43fTXbTMzSl`
3. ✅ Program.cs requires ClientSecret (throws if not configured)
4. ✅ Role hierarchy implemented (viewer → contributor → manager → admin)
5. ✅ Authorization policies defined (RequireViewer, RequireContributor, RequireManager, RequireAdmin)
4. In "Credentials" tab, copy the generated Client Secret
5. Update docker-compose.yml: Add environment variable
   ```yaml
   Keycloak__ClientSecret: <generated-secret>
   ```
6. Update Program.cs OIDC configuration:
   ```csharp
   options.ClientSecret = builder.Configuration["Keycloak:ClientSecret"];
   options.UsePkce = false; // No longer needed with confidential client
   ```
7. Rebuild and restart: `docker compose up --build`
8. Test login to verify authentication still works
9. Update CREDENTIALS.md with new configuration

**Time Estimate**: 30-45 minutes

---

### 🟡 HIGH: Enable Asset & Share Endpoints

**Prerequisite**: Keycloak client secret implemented (above)

**Steps**:
1. Enable Asset endpoints:
   - Uncomment line 191 in [Program.cs](Program.cs#L191): `app.MapAssetEndpoints();`
2. Enable Share endpoints:
   - Uncomment line 192 in [Program.cs](Program.cs#L192): `app.MapShareEndpoints();`
3. Rebuild Docker image: `docker compose up --build`
4. Test endpoints manually:
   ```bash
   # Get assets
   curl -H "Authorization: Bearer $(gettoken)" http://localhost:7252/api/assets

   # Upload asset
   curl -X POST -F "file=@image.jpg" \
     -H "Authorization: Bearer $(gettoken)" \
     http://localhost:7252/api/assets/upload

   # Create share
   curl -X POST http://localhost:7252/api/shares \
     -H "Content-Type: application/json" \
     -H "Authorization: Bearer $(gettoken)" \
     -d '{"assetId":"...","expiresInHours":24}'
   ```
5. Monitor Hangfire dashboard at http://localhost:7252/hangfire
   - Watch thumbnail generation jobs process
   - Verify no job failures

**Time Estimate**: 1-2 hours (code is ready, just needs testing)

---

### 🟡 MEDIUM: Worker Container Investigation

**Issue**: Worker service exits with code 139 (segmentation fault or out-of-memory)

**Impact**: Non-blocking - Hangfire jobs can be queued in database, but no background processing occurs. For MVP, Jobs can be processed manually or with alternative worker deployment.

**Steps** (if time allows):
1. Check worker logs: `docker logs assethub-worker`
2. Reduce memory constraints or add heap size limits
3. Verify ImageMagick and ffmpeg are properly installed in worker container
4. Consider running Hangfire jobs directly in API container temporarily

**Time Estimate**: 30 minutes - 1 hour

---

### 🟢 LOW: Phase 3 UI Development

**Status**: Deferred until Asset/Share endpoints fully tested

**When Ready**:
- Collections page (tree navigation, breadcrumbs)
- Asset grid with search/filter
- Upload UI with progress tracking
- Share management UI

---

## Comprehensive Testing Plan

**Priority**: HIGH  
**Status**: ⚠️ PENDING - Scheduled for dedicated testing session  
**Last Updated**: 2026-02-04

### Overview

Following the major architectural refactoring (removal of primary collection concept), comprehensive testing is required to verify all functionality works correctly with the new many-to-many collection relationships.

### Testing Scope

#### 1. Unit Tests (Backend)

**Repository Layer**
- [ ] **AssetRepository**
  - `GetByIdAsync` - Verify returns asset without Collection navigation
  - `GetByCollectionAsync` - Verify queries via AssetCollections join table
  - `CountByCollectionAsync` - Verify counts via join table
  - `DeleteByCollectionAsync` - Verify deletes assets via join table lookup
  - `SearchAsync` - Verify search works without Collection include

- [ ] **AssetCollectionRepository**
  - `GetCollectionsForAssetAsync` - Verify returns all linked collections
  - `AddToCollectionAsync` - Verify creates join table entry
  - `RemoveFromCollectionAsync` - Verify removes join table entry
  - `BelongsToCollectionAsync` - Verify membership check via join table
  - `GetCollectionIdsForAssetAsync` - Verify returns correct collection IDs

**Authorization Helper**
- [ ] **CanAccessAssetAsync**
  - Verify returns true when user has viewer+ role in any asset collection
  - Verify returns true for system admins regardless of collection membership
  - Verify returns false when user has no access to any asset collection
  - Verify returns false for orphaned assets (no collections)

#### 2. Integration Tests (API Endpoints)

**Asset Management**
- [ ] **Upload Asset** (`POST /api/assets/upload`)
  - Verify asset created without CollectionId field
  - Verify AssetCollections entry created
  - Verify contributor+ can upload
  - Verify viewer cannot upload

- [ ] **Get Asset** (`GET /api/assets/{id}`)
  - Verify returns asset data without CollectionId/CollectionName
  - Verify permission check via any collection membership
  - Verify 403 when user has no access to any asset collection
  - Verify 404 for non-existent asset

- [ ] **Update Asset** (`PATCH /api/assets/{id}`)
  - Verify contributor+ can edit metadata
  - Verify permission checked via any collection
  - Verify viewer cannot edit

- [ ] **Delete Asset** (`DELETE /api/assets/{id}`)
  - Verify manager+ can delete
  - Verify permission checked via any collection
  - Verify asset removed from all collections

- [ ] **Get All Assets** (`GET /api/assets/all`)
  - Verify returns assets from all accessible collections
  - Verify user role calculated correctly (highest across all collections)
  - Verify search/filter works correctly
  - Verify pagination works

**Collection Assignment**
- [ ] **Get Asset Collections** (`GET /api/assets/{id}/collections`)
  - Verify returns all collections asset belongs to
  - Verify no "primary" indicator
  - Verify permission check via any collection

- [ ] **Add to Collection** (`POST /api/assets/{id}/collections/{collectionId}`)
  - Verify contributor+ can add
  - Verify creates join table entry
  - Verify duplicate prevention
  - Verify 403 for viewers

- [ ] **Remove from Collection** (`DELETE /api/assets/{id}/collections/{collectionId}`)
  - Verify contributor+ can remove
  - Verify removes join table entry
  - Verify asset can be removed from all collections
  - Verify 403 for viewers

**Rendition Endpoints**
- [ ] **Download Original** (`GET /api/assets/{id}/original/download`)
  - Verify permission via CanAccessAssetAsync
  - Verify works for assets in multiple collections

- [ ] **Preview Original** (`GET /api/assets/{id}/original/preview`)
  - Verify permission check works
  - Verify PDF preview works

- [ ] **Get Thumbnail** (`GET /api/assets/{id}/thumb`)
  - Verify permission check works
  - Verify returns correct size

- [ ] **Get Medium** (`GET /api/assets/{id}/medium`)
  - Verify permission check works
  - Verify returns correct size

- [ ] **Get Poster** (`GET /api/assets/{id}/poster`)
  - Verify permission check works
  - Verify works for video assets

**Share Endpoints**
- [ ] **Create Share** (`POST /api/shares`)
  - Verify requires asset belongs to at least one collection
  - Verify uses join table for validation
  - Verify contributor+ can create shares
  - Verify shares work for multi-collection assets

- [ ] **Download Shared Asset** (`GET /shares/{token}/download`)
  - Verify validates asset-collection membership via join table
  - Verify works for collection shares
  - Verify works for asset shares

- [ ] **Preview Shared Asset** (`GET /shares/{token}/preview`)
  - Verify validates asset-collection membership via join table
  - Verify works correctly

#### 3. Edge Cases & Scenarios

**Multi-Collection Assets**
- [ ] Asset belongs to 2+ collections
  - Verify user with different roles in each collection gets highest role
  - Verify removing from one collection doesn't affect other memberships
  - Verify deletion removes from all collections

**Orphaned Assets**
- [ ] Asset belongs to zero collections
  - Verify cannot be accessed via normal endpoints
  - Verify admin can still access
  - Verify can be re-assigned to a collection

**Permission Scenarios**
- [ ] User has viewer in Collection A, contributor in Collection B
  - Asset belongs to both collections
  - Verify user gets contributor permissions

- [ ] User has manager in Collection A only
  - Asset belongs to Collection A and B
  - Verify user can delete asset (has manager in any collection)

**Collection Deletion**
- [ ] Delete collection that has assets
  - Verify cascade behavior on AssetCollections
  - Verify assets remain if in other collections
  - Verify orphaned assets handled correctly

#### 4. UI Testing

**AssetDetail Page**
- [ ] Verify collections section displays correctly
- [ ] Verify Add to Collection works
- [ ] Verify Remove from Collection works with confirmation
- [ ] Verify proper button visibility based on role

**AllAssets Page**
- [ ] Verify asset grid displays without collection names
- [ ] Verify no "Go to collection" button
- [ ] Verify role-based actions work correctly

**Admin Page**
- [ ] Verify share management works
- [ ] Verify collection access management works
- [ ] Verify user management works

#### 5. Performance Testing

- [ ] **Large Collections**: Test with 1000+ assets in a single collection
- [ ] **Many Collections**: Test asset in 20+ collections
- [ ] **Search Performance**: Verify search across all collections performs adequately
- [ ] **Permission Checks**: Profile CanAccessAssetAsync with many collections

#### 6. Database Integrity

- [ ] Verify no orphaned records in AssetCollections table
- [ ] Verify CollectionId column is fully removed from Assets table
- [ ] Verify all foreign key constraints are correct
- [ ] Run database consistency check

### Test Execution Plan

**Phase 1: Repository & Unit Tests (Priority 1)**
- Focus on data access layer correctness
- Ensure join table queries work properly
- Estimated time: 3-4 hours

**Phase 2: API Integration Tests (Priority 2)**
- Test all endpoints with various permission scenarios
- Verify authorization logic works correctly
- Estimated time: 4-6 hours

**Phase 3: UI & E2E Tests (Priority 3)**
- Manual testing of UI workflows
- Verify user experience is consistent
- Estimated time: 2-3 hours

**Phase 4: Performance & Edge Cases (Priority 4)**
- Stress testing with large datasets
- Verify edge case handling
- Estimated time: 2-3 hours

### Testing Framework Setup

**Required Packages**:
```xml
<PackageReference Include="xUnit" Version="2.6.6" />
<PackageReference Include="xUnit.runner.visualstudio" Version="2.5.6" />
<PackageReference Include="Moq" Version="4.20.70" />
<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.0" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.0" />
```

**Test Project Structure**:
```
AssetHub.Tests/
  ├── Unit/
  │   ├── Repositories/
  │   │   ├── AssetRepositoryTests.cs
  │   │   └── AssetCollectionRepositoryTests.cs
  │   └── Helpers/
  │       └── CanAccessAssetAsyncTests.cs
  ├── Integration/
  │   ├── AssetEndpointsTests.cs
  │   ├── ShareEndpointsTests.cs
  │   └── CollectionEndpointsTests.cs
  └── TestFixtures/
      ├── DatabaseFixture.cs
      └── AuthenticationFixture.cs
```

### Success Criteria

- [ ] All unit tests pass (100% of new repository methods)
- [ ] All integration tests pass (all endpoints)
- [ ] No 500 errors in manual testing
- [ ] Performance requirements met (< 1s response time)
- [ ] No regression in existing functionality
- [ ] Code coverage > 80% for modified code

### Notes for Future Testing Session

1. **Start with Repository Tests**: These are the foundation - if these fail, everything else will fail
2. **Use In-Memory Database**: For unit tests, use EF InMemory provider for speed
3. **Real Database for Integration**: Use test container or dedicated test database
4. **Mock Authentication**: Use test authentication handler to simulate different users/roles
5. **Document Test Data**: Create seed data scripts for consistent test scenarios
6. **Regression Testing**: Ensure existing functionality (collections, shares, admin) still works

---

## Known Issues & Workarounds

| Issue | Impact | Status | Workaround |
|-------|--------|--------|------------|
| Worker crashes with exit code 139 | Background jobs not processing | Non-blocking | Run jobs in API container or external worker |
| Keycloak /health/ready returns 404 | Docker compose health check limited | Minor | Check via admin console instead |
| ~~Phase 1C requires client secret~~ | ~~Security concern~~ | ✅ DONE | Confidential client implemented |
| ~~Asset/Share endpoints disabled~~ | ~~Features not available~~ | ✅ DONE | Endpoints enabled and tested |

---

## Phase Completion Summary

| Phase | Status | Blockers | Next Action |
|-------|--------|----------|-------------|
| Phase 1A: Docker & Database | ✅ COMPLETE | None | Monitor logs |
| Phase 1B: Collections API | ✅ COMPLETE | None | In production use |
| **Phase 1C: Authentication** | ✅ COMPLETE | None | Dual auth (Cookie + JWT Bearer) working |
| Phase 2A: Upload & Processing | ✅ TESTED | None | Image thumbnails generating correctly |
| Phase 2B: Video & Presigned URLs | ✅ CODE COMPLETE | None | Ready for video upload testing |
| Phase 3A: UI - Collections & Grid | ❌ NOT STARTED | None | Design & build Blazor components |
| Phase 3B: Sharing & Audit | ✅ COMPLETE | None | Full share workflow working |
| Phase 3C: Testing | ❌ NOT STARTED | All features | Create unit/integration tests |
| Phase 3D: Deployment & Docs | 🔄 PARTIAL | Prod config | Create prod Compose, README |

---

## API Endpoint Testing Results (2026-01-27)

### Successful Tests ✅

| Endpoint | Method | Result |
|----------|--------|--------|
| `/api/collections` | GET | Lists collections |
| `/api/collections` | POST | Creates collection |
| `/api/assets` | GET | Lists assets (empty initially) |
| `/api/assets` | POST | Uploads file + triggers Hangfire job |
| `/api/assets/{id}` | GET | Returns asset with thumb/medium keys populated |
| `/api/shares` | POST | Creates share with token + URL |

### Fixes Applied During Testing

1. **Anti-forgery tokens** - Added `DisableAntiforgery()` to API endpoint groups (JWT Bearer doesn't use CSRF tokens)
2. **Npgsql JSONB support** - Added `EnableDynamicJson()` to NpgsqlDataSourceBuilder for Dictionary<string,object> columns
3. **ImageMagick** - Added to Dockerfile for thumbnail generation
4. **Database name mismatch** - Fixed appsettings.json to use `assethub` (was `asethub`)
5. **Migration Designer.cs** - Created missing file so EF Core recognizes migrations

### Pending Implementation

- None - all core API endpoints functional

---

## Development Environment Status

**Working** ✅:
- ASP.NET Core 9 API running on http://localhost:7252
- Collections CRUD fully functional
- Keycloak OIDC authentication (browser login working)
- JWT Bearer authentication (API access working)
- PostgreSQL database with EF migrations
- MinIO object storage
- Hangfire job scheduling (image processing tested)
- Docker Compose multi-service orchestration
- Asset upload + thumbnail generation
- Share link creation

**Ready to Enable** 🟡:
- Video processing service
- Audit logging

**Deferred** ❌:
- UI/Frontend (Blazor components)
- Automated testing
- Production deployment configuration
- Full-text search (backend ready, UI integration needed)

**Test Credentials**:
- App Login: http://localhost:7252 → testuser / testuser123
- Keycloak Admin: http://keycloak:8080/admin → admin / admin123
- Media Admin: mediaadmin / mediaadmin123
3. **Video Transcoding**: Not in MVP; can add HLS streaming in Phase 2
4. **Mobile**: Desktop-first in MVP; responsive design in Phase 2
5. **Groups**: Single user ACL in MVP; group management in Phase 2

---

This plan is aggressive but realistic for a 2-3 week MVP. The key is strict scope management and testing as you go.

Good luck! 🚀

---

## Session: Large File Handling (Presigned URL Architecture)

**Problem**: The system needs to handle video files up to 700 MB. The existing implementation had multiple critical bottlenecks:

1. **Kestrel 28.6 MB default limit** — blocks large uploads entirely
2. **All uploads stream through SignalR** — Blazor Server routes file data through the SignalR circuit, which is extremely slow for large files
3. **All downloads proxy through server RAM** — `DownloadAsync` copies entire objects into `MemoryStream` before sending to client (700 MB file = 700 MB RAM per concurrent download)
4. **ZIP download-all buffers everything** — loads up to 1000 assets into a single `MemoryStream`
5. **`MaxUploadSizeMb` config existed but was never enforced** — dead config

### Architecture: Presigned URL Flow

#### Upload (browser → MinIO directly)
```
Browser → API: POST /api/assets/init-upload {fileName, size, contentType, collectionId}
API: Creates asset record (status="uploading"), generates presigned PUT URL
API → Browser: {assetId, uploadUrl, expiresInSeconds}
Browser → MinIO: PUT <presignedUrl> (via JS XMLHttpRequest with progress tracking)
Browser → API: POST /api/assets/{id}/confirm-upload
API: Stat object in MinIO, verify exists, update asset to "processing", schedule Hangfire job
```

#### Download (API → presigned redirect)
```
Browser → API: GET /api/assets/{id}/download (with auth)
API: Auth check → generate short-lived presigned GET URL (5 min)
API → Browser: 302 Redirect to presigned MinIO URL
Browser → MinIO: GET <presignedUrl> (direct download, zero server RAM)
```

### Changes Implemented

#### Infrastructure Layer

**`MinIOSettings.cs`** — Added `PublicUrl` and `PublicUseSSL` properties. When the API server uses an internal MinIO endpoint (e.g., `minio:9000` in Docker), presigned URLs would contain that internal hostname which browsers can't resolve. `PublicUrl` provides the browser-accessible endpoint (e.g., `localhost:9000`).

**`IMinIOAdapter.cs`** — Added:
- `GetPresignedUploadUrlAsync()` — generates presigned PUT URL for browser uploads
- `StatObjectAsync()` — returns object metadata (size, content type, etag) without downloading; used by confirm-upload to verify the file was actually uploaded
- `ObjectStatInfo` record type

**`MinIOAdapter.cs`** — Now accepts two `IMinioClient` instances:
- Internal client: for server-side operations (upload, download, delete, stat, bucket management)
- Public client: for presigned URL generation (configured with `PublicUrl` endpoint so URLs are browser-accessible)

**`Program.cs`** — Registers both MinIO clients:
- Standard `IMinioClient` singleton with internal endpoint
- Keyed `IMinioClient("public")` singleton with public endpoint (falls back to internal if `PublicUrl` not configured)
- `MinIOAdapter` factory registration that injects both clients
- Kestrel `MaxRequestBodySize` set to `MaxUploadSizeMb` (for IFormFile fallback path)

#### API Endpoints

**`AssetEndpoints.cs`**:
- **`POST /api/assets/init-upload`** — Step 1 of presigned upload. Validates auth + collection access + file size, creates asset record with `StatusUploading`, generates presigned PUT URL (15 min expiry)
- **`POST /api/assets/{id}/confirm-upload`** — Step 2. Verifies object exists in MinIO via `StatObject`, updates asset to `StatusProcessing`, schedules Hangfire job
- **Existing `POST /api/assets`** — Kept as fallback for small files and API clients. Now enforces `MaxUploadSizeMb`
- **All download/preview endpoints** — Replaced `DownloadAsync` → `Results.File` (MemoryStream proxy) with `GetPresignedDownloadUrlAsync` → `Results.Redirect` (zero RAM usage)
- Added `StatusUploading = "uploading"` to `Asset` entity

**`ShareEndpoints.cs`**:
- **`DownloadSharedAsset`** — After token/password validation, redirects to presigned URL instead of proxying through RAM
- **`PreviewSharedAsset`** — Same presigned redirect for all renditions (thumb, medium, PDF)
- **`DownloadAllSharedAssets`** — Streams ZIP directly to `Response.BodyWriter` instead of buffering in `MemoryStream`. Each asset still fetched individually but written to the response stream incrementally

#### Frontend (Blazor Server)

**`fileUpload.js`** — New JS interop module for direct-to-MinIO upload:
- `uploadFile()` — PUT file directly to MinIO via presigned URL using `XMLHttpRequest`
- Real-time progress tracking via `xhr.upload.progress` events
- Callbacks to Blazor via `DotNetObjectReference` (progress %, loaded/total bytes, completion, errors)
- `getFileMetadata()` — reads file names/sizes/types without sending data over SignalR

**`AssetUpload.razor`** — Complete rewrite:
- Uses `InputFile @ref` + JS interop instead of `OpenReadStream()`
- Flow: file selection → `getFileMetadata()` (JS) → `InitUploadAsync()` (API) → `uploadFile()` (JS → MinIO) → `ConfirmUploadAsync()` (API)
- Real progress bar (`MudProgressLinear`) instead of indeterminate spinner
- Implements `IAsyncDisposable` for JS module cleanup
- `UploadCallbackHelper` class receives JS interop callbacks with `[JSInvokable]` methods

**`AssetHubApiClient.cs`** — Added `InitUploadAsync()` and `ConfirmUploadAsync()` methods

**`InitUploadResult.cs`** — New DTO for the init-upload response

**`PresignedUploadDtos.cs`** — New DTOs: `InitUploadRequest`, `InitUploadResponse`

#### Configuration

- `appsettings.json` / `appsettings.Development.json` — Added `MinIO:PublicUrl` and `MinIO:PublicUseSSL`
- `docker-compose.yml` — Added `MinIO__PublicUrl` and `MinIO__PublicUseSSL` env vars for the API container

### CORS Requirement

For presigned uploads to work, MinIO must allow CORS from the application origin. In development:
```bash
mc alias set local http://localhost:9000 minioadmin minioadmin_dev_password
mc admin config set local api cors_allow_origin="http://localhost:7252"
mc admin service restart local
```

Or via the MinIO Console (http://localhost:9001) → Configuration → API → CORS.

### Impact Summary

| Metric | Before | After |
|--------|--------|-------|
| Max upload size | 28.6 MB (Kestrel default) | 700 MB+ (presigned, no Kestrel limit) |
| Upload path | SignalR → API → MinIO | Browser → MinIO (direct PUT) |
| Download RAM usage | Full file in MemoryStream | 0 bytes (presigned redirect) |
| Upload progress | Indeterminate spinner | Real % with bytes loaded |
| ZIP download RAM | All assets buffered | Streamed to response |
| MaxUploadSizeMb enforced | No | Yes (both upload paths) |
