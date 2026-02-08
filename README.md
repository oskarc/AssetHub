# AssetHub

**A self-hosted digital asset management system for teams who want full control over their files.**

AssetHub lets you organise images, videos, and documents into hierarchical collections, control who sees what with per-collection roles, share files via password-protected links, and get automatic thumbnails and previews — all running on your own infrastructure.

Built with ASP.NET Core 9, Blazor Server, Keycloak, MinIO, and PostgreSQL. One `docker compose up` gets you a working system.

---

## What You Get

- **Hierarchical collections** — Nest folders as deep as you like. Assets can live in multiple collections at once.
- **Fine-grained access control** — Viewer, Contributor, Manager, and Admin roles assigned per collection. A user can be a viewer in one collection and a manager in another.
- **Drag-and-drop upload** — Multi-file upload with progress tracking. Thumbnails and previews are generated automatically in the background.
- **Share links** — Create time-limited, optionally password-protected public links for any asset or collection. No account needed to view.
- **Full-text search** — PostgreSQL trigram search across asset names, descriptions, and tags.
- **Admin dashboard** — Manage shares, collection access, and users. Create Keycloak users directly from the UI.
- **Localisation** — Swedish and English, switchable from the UI.
- **Audit trail** — Every upload, download, share, and access change is logged.

---

## Quick Start

You need **Docker Desktop** (or Docker Engine + Compose on Linux) and **Git**. That's it.

### 1. Add Keycloak to your hosts file

Append this line so token issuer URLs resolve correctly from both your browser and the server:

| OS | File |
|----|------|
| Windows | `C:\Windows\System32\drivers\etc\hosts` |
| Mac / Linux | `/etc/hosts` |

```
127.0.0.1 keycloak
```

### 2. Clone, configure, start

```bash
git clone <repository-url>
cd AssetHub
docker compose up --build
```

On first boot the app will automatically:
- Run database migrations and enable `pg_trgm` for search
- Create the MinIO storage bucket
- Import the Keycloak realm with test users

Wait until the health check passes (usually under 30 seconds), then open the app.

### 3. Open the app

| Service | URL | Credentials |
|---------|-----|-------------|
| **AssetHub** | http://localhost:7252 | See test users below |
| Keycloak Admin | http://keycloak:8080/admin | `admin` / `admin123` |
| MinIO Console | http://localhost:9001 | `minioadmin` / `minioadmin_dev_password` |
| Hangfire Dashboard | http://localhost:7252/hangfire | (no auth in dev) |

### 4. Log in

| User | Password | Role |
|------|----------|------|
| `mediaadmin` | `mediaadmin123` | Admin — full access, can manage users |
| `testuser` | `testuser123` | Viewer — read-only access to assigned collections |

Create a collection, upload some files, and explore. The `mediaadmin` account has access to everything; `testuser` needs to be granted access to specific collections.

---

## Architecture

```
┌─────────────────────────────────────────────────┐
│  Browser                                        │
│  (Blazor Server + MudBlazor)                    │
└──────────────┬──────────────────────────────────┘
               │ SignalR / HTTP
┌──────────────▼──────────────────────────────────┐
│  ASP.NET Core 9 — Minimal APIs                  │
│  ┌──────────┐ ┌────────────┐ ┌──────────────┐   │
│  │Endpoints │ │ Services   │ │ Repositories │   │
│  └──────────┘ └────────────┘ └──────────────┘   │
│  Cookie + JWT Bearer Auth (Keycloak OIDC)       │
│  Health checks: /health  /health/ready          │
└──┬──────────┬──────────┬────────────────────────┘
   │          │          │
┌──▼──┐  ┌───▼───┐  ┌───▼────┐  ┌───────────┐
│ PG  │  │ MinIO │  │Keycloak│  │ Hangfire  │
│ 16  │  │ (S3)  │  │ (OIDC) │  │ (worker)  │
└─────┘  └───────┘  └────────┘  └───────────┘
```

### Tech Stack

| Layer | Technology |
|-------|-----------|
| API | ASP.NET Core 9 (Minimal APIs) |
| UI | Blazor Server + MudBlazor v8 |
| Auth | Keycloak 24 (OIDC, dual Cookie + JWT Bearer) |
| Database | PostgreSQL 16 + EF Core (Npgsql) |
| Storage | MinIO (S3-compatible, presigned URLs) |
| Background jobs | Hangfire with PostgreSQL storage |
| Media processing | ImageMagick + ffmpeg (in container) |
| Deployment | Docker Compose |

### Project Structure

```
AssetHub/
├── Program.cs                          # DI, middleware, endpoints, health checks
├── Endpoints/                          # Minimal API route handlers
│   ├── AssetEndpoints.cs
│   ├── CollectionEndpoints.cs
│   ├── ShareEndpoints.cs
│   └── AdminEndpoints.cs
├── src/
│   ├── Dam.Domain/                     # Entities
│   ├── Dam.Application/                # DTOs, interfaces, validation, role hierarchy
│   ├── Dam.Infrastructure/             # EF Core, MinIO adapter, Keycloak, migrations
│   ├── Dam.Ui/                         # Blazor pages, components, localisation
│   └── Dam.Worker/                     # Hangfire background worker
├── tests/
│   └── Dam.Tests/                      # xUnit + Testcontainers integration tests
├── docs/
│   └── DEPLOYMENT.md                   # Production deployment guide
├── keycloak/import/                    # Realm auto-import
├── docker-compose.yml                  # Development stack
├── docker-compose.prod.yml             # Production stack
├── .env.template                       # Environment variable reference
├── Dockerfile / Dockerfile.Worker
└── appsettings.*.json
```

---

## Role-Based Access Control

Roles are assigned **per collection** through Access Control Lists. Higher roles inherit all lower permissions.

| Role | Can view | Can upload | Can edit | Can share | Can delete | Can manage access | Admin panel |
|------|----------|-----------|---------|----------|-----------|------------------|-------------|
| Viewer | ✅ | | | | | | |
| Contributor | ✅ | ✅ | ✅ | ✅ | | | |
| Manager | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | |
| Admin | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

**Key concepts:**
- A user can hold different roles on different collections.
- Assets inherit permissions from their collections — access is never per-asset.
- Assets can belong to multiple collections simultaneously (many-to-many, no primary).
- The role hierarchy is centralised in `RoleHierarchy.cs` for consistency across API and UI.

---

## API Reference

All endpoints require authentication unless marked *(public)*.

### Collections

```http
GET    /api/collections                           # Root collections
GET    /api/collections/{id}                      # Collection details
POST   /api/collections                           # Create root collection
POST   /api/collections/{id}/children             # Create child collection
PATCH  /api/collections/{id}                      # Rename / move collection
DELETE /api/collections/{id}                      # Delete (cascades to children)
GET    /api/collections/{id}/children             # List children
GET    /api/collections/{id}/download-all         # Zip download
```

### Collection ACLs

```http
GET    /api/collections/{id}/acl                  # List access entries
POST   /api/collections/{id}/acl                  # Grant role to user
DELETE /api/collections/{id}/acl/{type}/{id}      # Revoke access
```

### Assets

```http
GET    /api/assets                                # List (filtered by collection access)
GET    /api/assets/all                            # All assets (admin only)
GET    /api/assets/{id}                           # Asset details
POST   /api/assets                                # Upload (multipart form)
PATCH  /api/assets/{id}                           # Update name, description, tags
DELETE /api/assets/{id}                           # Delete asset + storage
GET    /api/assets/collection/{collectionId}      # Assets in collection
```

### Asset ↔ Collection

```http
GET    /api/assets/{id}/collections               # Collections containing asset
POST   /api/assets/{id}/collections/{collId}      # Add asset to collection
DELETE /api/assets/{id}/collections/{collId}      # Remove from collection
```

### Renditions

```http
GET    /api/assets/{id}/download                  # Original (presigned redirect)
GET    /api/assets/{id}/preview                   # Inline preview
GET    /api/assets/{id}/thumb                     # Thumbnail
GET    /api/assets/{id}/medium                    # Medium rendition
GET    /api/assets/{id}/poster                    # Video poster frame
```

### Shares

```http
POST   /api/shares                                # Create share link
DELETE /api/shares/{id}                           # Revoke share
PUT    /api/shares/{id}/password                  # Update password
GET    /api/shares/{token}                        # View shared content (public)
GET    /api/shares/{token}/download               # Download via share (public)
GET    /api/shares/{token}/download-all           # Zip download (public)
GET    /api/shares/{token}/preview                # Preview shared asset (public)
```

### Admin *(admin role required)*

```http
GET    /api/admin/shares                          # All shares
POST   /api/admin/shares/{id}/revoke              # Revoke any share
GET    /api/admin/collections/access              # All collection access
POST   /api/admin/collections/{id}/acl            # Grant access
DELETE /api/admin/collections/{id}/acl/{userId}   # Revoke access
GET    /api/admin/users                           # Users with access
GET    /api/admin/keycloak-users                  # All Keycloak users
POST   /api/admin/users                           # Create Keycloak user
```

### Health

```http
GET    /health                                    # Liveness (always 200)
GET    /health/ready                              # Readiness (checks PG, MinIO, Keycloak)
```

---

## Production Deployment

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for the full guide. The short version:

```bash
cp .env.template .env        # Fill in your passwords and domain
docker compose -f docker-compose.prod.yml up -d
curl http://localhost:7252/health/ready   # Wait for "Healthy"
```

The guide covers reverse proxy setup (Caddy / Nginx / Traefik), TLS, backup & restore, upgrades, and a security hardening checklist.

---

## Testing

The test suite lives in `tests/Dam.Tests/` and uses **real PostgreSQL** via Testcontainers — no in-memory fakes.

| Category | Tests | What's covered |
|----------|-------|----------------|
| Asset repository | 21 | CRUD, search (ILike, trigram), pagination, status filtering |
| Collection repository | 17 | Hierarchy, cascading deletes, ACL includes, root queries |
| Asset ↔ Collection repository | 12 | Many-to-many, duplicates, orphaning, batch lookups, caching |
| Collection ACL repository | 11 | Grant/revoke, update-in-place, user isolation |
| Share repository | 15 | Token hash lookup, increment access, scoped queries, navigation |
| Edge cases | 10 | Multi-collection assets, cascading deletes, hierarchical cleanup |
| **Total** | **86** | |

### Running tests

```bash
# Requires Docker running (for Testcontainers)
dotnet test tests/Dam.Tests/Dam.Tests.csproj
```

Each test class gets its own isolated database — tests never interfere with each other.

---

## Development

### Prerequisites for local development (outside Docker)

- .NET 9 SDK
- PostgreSQL 16 running locally
- MinIO running locally
- Keycloak running locally with the `media` realm imported

### Build and run

```bash
dotnet restore
dotnet build          # 0 errors, 0 warnings
dotnet run --project AssetHub.csproj
```

### Useful commands

```bash
# Follow logs
docker compose logs -f

# Database shell
docker exec -it assethub-postgres psql -U postgres -d assethub

# Rebuild everything
docker compose down && docker compose up --build

# Backup database
docker exec assethub-postgres pg_dump -U postgres assethub > backup.sql

# Restore database
docker exec -i assethub-postgres psql -U postgres assethub < backup.sql
```

---

## Monitoring

| Tool | URL | Purpose |
|------|-----|---------|
| Health check | http://localhost:7252/health/ready | Readiness probe (PG + MinIO + Keycloak) |
| Hangfire | http://localhost:7252/hangfire | Job queues, processing status, failure history |
| Keycloak Admin | http://keycloak:8080/admin | Users, realms, clients, sessions |
| MinIO Console | http://localhost:9001 | Buckets, objects, storage usage |

---

## Troubleshooting

**App won't start?**
Check the logs: `docker compose logs assethub-api`. Common causes: PostgreSQL not ready yet (the app retries on startup), incorrect connection string, port conflict.

**Can't log in?**
Make sure `127.0.0.1 keycloak` is in your hosts file. Without it, the token issuer URL won't match and auth will silently fail. Check Keycloak is running: `docker compose logs keycloak`.

**Uploads fail?**
Verify MinIO is healthy via the console at http://localhost:9001. The app creates the bucket automatically on startup — check logs for errors if it didn't.

**Health check failing?**
Hit `/health/ready` to see which dependency is down. The response body names the unhealthy component.

For production troubleshooting, see [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md#8-troubleshooting).

---

## Credentials

See [CREDENTIALS.md](CREDENTIALS.md) for all default passwords, OAuth client config, and connection strings.

---

## Project Status

**MVP is complete.** All core features are implemented and operational. The codebase builds with 0 errors and 0 warnings.

See [IMPLEMENTATION_PLAN_V2.md](instructions%20and%20docs/IMPLEMENTATION_PLAN_V2.md) for remaining work — frontend testing, metrics/observability, and optional enhancements.

### What's done

- Docker Compose development and production stacks
- Full collections API with hierarchical support and cascading deletes
- Keycloak OIDC authentication (Cookie + JWT Bearer)
- Asset upload with automatic thumbnail/preview generation
- Video metadata extraction and poster frames
- Share links with BCrypt passwords and expiration
- Complete Blazor Server UI (13+ components, two languages)
- Admin dashboard (shares, access, user management)
- User creation via Keycloak Admin API
- 86 integration tests with real PostgreSQL
- One-click production deployment with auto-migration
- Health check endpoints (`/health`, `/health/ready`)
- Full deployment documentation

### What's next

- Frontend testing (bUnit component tests, Playwright E2E)
- Metrics & observability (OpenTelemetry, structured logging, dashboards)
- API integration tests (endpoint-level testing with `WebApplicationFactory`)
- Document preview (PDF, PPTX)
- Video transcoding (HLS/DASH)

---

## Contributing

1. Create a feature branch: `git checkout -b feature/your-feature`
2. Make your changes and ensure the build is clean: `dotnet build`
3. Run the tests: `dotnet test`
4. Push and open a Pull Request

### Code conventions

- C# naming conventions (PascalCase for public members)
- Follow existing patterns in the `Endpoints/` files for new routes
- Use `IUserFeedbackService` for all user-facing messages in the UI
- Add localisation keys to `.resx` files for any new text
- Keep `RoleHierarchy.cs` as the single source of truth for permission checks

---

## License

MIT — see LICENSE for details.

---

*Last updated: February 9, 2026*
