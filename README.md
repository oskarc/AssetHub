# AssetHub

**A modular, self-hosted digital asset management system built for teams who need enterprise features without vendor lock-in.**

AssetHub lets you organise images, videos, and documents into collections, control who sees what with per-collection roles, share files via password-protected links, and get automatic thumbnails and previews — all running on your own infrastructure with fully replaceable components.

Built with ASP.NET Core 9, Blazor Server, and a pluggable architecture. Swap out authentication, storage, database, or any service layer without changing application code.

---

## Who Is This For?

| Audience | Why AssetHub? |
|----------|---------------|
| **IT Teams** | Full control over infrastructure. No SaaS dependency, no per-seat licensing, no data leaving your network. |
| **Developers** | Clean separation of concerns with interface-driven services. Replace any component by implementing standard interfaces. |
| **Enterprise Architects** | Integrate with existing AD/LDAP via Keycloak federation. Connect to corporate S3 or Azure Blob. Use your existing PostgreSQL cluster. |
| **Small Teams** | One `docker compose up` gets you running. Zero configuration needed for evaluation. |
| **Security-Conscious Orgs** | Audit trail for every action. ClamAV malware scanning. Role-based access control. Self-hosted = your data never leaves your premises. |

---

## Features

- **Flat collections** — Simple folder structure. Assets can live in multiple collections simultaneously.
- **Fine-grained access control** — Viewer, Contributor, Manager, Admin roles assigned per collection.
- **Drag-and-drop upload** — Multi-file upload with progress tracking. Presigned uploads bypass the API for large files.
- **Auto-generated renditions** — Thumbnails, medium previews, and video poster frames created automatically via background jobs.
- **Share links** — Time-limited, password-protected public links with signed access tokens. No account needed to view.
- **Zip downloads** — Download entire collections or shared content as a zip archive, built asynchronously in the background.
- **Full-text search** — PostgreSQL trigram search across names, descriptions, and tags.
- **Admin dashboard** — Manage shares, access, users, and audit logs. Create and sync identity provider users from the UI.
- **Localisation** — Swedish and English out of the box, extensible to any language via `.resx` resource files.
- **Audit trail** — Every upload, download, share, and access change logged with user and timestamp.
- **Malware scanning** — ClamAV integration scans uploads before processing. Disable or swap for corporate AV.
- **Video support** — Poster frame extraction, streaming previews, metadata parsing.
- **Smart deletion** — Multi-collection-aware deletion with remove-from-collection / delete-from-storage options.
- **Copyright & metadata** — Store copyright info and tags per asset. Metadata extraction from uploaded files.
- **Health checks** — Liveness and readiness endpoints for container orchestration.
- **Email notifications** — Share-link emails via SMTP with customisable Keycloak email themes (HTML + text, Swedish/English).

---

## Quick Start

```bash
git clone <repository-url>
cd AssetHub

# Add hostnames to hosts file (required for OIDC same-site cookies)
# Windows: Add to C:\Windows\System32\drivers\etc\hosts
# Linux/Mac: Add to /etc/hosts
# 127.0.0.1 assethub.local keycloak.assethub.local

docker compose up --build
```

Open https://assethub.local:7252 and log in:

| User | Password | Role |
|------|----------|------|
| `mediaadmin` | `mediaadmin123` | Admin |
| `testuser` | `testuser123` | Viewer |

---

## Architecture Overview

AssetHub follows **Clean Architecture** with strict dependency rules: inner layers never reference outer layers. Every external service is abstracted behind an interface in the Application layer.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  PRESENTATION LAYER                                                         │
│  ┌─────────────────────┐  ┌─────────────────────────────────────────────┐   │
│  │  Blazor Server UI   │  │  REST API (Minimal APIs)                    │   │
│  │  (MudBlazor v8)     │  │  Cookie + JWT Bearer auth                   │   │
│  └──────────┬──────────┘  └──────────────────────┬──────────────────────┘   │
└─────────────┼────────────────────────────────────┼──────────────────────────┘
              │                                    │
┌─────────────▼────────────────────────────────────▼──────────────────────────┐
│  APPLICATION LAYER  (AssetHub.Application)                                   │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │ Asset        │ │ Collection   │ │ Share        │ │ Admin                ││
│  │ Services     │ │ Services     │ │ Services     │ │ Services             ││
│  └──────────────┘ └──────────────┘ └──────────────┘ └──────────────────────┘│
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │ IMinIOAdapter│ │ IEmailService│ │ IAuditService│ │ IKeycloakUserService ││
│  │ (storage)    │ │ (email)      │ │ (logging)    │ │ (identity)           ││
│  └──────────────┘ └──────────────┘ └──────────────┘ └──────────────────────┘│
│                        ▲ INTERFACES — SWAP IMPLEMENTATIONS ▲                 │
└────────────────────────┼────────────────────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────────────────────┐
│  INFRASTRUCTURE LAYER  (AssetHub.Infrastructure)                             │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │ MinIOAdapter │ │ SmtpEmail    │ │ AuditService │ │ KeycloakUserService  ││
│  │              │ │ Service      │ │ (PostgreSQL) │ │                      ││
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘ └──────────┬───────────┘│
└─────────┼────────────────┼────────────────┼────────────────────┼────────────┘
          │                │                │                    │
┌─────────▼────────────────▼────────────────▼────────────────────▼────────────┐
│  EXTERNAL SERVICES (Containers / Corporate Infrastructure)                  │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │    MinIO     │ │   Mailpit    │ │  PostgreSQL  │ │      Keycloak        ││
│  │   (S3 API)   │ │   (SMTP)     │ │     16       │ │       (OIDC)         ││
│  └──────────────┘ └──────────────┘ └──────────────┘ └──────────────────────┘│
│  ┌──────────────┐ ┌──────────────────────────────────┐                      │
│  │    ClamAV    │ │  Hangfire Worker (separate proc)  │                      │
│  │  (malware)   │ │  ImageMagick + ffmpeg              │                      │
│  └──────────────┘ └──────────────────────────────────┘                      │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Project Structure

The solution is split into six projects following Clean Architecture, plus three test projects:

```
AssetHub.sln
│
├── src/
│   ├── AssetHub.Domain/            # Entities, enums, value objects — zero dependencies
│   ├── AssetHub.Application/       # Service interfaces, DTOs, business rules
│   ├── AssetHub.Infrastructure/    # EF Core, MinIO, SMTP, ClamAV, Keycloak implementations
│   ├── AssetHub.Api/               # ASP.NET Core host — Minimal API endpoints, auth, DI wiring
│   ├── AssetHub.Ui/                # Blazor Server components, pages, layouts (Razor Class Library)
│   └── AssetHub.Worker/            # Hangfire background job processor (separate container)
│
├── tests/
│   ├── AssetHub.Tests/             # Integration + unit tests (xUnit, Testcontainers, Moq)
│   ├── AssetHub.Ui.Tests/          # Blazor component tests (bUnit)
│   └── E2E/                        # End-to-end tests (Playwright, TypeScript)
│
├── docker/                         # Dockerfiles, compose files (dev + prod), init scripts
├── keycloak/                       # Realm import JSON, custom email themes (sv/en)
├── certs/                          # TLS certificates (dev: self-signed, prod: CA-issued)
└── docs/                           # Deployment guide, security audit, application audit
```

### Layer Dependencies

```
Domain  ←  Application  ←  Infrastructure  ←  Api / Worker
                                                  ↑
                                                 Ui (Razor Class Library)
```

Each layer only depends on the layers to its left. The Api and Worker projects are the composition roots that wire up dependency injection.

---

## Modular Components — What You Can Replace

AssetHub is designed with clean interfaces so you can swap components to match your corporate infrastructure. Every external dependency has an abstraction layer.

### Identity & Authentication

| Default | Interface | Corporate Alternatives |
|---------|-----------|----------------------|
| **Keycloak** (OIDC) | Standard OIDC/OAuth 2.0 | Azure AD, Okta, Auth0, AWS Cognito, PingIdentity |

**How to replace:**
- AssetHub uses standard ASP.NET Core OIDC authentication
- Change `Keycloak:Authority` to your IdP's issuer URL
- Configure `ClientId` and `ClientSecret` for your OIDC application
- Keycloak supports AD/LDAP federation if you want to keep Keycloak as a broker

**Relevant interfaces:**
- `IKeycloakUserService` — User provisioning and lookup (Admin API calls)
- `IUserLookupService` — User search and enumeration
- `IUserSyncService` — Sync users from external sources

```csharp
// Replace KeycloakUserService with your implementation
services.AddScoped<IKeycloakUserService, AzureADUserService>();
```

---

### Object Storage

| Default | Interface | Corporate Alternatives |
|---------|-----------|----------------------|
| **MinIO** (S3 API) | `IMinIOAdapter` | AWS S3, Azure Blob Storage, Google Cloud Storage, NetApp StorageGRID |

**How to replace:**
- Implement `IMinIOAdapter` for your storage backend
- The interface covers: upload, download, presigned URLs, delete, bucket creation
- All file operations go through this single interface

**Key methods:**
```csharp
public interface IMinIOAdapter
{
    Task<string> UploadFileAsync(string objectName, Stream data, string contentType, CancellationToken ct);
    Task<Stream> DownloadFileAsync(string objectName, CancellationToken ct);
    Task<string> GetPresignedUrlAsync(string objectName, int expirySeconds, CancellationToken ct);
    Task DeleteFileAsync(string objectName, CancellationToken ct);
    Task EnsureBucketExistsAsync(CancellationToken ct);
}
```

**Why this matters:**
- Presigned URLs enable direct browser downloads without proxying through the app
- The `PublicUrl` configuration allows internal vs external endpoint separation
- Zero code changes needed — just swap the DI registration

---

### Database

| Default | Interface | Corporate Alternatives |
|---------|-----------|----------------------|
| **PostgreSQL 16** | Entity Framework Core | SQL Server, Oracle, MySQL (with provider change) |

**How to replace:**
- AssetHub uses EF Core with Npgsql provider
- Swap `UseNpgsql()` for `UseSqlServer()`, `UseOracle()`, etc.
- Full-text search uses `pg_trgm` — you'll need equivalent functionality in your DB
- Migrations are code-first and auto-applied on startup

**Considerations:**
- PostgreSQL's trigram search is used for fuzzy matching — SQL Server has `CONTAINS()`, Oracle has Oracle Text
- The audit log table grows with usage — ensure your DBA is aware

---

### Email

| Default | Interface | Corporate Alternatives |
|---------|-----------|----------------------|
| **SMTP (Mailpit for dev)** | `IEmailService` | SendGrid, AWS SES, Exchange Online, Mailgun |

**How to replace:**
```csharp
// Current implementation
services.AddScoped<IEmailService, SmtpEmailService>();

// Replace with your corporate email service
services.AddScoped<IEmailService, SendGridEmailService>();
services.AddScoped<IEmailService, ExchangeOnlineEmailService>();
```

**Interface:**
```csharp
public interface IEmailService
{
    Task SendShareNotificationAsync(string recipientEmail, string shareUrl, string senderName, CancellationToken ct);
    Task SendPasswordResetAsync(string email, string resetUrl, CancellationToken ct);
}
```

---

### Malware Scanning

| Default | Interface | Corporate Alternatives |
|---------|-----------|----------------------|
| **ClamAV** | `IMalwareScannerService` | Windows Defender ATP, CrowdStrike, Carbon Black, Symantec |

**How to replace:**
```csharp
public interface IMalwareScannerService
{
    Task<ScanResult> ScanAsync(Stream fileStream, string fileName, CancellationToken ct);
    bool IsEnabled { get; }
}
```

**Configuration:**
```json
{
  "ClamAV": {
    "Enabled": true,
    "Host": "clamav",
    "Port": 3310
  }
}
```

Set `Enabled: false` to disable scanning entirely, or implement your own scanner.

---

### Background Jobs

| Default | Interface | Corporate Alternatives |
|---------|-----------|----------------------|
| **Hangfire + PostgreSQL** | Hangfire abstraction | Azure Service Bus, AWS SQS, RabbitMQ |

**Current jobs:**
- Thumbnail generation (ImageMagick)
- Medium rendition generation
- Video poster extraction (ffmpeg)
- Malware scanning
- Email notifications
- Zip archive building

The Worker runs as a **separate container** (`AssetHub.Worker`) so it can be scaled independently from the API. It shares the same Infrastructure layer but has its own Dockerfile with ImageMagick and ffmpeg pre-installed.

**How to replace:**
- Hangfire is deeply integrated but uses standard job patterns
- For enterprise message queues, wrap queue consumers in Hangfire jobs, or replace Hangfire entirely with MassTransit/NServiceBus

---

### Media Processing

| Default | Tools | Corporate Alternatives |
|---------|-------|----------------------|
| **ImageMagick + ffmpeg** | `IMediaProcessingService` | Cloud-based: AWS MediaConvert, Azure Media Services |

**How to replace:**
```csharp
public interface IMediaProcessingService
{
    Task<ProcessingResult> GenerateThumbnailAsync(string sourcePath, string outputPath, int maxWidth, int maxHeight, CancellationToken ct);
    Task<ProcessingResult> GenerateVideoPreviewAsync(string sourcePath, string outputPath, CancellationToken ct);
    Task<VideoMetadata> ExtractVideoMetadataAsync(string sourcePath, CancellationToken ct);
}
```

**Considerations:**
- ImageMagick and ffmpeg run in the Worker container with a restrictive security policy (SVG/MVG/MSL processing disabled)
- For cloud processing, implement the interface to upload, process, and download
- The current implementation is synchronous — cloud services may need async polling

---

## Container Reference

| Container | Purpose | Port | Replaceable With |
|-----------|---------|------|------------------|
| `assethub-api` | ASP.NET Core API + Blazor UI | 7252 | — (core application) |
| `assethub-worker` | Hangfire background processor | — | Azure Functions, AWS Lambda |
| `assethub-postgres` | Primary database | 5432 | SQL Server, Oracle, managed PostgreSQL |
| `assethub-minio` | S3-compatible object storage | 9000/9001 | AWS S3, Azure Blob, GCS |
| `assethub-keycloak` | OIDC identity provider | 8080/8443 | Azure AD, Okta, Auth0 |
| `assethub-clamav` | Malware scanning | 3310 | Enterprise AV, Windows Defender ATP |
| `assethub-mailpit` | Dev email capture | 8025/1025 | SMTP relay, SendGrid, SES |

### Minimal Production Stack

For a minimal deployment using corporate infrastructure:

```yaml
# docker-compose.minimal.yml — API only, external services
services:
  api:
    image: assethub:latest
    environment:
      # Use your corporate PostgreSQL
      ConnectionStrings__Postgres: "Server=db.corp.com;Database=assethub;..."
      # Use Azure AD instead of Keycloak
      Keycloak__Authority: "https://login.microsoftonline.com/{tenant}/v2.0"
      # Use AWS S3 instead of MinIO
      MinIO__Endpoint: "s3.amazonaws.com"
      MinIO__AccessKey: "${AWS_ACCESS_KEY}"
      # Disable ClamAV, use corporate AV
      ClamAV__Enabled: "false"
```

---

## Service Interface Reference

| Service | Interface | Default Implementation | Purpose |
|---------|-----------|----------------------|---------|
| Object Storage | `IMinIOAdapter` | `MinIOAdapter` | File upload, download, presigned URLs |
| Email | `IEmailService` | `SmtpEmailService` | Notifications, share emails |
| Malware Scan | `IMalwareScannerService` | `ClamAvScannerService` | Upload scanning |
| Media | `IMediaProcessingService` | `MediaProcessingService` | Thumbnails, video processing |
| Users | `IKeycloakUserService` | `KeycloakUserService` | User provisioning, lookup |
| User Sync | `IUserSyncService` | `UserSyncService` | Directory synchronisation |
| Audit | `IAuditService` | `AuditService` | Action logging |

---

## Role-Based Access Control

Roles are assigned **per collection** through Access Control Lists. Higher roles inherit all lower permissions.

| Role | Can view | Can upload | Can edit | Can share | Can delete | Can manage access | Admin panel |
|------|----------|-----------|---------|----------|-----------|------------------|-------------|
| Viewer | Yes | | | | | | |
| Contributor | Yes | Yes | Yes | Yes | | | |
| Manager | Yes | Yes | Yes | Yes | Yes | Yes | |
| Admin | Yes | Yes | Yes | Yes | Yes | Yes | Yes |

**Key concepts:**
- A user can hold different roles on different collections
- Assets inherit permissions from their collections — access is never per-asset
- Assets can belong to multiple collections simultaneously (many-to-many)
- The role hierarchy is centralised in `RoleHierarchy.cs` for consistency

---

## API Reference

All endpoints require authentication unless marked *(public)*.

### Collections

```http
GET    /api/collections                           # List all collections
GET    /api/collections/{id}                      # Collection details
POST   /api/collections                           # Create collection
PATCH  /api/collections/{id}                      # Rename collection
DELETE /api/collections/{id}                      # Delete collection
POST   /api/collections/{id}/download-all         # Zip download all assets
```

### Collection ACLs

```http
GET    /api/collections/{id}/acl                         # List access entries
POST   /api/collections/{id}/acl                         # Grant role to user
DELETE /api/collections/{id}/acl/{principalType}/{principalId}  # Revoke access
GET    /api/collections/{id}/acl/users/search            # Search users for ACL
```

### Assets

```http
GET    /api/assets                                # List all assets (admin only)
GET    /api/assets/all                            # All assets (admin only)
GET    /api/assets/{id}                           # Asset details
POST   /api/assets                                # Upload (multipart form)
POST   /api/assets/init-upload                    # Init presigned upload
POST   /api/assets/{id}/confirm-upload            # Confirm upload completed
PATCH  /api/assets/{id}                           # Update name, description, tags
DELETE /api/assets/{id}                           # Delete asset + storage
GET    /api/assets/collection/{collectionId}      # Assets in collection
GET    /api/assets/{id}/deletion-context          # Get deletion impact info
```

### Asset Collections

```http
GET    /api/assets/{id}/collections               # Collections containing asset
POST   /api/assets/{id}/collections/{collId}      # Add asset to collection
DELETE /api/assets/{id}/collections/{collId}      # Remove from collection
```

### Renditions

```http
GET    /api/assets/{id}/download                  # Original file (presigned redirect)
GET    /api/assets/{id}/preview                   # Original inline preview
GET    /api/assets/{id}/thumb                     # Thumbnail preview
GET    /api/assets/{id}/thumb/download            # Thumbnail download
GET    /api/assets/{id}/medium                    # Medium rendition preview
GET    /api/assets/{id}/medium/download           # Medium rendition download
GET    /api/assets/{id}/poster                    # Video poster frame
```

### Shares

```http
POST   /api/shares                                # Create share link
DELETE /api/shares/{id}                           # Revoke share
PUT    /api/shares/{id}/password                  # Update password
GET    /api/shares/{token}                        # View shared content (public)
POST   /api/shares/{token}/access-token           # Get signed access token (public)
GET    /api/shares/{token}/download               # Download via share (public)
POST   /api/shares/{token}/download-all           # Zip download (public)
GET    /api/shares/{token}/preview                # Preview shared asset (public)
```

### Admin *(admin role required)*

```http
GET    /api/admin/shares                          # All shares
GET    /api/admin/shares/{id}/token               # Get share token
DELETE /api/admin/shares/{id}                     # Revoke share
GET    /api/admin/collections/access              # All collection access
POST   /api/admin/collections/{id}/acl            # Grant access
DELETE /api/admin/collections/{id}/acl/{userId}   # Revoke access
GET    /api/admin/users                           # Users with access
GET    /api/admin/keycloak-users                  # All Keycloak users
POST   /api/admin/users                           # Create user
POST   /api/admin/users/{userId}/reset-password   # Reset password
POST   /api/admin/users/sync                      # Sync deleted users
DELETE /api/admin/users/{userId}                  # Delete user
GET    /api/admin/audit                           # Audit log
```

### Dashboard

```http
GET    /api/dashboard                             # Dashboard metrics
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
docker compose -f docker/docker-compose.prod.yml up -d
curl http://localhost:7252/health/ready   # Wait for "Healthy"
```

The production compose file includes:
- Resource memory limits per container (512 MB - 1 GB)
- Internal-only networking (no ports exposed except API on localhost)
- `restart: unless-stopped` for all services
- Health checks with start periods for slow-starting services (ClamAV, Keycloak)

The deployment guide covers reverse proxy setup (Caddy / Nginx / Traefik), TLS certificate management, backup & restore procedures, upgrade & rollback steps, and a security hardening checklist.

---

## CI/CD

GitHub Actions runs on every push and pull request to `main` and `develop`:

| Job | What it does |
|-----|-------------|
| **build-and-test** | Restore, build (Release), run all .NET tests with code coverage (Cobertura) |
| **security-audit** | `dotnet list package --vulnerable` — fails the build on known CVEs |
| **docker-build** | Builds API + Worker images, scans with Trivy for CRITICAL/HIGH OS and library vulnerabilities (main branch only) |

---

## Testing

AssetHub has comprehensive test coverage across three layers:

### Integration Tests (Backend)

```bash
# Requires Docker running (for Testcontainers)
dotnet test
```

Uses **real PostgreSQL** via Testcontainers — no in-memory fakes. 334+ tests covering:
- Repository CRUD operations
- Service layer business logic
- API endpoint authorization
- Edge cases and negative paths

### Component Tests (Frontend)

```bash
dotnet test tests/AssetHub.Ui.Tests/
```

221+ bUnit tests covering all Blazor components, dialogs, and service clients.

### E2E Tests (Playwright)

```bash
cd tests/E2E
npm install
npx playwright install chromium
npm test
```

173+ tests across 15 spec files covering every feature:
- Authentication flows (Keycloak OIDC)
- Collection and asset CRUD
- Share creation, password protection, public access
- Admin panel operations and user management
- ACL and role-based restrictions
- Responsive design and accessibility
- Localisation (Swedish/English)

---

## Development

### Prerequisites (local development outside Docker)

- .NET 9 SDK
- PostgreSQL 16
- MinIO
- Keycloak with the `media` realm

### Build and run

```bash
dotnet restore
dotnet build
dotnet run --project src/AssetHub.Api/AssetHub.Api.csproj
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
```

---

## Monitoring

| Tool | URL | Purpose |
|------|-----|---------|
| Health check | https://assethub.local:7252/health/ready | Readiness probe (PG + MinIO + Keycloak) |
| Hangfire | https://assethub.local:7252/hangfire | Job queues, processing status |
| Keycloak Admin | https://keycloak.assethub.local:8443/admin | Users, sessions, clients |
| MinIO Console | http://localhost:9001 | Storage usage, buckets |
| Mailpit | http://localhost:8025 | Email capture (dev only) |

---

## Troubleshooting

| Symptom | Solution |
|---------|----------|
| App won't start | Check `docker compose logs assethub-api`. Usually PostgreSQL or Keycloak not ready yet. |
| Can't log in | Add `127.0.0.1 assethub.local keycloak.assethub.local` to hosts file. Token issuer must match. |
| Uploads fail | Check MinIO console at http://localhost:9001. Bucket should be auto-created. |
| Health check fails | Hit `/health/ready` to see which dependency is down. |
| Certificate errors | Trust the self-signed certificate in your OS certificate store. See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md). |
| ClamAV slow to start | First boot downloads virus definitions (2-5 min). Health check has a 5-minute start period. |
| Thumbnails not generating | Check Worker logs: `docker compose logs assethub-worker`. ImageMagick/ffmpeg errors will appear there. |

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md#troubleshooting) for production troubleshooting.

---

## Credentials

See [CREDENTIALS.md](CREDENTIALS.md) for all default passwords, OAuth config, and connection strings.

---

## Project Status

**Production-ready.** All core features implemented and tested. The codebase builds with 0 errors and 0 warnings.

### Implemented

- Flat collections with multi-collection asset support
- OIDC authentication (Keycloak, but swappable for Azure AD, Okta, etc.)
- Asset upload with presigned URLs and auto-generated thumbnails/previews
- Video support (poster frames, metadata extraction, streaming previews)
- Password-protected share links with expiration and signed access tokens
- Zip download for collections and shared content
- Full Blazor Server UI with Swedish/English localisation
- Admin dashboard (shares, ACLs, user management, audit log)
- ClamAV malware scanning on uploads
- Smart asset deletion (multi-collection aware with remove/delete options)
- User management (create, sync, delete) via Keycloak Admin API
- Structured logging (Serilog with request enrichment)
- Health check endpoints (`/health`, `/health/ready`)
- Custom Keycloak email themes (HTML + text, Swedish/English)
- CI pipeline with build, test, vulnerability scanning, and container image scanning
- Comprehensive test coverage (334+ backend + 221+ component + 173+ E2E)
- Production Docker Compose with auto-migration, resource limits, and TLS

### Roadmap

- Observability (Prometheus metrics, Grafana dashboards)
- Document preview (PDF, Office documents)
- Video transcoding (HLS/DASH adaptive streaming)
- Group-based ACLs (Keycloak groups/roles)

---

## Contributing

1. Create a feature branch: `git checkout -b feature/your-feature`
2. Ensure clean build: `dotnet build`
3. Run tests: `dotnet test`
4. Run E2E tests: `cd tests/E2E && npm test`
5. Open a Pull Request

### Code conventions

- PascalCase for public members
- Interface-driven services (add to `AssetHub.Application/Services/`)
- Localisation keys in `.resx` files for user-facing text
- `RoleHierarchy.cs` is the single source of truth for permissions

---

## License

MIT — see LICENSE for details.
