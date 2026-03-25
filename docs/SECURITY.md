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
| `RequireAdmin` | admin only | All `/api/v1/admin/*` endpoints |

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
| **Client-side pre-validation** | Early rejection | Files are validated for type and size in the browser before upload, providing instant user feedback and reducing unnecessary server load. |
| **File size limits** | Resource protection | Configurable max upload size (default 1500 MB). Enforced at client-side, Kestrel, and application layers. |
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
3. **Password authentication** — If the share has a password, the visitor must authenticate via `POST /api/v1/shares/{token}/access-token`. On success, a short-lived signed access token is issued.
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
| Aspire Dashboard | Yes | Yes | — | — | Yes (built-in) | 100 |
| Mailpit (dev) | Yes | Yes | — | — | — | 50 |

### Notes

- **ClamAV capabilities** — Requires `CHOWN`, `SETUID`, `SETGID`, `FOWNER`, and `DAC_OVERRIDE` for its entrypoint setup; all other capabilities are dropped.
- **Read-only root filesystem** — The API and Worker containers run with read-only root filesystems. Writable storage is limited to tmpfs mounts and Docker volumes.
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

### Network Segmentation

The production Docker Compose uses two isolated Docker networks:

- **`backend`** — Data stores: PostgreSQL, MinIO, ClamAV, Keycloak
- **`observability`** — Monitoring: Aspire Dashboard

The API and Worker bridge both networks. Other services cannot cross boundaries. Only the API port (7252) is exposed on `127.0.0.1` for the reverse proxy — all other service ports are internal-only.

### Reverse Proxy Hardening

When placing AssetHub behind a reverse proxy, block these paths from public access:

| Path | Reason | Mitigation |
|------|--------|------------|
| `/health`, `/health/ready` | Internal health checks | Restrict to monitoring IPs |
| `/hangfire` | Background job dashboard | Restrict to admin IPs |

---

## Audit & Observability

| Component | Purpose |
|-----------|---------|
| **Audit trail** | Every action logged with user, timestamp, target entity, IP address, and user agent. Events include uploads, downloads, shares, access changes, deletions, and malware detections. |
| **Aspire Dashboard** | Traces, metrics, and structured logs via OpenTelemetry (OTLP export). Both API and Worker export telemetry. |
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
| **Credentials management** | File-based Docker secrets for all sensitive credentials in production; environment variables via `.env.template` for development |
| **ImageMagick policy** | Restrictive `policy.xml` disables SVG, MVG, MSL, Ghostscript, and network coders to prevent SSRF and server-side XSS |
| **Request validation** | Server-side `ValidationFilter` enforces DataAnnotation constraints on all endpoint DTOs, returning structured 400 errors |
| **Query limits** | Hard caps on admin queries to prevent unbounded memory use (CWE-400) |
| **Process timeouts** | ImageMagick and ffmpeg have 5-minute hard timeouts — process trees are killed if exceeded |

---

## API Reference

All endpoints require authentication unless marked *(anonymous)*.

### Collections

```http
GET    /api/v1/collections                           # List root collections the user can access
GET    /api/v1/collections/{id}                      # Collection details with ACLs
POST   /api/v1/collections                           # Create collection (Contributor+)
PATCH  /api/v1/collections/{id}                      # Update name, description
DELETE /api/v1/collections/{id}                      # Delete collection (cascades relationships)
POST   /api/v1/collections/{id}/download-all         # Queue zip download of all assets
```

### Collection ACLs

```http
GET    /api/v1/collections/{collectionId}/acl                         # List access entries
POST   /api/v1/collections/{collectionId}/acl                         # Grant or update role for user
DELETE /api/v1/collections/{collectionId}/acl/{principalType}/{principalId}  # Revoke access
GET    /api/v1/collections/{collectionId}/acl/users/search            # Search users for ACL assignment
```

### Assets

```http
GET    /api/v1/assets                                # List ready assets with pagination (Admin)
GET    /api/v1/assets/all                            # Search all assets with filters (Admin)
GET    /api/v1/assets/{id}                           # Asset details
POST   /api/v1/assets                                # Upload (multipart form)
POST   /api/v1/assets/init-upload                    # Initiate presigned upload
POST   /api/v1/assets/{id}/confirm-upload            # Confirm presigned upload completed
PATCH  /api/v1/assets/{id}                           # Update title, description, copyright, tags
DELETE /api/v1/assets/{id}                           # Delete asset (with optional collection scope)
GET    /api/v1/assets/collection/{collectionId}      # Assets in collection (search, sort, filter)
GET    /api/v1/assets/{id}/deletion-context          # Get deletion impact info
```

### Asset Collections

```http
GET    /api/v1/assets/{id}/collections               # Collections containing asset
POST   /api/v1/assets/{id}/collections/{collectionId}  # Add asset to collection
DELETE /api/v1/assets/{id}/collections/{collectionId}  # Remove from collection
```

### Renditions

```http
GET    /api/v1/assets/{id}/download                  # Original file (presigned redirect)
GET    /api/v1/assets/{id}/preview                   # Original inline preview
GET    /api/v1/assets/{id}/thumb                     # Thumbnail (200x200)
GET    /api/v1/assets/{id}/thumb/download            # Thumbnail download
GET    /api/v1/assets/{id}/medium                    # Medium rendition (800x800)
GET    /api/v1/assets/{id}/medium/download           # Medium rendition download
GET    /api/v1/assets/{id}/poster                    # Video poster frame
```

### Shares

```http
POST   /api/v1/shares                                # Create share link
DELETE /api/v1/shares/{id}                           # Revoke share (creator or admin)
PUT    /api/v1/shares/{id}/password                  # Set, change, or remove password
GET    /api/v1/shares/{token}                        # View shared content (anonymous)
POST   /api/v1/shares/{token}/access-token           # Authenticate with password (anonymous)
GET    /api/v1/shares/{token}/download               # Download via share (anonymous)
POST   /api/v1/shares/{token}/download-all           # Zip download of shared collection (anonymous)
GET    /api/v1/shares/{token}/preview                # Preview shared asset (anonymous)
```

### Zip Downloads

```http
GET    /api/v1/zip-downloads/{jobId}                 # Poll build progress (authenticated)
GET    /api/v1/zip-downloads/{jobId}/share           # Poll build progress (anonymous, X-Share-Token header)
```

### Admin *(admin role required)*

```http
GET    /api/v1/admin/shares                          # All shares (paginated, filterable)
GET    /api/v1/admin/shares/{id}/token               # Retrieve share token (encrypted storage)
GET    /api/v1/admin/shares/{id}/password            # Retrieve share password (encrypted storage)
DELETE /api/v1/admin/shares/{id}                     # Revoke share
GET    /api/v1/admin/collections/access              # All collection access (hierarchical tree)
POST   /api/v1/admin/collections/{collectionId}/acl            # Grant access
DELETE /api/v1/admin/collections/{collectionId}/acl/{principalId}  # Revoke access (?principalType=)
GET    /api/v1/admin/users                           # Users with access
GET    /api/v1/admin/keycloak-users                  # All Keycloak users
POST   /api/v1/admin/users                           # Create user in Keycloak
POST   /api/v1/admin/users/{userId}/reset-password   # Reset password
POST   /api/v1/admin/users/sync                      # Sync deleted users (supports dry-run)
DELETE /api/v1/admin/users/{userId}                  # Delete user
GET    /api/v1/admin/audit                           # Recent audit events (default 200, max 200)
GET    /api/v1/admin/audit/paginated                 # Paginated audit log
```

### Dashboard

```http
GET    /api/v1/dashboard                             # Stats: assets, collections, shares (active/total), audit count
```

### Health

```http
GET    /health                                    # Liveness (always 200)
GET    /health/ready                              # Readiness (checks PostgreSQL, MinIO, Keycloak, ClamAV)
```
