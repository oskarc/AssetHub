# AssetHub Application Audit Report

**Date:** 2026-02-24 (Revision 3 — comprehensive deep-scan re-audit)
**Scope:** Full application architecture, code quality, security, infrastructure, testing, and operational readiness review
**Application:** AssetHub (.NET 9, Blazor Server, Keycloak, PostgreSQL, MinIO, ClamAV, Wolverine/RabbitMQ, Redis)

---

## Executive Summary

This is the **third audit** of AssetHub, conducted as a comprehensive deep scan after multiple rounds of remediation. The codebase has continued to improve: the TLS bypass is now properly scoped to `IsDevelopment()`, Keycloak healthchecks have been fixed for the UBI minimal image, the anonymous Blazor framework bypass was corrected, and DashboardEndpoints was fixed. This audit identified 12 new findings across security, code quality, testing, and infrastructure.

**Previous Score: 8.8 / 10**
**Current Score: 8.9 / 10**

| Dimension                      | Rev 1  | Rev 2  | Rev 3  | Delta |
| ------------------------------ | ------ | ------ | ------ | ----- |
| Architecture & Separation      | 8.5/10 | 8.5/10 | 8.5/10 | —     |
| Code Quality & Patterns        | 8.0/10 | 9.0/10 | 9.0/10 | —     |
| Security Posture               | 8.1/10 | 8.6/10 | 8.8/10 | +0.2  |
| Docker & Infrastructure        | 8.4/10 | 8.5/10 | 8.8/10 | +0.3  |
| Testing & Coverage             | 8.0/10 | 9.0/10 | 9.0/10 | —     |
| Configuration & Best Practices | 8.5/10 | 8.5/10 | 8.8/10 | +0.3  |
| CI/CD & Ways of Working        | 6.5/10 | 8.0/10 | 8.0/10 | —     |

---

## Resolved Since Revision 2

### Findings Closed (8 from Rev 2)

| ID         | Finding                                          | Resolution |
| ---------- | ------------------------------------------------ | ---------- |
| CFG-002    | DangerousAcceptAnyServerCertificateValidator      | RE-OPENED then RESOLVED — Was fully removed in Rev 2, which broke dev TLS. Now properly scoped to `IsDevelopment()` in 4 locations: JWT backchannel, OIDC backchannel, Keycloak HttpClient, UI API client |
| CFG-004    | Nginx proxy_pass https to HTTP upstream           | ✅ RESOLVED — Verified DEPLOYMENT.md now uses `proxy_pass http://assethub;` |
| CFG-005    | Caddy example has unnecessary tls_insecure_skip_verify | ✅ RESOLVED — Simplified to `reverse_proxy 127.0.0.1:7252` |
| CQ-006     | DashboardEndpoints uses manual status code        | ✅ RESOLVED — Now uses `result.ToHttpResult()` |
| DOCKER-006 | Production Keycloak missing healthcheck           | ✅ RESOLVED — Both dev and prod compose now use bash `/dev/tcp` healthcheck (compatible with UBI minimal) |
| DOCKER-007 | Mailpit uses floating latest tag                  | Minor — Still open but deprioritized (dev-only) |
| CQ-007     | E2E tests pass share passwords via query string   | ✅ RESOLVED — Tests now use `X-Share-Password` header |
| CQ-008     | Overly broad status code assertions in E2E tests  | ✅ RESOLVED — Assertions now expect specific status codes |
| CQ-009     | E2E test references removed collection hierarchy  | ✅ RESOLVED — Obsolete sub-collection test removed |
| SEC-004    | SVG uploads allowed — potential stored XSS vector | ✅ RESOLVED — `application/svg+xml` removed from upload allowlist |
| TEST-007   | No unit tests for ShareAccessService              | ✅ RESOLVED — Added 28 comprehensive unit tests |

---

## 1. Architecture & Separation of Concerns — Score: 8.5/10

### Positive Findings

- **Clean layer separation**: Domain → Application → Infrastructure → Api → Ui. Project references enforce the dependency rule correctly:
  - Domain: no project references (pure entities)
  - Application: references only Domain
  - Infrastructure: references Application and Domain
  - Ui: references only Application
  - Api: references all layers (composition root)
- **Interface Segregation**: Asset services split into `IAssetService`, `IAssetQueryService`, `IAssetUploadService`
- **ServiceResult pattern**: Used consistently across all service methods — no exceptions as control flow
- **Request-scoped authorization caching**: `CollectionAuthorizationService` caches ACL lookups per-request with batch preloading
- **Shared infrastructure**: `InfrastructureServiceExtensions` registers DB, Wolverine/RabbitMQ, MinIO, Redis, repositories — shared between API and Worker hosts

### Open Findings

**[ARC-001] Large constructor injection in service classes** — Severity: Low — Status: OPEN

`ShareAccessService` injects 13 dependencies, `AdminService` injects 14 including the raw `AssetHubDbContext`. While the ISP refactoring of AssetService was good, these orchestration services remain large. Acceptable for now — decompose if they grow.

---

**[ARC-002] Polymorphic FK on Share entity** — Severity: Low — Status: OPEN (renumbered from ARC-003)

`Share.ScopeType + ScopeId` is a polymorphic foreign key with application-level enforcement only. The DB cannot enforce referential integrity. Low risk since all access goes through `ShareAccessService` which validates scope.

---

## 2. Code Quality & Patterns — Score: 9.0/10

### Positive Findings

- **Consistent endpoint pattern**: All 6 endpoint files follow the same structure — `MapGroup` with auth, `DisableAntiforgery()` on mutations, delegate to service, return `result.ToHttpResult()`
- **Consistent error model**: `ServiceResult<T>` → `ToHttpResult()` → `ApiError` JSON. No raw status codes
- **Input validation**: Shared `InputValidation` class with `[GeneratedRegex]` patterns for username, email, password. DTO annotations on all inputs
- **Enum string conversions**: Bidirectional `ToDbString()`/`ToEnum()` with graceful fallbacks for `AssetStatus`, `AssetType`, `ZipDownloadStatus` (returns `Unknown`). Strict for `AclRole`, `ShareScopeType`, `PrincipalType` (throws — appropriate since these are invariant)
- **Constants centralized**: `Constants.cs` with `Limits`, `StoragePrefixes`, `Policies`, `SortBy`, `ContentTypes`, etc.

### New Findings

**[CQ-007] E2E tests pass share passwords via query string** — Severity: Medium — Status: NEW

[08-api.spec.ts:216-241](tests/E2E/tests/specs/08-api.spec.ts#L216-L241) — Multiple share API tests pass passwords in the URL query string:
```typescript
`${env.baseUrl}/api/shares/${shareToken}?password=test-password-123`
```

However, the API only reads passwords from the `X-Share-Password` header ([ShareEndpoints.cs:129-131](src/AssetHub.Api/Endpoints/ShareEndpoints.cs#L129-L131)). This means these tests are passing `null` as the password — they only pass because the share access logic still produces some response (401/404), and the assertions use overly broad `expect([200, 400, 401, 403, 404]).toContain(res.status())` which accepts any status code.

**Impact:** These tests don't actually test password-protected share access. They test unauthenticated access and pass by coincidence due to permissive assertions.

**Recommendation:** Use the `X-Share-Password` header as the API expects:
```typescript
const res = await request.get(`${env.baseUrl}/api/shares/${shareToken}`, {
  headers: { 'X-Share-Password': 'test-password-123' }
});
expect(res.status()).toBe(200);
```

---

**[CQ-008] Overly broad status code assertions in E2E tests** — Severity: Medium — Status: NEW

Multiple E2E tests accept wide ranges of status codes, making them unable to catch regressions:

| File | Line | Assertion | Problem |
|------|------|-----------|---------|
| [08-api.spec.ts:218](tests/E2E/tests/specs/08-api.spec.ts#L218) | `expect([200, 400, 401, 403, 404])` | Accepts any of 5 codes for "access share with password" |
| [08-api.spec.ts:235](tests/E2E/tests/specs/08-api.spec.ts#L235) | `expect([200, 301, 302, 307, 401, 403, 404])` | 7 possible codes for share download |
| [08-api.spec.ts:297-302](tests/E2E/tests/specs/08-api.spec.ts#L297-L302) | `expect([200, 204, 403, 404])` | Admin endpoints accept 404 |
| [08-api.spec.ts:323](tests/E2E/tests/specs/08-api.spec.ts#L323) | `expect([200, 401, 302])` | Unauth test accepts 200 |

**Impact:** A broken endpoint returning 403 instead of 200 would still pass. These tests provide false confidence.

**Recommendation:** Assert the single expected status code. If the test isn't reliable enough for a specific assertion, fix the test setup rather than broadening the assertion.

---

**[CQ-009] E2E test `create sub-collection` references removed hierarchy feature** — Severity: Low — Status: NEW

[08-api.spec.ts:64-70](tests/E2E/tests/specs/08-api.spec.ts#L64-L70) — The test creates a "sub-collection" and asserts `result.parentId || result.ParentId` equals `testCollectionId`. However, the migration `20260223194914_RemoveCollectionHierarchy` removed collection hierarchy. The `Collection` entity has no `ParentId` field. This test likely either silently passes with `undefined` or is skipped.

**Recommendation:** Remove or rewrite this test to match the flat collection model.

---

## 3. Security Posture — Score: 8.8/10 (+0.2)

### Positive Findings

- **FallbackPolicy**: All endpoints require auth by default. Every Blazor page has explicit `[Authorize]` or `[AllowAnonymous]`
- **PKCE + Authorization Code flow**: OIDC uses code flow with PKCE, not implicit grant
- **Cookie security**: `__Host.` prefix (enforces Secure + Path=/), `SameSite=Strict`, `HttpOnly=true`
- **Share system**: 256-bit CSPRNG tokens, SHA-256 hashed in DB, BCrypt for passwords, Data Protection encrypted tokens for admin recovery, 30-min access tokens for media URLs
- **IDOR protection**: Collection shares validate asset membership before serving
- **File validation**: Content-type allowlist + magic byte validation + ClamAV scanning
- **No raw SQL**: All queries go through EF Core parameterized queries
- **Open redirect protection**: `UrlSafetyHelper.SafeReturnUrl()` validates return URLs
- **TLS bypass properly scoped**: `DangerousAcceptAnyServerCertificateValidator` in 4 locations, all gated on `environment.IsDevelopment()`

### New Findings

**[SEC-002] SignalR hub open to anonymous WebSocket connections** — Severity: Medium — Status: NEW

[WebApplicationExtensions.cs:134-152](src/AssetHub.Api/Extensions/WebApplicationExtensions.cs#L134-L152) — The `/_blazor` SignalR hub has `AllowAnonymous` appended to support the anonymous Share page. While page-level `[Authorize]` still works (unauthenticated users can only interact with `[AllowAnonymous]` pages), the transport itself is open.

**Risk:** An attacker could establish many anonymous WebSocket connections to exhaust server resources. No rate limiting is applied to `/_blazor` connection establishment.

**Mitigation present:** This is an accepted pattern for Blazor Server apps with anonymous pages. The comment documents the reasoning.

**Recommendation:** Add connection-level rate limiting or concurrent connection limits for `/_blazor`.

---

**[SEC-003] No global rate limiting for authenticated API endpoints** — Severity: Medium — Status: NEW

Only anonymous share endpoints have rate limits (`ShareAnonymous`: 30/min, `SharePassword`: 10/5min in [ServiceCollectionExtensions.cs:82-111](src/AssetHub.Api/Extensions/ServiceCollectionExtensions.cs#L82-L111)). Authenticated endpoints like `/api/assets`, `/api/collections`, `/api/admin` have no rate limiting.

**Risk:** A compromised account could enumerate assets, trigger unlimited downloads/uploads, or abuse admin endpoints without throttling.

**Recommendation:** Add a global authenticated rate limiter (e.g., 200 req/min per user).

---

**[SEC-004] SVG uploads allowed — stored XSS vector** — Severity: Medium — Status: NEW

[Constants.cs:140](src/AssetHub.Application/Constants.cs#L140) — `application/svg+xml` is in the upload allowlist. SVG files can contain embedded JavaScript via `<script>` tags or event handlers (`onload`, `onclick`). If an SVG is ever served inline (without `Content-Disposition: attachment`) or rendered in the browser via a presigned URL, it could execute JavaScript in the context of the MinIO domain.

**Mitigation present:**
- FileMagicValidator allows SVGs through the text-based skip list (no magic byte check possible)
- The ImageMagick policy (`docker/imagemagick-policy.xml`) disables SVG processing
- MinIO presigned URLs serve from a different origin than the app

**Recommendation:** Either remove `application/svg+xml` from the allowlist, or ensure all SVG downloads include `Content-Disposition: attachment` and `Content-Type: application/octet-stream` headers.

---

**[SEC-005] ForwardedHeaders trusts all proxy sources** — Severity: Low — Status: NEW

[WebApplicationExtensions.cs:62-63](src/AssetHub.Api/Extensions/WebApplicationExtensions.cs#L62-L63) — `KnownNetworks.Clear()` and `KnownProxies.Clear()` means any `X-Forwarded-For` header is trusted. The comment correctly notes this is safe only behind a reverse proxy.

**Risk if violated:** IP spoofing to bypass rate limiting.

**Recommendation:** Restrict to Docker bridge network CIDR (`172.16.0.0/12`) instead of clearing entirely.

---

**[SEC-006] OIDC TokenValidationParameters less explicit than JWT Bearer** — Severity: Low — Status: NEW

[AuthenticationExtensions.cs:175-180](src/AssetHub.Api/Extensions/AuthenticationExtensions.cs#L175-L180) — The OIDC `TokenValidationParameters` doesn't explicitly set `ValidateAudience` or `ValidateLifetime` (both default to true in the OIDC middleware). The JWT Bearer config at lines 58-67 is more explicit. Not a vulnerability, but a defense-in-depth improvement.

---

**[SEC-007] `unsafe-inline` in CSP script-src** — Severity: Low — Status: NEW

[WebApplicationExtensions.cs:87](src/AssetHub.Api/Extensions/WebApplicationExtensions.cs#L87) — `script-src 'self' 'unsafe-inline'` is required for Blazor Server but weakens XSS mitigation from CSP. This is a known limitation of Blazor Server and cannot be avoided without nonce-based CSP (which Blazor doesn't currently support).

---

## 4. Docker & Infrastructure — Score: 8.8/10 (+0.3)

### Resolved

- **Keycloak healthcheck**: Both compose files now use `exec 3<>/dev/tcp/localhost/8080` — compatible with UBI minimal (no curl/grep needed)
- **DEPLOYMENT.md proxy examples**: Nginx uses `http://`, Caddy simplified

### Open Findings

**[DOCKER-008] No Docker log rotation in production compose** — Severity: Low — Status: NEW

[docker-compose.prod.yml](docker/docker-compose.prod.yml) — No log driver configuration. Containers will use the default `json-file` driver with no rotation, eventually filling disk space.

**Recommendation:** Add to each service:
```yaml
logging:
  driver: json-file
  options:
    max-size: "50m"
    max-file: "5"
```

---

**[DOCKER-009] No container security hardening** — Severity: Low — Status: NEW

No services in production compose use `cap_drop: [ALL]`, `read_only: true`, or `security_opt: [no-new-privileges:true]`. These are defense-in-depth measures against container escape.

**Recommendation:** Add to API and Worker services:
```yaml
cap_drop: [ALL]
read_only: true
security_opt: [no-new-privileges:true]
tmpfs: [/tmp]
```

---

**[DOCKER-004] Keycloak admin credentials as env vars** — Severity: Low — Status: OPEN (from Rev 1)

Still using environment variables. Docker secrets would be more secure for production.

---

## 5. Testing & Coverage — Score: 9.0/10

### Positive Findings

- **180+ C# tests**: Repositories, services, endpoints, edge cases (concurrency, security, resilience, smart deletion)
- **15 Playwright E2E specs**: Auth, navigation, collections, assets, shares, admin, ACL, API, viewer role, edge cases, responsive, workflows, language, UI features
- **Proper test infrastructure**: `CustomWebApplicationFactory` with in-memory DB substitution, `TestAuthHandler` for auth bypass, `PostgresFixture` for repository tests
- **bUnit component tests**: AssetGrid, AssetUpload, CreateShareDialog, EditAssetDialog, CollectionTree, ManageAccessDialog, etc.

### Open Findings

**[TEST-007] No unit tests for ShareAccessService** — Severity: Medium — Status: NEW

`ShareAccessService` is one of the most security-critical services (handles anonymous share access, password verification, access token generation/validation, IDOR protection) but has no dedicated unit test file. The endpoint tests (`ShareEndpointTests.cs`) cover the HTTP layer but not the service logic in isolation.

**Recommendation:** Add `ShareAccessServiceTests.cs` covering:
- Token hash lookup (valid, expired, revoked)
- Password verification (correct, wrong, rate limiting)
- Access token generation and validation
- Collection share IDOR protection (asset not in collection)
- Expiry boundary conditions

---

**[TEST-008] No unit tests for AssetUploadService** — Severity: Medium — Status: NEW

`AssetUploadService` handles file uploads with malware scanning, magic byte validation, presigned URLs, and storage — all critical paths. No dedicated unit test file exists. The malware scan tests exist but are focused on the scanner service, not the upload orchestration.

**Recommendation:** Add `AssetUploadServiceTests.cs` covering:
- Content-type rejection for disallowed types
- Magic byte validation failure path
- Malware detection rejection path
- File size limit enforcement
- Authorization checks (contributor required)
- Presigned upload init/confirm flow

---

**[TEST-005] E2E tests not in CI** — Severity: Low — Status: OPEN (from Rev 2)

The 15 Playwright specs still run only locally.

---

**[TEST-006] SecurityTests uses fake share tokens** — Severity: Low — Status: OPEN (from Rev 2)

---

## 6. Configuration & Best Practices — Score: 8.8/10 (+0.3)

### Positive Findings

- **Options validation on startup**: `KeycloakSettings`, `AppSettings`, `MinIOSettings` all use `ValidateDataAnnotations().ValidateOnStart()`
- **Connection pool limits enforced**: `InfrastructureServiceExtensions` sets `MaxPoolSize = 50` and `Timeout = 15s` when Npgsql defaults detected
- **Kestrel limits configured**: Max request body size from config, header timeout 30s, keep-alive 2min
- **Centralized constants**: All magic numbers extracted to `Constants.Limits`
- **Data Protection persisted to DB**: Keys survive container restarts

### Open Findings

**[CFG-003] Keycloak `--import-realm` on every production start** — Severity: Low — Status: OPEN (from Rev 1)

Keycloak safely skips existing realms, but it's unnecessary work on startup.

---

**[CFG-007] Keycloak realm missing brute force protection** — Severity: Medium — Status: NEW

The Keycloak realm import (`keycloak/import/`) should enable brute force protection to complement the application-level `SharePassword` rate limiter. Without it, direct Keycloak login attempts are unlimited.

**Recommendation:** Add to realm configuration:
```json
"bruteForceProtected": true,
"failureFactor": 5,
"maxFailureWaitSeconds": 900,
"waitIncrementSeconds": 60
```

---

## 7. CI/CD & Ways of Working — Score: 8.0/10

### Open Findings

**[CI-003] No integration test stage in CI** — Severity: Medium — Status: OPEN (from Rev 2)

CI runs only unit tests. No docker-compose-based integration tests.

---

**[CI-004] No SAST/DAST tooling** — Severity: Low — Status: OPEN (from Rev 2)

No CodeQL or OWASP ZAP configured.

---

## All Findings Summary

### New Findings (Revision 3) — 12 findings (5 now resolved)

| ID         | Severity   | Category      | Finding |
| ---------- | ---------- | ------------- | ------- |
| CQ-007     | **Medium** | Code Quality  | ✅ RESOLVED — E2E tests use X-Share-Password header |
| CQ-008     | **Medium** | Code Quality  | ✅ RESOLVED — Tightened E2E assertions |
| SEC-002    | **Medium** | Security      | SignalR hub open to anonymous WebSocket connections (no connection rate limit) |
| SEC-003    | **Medium** | Security      | No global rate limiting for authenticated API endpoints |
| SEC-004    | **Medium** | Security      | ✅ RESOLVED — SVG removed from upload allowlist |
| CFG-007    | **Medium** | Configuration | Keycloak realm missing brute force protection |
| TEST-007   | **Medium** | Testing       | ✅ RESOLVED — Added ShareAccessService unit tests (28 tests) |
| TEST-008   | **Medium** | Testing       | No unit tests for AssetUploadService |
| CQ-009     | Low        | Code Quality  | ✅ RESOLVED — Obsolete sub-collection test removed |
| SEC-005    | Low        | Security      | ForwardedHeaders trusts all proxy sources |
| SEC-006    | Low        | Security      | OIDC TokenValidationParameters less explicit than JWT Bearer |
| SEC-007    | Low        | Security      | `unsafe-inline` in CSP script-src (Blazor limitation) |
| DOCKER-008 | Low        | Infrastructure| No Docker log rotation in production compose |
| DOCKER-009 | Low        | Infrastructure| No container security hardening (cap_drop, read_only) |

### Still Open from Previous Revisions — 8 findings

| ID         | Severity   | Finding |
| ---------- | ---------- | ------- |
| CI-003     | Medium     | No integration test stage in CI |
| ARC-001    | Low        | Large constructor injection in some services |
| ARC-002    | Low        | Polymorphic FK on Share entity |
| DOCKER-004 | Low        | Keycloak admin credentials as env vars |
| CFG-003    | Low        | Keycloak `--import-realm` on every production start |
| CI-004     | Low        | No SAST/DAST tooling |
| TEST-005   | Low        | E2E tests not in CI |
| TEST-006   | Low        | SecurityTests uses fake share tokens |

---

## Priority Remediation Roadmap

### Immediate (Before Next Deployment)

| ID      | Action |
| ------- | ------ |
| CQ-007  | Fix E2E share tests to use `X-Share-Password` header instead of query string |
| CQ-008  | Tighten E2E assertions to expect specific status codes |
| CFG-007 | Enable brute force protection in Keycloak realm config |

### Short-Term (Within 2 Weeks)

| ID       | Action |
| -------- | ------ |
| SEC-004  | Remove `application/svg+xml` from upload allowlist or enforce download disposition |
| TEST-007 | Add ShareAccessService unit tests |
| TEST-008 | Add AssetUploadService unit tests |
| CQ-009   | Remove or rewrite sub-collection E2E test |
| SEC-002  | Add connection rate limiting for `/_blazor` WebSocket |
| SEC-003  | Add global authenticated rate limiter |

### Long-Term (Within 3 Months)

| ID         | Action |
| ---------- | ------ |
| CI-003     | Add integration test stage to CI |
| TEST-005   | Integrate E2E tests into CI |
| DOCKER-008 | Add log rotation to production compose |
| DOCKER-009 | Add container security hardening |
| SEC-005    | Restrict ForwardedHeaders to Docker network CIDR |
| CI-004     | Add CodeQL SAST |
| TEST-006   | Use real share tokens in SecurityTests |

---

## Conclusion

AssetHub continues to mature with each audit cycle. The **score improved from 8.8 to 8.9** driven by TLS bypass now properly scoped, Docker healthchecks fixed, deployment docs corrected, and the Blazor anonymous access approach properly implemented.

**Key strengths:**
- Excellent ServiceResult pattern consistency across all endpoints
- Strong share system security (CSPRNG tokens, SHA-256, BCrypt, Data Protection, IDOR checks)
- Clean architecture with proper layer separation
- Comprehensive test suite (180+ unit/integration tests, 15 E2E specs)
- Defense-in-depth authentication (FallbackPolicy + explicit page attributes + smart auth selector)

**Primary areas for improvement:**
- E2E test quality (CQ-007, CQ-008) — passwords sent wrong way, assertions too broad
- Missing unit tests for critical security services (TEST-007, TEST-008)
- Server resource protection (SEC-002, SEC-003) — rate limiting gaps
- SVG upload XSS risk (SEC-004) — either remove from allowlist or enforce safe serving

The application is **production-ready** with the understanding that the E2E test fixes and Keycloak brute force protection should be addressed before deployment.
