# AssetHub - Digital Asset Management System

A modern, self-hosted digital asset management (DAM) system built with ASP.NET Core 9, Blazor Server, Keycloak, and MinIO. Features hierarchical collections, many-to-many asset-collection relationships, role-based access control, automated media processing, public share links, multi-language UI, and a full admin interface.

## Features

### Core
- **Hierarchical Collections** — Nested folders with parent-child relationships
- **Multi-Collection Assets** — Assets belong to one or more collections (many-to-many, no primary/secondary distinction)
- **Role-Based Access Control** — Per-collection permissions: viewer / contributor / manager / admin
- **Multi-File Upload** — Drag-and-drop with progress tracking and automatic background processing
- **Image Processing** — Automatic thumbnail and medium rendition generation (ImageMagick)
- **Video Support** — Metadata extraction and poster frame generation (ffmpeg)
- **Share Links** — Time-limited public tokens with optional password protection (BCrypt hashed)
- **Audit Logging** — Tracks uploads, shares, downloads, and access changes
- **Full-Text Search** — PostgreSQL trigram search on asset metadata
- **Localization** — Full Swedish and English UI with language switcher

### UI
- **Blazor Server + MudBlazor** — Responsive asset browser with grid view, filters, and sorting
- **Login Gate** — Authentication required before any content is visible
- **Asset Detail** — Preview, metadata editing, tag management, collection membership display
- **All Assets View** — Admin-only page to browse all assets across collections
- **Admin Dashboard** — Three tabs: Shares management, Collection Access, User management
- **User Access Modal** — View and revoke per-collection access for any user
- **Create User** — Create Keycloak users directly from the admin UI
- **Role-Based Visibility** — UI elements hidden/shown based on the user's role
- **Empty States** — Friendly messages with suggested actions when lists are empty
- **Error Handling** — Consistent toast notifications via `IUserFeedbackService`; no technical details exposed to users

### Technical Highlights
- **Authentication**: OIDC via Keycloak (dual Cookie + JWT Bearer scheme)
- **Async Processing**: Hangfire background jobs with PostgreSQL storage
- **Object Storage**: S3-compatible MinIO with presigned URLs for direct browser downloads
- **Clean Architecture**: Domain / Application / Infrastructure / UI layer separation
- **CancellationToken Support**: Propagated through endpoints, services, and repositories
- **Centralized Role Logic**: `RoleHierarchy.cs` for consistent permission checks across API and UI

## Tech Stack

| Component | Technology |
|-----------|-----------|
| API | ASP.NET Core 9 (Minimal APIs) |
| Frontend | Blazor Server + MudBlazor |
| Auth | Keycloak 24.0.1 (OIDC) |
| Database | PostgreSQL 16 (EF Core) |
| Storage | MinIO (S3-compatible) |
| Background Jobs | Hangfire (PostgreSQL storage) |
| Media Tools | ImageMagick, ffmpeg (CLI in container) |
| Deployment | Docker Compose |

## Prerequisites

- **Docker Desktop** (Windows/Mac) or Docker Engine + Docker Compose (Linux)
- **Git**
- **.NET 9 SDK** (optional, only if running outside Docker)
- **Available ports**: 7252 (App), 8080 (Keycloak), 5432 (PostgreSQL), 9000/9001 (MinIO)
- **Hosts file entry**: `127.0.0.1 keycloak` (see Quick Start)

## Quick Start

### 1. Add Keycloak to Hosts File

Add this line to your hosts file:
- **Windows**: `C:\Windows\System32\drivers\etc\hosts`
- **Mac/Linux**: `/etc/hosts`

```
127.0.0.1 keycloak
```

This ensures JWT tokens issued by Keycloak have the correct issuer URL for both browser and server-side validation.

### 2. Clone and Navigate
```bash
git clone <repository-url>
cd AssetHub
```

### 3. Start All Services
```bash
docker compose up --build
```

This starts:
- **PostgreSQL 16** on `localhost:5432`
- **MinIO** on `localhost:9000` (API) / `localhost:9001` (Console)
- **Keycloak** on `keycloak:8080`
- **AssetHub App** on `localhost:7252`
- **Hangfire Worker** for background job processing

### 4. Access the Application

| Service | URL | Credentials |
|---------|-----|-------------|
| App | http://localhost:7252 | See test users below |
| Keycloak Admin | http://keycloak:8080/admin | admin / admin123 |
| MinIO Console | http://localhost:9001 | minioadmin / minioadmin_dev_password |
| Hangfire Dashboard | http://localhost:7252/hangfire | — |

### 5. Log In

| User | Password | Role |
|------|----------|------|
| `testuser` | `testuser123` | Viewer |
| `mediaadmin` | `mediaadmin123` | Admin |

## API Endpoints

### Collections — `/api/collections`
```
GET    /api/collections                    # List root collections
GET    /api/collections/{id}               # Get collection details
POST   /api/collections                    # Create root collection
POST   /api/collections/{id}/children      # Create child collection
PATCH  /api/collections/{id}               # Update collection
DELETE /api/collections/{id}               # Delete collection
GET    /api/collections/{id}/children      # List child collections
GET    /api/collections/{id}/download-all  # Download all assets as zip
```

### Collection ACLs — `/api/collections/{id}/acl`
```
GET    /api/collections/{id}/acl                          # List ACL entries
POST   /api/collections/{id}/acl                          # Assign role to user
DELETE /api/collections/{id}/acl/{principalType}/{principalId}  # Revoke access
```

### Assets — `/api/assets`
```
GET    /api/assets                                  # List assets (filtered)
GET    /api/assets/all                              # List all assets (admin only)
GET    /api/assets/{id}                             # Get asset details
POST   /api/assets                                  # Upload asset (multipart form)
PATCH  /api/assets/{id}                             # Update metadata (name, description, tags)
DELETE /api/assets/{id}                             # Delete asset
GET    /api/assets/collection/{collectionId}        # Assets in a collection
```

### Asset Collections (many-to-many)
```
GET    /api/assets/{id}/collections                 # List collections for asset
POST   /api/assets/{id}/collections/{collectionId}  # Add asset to collection
DELETE /api/assets/{id}/collections/{collectionId}  # Remove asset from collection
```

### Asset Renditions
```
GET    /api/assets/{id}/download   # Download original (presigned URL redirect)
GET    /api/assets/{id}/preview    # Preview original (inline)
GET    /api/assets/{id}/thumb      # Thumbnail rendition
GET    /api/assets/{id}/medium     # Medium rendition
GET    /api/assets/{id}/poster     # Video poster frame
```

### Shares — `/api/shares`
```
POST   /api/shares                        # Create share token (contributor+ required)
DELETE /api/shares/{id}                   # Revoke share
PUT    /api/shares/{id}/password          # Update share password
GET    /api/shares/{token}                # Get shared content (public)
GET    /api/shares/{token}/download       # Download via share link (public)
GET    /api/shares/{token}/download-all   # Download all shared assets (public)
GET    /api/shares/{token}/preview        # Preview shared asset (public)
```

### Admin — `/api/admin` (admin role required)
```
GET    /api/admin/shares                                    # List all shares
POST   /api/admin/shares/{id}/revoke                        # Revoke any share
GET    /api/admin/collections/access                        # List all collection access
POST   /api/admin/collections/{collectionId}/acl            # Grant collection access
DELETE /api/admin/collections/{collectionId}/acl/{principalId}  # Revoke access
GET    /api/admin/users                                     # List users with access
GET    /api/admin/keycloak-users                            # List all Keycloak users
POST   /api/admin/users                                     # Create new Keycloak user
```

## Authentication & Security

- **OIDC** via Keycloak (media realm)
- **Dual auth scheme**: Cookie (Blazor UI) + JWT Bearer (API clients)
- **Login gate**: All navigation and content hidden until authenticated
- **Share passwords**: BCrypt hashed
- **Authorization checks**: Every endpoint validates the user's role on the relevant collection(s)

### Auth Flow
1. Browser navigates to app → redirected to Keycloak login
2. Keycloak authenticates and returns tokens
3. Cookie auth persists the session for Blazor Server
4. API endpoints validate JWT Bearer tokens against Keycloak's public key
5. Claims (user ID, roles) extracted from token and used for authorization

## Role-Based Access Control (RBAC)

Roles are assigned per-collection through Access Control Lists (ACLs). Higher roles inherit all permissions of lower roles.

### Role Hierarchy

| Role | Level | Description |
|------|-------|-------------|
| **Viewer** | 1 | View and download assets |
| **Contributor** | 2 | Upload assets, edit metadata, create share links |
| **Manager** | 3 | Delete assets, manage collection ACLs |
| **Admin** | 4 | Full system access including admin dashboard |

### Permission Matrix

| Action | Viewer | Contributor | Manager | Admin |
|--------|--------|-------------|---------|-------|
| View / download assets | ✅ | ✅ | ✅ | ✅ |
| Upload assets | ❌ | ✅ | ✅ | ✅ |
| Edit asset metadata | ❌ | ✅ | ✅ | ✅ |
| Create share links | ❌ | ✅ | ✅ | ✅ |
| Delete assets | ❌ | ❌ | ✅ | ✅ |
| Manage collection access | ❌ | ❌ | ✅ | ✅ |
| All Assets page | ❌ | ❌ | ❌ | ✅ |
| Admin dashboard | ❌ | ❌ | ❌ | ✅ |
| Create users | ❌ | ❌ | ❌ | ✅ |

### Key Concepts

1. **Collection-scoped permissions** — A user may have different roles on different collections.
2. **Assets inherit collection permissions** — Access is determined by the collection's ACL, not by who uploaded the asset.
3. **No primary collection** — Assets have equal relationships with all their collections (many-to-many).
4. **Ownership is for audit only** — `CreatedByUserId` is tracked but does not affect permissions.

The role hierarchy is centralized in `Dam.Application/RoleHierarchy.cs`:

```csharp
RoleHierarchy.CanUpload(userRole);        // contributor+
RoleHierarchy.CanShare(userRole);         // contributor+
RoleHierarchy.CanDelete(userRole);        // manager+
RoleHierarchy.CanManageAccess(userRole);  // manager+
```

## Credentials & Configuration

See [CREDENTIALS.md](CREDENTIALS.md) for:
- Keycloak admin credentials
- Test user accounts
- MinIO access keys
- OAuth client configuration
- Connection strings

## Development

### Project Structure
```
AssetHub/
├── Program.cs                         # App configuration & DI
├── Endpoints/                         # Minimal API endpoints
│   ├── AssetEndpoints.cs
│   ├── CollectionEndpoints.cs
│   ├── ShareEndpoints.cs
│   └── AdminEndpoints.cs
├── src/
│   ├── Dam.Domain/                    # Entities, value objects
│   │   └── Entities/
│   ├── Dam.Application/               # DTOs, interfaces, services, RoleHierarchy
│   │   ├── Dtos/
│   │   ├── Repositories/
│   │   ├── Services/
│   │   └── Helpers/
│   ├── Dam.Infrastructure/            # EF Core, MinIO, Hangfire, migrations
│   │   ├── Data/
│   │   ├── Migrations/
│   │   ├── Repositories/
│   │   └── Services/
│   ├── Dam.Ui/                        # Blazor Server UI
│   │   ├── Pages/                     # Razor pages (Home, Assets, Admin, Share, etc.)
│   │   ├── Components/                # Reusable components (dialogs, grid, tree, upload)
│   │   ├── Layout/                    # MainLayout, NavMenu
│   │   ├── Services/                  # AssetHubApiClient, RolePermissions, UserFeedback
│   │   └── Resources/                 # Localization (.resx) files
│   └── Dam.Worker/                    # Hangfire background worker
├── keycloak/import/                   # Keycloak realm import file
├── docker-compose.yml
├── Dockerfile / Dockerfile.Worker
├── appsettings.*.json
└── instructions and docs/
    ├── IMPLEMENTATION_PLAN.md
    └── draft1.md
```

### Building Locally (without Docker)
```bash
dotnet restore
dotnet build
# Requires PostgreSQL, MinIO, and Keycloak running externally
dotnet run --project AssetHub.csproj
```

### Common Commands

```bash
# View logs
docker compose logs -f              # All services
docker logs assethub-api            # API only
docker logs assethub-worker         # Worker only

# Database access
docker exec -it assethub-postgres psql -U postgres -d assethub

# Rebuild and restart
docker compose down
docker compose up --build

# Scale workers
docker compose up -d --scale assethub-worker=3
```

### Database Backup & Restore
```bash
# Backup
docker exec assethub-postgres pg_dump -U postgres assethub > backup.sql

# Restore
docker exec -i assethub-postgres psql -U postgres assethub < backup.sql
```

## Monitoring

- **Hangfire Dashboard**: http://localhost:7252/hangfire — job queues, processing, failures, history
- **Keycloak Admin**: http://keycloak:8080/admin — users, realms, clients, roles
- **MinIO Console**: http://localhost:9001 — buckets, objects, storage usage

## Troubleshooting

### API won't start
```bash
docker logs assethub-api
docker exec assethub-postgres pg_isready
docker compose down && docker compose up --build
```

### Can't log in
- Verify `127.0.0.1 keycloak` is in your hosts file
- Check Keycloak is healthy: `docker logs assethub-keycloak`
- Verify `media` realm and test users exist in Keycloak admin console
- Check browser console (F12) for OIDC errors

### MinIO access issues
- Check bucket exists via MinIO Console at http://localhost:9001
- Verify credentials in appsettings match docker-compose environment

See [CREDENTIALS.md](CREDENTIALS.md) for additional troubleshooting.

## Project Status

### Completed
- [x] Docker Compose with all services (PostgreSQL, MinIO, Keycloak, Hangfire)
- [x] Database schema & EF Core migrations
- [x] Collections API — full CRUD with hierarchical support
- [x] Keycloak OIDC authentication (Cookie + JWT Bearer)
- [x] Asset upload, processing (ImageMagick thumbnails, ffmpeg poster frames)
- [x] Hangfire background job scheduling
- [x] Share links with password protection and expiration
- [x] Blazor Server UI — complete page set (Login, Home, Assets, AssetDetail, AllAssets, Admin, Share)
- [x] 13+ reusable Blazor components (grid, tree, dialogs, upload, language switcher)
- [x] Multi-collection asset assignment (many-to-many, no primary collection)
- [x] Asset metadata editing (name, description, tags)
- [x] Admin dashboard (shares, collection access, user management)
- [x] User creation via Keycloak Admin API
- [x] Role-based UI visibility
- [x] Error handling with `IUserFeedbackService`
- [x] Empty state messages across all views
- [x] Localization — Swedish and English with language switcher
- [x] CancellationToken propagation across endpoints and services
- [x] Centralized role hierarchy (`RoleHierarchy.cs`)
- [x] BCrypt password hashing for share links
- [x] Standardized API error responses (`ApiError`)

### Planned (Next Phase)
- [ ] Caching strategy (IMemoryCache / IDistributedCache for hot paths)
- [ ] Metrics & observability (OpenTelemetry, health checks, structured logging)
- [ ] Frontend testing (bUnit component tests, Playwright E2E)
- [ ] Deployment playbooks & onboarding guide (production Docker Compose, env templates, security hardening)
- [ ] Document preview (PDF/PPTX)
- [ ] Video transcoding (HLS/DASH)
- [ ] Advanced DLP / watermarking
- [ ] Mobile responsiveness

## Contributing

1. Create a feature branch: `git checkout -b feature/your-feature`
2. Make changes and commit: `git commit -am 'Add feature'`
3. Push: `git push origin feature/your-feature`
4. Open a Pull Request

### Code Style
- C# naming conventions (PascalCase for public members)
- Follow existing patterns in endpoint files for new API routes
- Use `IUserFeedbackService` for all user-facing error/success messages in UI
- Add localization keys to `.resx` files for any new user-facing text

## License

MIT License — See LICENSE file for details.

---

**Last Updated**: February 7, 2026  
**Status**: MVP Complete ✅ — All core features implemented and operational
