# Architecture

AssetHub follows **Clean Architecture** with strict dependency rules: inner layers never reference outer layers. Every external service is abstracted behind an interface in the Application layer, making components independently replaceable.

---

## Table of Contents

- [System Overview](#system-overview)
- [Project Structure](#project-structure)
- [Layer Dependencies](#layer-dependencies)
- [Modular Components](#modular-components)
  - [Identity & Authentication](#identity--authentication)
  - [Object Storage](#object-storage)
  - [Database](#database)
  - [Email](#email)
  - [Malware Scanning](#malware-scanning)
  - [Background Jobs](#background-jobs)
  - [Media Processing](#media-processing)
- [Service Interface Reference](#service-interface-reference)
- [Resilience & Fault Tolerance](#resilience--fault-tolerance)

---

## System Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  HOSTS (Composition Roots)                                                  │
│  ┌─────────────────────────────────────────┐  ┌──────────────────────────┐  │
│  │  AssetHub.Api                           │  │  AssetHub.Worker         │  │
│  │  ┌───────────────┐ ┌─────────────────┐  │  │  Wolverine consumers     │  │
│  │  │ Blazor Server │ │ Minimal APIs v1 │  │  │  ImageMagick + ffmpeg    │  │
│  │  │ (MudBlazor 8) │ │ Smart auth:     │  │  │                          │  │
│  │  │               │ │ Cookie/JWT/OIDC │  │  │                          │  │
│  │  └───────────────┘ └─────────────────┘  │  └──────────────────────────┘  │
│  └─────────────────────────────────────────┘                                │
└─────────────────────────────────────────────────────────────────────────────┘
              │                                    │
┌─────────────▼────────────────────────────────────▼──────────────────────────┐
│  APPLICATION LAYER  (AssetHub.Application)                                  │
│                                                                             │
│  Service interfaces (27 interfaces):                                        │
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
│                        ▲ INTERFACES — SWAP IMPLEMENTATIONS ▲                │
└────────────────────────┼────────────────────────────────────────────────────┘
              ┌──────────┘
│  DOMAIN (AssetHub.Domain) — Entities: Asset, Collection, CollectionAcl,     │
│  AssetCollection, Share, AuditEvent, ZipDownload + enums + value objects    │
└─────────────────────────────────────────────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────────────────────┐
│  INFRASTRUCTURE LAYER  (AssetHub.Infrastructure)                            │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │ MinIOAdapter │ │ SmtpEmail    │ │ ClamAv       │ │ KeycloakUser         ││
│  │ (dual client)│ │ Service      │ │ ScannerSvc   │ │ Service              ││
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘ └──────────┬───────────┘│
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐                         │
│  │ EF Core +    │ │ MediaProc.   │ │ Polly        │  All external calls     │
│  │ Repositories │ │ Service      │ │ Pipelines    │  wrapped in resilience  │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘  pipelines              │
└─────────┼────────────────┼────────────────┼─────────────────────────────────┘
          │                │                │
┌─────────▼────────────────▼────────────────▼─────────────────────────────────┐
│  EXTERNAL SERVICES (Docker containers)                                      │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │  PostgreSQL  │ │    MinIO     │ │   Keycloak   │ │      ClamAV          ││
│  │  16 (+ EF)   │ │  (S3 API)   │ │  (OIDC +     │ │   (clamd TCP)         ││
│  │              │ │              │ │  Admin API)  │ │                      ││
│  └──────────────┘ └──────────────┘ └──────────────┘ └──────────────────────┘│
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐                         │
│  │   RabbitMQ   │ │    Redis     │ │   Mailpit    │                         │
│  │  (Wolverine  │ │  (HybridCache│ │  (SMTP, dev) │                         │
│  │   messaging) │ │   L2 + SigR) │ │              │                         │
│  └──────────────┘ └──────────────┘ └──────────────┘                         │
│  ┌──────────────────────────────────────────────────────────────────────────┐│
│  │  Aspire Dashboard (traces, metrics, logs via OTLP)                     ││
│  │                                                                        ││
│  └──────────────────────────────────────────────────────────────────────────┘│
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
│   ├── AssetHub.Api/               # ASP.NET Core host — Versioned Minimal APIs (/api/v1/), auth, DI wiring, validation filters
│   ├── AssetHub.Ui/                # Blazor Server components, pages, layouts (Razor Class Library)
│   └── AssetHub.Worker/            # Wolverine message consumer (media processing, cleanup jobs)
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
│   ├── backup.sh                   # Full backup script (PostgreSQL, MinIO, Keycloak)
│   ├── restore.sh                  # Companion restore script with confirmation
│   ├── reverse-proxy/
│   │   ├── caddy/Caddyfile         # Production Caddy config (auto-TLS, WebSocket, security headers)
│   │   └── nginx/nginx.conf        # Production Nginx config (manual TLS, WebSocket, security headers)
│
├── keycloak/
│   ├── import/media-realm.json     # Keycloak realm definition (clients, roles, test users)
│   └── themes/assethub/            # Custom email themes (Swedish/English, HTML + plain text)
│
├── certs/                          # TLS certificates (dev: self-signed, prod: CA-issued)
├── docs/                           # ARCHITECTURE.md, DEPLOYMENT.md, SECURITY.md
├── .github/workflows/ci.yml        # CI pipeline (build, test, security audit, Docker image scan)
├── .env.template                   # Environment variable template for all services
├── Directory.Build.props           # Shared build settings (target framework, nullable, implicit usings)
└── CREDENTIALS.md                  # Default passwords, OAuth config, connection strings
```

---

## Layer Dependencies

```
Domain  ←  Application  ←  Infrastructure  ←  Api
                ↑                                ↑
                Ui (Razor Class Library) ─────────┘
                                             Worker → Infrastructure + Application
```

- **Domain** — no dependencies. Pure entities, enums, and value objects.
- **Application** — depends on Domain. Defines all service interfaces, DTOs, constants, and configuration models. This is the contract layer that outer layers implement or consume.
- **Infrastructure** — depends on Application + Domain. Contains all concrete implementations: EF Core repositories, MinIO adapter, SMTP email, ClamAV scanner, Keycloak client, media processing, and Polly resilience pipelines.
- **Ui** — depends on Application only (no Infrastructure reference). A Razor Class Library containing all Blazor Server components, pages, and layouts. Communicates with infrastructure exclusively through Application interfaces.
- **Api** — composition root, references all projects including Ui. Wires up dependency injection, configures authentication, defines versioned Minimal API endpoints (`/api/v1/`) with a `ValidationFilter` for request DTO validation, and hosts the Blazor Server app.
- **Worker** — composition root, references Infrastructure + Application (no Ui). Runs Wolverine message consumers for media processing and zip building, plus `IHostedService` classes for scheduled cleanup tasks (stale uploads, orphaned shares, audit retention).

---

## Modular Components

AssetHub is designed with clean interfaces so you can swap components to match your corporate infrastructure. Every external dependency has an abstraction layer.

### Identity & Authentication

**Default:** Keycloak 26 as the OIDC provider, with a custom realm (`media`) imported on first start.

#### Authentication Flow

A `PolicyScheme` named "Smart" routes requests based on the `Authorization` header — `Bearer` tokens go to JWT Bearer validation, all other requests use Cookie authentication backed by OIDC.

| Scheme | When Used | Details |
|--------|-----------|---------|
| **Cookie** | Blazor UI (browser) | `__Host.assethub.auth`, SameSite=Strict, HttpOnly, SecurePolicy conditional (SameAsRequest in dev, Always in prod) |
| **JWT Bearer** | API clients | Validates issuer, audience (`assethub-app`, `account`), lifetime. NameClaimType = `preferred_username` |
| **OIDC** | Login redirect | Authorization Code + PKCE, scopes: `openid profile email`, `SaveTokens`, `GetClaimsFromUserInfoEndpoint`, `MapInboundClaims = false` |

#### Keycloak Role Mapping

On `OnTokenValidated`, roles are extracted from both `realm_access.roles` and `resource_access.assethub-app.roles` in the Keycloak token JSON and mapped to standard `ClaimTypes.Role` claims. This enables ASP.NET Core's `User.IsInRole()`.

#### Authorization Policies

| Policy | Roles Allowed | Used By |
|--------|--------------|---------|
| FallbackPolicy | Any authenticated user | Default for all endpoints (anonymous requires `.AllowAnonymous()`) |
| `RequireViewer` | viewer, contributor, manager, admin | General access |
| `RequireContributor` | contributor, manager, admin | Collection creation |
| `RequireManager` | manager, admin | Management operations |
| `RequireAdmin` | admin only | All `/api/admin/*` endpoints |

#### OIDC Error Handling

`OnRemoteFailure` and `OnAuthenticationFailed` redirect to `/?authError=` with specific error codes instead of showing raw exceptions. The `kc_action` parameter is forwarded to Keycloak for action-specific flows (e.g., password change).

#### Keycloak Admin API Dependency

Beyond OIDC authentication, the application uses the Keycloak Admin REST API via `IKeycloakUserService` for:
- User creation with role assignment
- Password resets
- User deletion
- Realm role queries

`IUserLookupService` resolves user IDs to usernames/emails by querying the Keycloak database directly. `IUserSyncService` detects and cleans up references to users deleted from Keycloak.

#### Replacing Keycloak

OIDC authentication is standard and works with any compliant provider by changing `Keycloak__Authority`, `ClientId`, and `ClientSecret`. However, the admin operations require new implementations of `IKeycloakUserService` and `IUserLookupService` for your identity provider's management API.

Alternatively, Keycloak supports AD/LDAP user federation if you want to keep it as an authentication broker while using corporate directories.

---

### Object Storage

| Default | Interface | Corporate Alternatives |
|---------|-----------|----------------------|
| **MinIO** (S3 API) | `IMinIOAdapter` | AWS S3, Azure Blob Storage, Google Cloud Storage, NetApp StorageGRID |

#### Interface

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

#### How to Replace

Implement `IMinIOAdapter` for your storage backend and swap the DI registration. All file operations go through this single interface — zero code changes needed elsewhere.

#### Implementation Details

- **Dual-client architecture** — An internal MinIO client handles server-side operations (upload, download, delete, stat), while a separate public client generates presigned URLs that browsers access directly. This allows internal and external endpoints to differ (e.g., `minio:9000` internally vs `storage.corp.com` externally).
- **Presigned URL caching** — Download URLs are cached in-memory for 75% of their expiry time to reduce calls to MinIO.
- **Range downloads** — `DownloadRangeAsync` reads file headers (magic bytes) without pulling the entire object, used for content-type validation on upload.
- **Idempotent deletes** — `DeleteAsync` silently ignores `ObjectNotFoundException` / `BucketNotFoundException` so callers don't need to check existence first.
- **StorageException** — All MinIO SDK, network, and socket errors are wrapped in a `StorageException` with user-friendly messages, keeping infrastructure details out of the application layer.
- **Filename sanitisation** — Presigned download URLs with `forceDownload` strip control characters and quotes from filenames to prevent Content-Disposition header injection.

---

### Database

**Default:** PostgreSQL 16 via EF Core with the Npgsql provider.

#### Schema

| Entity | PostgreSQL-Specific Features | Notes |
|--------|----------------------------|-------|
| `Assets` | `Tags` (text[]), `MetadataJson` (jsonb) | GIN index on Tags for array containment queries; custom ValueComparers for change tracking on JSON columns |
| `Collections` | — | Case-insensitive unique index on Name (`lower("Name")`) |
| `CollectionAcls` | — | Unique composite index on (CollectionId, PrincipalType, PrincipalId) |
| `AssetCollections` | — | Many-to-many join table with unique (AssetId, CollectionId) |
| `Shares` | `PermissionsJson` (jsonb), `TokenHash` (unique index) | Polymorphic scope via ScopeType/ScopeId (referential integrity enforced by database trigger) |
| `AuditEvents` | `DetailsJson` (jsonb) | Composite index on (EventType, CreatedAt) for filtered pagination |
| `ZipDownloads` | — | Indexed on Status and ExpiresAt for cleanup jobs |
| `DataProtectionKeys` | — | ASP.NET Data Protection key ring (via `IDataProtectionKeyContext`) |

#### PostgreSQL-Specific Dependencies

- **3 jsonb columns** with serialization/deserialization converters and custom `ValueComparer` implementations
- **GIN index on Tags** — enables efficient array containment queries on the native `text[]` column
- **`pg_trgm` extension** — installed via migration, enables trigram-based fuzzy search
- **`EF.Functions.ILike()`** — case-insensitive pattern matching for asset search (title and description)
- **`EnableDynamicJson()`** — NpgsqlDataSource configuration for JSON column support
- **Connection pool tuning** — MaxPoolSize reduced to 50 (from Npgsql default of 100), connection timeout 15s

#### Migrations

Code-first, conditionally applied on startup. Both the API and Worker hosts call `Database.MigrateAsync()` when `Database:AutoMigrate` is `true` (default in development). In production, `AutoMigrate` is `false` — pending migrations are logged as warnings and must be applied manually. Currently 16 migrations from initial schema through to native array tags.

#### Replacing PostgreSQL

Requires changing the EF Core provider (e.g., `UseSqlServer()`), replacing all jsonb columns with the target database's JSON support, rewriting the `pg_trgm` search migration, replacing `ILike` calls with provider-appropriate equivalents, and regenerating migrations. This is a significant effort due to the deep use of PostgreSQL-native features.

---

### Email

**Default:** `SmtpEmailService` using `System.Net.Mail.SmtpClient`, wrapped in the `smtp` Polly pipeline (retry on transient failures). Dev environment uses Mailpit for email capture.

#### Interface

```csharp
public interface IEmailService
{
    Task SendEmailAsync(string to, IEmailTemplate template, CancellationToken ct);
    Task SendEmailAsync(IEnumerable<string> recipients, IEmailTemplate template, CancellationToken ct);
}
```

The service is template-driven — email content is defined by `IEmailTemplate` implementations, not by the service itself. Each message is sent as multipart/alternative with both HTML and plain-text views. Empty or whitespace-only recipients are silently filtered (logged as warning).

#### Template Architecture

- `IEmailTemplate` — interface with `Subject`, `GetHtmlBody()`, `GetPlainTextBody()`
- `EmailTemplateBase` — abstract base class providing a branded responsive HTML layout (header with app name + brand color, body, footer). Subclasses override `GetContentHtml()` and `GetContentPlainText()` only.
- `WelcomeEmailTemplate` — sent when an admin creates a new user (includes username, temporary password, login URL, getting-started instructions)
- `ShareCreatedEmailTemplate` — sent when a share link is created (includes share URL, password, content name, expiry date, sender name)

Add new email types by creating a new `EmailTemplateBase` subclass — no changes to `IEmailService` needed.

#### Configuration

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

#### Replacing SMTP

Implement `IEmailService` and register it in DI. The interface is transport-agnostic — a replacement could use SendGrid, AWS SES, or any other email API.

---

### Malware Scanning

**Default:** ClamAV via raw TCP (clamd `INSTREAM` protocol with length-prefixed chunked streaming). Health checks use `PING`/`PONG` and bypass the Polly pipeline to fail fast.

#### Interface

```csharp
public interface IMalwareScannerService
{
    Task<MalwareScanResult> ScanAsync(Stream stream, string fileName, CancellationToken ct);
    Task<MalwareScanResult> ScanAsync(byte[] data, string fileName, CancellationToken ct);
    Task<bool> IsAvailableAsync(CancellationToken ct);
}
```

`MalwareScanResult` is a record with `ScanCompleted`, `IsClean`, `ThreatName`, and `ErrorMessage` properties plus static factory methods (`Clean()`, `Infected(name)`, `Failed(msg)`, `Skipped()`).

#### Upload Behavior

- Scanning runs synchronously during both regular and presigned uploads, *before* the asset is queued for media processing
- **Disabled** (`Enabled: false`) — `Skipped()` is returned and the file is allowed through
- **Scanner unreachable** — `Failed()` is returned (`ScanCompleted = false`) and the **upload is rejected**
- **Malware detected** — upload rejected, file deleted (for presigned uploads), and an `asset.malware_detected` audit event is logged with the threat name
- Stream position is reset between retries for seekable streams

#### Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `ClamAV__Enabled` | `false` | Enable/disable scanning entirely |
| `ClamAV__Host` | `clamav` | clamd hostname |
| `ClamAV__Port` | `3310` | clamd TCP port |
| `ClamAV__TimeoutMs` | `30000` | TCP send/receive timeout |
| `ClamAV__ChunkSize` | `8192` | INSTREAM chunk size in bytes |

#### Replacing ClamAV

Implement `IMalwareScannerService` with your scanner's SDK or API. The interface is protocol-agnostic — the ClamAV implementation uses raw TCP, but a replacement could use HTTP, gRPC, or any other transport.

---

### Background Jobs & Messaging

| Default | Interface | Corporate Alternatives |
|---------|-----------|----------------------|
| **Wolverine + RabbitMQ** | Wolverine command/event bus | MassTransit, NServiceBus, Azure Service Bus |

#### Message Architecture

The API and Worker communicate via RabbitMQ queues using Wolverine as the messaging framework:

**Commands (API → Worker):**
- `ProcessImageCommand` → `process-image` queue — extract metadata, generate thumbnail + medium rendition
- `ProcessVideoCommand` → `process-video` queue — extract metadata, generate poster frame
- `BuildZipCommand` → `build-zip` queue — build ZIP archive from collection assets

**Events (Worker → API):**
- `AssetProcessingCompletedEvent` → `asset-processing-completed` queue — updates asset with renditions + metadata
- `AssetProcessingFailedEvent` → `asset-processing-failed` queue — marks asset as Failed

#### Scheduled Cleanup (IHostedService)

- **StaleUploadCleanupService** — daily ~3:00 AM UTC, deletes assets stuck in "Uploading" status > 24h
- **OrphanedSharesCleanupService** — weekly Sundays ~4:00 AM UTC, removes shares with deleted assets/collections
- **AuditRetentionService** — weekly Sundays ~5:00 AM UTC, deletes audit events older than retention period
- **ZipCleanupBackgroundService** (API) — hourly, removes expired ZIP downloads from MinIO
- **UserSyncBackgroundService** (API) — daily, syncs users deleted in Keycloak

The Worker runs as a **separate container** (`AssetHub.Worker`) so it can be scaled independently from the API. It shares the same Infrastructure layer but has its own Dockerfile with ImageMagick and ffmpeg pre-installed.

#### Wolverine Configuration

Both API and Worker configure Wolverine with:
- Auto-provisioned RabbitMQ queues
- Retry policy with cooldown: 1s, 2s, 5s, 10s, 30s delays
- `AutoApplyTransactions()` — wraps message handlers in EF Core transactions

#### Replacing Wolverine/RabbitMQ

The messaging pattern is standard command/event with dedicated queues. Replace Wolverine with MassTransit or NServiceBus by implementing equivalent consumers for the same message types. The `IMediaProcessingService` interface abstracts the enqueueing.

---

### Media Processing

**Tools:** ImageMagick (images) + ffmpeg (video), running in the Worker container.

#### Interface

```csharp
public interface IMediaProcessingService
{
    Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, CancellationToken ct);
    Task ProcessImageAsync(Guid assetId, string originalObjectKey, CancellationToken ct);
    Task ProcessVideoAsync(Guid assetId, string originalObjectKey, CancellationToken ct);
}
```

`ScheduleProcessingAsync` publishes a Wolverine command (`ProcessImageCommand` or `ProcessVideoCommand`) to RabbitMQ based on the asset type and returns a job ID. Non-image/video types (documents, etc.) are marked Ready immediately with no processing.

#### Rendition Output

| Asset Type | Renditions | Extras |
|------------|-----------|--------|
| **Image** | Thumbnail (200x200 JPEG) + Medium (800x800 JPEG) | EXIF/IPTC/GPS metadata extraction via MetadataExtractor; auto-populates Copyright field from EXIF if not already set |
| **Video** | Poster frame (800px wide JPEG, extracted at second 5) | — |
| **Other** | None (marked Ready immediately) | — |

All dimensions, JPEG quality, and poster frame timing are configurable via `ImageProcessing__*` environment variables.

#### ImageMagick Processing Pipeline

Auto-orient (EXIF rotation), flatten transparency to white, convert to sRGB, resize preserving aspect ratio (only shrinks), strip metadata from output, first frame only for animated images.

#### Failure Handling

On error, the asset is marked Failed with a user-visible message and an `asset.processing_failed` audit event is logged (including error type and message). Temp files are cleaned up in a `finally` block regardless of success or failure.

#### Security Hardening

- Both ImageMagick and ffmpeg have a hard **5-minute process timeout** — the process tree is killed if exceeded
- A custom `imagemagick-policy.xml` restricts processing to raster formats only. Disabled coders: SVG, MVG, MSL, PS/EPS/PDF (Ghostscript), TEXT/LABEL, XPS, URL/HTTP/HTTPS/FTP (SSRF prevention), ephemeral, X11. Also blocks `@*` path patterns and the gnuplot delegate.
- Resource limits: 16KP max dimensions, 128MP max area, 256 MiB memory, 2 GiB disk, 120s per-operation timeout, 4 threads
- The Worker container runs with `cap_drop: ALL`, `read_only: true`, `no-new-privileges`, and a 2 GB tmpfs at `/tmp` for transient processing files

---

## Service Interface Reference

### Infrastructure Adapters

Swappable implementations for external dependencies:

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

### Application Services

Core business logic:

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
| `IZipBuildService` | `ZipBuildService` | Async zip archive building via Wolverine + RabbitMQ |
| `IUserAdminService` | `UserAdminService` | Admin user operations (create, delete, reset password) |
| `IUserProvisioningService` | `UserProvisioningService` | Provision new users with default collection access |
| `IUserCleanupService` | `UserCleanupService` | Remove all ACLs and revoke shares for a deleted user |
| `IDashboardService` | `DashboardService` | Dashboard statistics (asset/collection/share counts) |
| `IAuditQueryService` | `AuditQueryService` | Query audit log (paginated + legacy endpoints) |

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

### How It Works in Practice

1. A MinIO upload fails with a socket timeout — Polly retries up to 3 times with exponential backoff (1 s, 2 s, 4 s)
2. If MinIO keeps failing (50%+ failure rate over 30 seconds), the circuit breaker opens and subsequent calls fail fast for 30 seconds instead of waiting for timeouts
3. After the break duration, the circuit moves to half-open — a single probe request decides whether to close or re-open the breaker

This pattern keeps the application responsive even when downstream services are degraded, and prevents a failing dependency from exhausting thread pool resources.
