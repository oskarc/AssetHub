# Security

AssetHub implements defense-in-depth security across multiple layers. This document covers authentication, authorization, input validation, data protection, container hardening, and network security.

---

## Table of Contents

- [Authentication & Authorization](#authentication--authorization)
- [Role-Based Access Control](#role-based-access-control)
- [Rate Limiting](#rate-limiting)
- [Upload Security](#upload-security)
- [Data Protection](#data-protection)
- [Container Hardening](#container-hardening)
- [Network Security](#network-security)
- [Audit & Observability](#audit--observability)
- [Infrastructure Security](#infrastructure-security)
- [API Reference](#api-reference)

---

## Authentication & Authorization

| Feature | Implementation |
|---------|---------------|
| **OIDC with PKCE** | Authorization Code flow with Proof Key for Code Exchange (no implicit grant) |
| **Smart auth routing** | Requests with `Authorization: Bearer` header route to JWT validation; all others use cookie auth |
| **Cookie security** | `__Host.` prefix, `SameSite=Strict`, `HttpOnly=true`, `Secure=Always` in production (`SameAsRequest` in dev) |
| **JWT Bearer** | For API clients, with explicit issuer, audience, and lifetime validation |
| **Fallback policy** | All endpoints require authentication by default — anonymous access must be explicitly allowed with `.AllowAnonymous()` |
| **Brute force protection** | Keycloak locks accounts after 5 failed login attempts (15 min lockout) |

### Authentication Schemes

The application uses a `PolicyScheme` named "Smart" that inspects each request:

- **`Authorization: Bearer <token>`** — Routes to JWT Bearer validation. Tokens are validated against the Keycloak issuer, with audience checks for `assethub-app` and `account`. The `preferred_username` claim is mapped to `NameClaimType`.
- **All other requests** — Routes to Cookie authentication backed by OIDC. The cookie name is `__Host.assethub.auth` with strict security settings.
- **OIDC login** — Uses Authorization Code + PKCE with scopes `openid profile email`. Tokens are saved, claims are fetched from the UserInfo endpoint, and inbound claims are not remapped.

### Keycloak Role Mapping

On `OnTokenValidated`, roles are extracted from both `realm_access.roles` and `resource_access.assethub-app.roles` in the Keycloak token JSON and mapped to standard `ClaimTypes.Role` claims. This enables ASP.NET Core's `User.IsInRole()` and policy-based authorization.

### Authorization Policies

| Policy | Roles Allowed | Used By |
|--------|--------------|---------|
| FallbackPolicy | Any authenticated user | Default for all endpoints |
| `RequireViewer` | viewer, contributor, manager, admin | General resource access |
| `RequireContributor` | contributor, manager, admin | Collection creation, uploads |
| `RequireManager` | manager, admin | Management operations |
| `RequireAdmin` | admin only | All `/api/admin/*` endpoints |

### Error Handling

`OnRemoteFailure` and `OnAuthenticationFailed` redirect to `/?authError=` with specific error codes instead of exposing raw exception details. The `kc_action` parameter is forwarded to Keycloak for action-specific flows (e.g., password change).

---

## Role-Based Access Control

Roles are assigned **per collection** through Access Control Lists. Higher roles inherit all lower permissions.

| Role | View | Upload | Edit Assets | Share | Edit Collection | Delete | Manage Access | Admin Panel |
|------|:----:|:------:|:-----------:|:-----:|:---------------:|:------:|:-------------:|:-----------:|
| Viewer | Yes | | | | | | | |
| Contributor | Yes | Yes | Yes | Yes | | | | |
| Manager | Yes | Yes | Yes | Yes | Yes | Yes | Yes | |
| Admin | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes |

### Key Concepts

- **Per-collection roles** — A user can hold different roles on different collections. There is no single global role (except Admin, which has system-wide access).
- **Collection-based permissions** — Assets inherit permissions from their collections. Access is never granted per-asset.
- **Multi-collection assets** — Assets can belong to multiple collections simultaneously (many-to-many). A user has access to an asset if they have a role on *any* collection containing it.
- **Centralized role logic** — The role hierarchy is defined in `RoleHierarchy.cs`. All permission checks call static methods like `CanUpload()`, `CanDelete()`, `CanManageAccess()` so the logic is never duplicated across the codebase.
- **Level-guarded ACL operations** — You can only grant or revoke roles at or below your own level. A Contributor cannot promote someone to Manager. A Manager cannot grant Admin access.
- **Admin override** — Users with the `admin` Keycloak role bypass per-collection checks and have full access to all resources.

---

## Rate Limiting

Rate limiting is applied at multiple levels to protect against abuse:

| Policy | Scope | Limit | Purpose |
|--------|-------|-------|---------|
| Global (authenticated) | Per user | 200 requests/min | Prevent API abuse from authenticated users |
| BlazorSignalR | Per IP | 60 connections/min | Protect the SignalR hub from connection floods |
| ShareAnonymous | Per IP | 30 requests/min | Limit anonymous share link access |
| SharePassword | Per IP | 10 attempts/5 min | Brute force protection for share passwords |

Rate limiting uses ASP.NET Core's built-in rate limiting middleware with fixed window policies. When a client exceeds the limit, they receive a `429 Too Many Requests` response.

The SharePassword policy is intentionally aggressive — 10 attempts per 5 minutes per IP — to make password guessing impractical while still allowing legitimate users who mistype their password.

---

## Upload Security

Multiple validation layers protect against malicious uploads:

| Check | Purpose | Details |
|-------|---------|---------|
| **Content-type allowlist** | Block dangerous file types | Only images, videos, audio, documents, and safe archive types. SVGs are blocked due to embedded script/XSS risk. |
| **Magic byte validation** | Prevent content-type spoofing | The first bytes of the uploaded file are compared against known signatures. A file claiming to be `image/jpeg` must have JPEG magic bytes. Uses `DownloadRangeAsync` to read only the header, not the entire file. |
| **ClamAV scanning** | Malware detection | Scans run synchronously *before* any processing. Infected files are rejected and deleted. Scan failures also reject the upload (fail-closed). |
| **File size limits** | Resource protection | Configurable max upload size (default 500 MB). Enforced at both the Kestrel and application layers. |
| **Batch limits** | Resource protection | Maximum 10 files per upload request to prevent resource exhaustion. |
| **Filename sanitisation** | Header injection prevention | Filenames in Content-Disposition headers have control characters and quotes stripped. |

### Presigned Upload Flow

For large files, the client requests a presigned upload URL from the API, uploads directly to MinIO, then confirms the upload. The confirmation step runs the same validation pipeline (content-type, magic bytes, ClamAV) on the already-uploaded file. If validation fails, the file is deleted from storage.

---

## Data Protection

| Feature | Implementation |
|---------|---------------|
| **Share tokens** | Encrypted at rest using ASP.NET Data Protection API. The token stored in the database is the encrypted form; the plain token is only returned to the user once at creation time. |
| **Share passwords** | Dual storage: BCrypt-hashed for validation (constant-time comparison), and separately encrypted with Data Protection for admin retrieval. Admins can view the original password through the admin panel. |
| **Access tokens** | Short-lived (30 min) signed tokens for embedded media (img/video src attributes). These allow the browser to fetch renditions without requiring cookie auth on every image request. |
| **Key storage** | Data Protection keys are stored in PostgreSQL via `IDataProtectionKeyContext` for multi-instance consistency. Both the API and Worker share the same key ring. |

### Token Lifecycle

1. **Share creation** — A cryptographically random token is generated, encrypted, and stored. The plain token is returned to the creator and included in the share URL.
2. **Share access** — The token from the URL is matched against stored encrypted tokens using the Data Protection API for decryption.
3. **Password authentication** — If the share has a password, the visitor must authenticate via `POST /api/shares/{token}/access-token`. On success, a short-lived signed access token is issued.
4. **Asset access** — Rendition URLs include the access token as a query parameter, allowing the browser to load images and videos without additional authentication prompts.

---

## Container Hardening

All containers are hardened in the production Docker Compose. The level varies by what each container needs:

| Container | `cap_drop: ALL` | `no-new-privileges` | `read_only` | `tmpfs` | Non-root User | PID Limit |
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

### Notes

- **ClamAV capabilities** — Requires `CHOWN`, `SETUID`, `SETGID`, `FOWNER`, and `DAC_OVERRIDE` for its entrypoint setup; all other capabilities are dropped.
- **Read-only root filesystem** — The API, Worker, Jaeger, and Prometheus containers run with read-only root filesystems. Writable storage is limited to tmpfs mounts and Docker volumes.
- **PID limits** — Prevent fork bombs and runaway process creation. Limits are set per-container based on expected workload.
- **Non-root users** — Every container runs as a non-root user. The API and Worker use the `app` user created in the Dockerfile. Infrastructure containers use their built-in non-root users.

---

## Network Security

### Headers

| Feature | Implementation |
|---------|---------------|
| **X-Forwarded-For protection** | Only trusted from RFC 1918 private networks (Docker bridge). Prevents IP spoofing via proxy headers from untrusted sources. |
| **Security headers** | `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy: camera=(), microphone=(), geolocation=()`, `X-XSS-Protection: 1; mode=block` |
| **Content Security Policy** | Production only — `default-src 'self'`, `script-src/style-src 'self' 'unsafe-inline'` (required by Blazor/MudBlazor), `img-src 'self' data: blob:`, `connect-src 'self' wss:` (SignalR), `frame-ancestors 'none'` |
| **HTTPS enforcement** | HSTS with 365-day max-age, subdomain inclusion, and preload in production |
| **Metrics endpoint** | IP-restricted to internal Docker network only via `MetricsIpRestrictionMiddleware` (RFC 1918 ranges) |

### Network Segmentation

The production Docker Compose uses two isolated Docker networks:

- **`backend`** — Data stores: PostgreSQL, MinIO, ClamAV, Keycloak
- **`observability`** — Monitoring: Jaeger, Prometheus, Grafana

The API and Worker bridge both networks. Other services cannot cross boundaries. Only the API port (7252) is exposed on `127.0.0.1` for the reverse proxy — all other service ports are internal-only.

### Reverse Proxy Hardening

When placing AssetHub behind a reverse proxy, block these paths from public access:

| Path | Reason | Mitigation |
|------|--------|------------|
| `/metrics` | Exposes runtime metrics, GC stats, request rates | App-level IP restriction + proxy block |
| `/health`, `/health/ready` | Internal health checks | Restrict to monitoring IPs |
| `/hangfire` | Background job dashboard | Restrict to admin IPs |

---

## Audit & Observability

| Component | Purpose |
|-----------|---------|
| **Audit trail** | Every action logged with user, timestamp, target entity, IP address, and user agent. Events include uploads, downloads, shares, access changes, deletions, and malware detections. |
| **Jaeger** | Distributed tracing via OpenTelemetry (OTLP export). Both API and Worker export trace spans. |
| **Prometheus** | Metrics collection via `/metrics` endpoint scraping every 15 seconds. |
| **Grafana** | Pre-configured dashboards for request rates, error rates, latency, upload volume, and infrastructure health. |
| **Structured logging** | Serilog with request enrichment (user, IP, request ID). JSON + compact format in production. |

### Audit Event Types

The audit system captures events across all major operations:

- `asset.uploaded`, `asset.downloaded`, `asset.deleted`, `asset.updated`, `asset.malware_detected`, `asset.processing_failed`
- `collection.created`, `collection.deleted`, `collection.updated`
- `acl.granted`, `acl.revoked`
- `share.created`, `share.revoked`, `share.accessed`, `share.password_authenticated`
- `user.created`, `user.deleted`, `user.password_reset`, `user.synced`
- `zip.requested`, `zip.completed`

Each event includes structured JSON details with context-specific data (e.g., asset name, collection, role granted, threat name for malware detections).

---

## Infrastructure Security

| Feature | Implementation |
|---------|---------------|
| **Docker log rotation** | 50 MB x 5 files per container (prevents disk exhaustion from log floods) |
| **Resource limits** | CPU, memory, and PID limits on all containers in production |
| **Health checks** | Liveness and readiness probes with start periods for slow services (ClamAV: 5 min, Keycloak: 2 min) |
| **Network segmentation** | Two isolated Docker networks (`backend` + `observability`); only API/Worker bridge both |
| **Credentials management** | Environment variables via `.env.template`; Docker file-based secrets documented as opt-in alternative |
| **ImageMagick policy** | Restrictive `policy.xml` disables SVG, MVG, MSL, Ghostscript, and network coders to prevent SSRF and server-side XSS |
| **Query limits** | Hard caps on admin queries to prevent unbounded memory use (CWE-400) |
| **Process timeouts** | ImageMagick and ffmpeg have 5-minute hard timeouts — process trees are killed if exceeded |

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
