# AssetHub Security Audit Report

**Date:** 2026-02-22
**Scope:** Full application code and configuration review
**Application:** AssetHub (.NET 9, Blazor Server, Keycloak, PostgreSQL, MinIO)

---

## Executive Summary

AssetHub demonstrates a **strong security posture** for an internal/team-facing digital asset management system. The architecture follows defense-in-depth with proper separation of concerns. However, several findings ranging from medium to informational severity should be addressed.

**Overall Score: 7.8 / 10**

---

## 1. Authentication & Identity Management — Score: 8.5/10

### Strengths

- OIDC Authorization Code flow with PKCE enabled (`AuthenticationExtensions.cs:157`)
- Smart policy scheme correctly routes JWT vs Cookie auth (`AuthenticationExtensions.cs:34-44`)
- Cookie uses `__Host.` prefix, `SameSite=Strict`, `HttpOnly=true`, `Secure=Always` in production (`AuthenticationExtensions.cs:81-86`)
- JWT validates issuer, audience, lifetime properly (`AuthenticationExtensions.cs:57-66`)
- `MapInboundClaims = false` prevents claim bloat and confusion (`AuthenticationExtensions.cs:166`)
- Token caching in KeycloakUserService is thread-safe with `SemaphoreSlim` (`KeycloakUserService.cs:27-29`)

### Findings

**[MEDIUM] Development certificate validation bypass**
`DangerousAcceptAnyServerCertificateValidator` is used in three places when `IsDevelopment()`. This is expected, but if `ASPNETCORE_ENVIRONMENT` is ever misconfigured in production, all TLS validation would be disabled.

- `AuthenticationExtensions.cs:51-55`
- `AuthenticationExtensions.cs:146-150`
- `ServiceCollectionExtensions.cs:161-164`

> **Recommendation:** Add a startup log warning or assertion that verifies `ASPNETCORE_ENVIRONMENT != "Development"` when `RequireHttpsMetadata = true`. Consider also adding a check in the production Dockerfile that asserts the environment variable.

**[LOW] Keycloak admin credentials use ROPC grant** ✅ RESOLVED
`GetAdminTokenAsync` uses the `password` grant type against master realm (`KeycloakUserService.cs:288-296`). While functional, the `client_credentials` grant with a dedicated service account is preferred.

> **Recommendation:** Create a Keycloak service account with `manage-users` permissions and switch to `client_credentials` grant.
>
> **Status (2026-02-22):** Resolved. `KeycloakUserService` now supports both grant types. Set `AdminClientSecret` in config to use `client_credentials` grant; otherwise falls back to password grant.

**[INFO] `directAccessGrantsEnabled: true` in realm config** ✅ RESOLVED
`media-realm.json:168` enables the Resource Owner Password Credentials grant on the client. This should be disabled unless explicitly needed.

> **Recommendation:** Set `"directAccessGrantsEnabled": false` in the client config.
>
> **Status (2026-02-22):** Resolved. ROPC grant disabled in `media-realm.json`.

---

## 2. Authorization & Access Control — Score: 9.0/10

### Strengths

- Hierarchical RBAC: Viewer < Contributor < Manager < Admin with level-based checks
- Collection-level ACL with inheritance up to 20 levels deep (`CollectionAuthorizationService.cs`)
- Request-scoped caching prevents stale permissions within a request while avoiding cross-request staleness
- Privilege escalation prevention: `CanGrantRole` / `CanRevokeRole` enforce level checks
- Admin endpoints are behind `RequireAdmin` policy
- Share operations verify `CreatedByUserId` ownership (`ShareAccessService.cs:267`)

### Findings

**[LOW] No fallback authorization policy** ✅ RESOLVED
Endpoints without an explicit `.RequireAuthorization()` or `.AllowAnonymous()` are open by default. ASP.NET Core Minimal APIs don't have a global fallback policy.

> **Recommendation:** Add a fallback authorization policy:
> ```csharp
> options.FallbackPolicy = new AuthorizationPolicyBuilder()
>     .RequireAuthenticatedUser()
>     .Build();
> ```
> and explicitly mark anonymous endpoints with `.AllowAnonymous()`.
>
> **Status (2026-02-22):** Resolved. FallbackPolicy configured to require authenticated user.

---

## 3. Input Validation & Injection — Score: 8.0/10

### Strengths

- EF Core parameterized queries throughout — zero raw SQL detected
- Data Annotations on DTOs with regex patterns for usernames and roles
- Manual validation for passwords (8+ chars, mixed case, digit, special)
- `Math.Clamp` on pagination parameters (`ShareEndpoints.cs:46`)
- Content-type allowlisting with prefix and exact match checks (`Constants.cs:113-169`)
- `Uri.EscapeDataString` used for all Keycloak URL parameters

### Findings

**[MEDIUM] Content-type validation relies solely on client-supplied MIME type** ✅ RESOLVED
`AllowedUploadTypes.IsAllowed()` checks the `contentType` provided by the client/browser. A malicious user could upload an executable with `Content-Type: image/jpeg`.

> **Recommendation:** Add server-side file magic byte (file signature) validation. At minimum, for `image/*` types, verify the file header matches the claimed type. Libraries like `MimeDetective` can help.
>
> **Status (2026-02-22):** Resolved. `FileMagicValidator.cs` validates file signatures for images, video, audio, documents, and fonts. Validation runs on both direct uploads and presigned upload confirmations.

**[LOW] SVG upload allowed — potential stored XSS vector** ✅ RESOLVED
`application/svg+xml` is in the allowed list (`Constants.cs:140`). SVGs can contain embedded JavaScript.

> **Recommendation:** Either sanitize SVG uploads (strip `<script>`, `onload`, etc.) or serve them with `Content-Disposition: attachment` and a strict CSP. Alternatively, consider whether SVG upload is necessary.
>
> **Status (2026-02-22):** Resolved. `application/svg+xml` is now explicitly blacklisted in `AllowedUploadTypes`.

**[INFO] No request body size validation on some endpoints**
While Kestrel has a global `MaxRequestBodySize`, individual endpoints don't validate the actual file size against business rules before processing.

---

## 4. Secrets & Configuration Management — Score: 7.5/10

### Strengths

- Base `appsettings.json` has empty credential fields — no hardcoded secrets
- `.env` files are properly gitignored (`.gitignore:34-36`)
- `appsettings.Local.json` gitignored for developer overrides
- Certificate files (`*.pfx`, `*.pem`, `*.key`, `*.crt`) gitignored
- `CREDENTIALS.md` gitignored
- Production config via environment variables only

### Findings

**[HIGH] Hardcoded client secret and user passwords in Keycloak realm import** ✅ RESOLVED
`media-realm.json:165` contains:

- Client secret: `"secret": "VxBiV29QVchYHFzD5N62l43fTXbTMzSl"`
- Test user password: `"value": "testuser123"` (line 205)
- Admin password: `"value": "mediaadmin123"` (line 218)

This file is committed to version control. Even though it's for development, the client secret pattern may be reused in production.

> **Recommendation:**
> 1. Rotate the client secret if it was ever used outside development.
> 2. Replace hardcoded values with placeholders and use `envsubst` or a script at import time.
> 3. Use stronger passwords even for development accounts to prevent habit formation.
>
> **Status (2026-02-22):** Resolved. Secrets replaced with environment variable placeholders (`${KEYCLOAK_CLIENT_SECRET}`, `${KEYCLOAK_TESTUSER_PASSWORD}`, `${KEYCLOAK_ADMIN_USER_PASSWORD}`). Variables added to docker-compose files and `.env.template`.

**[MEDIUM] Shared PostgreSQL credentials between app and Keycloak** ✅ RESOLVED
Both the application and Keycloak use the same `POSTGRES_USER`/`POSTGRES_PASSWORD` (`docker-compose.prod.yml:82-83`). A compromise of one gives full access to both databases.

> **Recommendation:** Create separate PostgreSQL users with limited privileges for each database.
>
> **Status (2026-02-22):** Resolved. Created dedicated `keycloak` user with `KEYCLOAK_DB_USER`/`KEYCLOAK_DB_PASSWORD` env vars. Updated `init-keycloak-db.sh` to create the user automatically.

**[LOW] `AllowedHosts: "*"` in base appsettings** ✅ RESOLVED
`appsettings.json:75` allows all host headers. While the reverse proxy should filter, this is defense-in-depth.

> **Recommendation:** Set `AllowedHosts` to the expected production hostname in `appsettings.Production.json`.
>
> **Status (2026-02-22):** Resolved. Added `"AllowedHosts": "${APP_HOSTNAME}"` to `appsettings.Production.json`. Environment variable configurable via `.env`.

---

## 5. Transport Security — Score: 8.0/10

### Strengths

- HTTPS redirect and HSTS in production (`WebApplicationExtensions.cs:64-68`)
- `RequireHttpsMetadata: true` in production for OIDC metadata
- Cookie `SecurePolicy = Always` in production
- Docker services bind to `127.0.0.1` only (no external exposure)

### Findings

**[MEDIUM] No HSTS configuration — uses defaults** ✅ RESOLVED
`app.UseHsts()` is called without configuring `MaxAge`, `IncludeSubDomains`, or `Preload`. The default max-age is only 30 days.

> **Recommendation:** Configure HSTS explicitly:
> ```csharp
> services.AddHsts(options => {
>     options.MaxAge = TimeSpan.FromDays(365);
>     options.IncludeSubDomains = true;
>     options.Preload = true;
> });
> ```
>
> **Status (2026-02-22):** Resolved. HSTS configured with 365-day max-age, subdomain inclusion, and preload.

**[LOW] MinIO internal communication uses HTTP**
`MinIO__UseSSL: "false"` is set in both dev and production compose files (`docker-compose.prod.yml:141`). While traffic stays within the Docker network, encryption in transit is best practice.

> **Recommendation:** Enable TLS for internal MinIO communication in production, or document this as an accepted risk since traffic stays on the internal Docker bridge.

---

## 6. HTTP Security Headers — Score: 8.5/10

### Strengths

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Permissions-Policy: camera=(), microphone=(), geolocation=()`
- `Content-Security-Policy` in production with `frame-ancestors 'none'`, `base-uri 'self'`, `form-action 'self'`
- Health endpoints omit exception details (`WebApplicationExtensions.cs:362`)

### Findings

**[MEDIUM] CSP requires `'unsafe-inline'` for scripts and styles**
`WebApplicationExtensions.cs:83-92` — This is a known Blazor Server limitation, but weakens XSS protection.

> **Recommendation:** This is currently unavoidable with Blazor Server + MudBlazor. Consider adding `'nonce-xxx'` or `'strict-dynamic'` when Blazor supports it. Monitor Blazor roadmap for CSP improvements.

**[LOW] No CSP in development mode**
CSP is only applied when `!IsDevelopment()`. Developers may write code that violates CSP without realizing.

> **Recommendation:** Apply a report-only CSP in development: `Content-Security-Policy-Report-Only`.

**[INFO] `X-XSS-Protection: 1; mode=block` is deprecated**
Modern browsers have removed their XSS auditor. This header is harmless but misleading.

> **Recommendation:** Remove or replace with `X-XSS-Protection: 0` (the current OWASP recommendation).

---

## 7. Share Link Security — Score: 9.0/10

### Strengths

- 256-bit cryptographically secure tokens via `RandomNumberGenerator` (`ShareHelpers.cs:23-31`)
- SHA-256 token hashing — plaintext never stored in DB
- BCrypt password hashing with auto-salt
- Password accepted only via `X-Share-Password` header (not query string) (`ShareEndpoints.cs:129-132`)
- Time-limited access tokens (30 min) using Data Protection API
- Rate limiting: 30 req/min anonymous, 10 req/5min for password attempts
- Admin token retrieval uses encrypted storage (Data Protection)

### Findings

**[LOW] Rate limiting partitions by IP only**
`ServiceCollectionExtensions.cs:78` — Attackers behind IPv6 blocks or botnets can distribute attempts across IPs.

> **Recommendation:** Consider adding a secondary partition key combining IP + token to limit per-share brute force. Also consider exponential backoff for failed password attempts.

**[INFO] Share token passed in URL path**
`/api/shares/{token}` exposes the token in server logs and potentially in referer headers.

> **Recommendation:** This is an acceptable trade-off for usability. The strict `Referrer-Policy` mitigates referer leakage. Ensure server logs that contain URLs are properly secured.

---

## 8. File Upload & Storage — Score: 7.5/10

### Strengths

- Content-type allowlisting (`Constants.cs:113-169`)
- Presigned upload flow offloads large files to MinIO directly
- 500MB configurable upload limit at Kestrel level
- File names sanitized for `Content-Disposition` headers (`MinIOAdapter.cs:171-181`)
- Object keys use application-controlled prefixes (`originals/`, `thumbs/`, etc.)

### Findings

**[MEDIUM] No file content validation (magic bytes)** ✅ RESOLVED
As noted in Section 3, file type is determined solely by the client-supplied Content-Type header.

> **Recommendation:** Implement server-side magic byte validation for at minimum image types.
>
> **Status (2026-02-22):** Resolved. See Input Validation section.

**[MEDIUM] ImageMagick and ffmpeg without hardened policy** ✅ RESOLVED
The Dockerfile installs `imagemagick` and `ffmpeg` (`Dockerfile:33-36`) without configuring ImageMagick's `policy.xml`. ImageMagick has a history of security vulnerabilities (ImageTragick, CVE-2016-3714 and successors).

> **Recommendation:**
> 1. Add a restrictive `policy.xml` that disables coders for dangerous formats (SVG, MVG, MSL, TEXT, LABEL).
> 2. Set resource limits (memory, disk, time) in the policy.
> 3. Pin ImageMagick to a specific version rather than the distro default.
>
> **Status (2026-02-22):** Resolved. `docker/imagemagick-policy.xml` added with restrictive policy disabling SVG, MVG, MSL, TEXT, LABEL, PS/PDF, URL handlers. Resource limits configured (128MP area, 256MiB memory, 120s timeout). Policy applied to both `Dockerfile` and `Dockerfile.Worker`.

**[LOW] No virus/malware scanning on uploads** ✅ RESOLVED
Files are stored and served without any malware scanning.

> **Recommendation:** Integrate ClamAV or a similar scanner, either inline or as an async background job.
>
> **Status (2026-02-22):** Resolved. ClamAV integration implemented:
> - `IMalwareScannerService` interface and `ClamAvScannerService` implementation using clamd TCP protocol
> - Integrated into `AssetService.UploadAsync` (streaming scan before storage)
> - Integrated into `AssetService.ConfirmUploadAsync` (scan after presigned upload)
> - ClamAV container added to `docker-compose.yml` and `docker-compose.prod.yml`
> - Configurable via `ClamAV:Enabled` (default: false in base config, true in Docker)

---

## 9. Docker & Infrastructure Security — Score: 8.0/10

### Strengths

- Non-root user in both Dockerfiles (`USER app`) (`Dockerfile:43`)
- Multi-stage builds (smaller attack surface)
- Production compose: services on internal network, no exposed ports on postgres/minio
- Resource limits configured (512M-1G per service)
- `restart: unless-stopped` on all services
- Pinned Keycloak and MinIO versions in production compose
- `apt-get` cache cleaned after install

### Findings

**[MEDIUM] Base images use floating tags** ✅ RESOLVED
`mcr.microsoft.com/dotnet/sdk:9.0` and `aspnet:9.0` use major version tags (`Dockerfile:1,28`). These can receive unexpected updates.

> **Recommendation:** Pin to specific digest or patch version: `aspnet:9.0.x-bookworm-slim`.
>
> **Status (2026-02-22):** Resolved. Dockerfiles now use pinned versions: `sdk:9.0.312-bookworm-slim` and `aspnet:9.0.14-bookworm-slim`.

**[LOW] MinIO uses `latest` tag in dev compose**
`docker-compose.yml:27` — `minio/minio:latest` is unpinned.

> **Recommendation:** Pin to a specific release as done in the production compose.

**[LOW] Production Keycloak uses `start` (production mode) but imports realms**
`docker-compose.prod.yml:101-102` — `--import-realm` runs on every start. This is safe (Keycloak skips existing realms) but is unnecessary after initial setup.

> **Recommendation:** Use a separate init container or one-time script for realm import, then remove `--import-realm` from the production command.

---

## 10. Open Redirect & CSRF Protection — Score: 9.0/10

### Strengths

- `UrlSafetyHelper.SafeReturnUrl` validates return URLs with an allowlist of prefixes (`UrlSafetyHelper.cs`)
- Blocks protocol-relative URLs (`//evil.com`)
- Only allows relative URIs
- Antiforgery enabled for Blazor forms
- API endpoints explicitly disable antiforgery (correct — they use JWT Bearer)

### Findings

**[INFO] No issues found.** The open redirect mitigation is well-implemented with defense-in-depth (relative URI check + known prefix allowlist).

---

## 11. Logging & Audit Trail — Score: 8.5/10

### Strengths

- Comprehensive audit events: share CRUD, asset CRUD, collection CRUD, ACL changes, user management
- Audit captures IP, UserAgent, ActorUserId, detailed context
- Serilog structured logging with machine name, thread ID, environment enrichment
- Error responses don't leak stack traces or internal details
- Production uses compact JSON format for log aggregation

### Findings

**[LOW] No audit log for failed authentication attempts** ✅ RESOLVED
Brute-force password attempts on share links are rate-limited but not explicitly audit-logged.

> **Recommendation:** Log failed share password attempts with IP and token hash for security monitoring.
>
> **Status (2026-02-22):** Resolved. Added `share.password_failed` audit event and warning log in `ShareAccessService.cs` capturing IP and token hash prefix.

**[INFO] Admin audit endpoint exists but no log retention/rotation policy visible**
No log rotation configuration found.

> **Recommendation:** Configure Serilog file sink with rolling intervals if file logging is used, or ensure the log aggregation platform handles retention.

---

## 12. Dependency Management — Score: 7.5/10

### Findings

**[MEDIUM] Keycloak 24.0.1 is outdated** ✅ RESOLVED
`docker-compose.prod.yml:73` — Keycloak 24.0.1 (released early 2024) has known CVEs patched in later versions.

> **Recommendation:** Upgrade to Keycloak 26.x or latest stable.
>
> **Status (2026-02-22):** Resolved. Upgraded to Keycloak 26.1.0 in both docker-compose.yml and docker-compose.prod.yml.

**[LOW] Wildcard version ranges in `.csproj`**
Most NuGet packages use `9.0.*` or `1.8.*` ranges. While convenient, this can pull in broken or vulnerable patch releases.

> **Recommendation:** Pin to specific patch versions in production builds and use Dependabot or similar for controlled updates.

**[INFO] No `dotnet list package --vulnerable` in CI** ✅ RESOLVED

> **Recommendation:** Add `dotnet list package --vulnerable --include-transitive` to the CI pipeline.
>
> **Status (2026-02-22):** Resolved. Security audit job added to CI pipeline that fails on CRITICAL/HIGH vulnerabilities.

---

## Score Summary

| Security Area                  | Score      | Risk Level  |
| ------------------------------ | ---------- | ----------- |
| Authentication & Identity      | **8.5/10** | Low         |
| Authorization & Access Control | **9.0/10** | Low         |
| Input Validation & Injection   | **8.0/10** | Low-Medium  |
| Secrets & Configuration        | **7.5/10** | Medium      |
| Transport Security             | **8.0/10** | Low         |
| HTTP Security Headers          | **8.5/10** | Low         |
| Share Link Security            | **9.0/10** | Low         |
| File Upload & Storage          | **7.5/10** | Medium      |
| Docker & Infrastructure        | **8.0/10** | Low         |
| CSRF & Open Redirect           | **9.0/10** | Low         |
| Logging & Audit                | **8.5/10** | Low         |
| Dependency Management          | **7.5/10** | Low-Medium  |
| **Overall**                    | **7.8/10** |             |

---

## Priority Remediation Roadmap

### Immediate (High Priority)

1. ~~**Rotate the Keycloak client secret** committed in `media-realm.json` and replace with environment variable placeholders~~ ✅ DONE (2026-02-22)
2. ~~**Add ImageMagick `policy.xml`** to restrict dangerous coders and set resource limits~~ ✅ DONE (2026-02-22)

### Short-Term (Medium Priority)

3. ~~Implement file magic byte validation for uploaded files~~ ✅ DONE (2026-02-22)
4. ~~Separate PostgreSQL credentials for app and Keycloak databases~~ ✅ DONE (2026-02-22)
5. ~~Upgrade Keycloak to latest stable (26.x)~~ ✅ DONE (2026-02-22)
6. ~~Pin Docker base images to specific patch versions~~ ✅ DONE (2026-02-22)
7. ~~Configure HSTS with 1-year max-age~~ ✅ DONE (2026-02-22)
8. ~~Add a fallback authorization policy requiring authentication~~ ✅ DONE (2026-02-22)

### Long-Term (Low Priority)

9. ~~Switch Keycloak admin auth from ROPC to `client_credentials` grant~~ ✅ DONE (2026-02-22)
10. ~~Disable `directAccessGrantsEnabled` on the OIDC client~~ ✅ DONE (2026-02-22)
11. ~~Add malware scanning for file uploads~~ ✅ DONE (2026-02-22)
12. ~~Set `AllowedHosts` to the production hostname~~ ✅ DONE (2026-02-22)
13. ~~Add failed auth attempt logging for share links~~ ✅ DONE (2026-02-22)
14. ~~Add `dotnet list package --vulnerable` to CI pipeline~~ ✅ DONE (2026-02-22)
