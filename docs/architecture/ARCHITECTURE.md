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
│  HOST (single process, single composition root)                             │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  AssetHub.Api                                                         │  │
│  │  ┌───────────────┐ ┌─────────────────┐ ┌───────────────────────────┐  │  │
│  │  │ Blazor Server │ │ 13 media/ZIP    │ │ In-process message        │  │  │
│  │  │ (MudBlazor 8) │ │ (internal REST) │ │ handlers + background      │  │  │
│  │  │ Local Identity│ │ Cookie auth     │ │ services (ImageMagick,     │  │  │
│  │  │ (cookie)      │ │                 │ │ ffmpeg) + outbox drain     │  │  │
│  │  └───────────────┘ └─────────────────┘ └───────────────────────────┘  │  │
│  │        Channel bus (System.Threading.Channels) — no external broker   │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
                                   │
┌───────────────────────────────────▼─────────────────────────────────────────┐
│  APPLICATION LAYER  (AssetHub.Application)                                  │
│                                                                             │
│  Service interfaces:                                                         │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │ Assets       │ │ Collections  │ │ Shares       │ │ Users (local Identity)││
│  │ Query,Upload │ │ CRUD, ACL,   │ │ Public,Auth, │ │ Admin, Lookup,       ││
│  │ Search,Trash │ │ Authorization│ │ Admin access │ │ Provision, Cleanup   ││
│  │ Del          │ │              │ │              │ │                      ││
│  └──────────────┘ └──────────────┘ └──────────────┘ └──────────────────────┘│
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │ IMinIOAdapter│ │ IEmailService│ │ IMalware-    │ │ IUserLookupService   ││
│  │ IMediaProc.  │ │ IAuditService│ │ ScannerSvc   │ │ IUserDirectoryAdmin  ││
│  │ IZipBuildSvc │ │ IDashboardSvc│ │ IAppMsgBus   │ │ (ASP.NET Identity)   ││
│  └──────────────┘ └──────────────┘ └──────────────┘ └──────────────────────┘│
│                        ▲ INTERFACES — SWAP IMPLEMENTATIONS ▲                │
└────────────────────────┼────────────────────────────────────────────────────┘
              ┌──────────┘
│  DOMAIN (AssetHub.Domain) — Entities: Asset, Collection, CollectionAcl,     │
│  AssetCollection, Share, AuditEvent, ZipDownload, OutboxMessage,            │
│  OrphanedObject + enums                                                     │
└─────────────────────────────────────────────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────────────────────┐
│  INFRASTRUCTURE LAYER  (AssetHub.Infrastructure)                            │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐│
│  │ MinIOAdapter │ │ SmtpEmail    │ │ ClamAv       │ │ ASP.NET Core Identity││
│  │ (dual client)│ │ Service      │ │ ScannerSvc   │ │ stores (EF Core)     ││
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘ └──────────┬───────────┘│
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐                         │
│  │ EF Core +    │ │ MediaProc.   │ │ Polly        │  All external calls     │
│  │ Repositories │ │ Service      │ │ Pipelines    │  wrapped in resilience  │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘  pipelines              │
└─────────┼────────────────┼────────────────┼─────────────────────────────────┘
          │                │                │
┌─────────▼────────────────▼────────────────▼─────────────────────────────────┐
│  EXTERNAL SERVICES (Docker containers)                                      │
│                                                                             │
│  Essential (default stack):                                                 │
│  ┌──────────────┐ ┌──────────────┐                                          │
│  │  PostgreSQL  │ │    MinIO     │   (identity, cache, and messaging are    │
│  │  16 (+ EF)   │ │  (S3 API)   │    all in-process — no broker, no Redis,  │
│  │              │ │              │    no separate identity provider)        │
│  └──────────────┘ └──────────────┘                                          │
│  Optional (`full` compose profile):                                         │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────────────────────────┐│
│  │    ClamAV    │ │   Mailpit    │ │  Aspire Dashboard                    ││
│  │  (clamd TCP) │ │  (SMTP, dev) │ │  (traces, metrics, logs via OTLP)    ││
│  └──────────────┘ └──────────────┘ └──────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Project Structure

The solution is split into five projects following Clean Architecture, plus three test projects:

```
AssetHub.sln
│
├── src/
│   ├── AssetHub.Domain/            # Entities, enums, value objects — zero dependencies
│   ├── AssetHub.Application/       # Service interfaces, DTOs, constants, config, business rules
│   ├── AssetHub.Infrastructure/    # EF Core, MinIO, SMTP, ClamAV, local Identity implementations
│   ├── AssetHub.Api/               # ASP.NET Core host — Blazor, auth, DI, in-process handlers, background jobs, 13 media/ZIP endpoints
│   ├── AssetHub.Ui/                # Blazor Server components, pages, layouts (Razor Class Library)
│
├── tests/
│   ├── AssetHub.Tests/             # Integration + unit tests (xUnit, Testcontainers, Moq)
│   ├── AssetHub.Ui.Tests/          # Blazor component tests (bUnit)
│   └── E2E/                        # End-to-end tests (Playwright, TypeScript)
│
├── docker/
│   ├── docker-compose.yml          # Development stack (3 essential services; `full` profile adds ClamAV/Mailpit/Aspire)
│   ├── docker-compose.prod.yml     # Production stack (hardened, internal networking)
│   ├── Dockerfile                  # API multi-stage build
│   ├── imagemagick-policy.xml      # Restrictive ImageMagick security policy
│   ├── backup.sh                   # Full backup script (PostgreSQL, MinIO)
│   ├── restore.sh                  # Companion restore script with confirmation
│   ├── reverse-proxy/
│   │   ├── caddy/Caddyfile         # Production Caddy config (auto-TLS, WebSocket, security headers)
│   │   └── nginx/nginx.conf        # Production Nginx config (manual TLS, WebSocket, security headers)
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
```

- **Domain** — no dependencies. Pure entities, enums, and value objects.
- **Application** — depends on Domain. Defines all service interfaces, DTOs, constants, and configuration models. This is the contract layer that outer layers implement or consume.
- **Infrastructure** — depends on Application + Domain. Contains all concrete implementations: EF Core repositories, MinIO adapter, SMTP email, ClamAV scanner, ASP.NET Core Identity stores, media processing, and Polly resilience pipelines.
- **Ui** — depends on Application only (no Infrastructure reference). A Razor Class Library containing all Blazor Server components, pages, and layouts. Communicates with infrastructure exclusively through Application interfaces.
- **Api** — the single composition root, references all projects including Ui. Wires up dependency injection, configures authentication, runs every in-process message handler and background job, hosts the Blazor Server app, and exposes the 13 remaining HTTP endpoints. Those exist only for what the browser must fetch directly (media bytes, ZIP job status); contract-023 removed the other 58, which duplicated over HTTP what the in-process facade already did.

---

## Modular Components

AssetHub is designed with clean interfaces so you can swap components to match your corporate infrastructure. Every external dependency has an abstraction layer.

### Identity & Authentication

**Default:** Local **ASP.NET Core Identity** — the app owns its own user store in PostgreSQL. There is no external identity provider; the 2026-08 reshape (contract-015) removed Keycloak/OIDC and there is no `Auth:Provider` switch.

#### Authentication Flow

There is one authentication scheme: the Identity application cookie (`IdentityConstants.ApplicationScheme`) is both the default and the challenge scheme (`AuthenticationExtensions.AddIdentityProvider`). The browser signs in through the app's own form and Identity issues the cookie. No bearer handler is registered and there is no scheme selector — nothing issues or accepts tokens.

| Scheme | When Used | Details |
|--------|-----------|---------|
| **Cookie** | Blazor UI (browser) | Identity application cookie (`Identity.Application`). Host-scoped `__Host-` prefix in production (Secure, Path=/, no Domain); prefix dropped in dev because the dev cookie isn't Secure over HTTP. SameSite=Strict, HttpOnly |

#### Role Claims

Identity roles (`viewer`, `contributor`, `manager`, `admin`) are stored in the Identity role store and surface as standard `ClaimTypes.Role` claims, enabling ASP.NET Core's `User.IsInRole()`. Nothing downstream of the claims principal knows how the user signed in — `RoleHierarchy`, the authorization policies, and `CollectionAuthorizationService` are provider-agnostic.

#### Authorization Policies

| Policy | Roles Allowed | Used By |
|--------|--------------|---------|
| FallbackPolicy | Any authenticated user | Default for all endpoints (anonymous requires `.AllowAnonymous()`) |
| `RequireViewer` | viewer, contributor, manager, admin | General access |
| `RequireContributor` | contributor, manager, admin | Collection creation |
| `RequireManager` | manager, admin | Management operations |
| `RequireAdmin` | admin only | Admin operations |

#### Seeding

`IdentitySeeder` creates the four roles and — **only when the user store is completely empty** — one bootstrap admin from `Identity:SeedAdmin`. It never overwrites an existing account and throws rather than inventing a default password. In development the same empty-store path optionally seeds a fixed-credential test viewer (`Identity:SeedTestViewer`, off by default, on in the dev compose) so the E2E gate runs from nothing; production never enables it.

#### User Management

`IUserLookupService` (reads: resolve IDs to usernames/emails) and `IUserDirectoryAdmin` (lifecycle + role assignment) are backed by the local Identity stores. **Password reset** (`PasswordResetLinkSender`) mints an Identity reset token, encodes it into a `/reset-password` link, and mails it via `IEmailService`. Its security properties are load-bearing: single-use (token derives from the rotated security stamp), expiring (24h), never logged (only the user id reaches the log), and non-enumerating (`/auth/forgot-password` always reports success).

#### Replacing the Identity store

Because auth is local, "replacing" it means implementing `IUserLookupService` and `IUserDirectoryAdmin` against a different backing store, or reintroducing an external provider behind the same claims principal. Any replacement must keep the four role claims and the provider-agnostic authorization surface intact.

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

Code-first, conditionally applied on startup. The API host calls `Database.MigrateAsync()` when `Database:AutoMigrate` is `true` (default in development). In production, `AutoMigrate` is `false` — pending migrations are logged as warnings and must be applied manually. The history starts from a single squashed `InitialCreate` migration: contract-029 (C16) collapsed the prior 47 into one, re-applying the raw `pg_trgm` extension, `tsvector` search function/triggers, and column defaults by hand in its `Up`. It is followed by `RemoveAssetVersioning` (contract-034), which drops the versioning schema.

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

Background work runs **inside the Api process** on an **in-process channel bus** — `System.Threading.Channels`, not a broker. RabbitMQ/Wolverine and the separate Worker host were removed by the reshape (contract-026 / C13b, contract-019). There is nothing external to provision, and nothing to swap.

#### Message Architecture

The publisher writes a message to `IAppMessageBus` (`InProcessMessageBus`, an unbounded channel); `MessageDispatcherService` (a `BackgroundService`) reads the channel and routes each message to its handler through an **explicit `switch`** over the six message types, running each in its own DI scope and re-publishing any events the handler returns.

**Commands:**
- `ProcessImageCommand` → `ProcessImageHandler` — extract metadata, generate thumbnail + medium rendition
- `ProcessVideoCommand` → `ProcessVideoHandler` — extract metadata, generate poster frame
- `ProcessAudioCommand` → `ProcessAudioHandler` — extract metadata
- `BuildZipCommand` → `BuildZipHandler` — build ZIP archive from collection/share assets

**Events (emitted by the processing handlers):**
- `AssetProcessingCompletedEvent` → `AssetProcessingCompletedHandler` — updates the asset with renditions + metadata
- `AssetProcessingFailedEvent` → `AssetProcessingFailedHandler` — marks the asset as Failed

#### Durability (in-app, not in a broker)

Because the channel is in memory, durability is provided by the application:
- **Transactional outbox** — `OutboxMessage` + `IOutboxPublisher` record a message in the *same* SQL transaction as the state change; `OutboxDrainService` drains committed rows onto the bus. A crash between commit and publish cannot lose the message.
- **Stuck-Processing reaper** — `StuckProcessingReaperService` re-enqueues any asset left in `Processing` past 5 minutes, recovering an in-flight media message a restart dropped.
- **Handler retry** — the dispatcher retries a failing handler on a 1-2-5-10-30s cooldown; after the last attempt it drops the message with a logged error (media assets are then recovered by the reaper; a lost ZIP job is re-clickable).

#### Scheduled Cleanup (BackgroundService + PeriodicTimer)

- **StaleUploadCleanupService** — deletes assets stuck in "Uploading" status past the threshold
- **TrashPurgeBackgroundService** — hard-deletes soft-deleted assets past the trash retention window
- **OrphanedSharesCleanupService** — removes shares whose asset/collection is gone
- **OrphanedObjectsSweeperService** — deletes MinIO objects orphaned by asset purges (via the `OrphanedObject` tombstone table)
- **AuditRetentionService** — deletes audit events older than the configured per-event retention
- **ZipCleanupBackgroundService** — removes expired ZIP downloads from MinIO
- **OutboxDrainService / StuckProcessingReaperService** — the durability services above

Media processing runs in the Api container, which ships ImageMagick and ffmpeg. New background work goes in the Api — there is nowhere else for it to go.

---

### Media Processing

**Tools:** ImageMagick (images) + ffmpeg (video), running in the API container.

#### Interface

```csharp
public interface IMediaProcessingService
{
    Task<string> ScheduleProcessingAsync(Guid assetId, string assetType, string originalObjectKey, CancellationToken ct);
    Task ProcessImageAsync(Guid assetId, string originalObjectKey, CancellationToken ct);
    Task ProcessVideoAsync(Guid assetId, string originalObjectKey, CancellationToken ct);
}
```

`ScheduleProcessingAsync` publishes a command (`ProcessImageCommand`, `ProcessVideoCommand`, or `ProcessAudioCommand`) to the in-process channel bus based on the asset type and returns a job ID. Non-media types (documents, etc.) are marked Ready immediately with no processing.

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
- The API container runs with `cap_drop: ALL`, `read_only: true`, `no-new-privileges`, and a 2 GB tmpfs at `/tmp` for transient processing files

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
| `IUserDirectoryAdmin` | `IdentityUserDirectoryAdmin` | Local Identity user lifecycle (create, reset password, delete, assign roles) |
| `IUserLookupService` | `IdentityUserLookupService` | Resolve user IDs to usernames/emails, check existence |
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
| `IZipBuildService` | `ZipBuildService` | Async zip archive building via the in-process channel bus |
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
| `clamav` | ClamAvScannerService | 2 attempts, constant 500 ms | Opens at 50% failure over 60 s (min 3 calls), 60 s break | Handles `SocketException` on the raw TCP clamd connection |
| `smtp` | SmtpEmailService | 2 attempts, exponential from 2 s | — | Retry-only; handles `SmtpException` and `SocketException` |
| `postgres` | EF Core (Npgsql) | Built-in `EnableRetryOnFailure()` | — | Handles transient database connection failures at the provider level |

### How It Works in Practice

1. A MinIO upload fails with a socket timeout — Polly retries up to 3 times with exponential backoff (1 s, 2 s, 4 s)
2. If MinIO keeps failing (50%+ failure rate over 30 seconds), the circuit breaker opens and subsequent calls fail fast for 30 seconds instead of waiting for timeouts
3. After the break duration, the circuit moves to half-open — a single probe request decides whether to close or re-open the breaker

This pattern keeps the application responsive even when downstream services are degraded, and prevents a failing dependency from exhausting thread pool resources.
