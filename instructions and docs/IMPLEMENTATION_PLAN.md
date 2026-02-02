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

#### 1. Multi-Collection Asset Assignment
**Priority**: High  
**Description**: Allow a single image/asset to belong to multiple collections simultaneously.
- Add many-to-many relationship between Assets and Collections
- Update UI to allow selecting multiple collections when uploading/editing
- Display collection membership in asset detail view

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

#### 7. Asset Collection Membership Display
**Priority**: Medium  
**Description**: On the asset detail view, show a list of all collections the asset belongs to.
- Display collection badges/chips on asset detail
- Click to navigate to parent collection
- Quick add/remove from collections in asset detail

#### 8. Role-Based UI Visibility ✅ COMPLETE
**Priority**: High  
**Status**: Implemented on 2026-02-01  
**Description**: Hide/show UI elements based on user's role on a collection/asset.
- [x] Viewers cannot see Share, Delete, or Upload buttons
- [x] Contributors can see Upload, Share, Edit
- [x] Managers can see Delete, manage ACL
- [x] All Assets page restricted to admin only
- [x] Centralized RolePermissions class for consistent role checks

#### 9. Empty State Messages
**Priority**: Medium  
**Description**: Show friendly messages when there is no data to display.
- Display "There is nothing to show here" or similar when lists/grids are empty
- Consistent empty state styling across all data views (assets, collections, shares, users)
- Provide helpful actions (e.g., "Create your first collection")

#### 10. Error Handling & User Feedback
**Priority**: High  
**Description**: Improve error handling with user-friendly messages.
- Display polite error messages for 401/500 errors (e.g., "Something went wrong while fetching users")
- Never expose technical error details to users
- Log API errors server-side for debugging
- Consistent error toast notifications via MudBlazor Snackbar

#### 11. API Error Logging
**Priority**: Medium  
**Description**: Add comprehensive logging for API errors.
- Log all exceptions with stack traces
- Log request context (user, endpoint, parameters)
- Configure log levels per environment (Debug for dev, Warning+ for prod)
- Consider structured logging (Serilog) for better searchability

#### 12. User Access Details Modal
**Priority**: Medium  
**Description**: On Admin page Users tab, "View Access" should open a modal showing the user's collection access.
- Display list of collections the user has access to
- Show role per collection (viewer, contributor, manager, admin)
- Show when access was granted (CreatedAt date)
- Include "Revoke Access" button per collection
- Quick navigation to collection

#### 13. Role Permissions Documentation
**Priority**: Low  
**Description**: Document the permission model for clarity.
- Clarify: Who can do what and when?
- Question: If a contributor uploads an image, who owns it? (Answer: The asset is owned by the collection, not the user. CreatedByUserId is tracked for audit purposes, but permissions are based on collection ACL, not asset ownership)
- Document in README or in-app help

#### 14. Add CancellationToken Support
**Priority**: Low  
**Description**: Add CancellationToken to repository methods and endpoints for proper request cancellation.
- Update IAssetRepository, ICollectionRepository methods
- Propagate CancellationToken through endpoint handlers
- Allows graceful cancellation of long-running operations

---

## Session Notes

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
