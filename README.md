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
## TLDR Features
┌─────────────────────────────────────────────────────────┐
│  🎯 Smart Collections    │  🔐 Fine-grained Access     │
│  Multi-collection assets │  Viewer/Contributor/Manager  │
├─────────────────────────────────────────────────────────┤
│  🔗 Secure Sharing       │  📦 Background Processing   │
│  Password + expiration   │  Thumbnails, previews, zips  │
├─────────────────────────────────────────────────────────┤
│  🛡️ Enterprise Security   │  🔧 Fully Modular          │
│  ClamAV, audit, RBAC     │  Swap S3, Auth, DB, Email    │
└─────────────────────────────────────────────────────────┘

---

## Features

- **Flat collections** — Simple folder structure. Assets can live in multiple collections simultaneously.
- **Fine-grained access control** — Viewer, Contributor, Manager, Admin roles assigned per collection.
- **Drag-and-drop upload** — Multi-file upload with progress tracking. Presigned uploads bypass the API for large files.
- **Auto-generated renditions** — Thumbnails, medium previews, and video poster frames created automatically via background jobs.
- **Share links** — Time-limited, password-protected public links with signed access tokens. No account needed to view.
- **Share lifecycle management** — Active, Expired, and Revoked statuses with admin filtering, password retrieval, and status-aware error codes.
- **Zip downloads** — Download entire collections or shared content as a zip archive, built asynchronously in the background.
- **Full-text search** — PostgreSQL trigram search across names, descriptions, and tags.
- **Admin dashboard** — Manage shares, access, users, and audit logs. Create and sync identity provider users from the UI.
- **Localisation** — Swedish and English out of the box, extensible to any language via `.resx` resource files.
- **Audit trail** — Every upload, download, share, and access change logged with user and timestamp.
- **Malware scanning** — ClamAV integration scans uploads before processing. Disable or swap for corporate AV.
- **Video support** — Poster frame extraction via ffmpeg, inline playback via the browser's native `<video>` element with presigned URLs.
- **Smart deletion** — Multi-collection-aware deletion with remove-from-collection / delete-from-storage options.
- **Copyright & metadata** — Store copyright info and tags per asset. EXIF, IPTC, and XMP metadata extracted automatically from images on upload.
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
│  HOSTS (Composition Roots)                                                  │
│  ┌─────────────────────────────────────────┐  ┌──────────────────────────┐  │
│  │  AssetHub.Api                           │  │  AssetHub.Worker         │  │
│  │  ┌───────────────┐ ┌─────────────────┐  │  │  Hangfire job processor  │  │
│  │  │ Blazor Server │ │ Minimal APIs    │  │  │  ImageMagick + ffmpeg   │  │
│  │  │ (MudBlazor 8) │ │ Smart auth:     │  │  │                        │  │
│  │  │               │ │ Cookie/JWT/OIDC │  │  │                        │  │
│  │  └───────────────┘ └─────────────────┘  │  └──────────────────────────┘  │
│  └─────────────────────────────────────────┘                                │
└─────────────────────────────────────────────────────────────────────────────┘
              │                                    │
┌─────────────▼────────────────────────────────────▼──────────────────────────┐
│  APPLICATION LAYER  (AssetHub.Application)                                   │
│                                                                              │
│  Service interfaces (26 interfaces):                                         │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │ Assets       │ │ Collections  │ │ Shares       │ │ Users                ││
│  │ Query,Upload │ │ CRUD, ACL,   │ │ Public,Auth, │ │ Admin, Lookup,       ││
│  │ Delete,Edit  │ │ Authorization│ │ Admin access │ │ Sync, Provision      ││
│  └──────────────┘ └──────────────┘ └──────────────┘ └──────────────────────┘│
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │ IMinIOAdapter│ │ IEmailService│ │ IMalware-    │ │ IKeycloakUserService ││
│  │ IMediaProc.  │ │ IAuditService│ │ ScannerSvc   │ │ IUserLookupService   ││
│  │ IZipBuildSvc │ │ IDashboardSvc│ │              │ │ IUserSyncService     ││
│  └──────────────┘ └──────────────┘ └──────────────┘ └──────────────────────┘│
│                        ▲ INTERFACES — SWAP IMPLEMENTATIONS ▲                 │
└────────────────────────┼────────────────────────────────────────────────────┘
              ┌──────────┘
│  DOMAIN (AssetHub.Domain) — Entities: Asset, Collection, CollectionAcl,      │
│  AssetCollection, Share, AuditEvent, ZipDownload + enums + value objects      │
└──────────────────────────────────────────────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────────────────────┐
│  INFRASTRUCTURE LAYER  (AssetHub.Infrastructure)                             │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │ MinIOAdapter │ │ SmtpEmail    │ │ ClamAv       │ │ KeycloakUser         ││
│  │ (dual client)│ │ Service      │ │ ScannerSvc   │ │ Service              ││
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘ └──────────┬───────────┘│
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐                         │
│  │ EF Core +    │ │ MediaProc.   │ │ Polly        │  All external calls     │
│  │ Repositories │ │ Service      │ │ Pipelines    │  wrapped in resilience  │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘  pipelines             │
└─────────┼────────────────┼────────────────┼─────────────────────────────────┘
          │                │                │
┌─────────▼────────────────▼────────────────▼─────────────────────────────────┐
│  EXTERNAL SERVICES (Docker containers)                                      │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │  PostgreSQL  │ │    MinIO     │ │   Keycloak   │ │      ClamAV          ││
│  │  16 (+ EF    │ │  (S3 API)   │ │  (OIDC +     │ │   (clamd TCP)        ││
│  │   + Hangfire)│ │              │ │  Admin API)  │ │                      ││
│  └──────────────┘ └──────────────┘ └──────────────┘ └──────────────────────┘│
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │   Mailpit    │ │    Jaeger    │ │  Prometheus  │ │      Grafana         ││
│  │ (SMTP, dev)  │ │  (OTLP)     │ │  (metrics)   │ │   (dashboards)       ││
│  └──────────────┘ └──────────────┘ └──────────────┘ └──────────────────────┘│
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
│   ├── AssetHub.Application/       # Service interfaces, DTOs, constants, config, business rules
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
├── docker/
│   ├── docker-compose.yml          # Development stack (all services, exposed ports)
│   ├── docker-compose.prod.yml     # Production stack (hardened, internal networking)
│   ├── Dockerfile                  # API multi-stage build
│   ├── Dockerfile.Worker           # Worker multi-stage build (includes ImageMagick + ffmpeg)
│   ├── imagemagick-policy.xml      # Restrictive ImageMagick security policy
│   ├── init-keycloak-db.sh         # Creates Keycloak database on first PostgreSQL start
│   ├── prometheus.yml              # Prometheus scrape targets
│   └── grafana/provisioning/       # Pre-configured Grafana datasources and dashboards
│
├── keycloak/
│   ├── import/media-realm.json     # Keycloak realm definition (clients, roles, test users)
│   └── themes/assethub/            # Custom email themes (Swedish/English, HTML + plain text)
│
├── certs/                          # TLS certificates (dev: self-signed, prod: CA-issued)
├── docs/                           # DEPLOYMENT.md, OBSERVABILITY.md, SECURITY-AUDIT.md, APPLICATION-AUDIT.md
├── .github/workflows/ci.yml        # CI pipeline (build, test, security audit, Docker image scan)
├── .env.template                   # Environment variable template for all services
├── Directory.Build.props           # Shared build settings (target framework, nullable, implicit usings)
└── CREDENTIALS.md                  # Default passwords, OAuth config, connection strings
```

### Layer Dependencies

```
Domain  ←  Application  ←  Infrastructure  ←  Api
                ↑                                ↑
                Ui (Razor Class Library) ─────────┘
                                             Worker → Infrastructure + Application
```

- **Domain** — no dependencies
- **Application** — depends on Domain
- **Infrastructure** — depends on Application + Domain
- **Ui** — depends on Application only (no Infrastructure reference)
- **Api** — composition root, references all projects including Ui
- **Worker** — composition root, references Infrastructure + Application (no Ui)

---

## Modular Components — What You Can Replace

AssetHub is designed with clean interfaces so you can swap components to match your corporate infrastructure. Every external dependency has an abstraction layer.

### Identity & Authentication

**Default:** Keycloak 26 as the OIDC provider, with a custom realm (`media`) imported on first start.

**Authentication flow:**
A `PolicyScheme` named "Smart" routes requests based on the `Authorization` header — `Bearer` tokens go to JWT Bearer validation, all other requests use Cookie authentication backed by OIDC.

| Scheme | When used | Details |
|--------|-----------|---------|
| **Cookie** | Blazor UI (browser) | `__Host.assethub.auth`, SameSite=Strict, HttpOnly, SecurePolicy conditional (SameAsRequest in dev, Always in prod) |
| **JWT Bearer** | API clients | Validates issuer, audience (`assethub-app`, `account`), lifetime. NameClaimType = `preferred_username` |
| **OIDC** | Login redirect | Authorization Code + PKCE, scopes: `openid profile email`, `SaveTokens`, `GetClaimsFromUserInfoEndpoint`, `MapInboundClaims = false` |

**Keycloak role mapping:** On `OnTokenValidated`, roles are extracted from both `realm_access.roles` and `resource_access.assethub-app.roles` in the Keycloak token JSON and mapped to standard `ClaimTypes.Role` claims. This enables ASP.NET Core's `User.IsInRole()`.

**Authorization policies:**

| Policy | Roles allowed | Used by |
|--------|--------------|---------|
| FallbackPolicy | Any authenticated user | Default for all endpoints (anonymous requires `.AllowAnonymous()`) |
| `RequireViewer` | viewer, contributor, manager, admin | — |
| `RequireContributor` | contributor, manager, admin | Collection creation |
| `RequireManager` | manager, admin | — |
| `RequireAdmin` | admin only | All `/api/admin/*` endpoints |

**OIDC error handling:** `OnRemoteFailure` and `OnAuthenticationFailed` redirect to `/?authError=` with specific error codes instead of showing raw exceptions. The `kc_action` parameter is forwarded to Keycloak for action-specific flows (e.g., password change).

**Keycloak Admin API dependency:** Beyond OIDC authentication, the application uses the Keycloak Admin REST API via `IKeycloakUserService` for user creation, password resets, role assignment, user deletion, and realm role queries. `IUserLookupService` resolves user IDs to usernames/emails by querying the Keycloak database directly. `IUserSyncService` detects and cleans up references to users deleted from Keycloak.

**Replacing Keycloak:** OIDC authentication is standard and works with any compliant provider by changing `Keycloak__Authority`, `ClientId`, and `ClientSecret`. However, the admin operations require new implementations of `IKeycloakUserService` and `IUserLookupService` for your identity provider's management API. Alternatively, Keycloak supports AD/LDAP user federation if you want to keep it as an authentication broker while using corporate directories.

---

### Object Storage

| Default | Interface | Corporate Alternatives |
|---------|-----------|----------------------|
| **MinIO** (S3 API) | `IMinIOAdapter` | AWS S3, Azure Blob Storage, Google Cloud Storage, NetApp StorageGRID |

**How to replace:**
- Implement `IMinIOAdapter` for your storage backend
- All file operations go through this single interface
- Zero code changes needed — just swap the DI registration

**Interface:**
```csharp
public interface IMinIOAdapter
{
    Task UploadAsync(string bucketName, string objectKey, Stream data, string contentType, CancellationToken ct);
    Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken ct);
    Task<byte[]> DownloadRangeAsync(string bucketName, string objectKey, long offset, int length, CancellationToken ct);
    Task DeleteAsync(string bucketName, string objectKey, CancellationToken ct);
    Task<bool> ExistsAsync(string bucketName, string objectKey, CancellationToken ct);
    Task<ObjectStatInfo?> StatObjectAsync(string bucketName, string objectKey, CancellationToken ct);
    Task<string> GetPresignedDownloadUrlAsync(string bucketName, string objectKey, int expirySeconds, bool forceDownload, string? downloadFileName, CancellationToken ct);
    Task<string> GetPresignedUploadUrlAsync(string bucketName, string objectKey, int expirySeconds, CancellationToken ct);
    Task EnsureBucketExistsAsync(string bucketName, CancellationToken ct);
}
```

**Implementation details:**
- **Dual-client architecture** — An internal MinIO client handles server-side operations (upload, download, delete, stat), while a separate public client generates presigned URLs that browsers access directly. This allows internal and external endpoints to differ (e.g. `minio:9000` internally vs `storage.corp.com` externally).
- **Presigned URL caching** — Download URLs are cached in-memory for 75% of their expiry time to reduce calls to MinIO.
- **Range downloads** — `DownloadRangeAsync` reads file headers (magic bytes) without pulling the entire object, used for content-type validation on upload.
- **Idempotent deletes** — `DeleteAsync` silently ignores `ObjectNotFoundException` / `BucketNotFoundException` so callers don't need to check existence first.
- **StorageException** — All MinIO SDK, network, and socket errors are wrapped in a `StorageException` with user-friendly messages, keeping infrastructure details out of the application layer.
- **Filename sanitisation** — Presigned download URLs with `forceDownload` strip control characters and quotes from filenames to prevent Content-Disposition header injection.

---

### Database

**Default:** PostgreSQL 16 via EF Core with the Npgsql provider. Hangfire job storage also uses PostgreSQL.

**Tables:**

| Entity | PostgreSQL-specific features | Notes |
|--------|----------------------------|-------|
| `Assets` | `Tags` (jsonb), `MetadataJson` (jsonb) | Custom ValueComparers for change tracking on JSON columns |
| `Collections` | — | Indexed on Name |
| `CollectionAcls` | — | Unique composite index on (CollectionId, PrincipalType, PrincipalId) |
| `AssetCollections` | — | Many-to-many join table with unique (AssetId, CollectionId) |
| `Shares` | `PermissionsJson` (jsonb), `TokenHash` (unique index) | Polymorphic scope via ScopeType/ScopeId (no FK, enforced at app level) |
| `AuditEvents` | `DetailsJson` (jsonb) | Composite index on (EventType, CreatedAt) for filtered pagination |
| `ZipDownloads` | — | Indexed on Status and ExpiresAt for cleanup jobs |
| `DataProtectionKeys` | — | ASP.NET Data Protection key ring (via `IDataProtectionKeyContext`) |

**PostgreSQL-specific dependencies:**
- **4 jsonb columns** with serialization/deserialization converters and custom `ValueComparer` implementations
- **`pg_trgm` extension** — installed via migration, enables trigram-based fuzzy search
- **`EF.Functions.ILike()`** — case-insensitive pattern matching for asset search (title and description)
- **`EnableDynamicJson()`** — NpgsqlDataSource configuration for JSON column support
- **Connection pool tuning** — MaxPoolSize reduced to 50 (from Npgsql default of 100), connection timeout 15s

**Migrations:** Code-first, auto-applied on startup by both the API and Worker hosts via `Database.MigrateAsync()`. Currently 11 migrations from initial schema through to share password encryption.

**Replacing PostgreSQL:** Requires changing the EF Core provider (e.g., `UseSqlServer()`), replacing all jsonb columns with the target database's JSON support, rewriting the `pg_trgm` search migration, replacing `ILike` calls with provider-appropriate equivalents, switching the Hangfire storage provider, and regenerating migrations. This is a significant effort due to the deep use of PostgreSQL-native features.

---

### Email

**Default implementation:** `SmtpEmailService` using `System.Net.Mail.SmtpClient`, wrapped in the `smtp` Polly pipeline (retry on transient failures). Dev environment uses Mailpit for email capture.

**Interface:**
```csharp
public interface IEmailService
{
    Task SendEmailAsync(string to, IEmailTemplate template, CancellationToken ct);
    Task SendEmailAsync(IEnumerable<string> recipients, IEmailTemplate template, CancellationToken ct);
}
```

The service is template-driven — email content is defined by `IEmailTemplate` implementations, not by the service itself. Each message is sent as multipart/alternative with both HTML and plain-text views. Empty or whitespace-only recipients are silently filtered (logged as warning).

**Template architecture:**
- `IEmailTemplate` — interface with `Subject`, `GetHtmlBody()`, `GetPlainTextBody()`
- `EmailTemplateBase` — abstract base class providing a branded responsive HTML layout (header with app name + brand color, body, footer). Subclasses override `GetContentHtml()` and `GetContentPlainText()` only.
- `WelcomeEmailTemplate` — sent when an admin creates a new user (includes username, temporary password, login URL, getting-started instructions)
- `ShareCreatedEmailTemplate` — sent when a share link is created (includes share URL, password, content name, expiry date, sender name)

Add new email types by creating a new `EmailTemplateBase` subclass — no changes to `IEmailService` needed.

**Configuration:**

| Key | Default | Description |
|-----|---------|-------------|
| `Email__Enabled` | `false` | When false, emails are logged but not sent |
| `Email__SmtpHost` | — | SMTP server hostname |
| `Email__SmtpPort` | `587` | SMTP port (587 for TLS, 465 for SSL) |
| `Email__SmtpUsername` | — | SMTP auth username (optional — skipped if empty) |
| `Email__SmtpPassword` | — | SMTP auth password |
| `Email__UseSsl` | `true` | Enable SSL/TLS |
| `Email__FromAddress` | — | Sender email address |
| `Email__FromName` | `AssetHub` | Sender display name |

**Replacing SMTP:** Implement `IEmailService` and register it in DI. The interface is transport-agnostic — a replacement could use SendGrid, AWS SES, or any other email API.

---

### Malware Scanning

**Default implementation:** ClamAV via raw TCP (clamd `INSTREAM` protocol with length-prefixed chunked streaming). Health checks use `PING`/`PONG` and bypass the Polly pipeline to fail fast.

**Interface:**
```csharp
public interface IMalwareScannerService
{
    Task<MalwareScanResult> ScanAsync(Stream stream, string fileName, CancellationToken ct);
    Task<MalwareScanResult> ScanAsync(byte[] data, string fileName, CancellationToken ct);
    Task<bool> IsAvailableAsync(CancellationToken ct);
}
```

`MalwareScanResult` is a record with `ScanCompleted`, `IsClean`, `ThreatName`, and `ErrorMessage` properties plus static factory methods (`Clean()`, `Infected(name)`, `Failed(msg)`, `Skipped()`).

**Behavior during upload:**
- Scanning runs synchronously during both regular and presigned uploads, *before* the asset is queued for media processing
- **Disabled** (`Enabled: false`) → `Skipped()` is returned and the file is allowed through
- **Scanner unreachable** → `Failed()` is returned (`ScanCompleted = false`) and the **upload is rejected**
- **Malware detected** → upload rejected, file deleted (for presigned uploads), and an `asset.malware_detected` audit event is logged with the threat name
- Stream position is reset between retries for seekable streams

**Configuration:**

| Key | Default | Description |
|-----|---------|-------------|
| `ClamAV__Enabled` | `false` | Enable/disable scanning entirely |
| `ClamAV__Host` | `clamav` | clamd hostname |
| `ClamAV__Port` | `3310` | clamd TCP port |
| `ClamAV__TimeoutMs` | `30000` | TCP send/receive timeout |
| `ClamAV__ChunkSize` | `8192` | INSTREAM chunk size in bytes |

**Replacing ClamAV:** Implement `IMalwareScannerService` with your scanner's SDK or API. The interface is protocol-agnostic — the ClamAV implementation uses raw TCP, but a replacement could use HTTP, gRPC, or any other transport.

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
- Stale upload cleanup (daily)
- Orphaned share cleanup (weekly)
- ZIP expiry cleanup
- User sync

The Worker runs as a **separate container** (`AssetHub.Worker`) so it can be scaled independently from the API. It shares the same Infrastructure layer but has its own Dockerfile with ImageMagick and ffmpeg pre-installed. Both the API and Worker run configurable Hangfire worker pools (2–8 threads).

**How to replace:**
- Hangfire is deeply integrated but uses standard job patterns
- For enterprise message queues, wrap queue consumers in Hangfire jobs, or replace Hangfire entirely with MassTransit/NServiceBus

---

### Media Processing

**Tools:** ImageMagick (images) + ffmpeg (video), running in the Worker container.

**Interface:**
```csharp
public interface IMediaProcessingService
{
    Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, CancellationToken ct);
    Task ProcessImageAsync(Guid assetId, string originalObjectKey, CancellationToken ct);
    Task ProcessVideoAsync(Guid assetId, string originalObjectKey, CancellationToken ct);
}
```

`ScheduleProcessingAsync` enqueues a Hangfire background job based on the asset type and returns a job ID. Non-image/video types (documents, etc.) are marked Ready immediately with no processing. `ProcessImageAsync` and `ProcessVideoAsync` are called by the Worker — they download the original from MinIO, generate renditions locally, and upload the results back.

**What gets generated:**

| Asset type | Renditions | Extras |
|------------|-----------|--------|
| **Image** | Thumbnail (200×200 JPEG) + Medium (800×800 JPEG) | EXIF/IPTC/GPS metadata extraction via MetadataExtractor; auto-populates Copyright field from EXIF if not already set |
| **Video** | Poster frame (800px wide JPEG, extracted at second 5) | — |
| **Other** | None (marked Ready immediately) | — |

All dimensions, JPEG quality, and poster frame timing are configurable via `ImageProcessing__*` environment variables.

**ImageMagick processing pipeline:** auto-orient (EXIF rotation), flatten transparency to white, convert to sRGB, resize preserving aspect ratio (only shrinks), strip metadata from output, first frame only for animated images.

**Failure handling:** On error, the asset is marked Failed with a user-visible message and an `asset.processing_failed` audit event is logged (including error type and message). Temp files are cleaned up in a `finally` block regardless of success or failure.

**Security hardening:**
- Both ImageMagick and ffmpeg have a hard **5-minute process timeout** — the process tree is killed if exceeded
- A custom `imagemagick-policy.xml` restricts processing to raster formats only. Disabled coders: SVG, MVG, MSL, PS/EPS/PDF (Ghostscript), TEXT/LABEL, XPS, URL/HTTP/HTTPS/FTP (SSRF prevention), ephemeral, X11. Also blocks `@*` path patterns and the gnuplot delegate.
- Resource limits: 16KP max dimensions, 128MP max area, 256 MiB memory, 2 GiB disk, 120s per-operation timeout, 4 threads
- The Worker container runs with `cap_drop: ALL`, `read_only: true`, `no-new-privileges`, and a 2 GB tmpfs at `/tmp` for transient processing files

---

## Container Reference

| Container | Purpose | Internal Port | Dev Exposed | Prod Exposed | Swappable? |
|-----------|---------|--------------|-------------|--------------|------------|
| `assethub-api` | ASP.NET Core API + Blazor UI | 7252 | 127.0.0.1:7252 | 127.0.0.1:7252 | — (core application) |
| `assethub-worker` | Hangfire background processor (ImageMagick, ffmpeg, zip) | — | — | — | — (core application) |
| `assethub-postgres` | Primary database (EF Core + Hangfire) | 5432 | 127.0.0.1:5432 | not exposed | Any PostgreSQL instance (uses Npgsql provider) |
| `assethub-minio` | S3-compatible object storage | 9000 / 9001 | 127.0.0.1:9000, :9001 | not exposed | AWS S3 or any S3-compatible store (MinIO SDK) |
| `assethub-keycloak` | OIDC identity provider + Admin API | 8080 / 8443 | 127.0.0.1:8080, :8443 | not exposed | Requires `IKeycloakUserService` + `IUserLookupService` adapter rewrites (see note below) |
| `assethub-clamav` | Malware scanning (clamd TCP) | 3310 | not exposed | not exposed | Set `ClamAV__Enabled=false` to disable; replacing requires a new `IMalwareScannerService` impl |
| `assethub-jaeger` | Distributed tracing (OTLP) | 16686 / 4317 / 4318 | 127.0.0.1:16686, :4317, :4318 | not exposed | Any OTLP-compatible backend (change `OpenTelemetry__OtlpEndpoint`) |
| `assethub-prometheus` | Metrics collection | 9090 | 127.0.0.1:9090 | not exposed | Any Prometheus-compatible scraper |
| `assethub-grafana` | Metrics dashboards | 3000 | 127.0.0.1:3000 | not exposed | Any visualisation tool reading Prometheus |
| `assethub-mailpit` | Dev email capture (dev only) | 8025 / 1025 | 127.0.0.1:8025, :1025 | not present | Configure `Email__*` env vars for any SMTP relay |

> **Keycloak dependency note:** OIDC authentication is standard and works with any compliant provider. However, the application also calls the Keycloak Admin REST API for user creation, password resets, role assignment, and deleted-user sync. Swapping Keycloak for Azure AD, Okta, or Auth0 requires new implementations of `IKeycloakUserService` and `IUserLookupService`.
>
> **Database note:** The EF Core data access layer uses the Npgsql provider (PostgreSQL). Switching to SQL Server or another database engine would require changing the provider and regenerating migrations.

### Minimal Production Stack

The compose stack is modular — you can point individual services at existing corporate infrastructure by overriding environment variables:

| Component | Config keys | Notes |
|-----------|------------|-------|
| **PostgreSQL** | `ConnectionStrings__Postgres` | Standard Npgsql connection string. EF Core auto-migrates on startup. |
| **MinIO / S3** | `MinIO__Endpoint`, `MinIO__AccessKey`, `MinIO__SecretKey`, `MinIO__UseSSL`, `MinIO__PublicUrl` | MinIO SDK is S3-compatible, so AWS S3 or any S3-compatible store works. `PublicUrl` is the endpoint browsers use for presigned URLs (can differ from the internal endpoint). |
| **ClamAV** | `ClamAV__Enabled` | Set to `false` to skip malware scanning entirely (uploads return `Skipped`). |

> **Note:** Keycloak cannot simply be swapped for Azure AD or Okta by changing a URL. The application uses the Keycloak Admin REST API for user creation, password resets, role assignment, and user sync. Replacing Keycloak requires implementing alternative `IKeycloakUserService` and `IUserLookupService` adapters. OIDC authentication itself is standard and works with any compliant provider.
>
> Both the API and the Worker (Hangfire background jobs for media processing and zip builds) are required for a functional deployment.

---

## Resilience & Fault Tolerance

Every external dependency is wrapped in a [Polly](https://github.com/App-vNext/Polly) resilience pipeline so that transient failures don't cascade into user-visible errors. Pipelines are registered centrally in `InfrastructureServiceExtensions` and injected via `ResiliencePipelineProvider<string>`.

| Pipeline | Used By | Retry | Circuit Breaker | Notes |
|----------|---------|-------|-----------------|-------|
| `minio` | MinIOAdapter | 3 attempts, exponential from 1 s | Opens at 50% failure over 30 s (min 5 calls), 30 s break | Handles `HttpRequestException`, `SocketException`, transient MinIO SDK errors; ignores `ObjectNotFoundException` / `BucketNotFoundException` |
| `keycloak` | KeycloakUserService | 3 attempts, exponential from 500 ms | Opens at 50% failure over 30 s, 30 s break | Only retries 5xx and network errors — never retries 4xx (auth/conflict). Per-attempt + total request timeout managed by Polly (HttpClient.Timeout disabled) |
| `clamav` | ClamAvScannerService | 2 attempts, constant 500 ms | Opens at 50% failure over 60 s (min 3 calls), 60 s break | Handles `SocketException` on the raw TCP clamd connection |
| `smtp` | SmtpEmailService | 2 attempts, exponential from 2 s | — | Retry-only; handles `SmtpException` and `SocketException` |
| `postgres` | EF Core (Npgsql) | Built-in `EnableRetryOnFailure()` | — | Handles transient database connection failures at the provider level |

**How it works in practice:**
1. A MinIO upload fails with a socket timeout → Polly retries up to 3 times with exponential backoff (1 s → 2 s → 4 s)
2. If MinIO keeps failing (50%+ failure rate over 30 seconds), the circuit breaker opens and subsequent calls fail fast for 30 seconds instead of waiting for timeouts
3. After the break duration, the circuit moves to half-open — a single probe request decides whether to close or re-open the breaker

This pattern keeps the application responsive even when downstream services are degraded, and prevents a failing dependency from exhausting thread pool resources.

---

## Service Interface Reference

**Infrastructure adapters** — swappable implementations for external dependencies:

| Interface | Default Implementation | Purpose |
|-----------|----------------------|---------|
| `IMinIOAdapter` | `MinIOAdapter` | Object storage (upload, download, presigned URLs, stat, delete) |
| `IEmailService` | `SmtpEmailService` | Template-driven email sending (single + multi-recipient) |
| `IMalwareScannerService` | `ClamAvScannerService` | Upload scanning (stream + byte array overloads) |
| `IMediaProcessingService` | `MediaProcessingService` | Schedule and execute thumbnail/poster generation |
| `IKeycloakUserService` | `KeycloakUserService` | Identity provider admin API (create user, reset password, delete, assign roles) |
| `IUserLookupService` | `UserLookupService` | Resolve user IDs to usernames/emails, check existence |
| `IUserSyncService` | `UserSyncService` | Detect and clean up orphaned references to deleted IdP users |
| `IAuditService` | `AuditService` | Record audit events (auto-captures IP + User-Agent from HTTP context) |

**Application services** — core business logic:

| Interface | Default Implementation | Purpose |
|-----------|----------------------|---------|
| `IAssetService` | `AssetService` | Asset commands (update metadata, delete, collection membership) |
| `IAssetQueryService` | `AssetQueryService` | Asset queries (get, list, search, rendition URLs, downloads) |
| `IAssetUploadService` | `AssetUploadService` | Asset upload (streaming and presigned URL workflows) |
| `IAssetDeletionService` | `AssetDeletionService` | Smart deletion (multi-collection aware remove/delete) |
| `ICollectionService` | `CollectionService` | Collection CRUD and zip download requests |
| `ICollectionAclService` | `CollectionAclService` | Per-collection ACL management (grant, revoke, list) |
| `ICollectionAuthorizationService` | `CollectionAuthorizationService` | Check user permissions on a collection |
| `IShareService` | `ShareService` | Create, revoke, and update share links |
| `IShareAdminService` | `ShareAdminService` | Admin share management (list, retrieve tokens/passwords) |
| `IPublicShareAccessService` | `ShareAccessService` | Anonymous share access (validate token, password auth) |
| `IAuthenticatedShareAccessService` | `ShareAccessService` | Authenticated share access (preview, download) |
| `IZipBuildService` | `ZipBuildService` | Async zip archive building via Hangfire |
| `IUserAdminService` | `UserAdminService` | Admin user operations (create, delete, reset password) |
| `IUserProvisioningService` | `UserProvisioningService` | Provision new users with default collection access |
| `IUserCleanupService` | `UserCleanupService` | Remove all ACLs and revoke shares for a deleted user |
| `IDashboardService` | `DashboardService` | Dashboard statistics (asset/collection/share counts) |
| `IAuditQueryService` | `AuditQueryService` | Query audit log (paginated + legacy endpoints) |

---

## Role-Based Access Control

Roles are assigned **per collection** through Access Control Lists. Higher roles inherit all lower permissions.

| Role | View | Upload | Edit assets | Share | Edit collection | Delete | Manage access | Admin panel |
|------|------|--------|-------------|-------|-----------------|--------|---------------|-------------|
| Viewer | Yes | | | | | | | |
| Contributor | Yes | Yes | Yes | Yes | | | | |
| Manager | Yes | Yes | Yes | Yes | Yes | Yes | Yes | |
| Admin | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes |

**Key concepts:**
- A user can hold different roles on different collections
- Assets inherit permissions from their collections — access is never per-asset
- Assets can belong to multiple collections simultaneously (many-to-many)
- The role hierarchy is centralised in `RoleHierarchy.cs` — all permission checks call static methods like `CanUpload()`, `CanDelete()`, `CanManageAccess()` so the logic is never duplicated
- ACL operations are level-guarded: you can only grant or revoke roles at or below your own level

---

## Security

AssetHub implements defense-in-depth security across multiple layers.

### Authentication & Authorization

| Feature | Implementation |
|---------|---------------|
| **OIDC with PKCE** | Authorization Code flow with Proof Key for Code Exchange (no implicit grant) |
| **Smart auth routing** | Requests with `Authorization: Bearer` header route to JWT validation; all others use cookie auth |
| **Cookie security** | `__Host.` prefix, `SameSite=Strict`, `HttpOnly=true`, `Secure=Always` in production (`SameAsRequest` in dev) |
| **JWT Bearer** | For API clients, with explicit issuer, audience, and lifetime validation |
| **Fallback policy** | All endpoints require authentication by default |
| **Brute force protection** | Keycloak locks accounts after 5 failed login attempts (15 min lockout) |

### Rate Limiting

| Policy | Scope | Limit |
|--------|-------|-------|
| Global (authenticated) | Per user | 200 requests/min |
| BlazorSignalR | Per IP | 60 connections/min |
| ShareAnonymous | Per IP | 30 requests/min |
| SharePassword | Per IP | 10 attempts/5 min |

### Upload Security

| Check | Purpose |
|-------|---------|
| **Content-type allowlist** | Only images, videos, audio, documents, and safe file types (SVGs blocked due to XSS risk) |
| **Magic byte validation** | Prevents content-type spoofing (JPEG header must match `image/jpeg` claim) |
| **ClamAV scanning** | Malware detection before processing |
| **File size limits** | Configurable max upload size (default 500 MB) |
| **Batch limits** | Maximum 10 files per upload request |

### Data Protection

| Feature | Implementation |
|---------|---------------|
| **Share tokens** | Encrypted at rest using ASP.NET Data Protection API |
| **Share passwords** | BCrypt-hashed for validation, Data Protection-encrypted for admin retrieval |
| **Access tokens** | Short-lived (30 min) signed tokens for embedded media (img/video src attributes) |
| **Key storage** | Data Protection keys stored in PostgreSQL for multi-instance consistency |

### Container Hardening

All containers are hardened. The level varies by what each container needs:

| Container | `cap_drop: ALL` | `no-new-privileges` | `read_only` | `tmpfs` | Non-root user | PID limit |
|-----------|:-:|:-:|:-:|:-:|:-:|:-:|
| API | Yes | Yes | Yes | /tmp:2G | Yes (`app`) | 200 |
| Worker | Yes | Yes | Yes | /tmp:2G | Yes (`app`) | 200 |
| PostgreSQL | Yes | Yes | — | — | Yes (`70:70`) | 100 |
| MinIO | Yes | Yes | — | — | Yes (built-in) | 100 |
| Keycloak | Yes | Yes | — | — | Yes (`1000:1000`) | 200 |
| ClamAV | Yes + selective `cap_add` | Yes | — | — | Yes (built-in) | 100 |
| Jaeger | Yes | Yes | Yes | /tmp:50M | Yes (`10001`) | 100 |
| Prometheus | Yes | Yes | Yes | — | Yes (`65534`) | 100 |
| Grafana | Yes | Yes | — | — | Yes (`472`) | 100 |
| Mailpit (dev) | Yes | Yes | — | — | — | 50 |

ClamAV requires `CHOWN`, `SETUID`, `SETGID`, `FOWNER`, and `DAC_OVERRIDE` for its entrypoint setup; all other capabilities are dropped.

### Network Security

| Feature | Implementation |
|---------|---------------|
| **X-Forwarded-For protection** | Only trusted from RFC 1918 private networks (Docker bridge) |
| **Security headers** | `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy: camera=(), microphone=(), geolocation=()`, `X-XSS-Protection: 1; mode=block` |
| **CSP** | Production only — `default-src 'self'`, `script-src/style-src 'self' 'unsafe-inline'` (required by Blazor/MudBlazor), `img-src 'self' data: blob:`, `connect-src 'self' wss:` (SignalR), `frame-ancestors 'none'` |
| **HTTPS enforcement** | HSTS with 365-day max-age, subdomain inclusion, and preload in production |
| **Metrics endpoint** | IP-restricted to internal Docker network only |

### Audit & Observability

| Component | Purpose |
|-----------|---------|
| **Audit trail** | Every action logged with user, timestamp, target, IP, and user agent |
| **Jaeger** | Distributed tracing via OpenTelemetry (OTLP export) |
| **Prometheus** | Metrics collection (request latency, error rates, etc.) |
| **Grafana** | Dashboards for monitoring (pre-configured with AssetHub panels) |
| **Structured logging** | Serilog with request enrichment (JSON + compact format) |

### Infrastructure Security

| Feature | Implementation |
|---------|---------------|
| **Docker log rotation** | 50 MB x 5 files per container (prevents disk exhaustion) |
| **Resource limits** | CPU, memory, and PID limits on all containers |
| **Health checks** | Liveness and readiness probes with start periods for slow services |
| **Network segmentation** | Two isolated Docker networks (`backend` + `observability`); only API/Worker bridge both |
| **Credentials management** | Environment variables via `.env.template`; Docker file-based secrets documented as opt-in alternative |
| **ImageMagick policy** | Restrictive policy.xml disables SVG, MVG, and MSL coders to prevent server-side XSS |
| **Query limits** | Hard caps on admin queries to prevent unbounded memory use (CWE-400) |

---

## API Reference

All endpoints require authentication unless marked *(anonymous)*.

### Collections

```http
GET    /api/collections                           # List root collections the user can access
GET    /api/collections/{id}                      # Collection details with ACLs
POST   /api/collections                           # Create collection (Contributor+)
PATCH  /api/collections/{id}                      # Update name, description
DELETE /api/collections/{id}                      # Delete collection (cascades relationships)
POST   /api/collections/{id}/download-all         # Queue zip download of all assets
```

### Collection ACLs

```http
GET    /api/collections/{collectionId}/acl                         # List access entries
POST   /api/collections/{collectionId}/acl                         # Grant or update role for user
DELETE /api/collections/{collectionId}/acl/{principalType}/{principalId}  # Revoke access
GET    /api/collections/{collectionId}/acl/users/search            # Search users for ACL assignment
```

### Assets

```http
GET    /api/assets                                # List ready assets with pagination (Admin)
GET    /api/assets/all                            # Search all assets with filters (Admin)
GET    /api/assets/{id}                           # Asset details
POST   /api/assets                                # Upload (multipart form)
POST   /api/assets/init-upload                    # Initiate presigned upload
POST   /api/assets/{id}/confirm-upload            # Confirm presigned upload completed
PATCH  /api/assets/{id}                           # Update title, description, copyright, tags
DELETE /api/assets/{id}                           # Delete asset (with optional collection scope)
GET    /api/assets/collection/{collectionId}      # Assets in collection (search, sort, filter)
GET    /api/assets/{id}/deletion-context          # Get deletion impact info
```

### Asset Collections

```http
GET    /api/assets/{id}/collections               # Collections containing asset
POST   /api/assets/{id}/collections/{collectionId}  # Add asset to collection
DELETE /api/assets/{id}/collections/{collectionId}  # Remove from collection
```

### Renditions

```http
GET    /api/assets/{id}/download                  # Original file (presigned redirect)
GET    /api/assets/{id}/preview                   # Original inline preview
GET    /api/assets/{id}/thumb                     # Thumbnail (200x200)
GET    /api/assets/{id}/thumb/download            # Thumbnail download
GET    /api/assets/{id}/medium                    # Medium rendition (800x800)
GET    /api/assets/{id}/medium/download           # Medium rendition download
GET    /api/assets/{id}/poster                    # Video poster frame
```

### Shares

```http
POST   /api/shares                                # Create share link
DELETE /api/shares/{id}                           # Revoke share (creator or admin)
PUT    /api/shares/{id}/password                  # Set, change, or remove password
GET    /api/shares/{token}                        # View shared content (anonymous)
POST   /api/shares/{token}/access-token           # Authenticate with password (anonymous)
GET    /api/shares/{token}/download               # Download via share (anonymous)
POST   /api/shares/{token}/download-all           # Zip download of shared collection (anonymous)
GET    /api/shares/{token}/preview                # Preview shared asset (anonymous)
```

### Zip Downloads

```http
GET    /api/zip-downloads/{jobId}                 # Poll build progress (authenticated)
GET    /api/zip-downloads/{jobId}/share           # Poll build progress (anonymous, X-Share-Token header)
```

### Admin *(admin role required)*

```http
GET    /api/admin/shares                          # All shares (paginated, filterable)
GET    /api/admin/shares/{id}/token               # Retrieve share token (encrypted storage)
GET    /api/admin/shares/{id}/password            # Retrieve share password (encrypted storage)
DELETE /api/admin/shares/{id}                     # Revoke share
GET    /api/admin/collections/access              # All collection access (hierarchical tree)
POST   /api/admin/collections/{collectionId}/acl            # Grant access
DELETE /api/admin/collections/{collectionId}/acl/{principalId}  # Revoke access (?principalType=)
GET    /api/admin/users                           # Users with access
GET    /api/admin/keycloak-users                  # All Keycloak users
POST   /api/admin/users                           # Create user in Keycloak
POST   /api/admin/users/{userId}/reset-password   # Reset password
POST   /api/admin/users/sync                      # Sync deleted users (supports dry-run)
DELETE /api/admin/users/{userId}                  # Delete user
GET    /api/admin/audit                           # Recent audit events (default 200, max 200)
GET    /api/admin/audit/paginated                 # Paginated audit log
```

### Dashboard

```http
GET    /api/dashboard                             # Stats: assets, collections, shares (active/total), audit count
```

### Health

```http
GET    /health                                    # Liveness (always 200)
GET    /health/ready                              # Readiness (checks PostgreSQL, MinIO, Keycloak, ClamAV)
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
- **Resource limits** — CPU, memory, and PID limits on every container (512 MB–1 GB memory, 100–200 PIDs)
- **Internal-only networking** — No ports exposed except the API on `127.0.0.1:7252`. All other service ports (PostgreSQL, MinIO, Keycloak, Jaeger, Prometheus, Grafana) are commented out.
- **Network segmentation** — Two isolated Docker networks: `backend` (data stores: PostgreSQL, MinIO, ClamAV, Keycloak) and `observability` (Jaeger, Prometheus, Grafana). The API and Worker bridge both networks; other services cannot cross boundaries.
- **Health checks** with `start_period` for slow-starting services (ClamAV: 5 min, Keycloak: 2 min)
- **`restart: unless-stopped`** on all services
- **Container hardening** — cap_drop ALL, no-new-privileges, non-root users, read-only root filesystem where supported, PID limits
- **Log rotation** — `json-file` driver with 50 MB × 5 files on every container
- **Keycloak production mode** — `KC_HOSTNAME_STRICT`, `KC_PROXY_HEADERS: xforwarded`, HTTPS required for OIDC metadata
- **Docker secrets support** — Commented-out template for file-based secrets (`POSTGRES_PASSWORD_FILE`, etc.) as an alternative to environment variables
- **Prometheus** — 30-day retention, `--web.enable-lifecycle` intentionally omitted (use `SIGHUP` to reload config)

The deployment guide covers reverse proxy setup (Caddy / Nginx / Traefik), TLS certificate management, backup & restore procedures, upgrade & rollback steps, and a security hardening checklist.

---

## CI/CD

GitHub Actions runs on every push and pull request to `main` and `develop`:

| Job | What it does | Runs on |
|-----|-------------|---------|
| **build-and-test** | Restore, build (Release), run all .NET tests with Cobertura code coverage, upload results as artifacts | Every push and PR |
| **security-audit** | `dotnet list package --vulnerable --include-transitive` — fails the build on known CVEs | Every push and PR |
| **docker-build** | Builds API + Worker images, scans both with Trivy for CRITICAL/HIGH OS and library vulnerabilities. Requires build-and-test + security-audit to pass first. | Push to `main` only |

---

## Testing

AssetHub has comprehensive test coverage across three layers:

### Backend Tests (`AssetHub.Tests`)

```bash
# Requires Docker running (for Testcontainers)
dotnet test tests/AssetHub.Tests/
```

Two test strategies in one project:

- **Integration tests** (repositories, endpoints, edge cases) — run against **real PostgreSQL** via Testcontainers and use `WebApplicationFactory<Program>` for full API stack testing with a custom auth handler
- **Unit tests** (services, helpers) — use Moq to isolate service logic from infrastructure

Coverage across 29 test files:

| Category | Files | What's tested |
|----------|-------|---------------|
| Repositories | 5 | Asset, Collection, CollectionAcl, AssetCollection, Share CRUD |
| Endpoints | 5 | Asset, Collection, Share, Admin, Dashboard API authorization and responses |
| Services | 12 | Upload validation, deletion, shares, ACLs, ClamAV scanning, media processing, Keycloak, zip builds, dashboards, audit logging, external service resilience |
| Edge cases | 6 | Authorization boundaries, ACL inheritance, concurrency, multi-collection access, smart deletion, security (rate limiting, CORS, header injection) |
| Helpers | 2 | Input validation, file magic byte detection |

### Component Tests (`AssetHub.Ui.Tests`)

```bash
dotnet test tests/AssetHub.Ui.Tests/
```

12 test files covering:

- **bUnit component tests** (9 files) — AssetGrid, AssetUpload, CollectionTree, CreateShareDialog, EditAssetDialog, ManageAccessDialog, AddToCollectionDialog, CreateCollectionDialog, LanguageSwitcher, EmptyState
- **Unit tests** (3 files) — AssetDisplayHelpers, RolePermissions, UserFeedbackService

### E2E Tests (Playwright)

```bash
cd tests/E2E
npm install
npx playwright install chromium
npm test
```

15 spec files running against a full Docker Compose stack:

| Spec | Coverage |
|------|----------|
| `01-auth` | Keycloak OIDC login/logout flows |
| `02-navigation` | Page routing, sidebar, breadcrumbs |
| `03-collections` | Collection CRUD |
| `04-assets` | Upload, preview, metadata editing |
| `05-shares` | Share creation, password protection, public access |
| `06-admin` | Admin panel operations, user management |
| `07-all-assets` | Admin asset search and filtering |
| `08-api` | Direct API endpoint testing |
| `09-acl` | Per-collection role assignment and enforcement |
| `10-viewer-role` | Viewer restrictions (no upload, no delete, no share) |
| `11-edge-cases` | Boundary conditions and error handling |
| `12-responsive-a11y` | Responsive layout, accessibility |
| `13-workflows` | Multi-step user workflows |
| `14-language` | Swedish/English localisation switching |
| `15-ui-features` | Grid/list toggle, pagination, clipboard |

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
| Health check | https://assethub.local:7252/health/ready | Readiness probe (PG + MinIO + Keycloak + ClamAV) |
| Hangfire | https://assethub.local:7252/hangfire | Job queues, processing status |
| Grafana | http://localhost:3000 | Metrics dashboards (pre-configured) |
| Prometheus | http://localhost:9090 | Raw metrics, alerting rules |
| Jaeger | http://localhost:16686 | Distributed tracing |
| Keycloak Admin | https://keycloak.assethub.local:8443/admin | Users, sessions, clients |
| MinIO Console | http://localhost:9001 | Storage usage, buckets |
| Mailpit | http://localhost:8025 | Email capture (dev only) |

### Grafana Dashboards

Pre-configured dashboards include:
- **AssetHub Overview** — Request rate, error rate, P95 latency, active users
- **Upload Metrics** — Upload volume, processing queue depth, malware detections
- **Infrastructure** — PostgreSQL connections, MinIO storage, Keycloak sessions

OpenTelemetry is enabled by default and exports to Jaeger (OTLP endpoint).

---

## Troubleshooting

| Symptom | Solution |
|---------|----------|
| App won't start | Check `docker compose logs assethub-api`. Usually PostgreSQL or Keycloak not ready yet. |
| Can't log in | Add `127.0.0.1 assethub.local keycloak.assethub.local` to hosts file. Token issuer must match. |
| Uploads fail | Check MinIO console at http://localhost:9001. Bucket should be auto-created. |
| Health check fails | Hit `/health/ready` to see which dependency is down. |
| Certificate errors | Trust the self-signed certificate in your OS certificate store. See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md). |
| ClamAV slow to start | First boot downloads virus definitions (2–5 min). Health check has a 5-minute start period. |
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
- Video support (poster frame extraction via ffmpeg, inline playback via native `<video>` element)
- PDF inline preview (browser-native rendering via iframe)
- EXIF, IPTC, and XMP metadata extraction from images (via MetadataExtractor)
- Password-protected share links with expiration and signed access tokens
- Share lifecycle management (Active/Expired/Revoked statuses with admin filtering and encrypted password retrieval)
- Zip download for collections and shared content
- Full Blazor Server UI with Swedish/English localisation
- Admin dashboard (shares, ACLs, user management, paginated audit log)
- ClamAV malware scanning on uploads
- Smart asset deletion (multi-collection aware with remove/delete options)
- User management (create, sync, delete) via Keycloak Admin API
- Polly resilience pipelines (retry + circuit breaker) on MinIO, Keycloak, ClamAV, and SMTP
- Structured logging (Serilog with request enrichment)
- Health check endpoints (`/health`, `/health/ready`)
- Custom Keycloak email themes (HTML + text, Swedish/English)
- CI pipeline with build, test, vulnerability scanning, and container image scanning
- Comprehensive test coverage (650+ .NET test methods across 47 files + 15 E2E spec files)
- Production Docker Compose with auto-migration, resource limits, TLS, and container hardening
- Observability stack (Prometheus metrics, Grafana dashboards, Jaeger tracing, OpenTelemetry)
- Rate limiting (global, SignalR, anonymous share endpoints, share password brute force)
- Container security hardening (all containers: cap_drop, no-new-privileges, non-root users, PID limits)
- Data Protection encryption for share tokens and passwords
- Query hard caps to prevent unbounded memory use

### Roadmap

- Office document preview (Word, Excel, PowerPoint)
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
- `Constants.cs` centralises all magic strings, limits, and configuration keys

---

## License

MIT — see LICENSE for details.
