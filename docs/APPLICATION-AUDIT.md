# AssetHub Application Audit Report

**Date:** 2026-02-23 (Revision 2 — post-remediation re-audit)
**Scope:** Full application architecture, code quality, infrastructure, testing, and operational readiness review
**Application:** AssetHub (.NET 9, Blazor Server, Keycloak, PostgreSQL, MinIO, ClamAV, Hangfire)

---

## Executive Summary

This is the **second audit** of AssetHub, conducted after the developer remediated findings from the initial 2026-02-23 review. The codebase has improved significantly: comprehensive exception handling was added, input validation was expanded across all DTOs, new test suites cover resilience/concurrency/security scenarios, CI now includes Trivy container scanning and code coverage, and DEPLOYMENT.md was created. Several new minor bugs were found during this re-audit.

**Previous Score: 8.3 / 10**
**Current Score: 8.8 / 10**

| Dimension                      | Previous | Current | Delta   |
| ------------------------------ | -------- | ------- | ------- |
| Architecture & Separation      | 8.5/10   | 8.5/10  | —       |
| Code Quality & Patterns        | 8.0/10   | 9.0/10  | +1.0    |
| Security Posture               | 8.1/10   | 8.6/10  | +0.5    |
| Docker & Infrastructure        | 8.4/10   | 8.5/10  | +0.1    |
| Testing & Coverage             | 8.0/10   | 9.0/10  | +1.0    |
| Configuration & Best Practices | 8.5/10   | 8.5/10  | —       |
| CI/CD & Ways of Working        | 6.5/10   | 8.0/10  | +1.5    |

---

## Resolved Findings from Initial Audit

### Fully Resolved (14 of 28 findings closed)

| ID         | Finding                                             | Status                  |
| ---------- | --------------------------------------------------- | ----------------------- |
| CQ-001     | No try-catch blocks in source code                  | ✅ RESOLVED — Comprehensive try-catch added to all infrastructure services (KeycloakUserService, MinIOAdapter, ClamAvScannerService, AssetUploadService, ZipBuildService) |
| RES-001    | No global exception handling middleware              | ✅ RESOLVED — `WebApplicationExtensions.cs:274-385` handles UnauthorizedAccessException, StorageException, BadHttpRequestException, InvalidOperationException, and generic Exception with correlation IDs |
| CQ-003     | Input validation underutilized on DTOs              | ✅ RESOLVED — `[Required]`, `[StringLength]`, `[RegularExpression]`, `[EmailAddress]`, `[MaxLength]` now on all input DTOs (ShareDtos, CollectionDtos, AdminDtos, AssetUploadDto) |
| CQ-002     | Inconsistent logging coverage                       | ✅ RESOLVED — All services now use `ILogger<T>` with structured Serilog, correlation IDs in error responses |
| CFG-002    | DangerousAcceptAnyServerCertificateValidator scope   | ✅ RESOLVED — Completely removed from source code (0 occurrences). Standard HttpClientHandler used everywhere |
| CFG-001    | No DEPLOYMENT.md                                    | ✅ RESOLVED — Comprehensive 15KB+ document covering prerequisites, proxy setup, env config, backup, troubleshooting, upgrade procedures |
| CI-001     | No deployment stage in CI                           | ✅ RESOLVED — Docker build + Trivy scanning stage added, triggered on main push |
| CI-002     | No container vulnerability scanning                 | ✅ RESOLVED — Trivy scans both API and Worker images; fails on CRITICAL/HIGH |
| TEST-001   | No error recovery / resilience tests                | ✅ RESOLVED — `ExternalServiceResilienceTests.cs` (9 tests) covers MinIO failure, scanner timeout, media processing failure |
| TEST-002   | No concurrency tests                                | ✅ RESOLVED — `ConcurrencyTests.cs` (10 tests) covers concurrent deletion, ACL modification, 100-concurrent share access increments |
| TEST-003   | No security-specific tests                          | ✅ RESOLVED — `SecurityTests.cs` (25+ tests) covers role-based authz, parameter tampering, data enumeration, XSS, SQL injection, privilege escalation |
| TEST-004   | No code coverage metrics                            | ✅ RESOLVED — XPlat Code Coverage with Cobertura format in CI, summary posted to GitHub PR |
| DOCKER-002 | ClamAV start_period insufficient                    | ✅ RESOLVED — Increased to 300s in both dev and prod compose |
| DOCKER-003 | Image version mismatch dev/prod                     | ✅ RESOLVED — PostgreSQL pinned to 16.6-alpine, MinIO pinned identically in both files |

### Still Open (14 findings remain, 5 new findings added)

See sections below for current status of each.

---

## 1. Architecture & Separation of Concerns — Score: 8.5/10

### Findings

**[ARC-001] Large constructor injection in service classes** — Severity: Medium — Status: OPEN

Several services inject 10+ dependencies. The codebase has been refactored to extract `AssetUploadService` and `AssetQueryService` from the original monolithic `AssetService`, which is a positive step. However, some services still have large constructor signatures.

**Recommendation:** Continue decomposing if these services grow further. Current state is acceptable.

---

**[ARC-002] Collection hierarchy depth not validated at creation time** — Severity: Low — Status: OPEN

Max depth of 20 is enforced during ACL traversal, but nothing prevents creating collections deeper than 20 levels. No change from initial audit.

---

**[ARC-003] Polymorphic FK on Share entity** — Severity: Low — Status: OPEN

`Share.ScopeType + ScopeId` remains a polymorphic FK with application-level enforcement only. No change from initial audit.

---

## 2. Code Quality & Patterns — Score: 9.0/10 (+1.0)

### Improvements Verified

- **Try-catch everywhere:** All infrastructure services now wrap external calls (MinIO, Keycloak HTTP, ClamAV socket, SMTP) with specific exception handlers. Catches `StorageException`, `HttpRequestException`, `SocketException`, `TaskCanceledException`, `MinioException`, `ObjectNotFoundException`.
- **Global exception middleware:** Comprehensive handler in `WebApplicationExtensions.cs:274-385` with correlation IDs, proper HTTP status mapping (503 for storage errors, 400 for bad requests, 500 for unexpected), and response-started guards.
- **DTO validation:** All input DTOs now have Data Annotation attributes.
- **Logging:** Structured logging with Serilog across all services.

### Remaining Findings

**[CQ-004] Enum-to-string value converter lacks graceful fallback** — Severity: Low — Status: OPEN

No change. Enum conversions still throw `ArgumentOutOfRangeException` on unknown values.

---

**[CQ-005] Nested LINQ subqueries in repository layer** — Severity: Low — Status: OPEN

No change. Some queries use nested `Where + Contains` instead of explicit joins.

---

**[CQ-006] DashboardEndpoints uses manual status code instead of ToHttpResult()** — Severity: Low — Status: NEW BUG

[DashboardEndpoints.cs:21-23](src/AssetHub.Api/Endpoints/DashboardEndpoints.cs#L21-L23) returns `Results.StatusCode(500)` on failure instead of using the `ToHttpResult()` extension that all other endpoints use. This produces a raw 500 response with no JSON body, inconsistent with the structured `ApiError` format the API otherwise returns.

```csharp
// Current (inconsistent):
return result.IsSuccess
    ? Results.Ok(result.Value)
    : Results.StatusCode(500);

// Should be (consistent with all other endpoints):
return result.ToHttpResult();
```

**Impact:** API consumers receive an empty 500 response instead of a structured error with code, message, and correlation ID. Minor because dashboard failures are rare, but breaks the API contract.

**Recommendation:** Replace with `return result.ToHttpResult();`

---

## 3. Security Posture — Score: 8.6/10 (+0.5)

### Improvements Verified

- **DangerousAcceptAnyServerCertificateValidator:** Completely removed (0 occurrences in source). TLS validation enabled everywhere.
- **Fallback authorization policy:** Implemented. All endpoints require authentication by default.
- **DTO validation attributes:** Comprehensive `[Required]`, `[StringLength]`, `[RegularExpression]`, `[EmailAddress]` on all input DTOs.
- **File magic byte validation:** 160+ signatures covering 40+ MIME types.
- **ClamAV integration:** Streaming protocol, health checks, graceful degradation.
- **Audit logging:** Failed share password attempts logged with IP and token hash prefix.

### Remaining Findings

**[SEC-001] FileMagicValidator has overly broad container format matching** — Severity: Low — Status: NEW

[FileMagicValidator.cs](src/AssetHub.Application/Helpers/FileMagicValidator.cs) — WebP validation only checks the RIFF header (`0x52494646`) without verifying "WEBP" at offset 8. HEIC/HEIF/AVIF/MP4/MOV all check for `[0x00, 0x00, 0x00]` at offset 0, which is very broad.

**Impact:** Low. The `AllowedUploadTypes` whitelist restricts accepted MIME types, and the validator operates in fail-open mode for unrecognized formats. A misidentified file type would still need to pass content-type checks. No security exploitation path identified.

**Recommendation:** For higher fidelity, verify the full RIFF+WEBP signature and specific ftyp box brands for MP4/MOV variants.

---

## 4. Docker & Infrastructure — Score: 8.5/10 (+0.1)

### Improvements Verified

- **ClamAV start_period:** Increased to 300s in both compose files.
- **Image pinning:** PostgreSQL `16.6-alpine`, MinIO `RELEASE.2025-01-20T14-49-07Z`, both consistent across dev/prod.
- **DEPLOYMENT.md:** Created with comprehensive setup guide.

### Remaining Findings

**[DOCKER-001] No reverse proxy in production compose** — Severity: Low (downgraded from Critical) — Status: RESOLVED BY DOCUMENTATION

The production compose intentionally excludes a reverse proxy to allow operators to choose their own (Nginx, Caddy, Traefik, cloud ALB). This is now properly documented in [DEPLOYMENT.md:170-228](docs/DEPLOYMENT.md) with complete Nginx and Caddy examples. Downgraded from Critical to Low since it's a documented architectural decision.

---

**[DOCKER-004] Keycloak admin credentials as env vars** — Severity: Low — Status: OPEN

No change. Still using environment variables rather than Docker secrets.

---

**[DOCKER-005] Dev HTTPS certificate generation undocumented** — Severity: Low — Status: OPEN

[DEPLOYMENT.md](docs/DEPLOYMENT.md) now references certificate setup, but no generation script exists.

---

**[DOCKER-006] Production Keycloak missing healthcheck** — Severity: Medium — Status: NEW BUG

[docker-compose.prod.yml:75-119](docker/docker-compose.prod.yml#L75-L119) — The production Keycloak service has **no healthcheck defined**, while the development compose has a proper TCP health check with 60s start_period. The API service depends on Keycloak with `condition: service_started` instead of `condition: service_healthy`.

**Impact:** The API container may start before Keycloak is ready to accept authentication requests. This causes a race condition on first deployment where the first few requests may fail with authentication errors until Keycloak finishes initializing. Subsequent container restarts are less affected because Keycloak starts faster with a warm database.

**Recommendation:** Add the same healthcheck from the development compose:
```yaml
keycloak:
  healthcheck:
    test: ["CMD-SHELL", "exec 3<>/dev/tcp/localhost/8080 && echo -e 'GET /health/ready HTTP/1.1\r\nHost: localhost\r\n\r\n' >&3 && timeout 1 cat <&3 | grep -q '200'"]
    interval: 15s
    timeout: 10s
    retries: 5
    start_period: 60s
```
And update the API dependency to `condition: service_healthy`.

---

**[DOCKER-007] Mailpit uses floating `latest` tag** — Severity: Low — Status: NEW

[docker-compose.yml:165](docker/docker-compose.yml#L165) — The Mailpit dev service uses `axllent/mailpit:latest` while all other services are pinned. Development-only, but inconsistent with the pinning standard applied everywhere else.

**Recommendation:** Pin to a specific version (e.g., `axllent/mailpit:v1.22`).

---

## 5. Testing & Coverage — Score: 9.0/10 (+1.0)

### New Tests Added (Verified)

| Test File | Tests | Purpose | Quality |
| --- | --- | --- | --- |
| `ExternalServiceResilienceTests.cs` | 9 | MinIO failure, scanner timeout, media processing failure | Excellent |
| `ConcurrencyTests.cs` | 10 | Race conditions, concurrent deletion, 100-thread share counter | Excellent |
| `SecurityTests.cs` | 25+ | Role authz, parameter tampering, XSS, SQL injection, privilege escalation | Excellent (1 minor issue) |
| `AssetServiceMalwareScanTests.cs` | 4 | All 4 scan result types (clean, infected, failed, skipped) | Excellent |
| `ClamAvScannerServiceTests.cs` | 9 | Config, connection failure, response parsing | Excellent |
| `KeycloakUserServiceTests.cs` | 9 | Grant type selection, config validation, user creation | Excellent |
| `FileMagicValidatorTests.cs` | 20+ | Magic bytes, spoofing attacks, edge cases | Excellent |
| `SmartDeletionServiceTests.cs` | 8 | Multi-collection deletion, partial access, admin bypass | Excellent |

**Total estimated test count:** 180+ C# tests + 15 Playwright E2E specs.

### Remaining Findings

**[TEST-005] E2E tests not integrated into CI** — Severity: Low — Status: OPEN

The 15 Playwright specs still run only locally.

---

**[TEST-006] SecurityTests uses fake share tokens** — Severity: Low — Status: NEW

[SecurityTests.cs](tests/AssetHub.Tests/EdgeCases/SecurityTests.cs) — Share access control tests use fabricated token strings (e.g., `"expired-share-token-xyz"`) instead of real tokens from seeded shares. The tests correctly verify 401/404 responses for invalid tokens, but they don't exercise the actual expiration and revocation logic paths.

**Impact:** Low. The share expiration/revocation logic is still tested indirectly through other test files. These tests verify the endpoint rejects bad tokens, which is valid. But a dedicated test using a real expired share would be more thorough.

**Recommendation:** Seed actual shares with past expiry dates or revoked status, then use their real tokens to verify the specific rejection reason.

---

## 6. Configuration & Best Practices — Score: 8.5/10

### Findings

**[CFG-003] Keycloak `--import-realm` on every production start** — Severity: Low — Status: OPEN

No change. Keycloak safely skips existing realms, but it's unnecessary work.

---

**[CFG-004] DEPLOYMENT.md Nginx example uses wrong proxy protocol** — Severity: High — Status: NEW BUG

[DEPLOYMENT.md:201-202](docs/DEPLOYMENT.md#L201-L202) — The Nginx reverse proxy example specifies `proxy_pass https://assethub;` but the upstream API listens on **HTTP** only (`ASPNETCORE_URLS: http://+:7252` in docker-compose.prod.yml:131). Nginx will attempt a TLS handshake with a plaintext HTTP server, causing an immediate connection failure.

```nginx
# CURRENT (broken):
proxy_pass https://assethub;

# CORRECT:
proxy_pass http://assethub;
```

**Impact:** High. Anyone following the Nginx deployment guide will get a non-functional reverse proxy. Nginx will log `SSL_do_handshake() failed` errors and return 502 Bad Gateway to all clients.

**Recommendation:** Change `proxy_pass https://assethub;` to `proxy_pass http://assethub;`. The `X-Forwarded-Proto https` header (already present at line 209) correctly tells the application that the original client connection was HTTPS.

---

**[CFG-005] DEPLOYMENT.md Caddy example has unnecessary tls_insecure_skip_verify** — Severity: Low — Status: NEW

[DEPLOYMENT.md:222-225](docs/DEPLOYMENT.md#L222-L225) — The Caddy example includes a `transport http { tls_insecure_skip_verify }` block. Since the upstream is plain HTTP, this TLS configuration block is nonsensical and confusing. It suggests the upstream uses HTTPS with an untrusted certificate, which is not the case.

```caddy
# CURRENT (confusing):
reverse_proxy 127.0.0.1:7252 {
    transport http {
        tls_insecure_skip_verify
    }
}

# CORRECT (simple):
reverse_proxy 127.0.0.1:7252
```

**Impact:** Low. Caddy ignores the `tls_insecure_skip_verify` directive when the upstream is HTTP, so it works. But it's misleading and could confuse operators into thinking TLS is involved.

**Recommendation:** Simplify to `reverse_proxy 127.0.0.1:7252`.

---

**[CFG-006] KC_HOSTNAME_STRICT not documented in .env.template** — Severity: Low — Status: NEW

[docker-compose.prod.yml:94](docker/docker-compose.prod.yml#L94) sets `KC_HOSTNAME_STRICT: "true"` as a hardcoded value (not an environment variable). While this is fine since it should always be `true` in production, the `.env.template` documents the related `KEYCLOAK_HOSTNAME` variable but doesn't mention `KC_HOSTNAME_STRICT` or explain that strict hostname validation is enforced.

**Impact:** Low. The value is correctly hardcoded. An operator who wants to disable it for debugging would need to edit the compose file directly, which is appropriate for a security-sensitive setting.

**Recommendation:** Add a comment in `.env.template` near `KEYCLOAK_HOSTNAME` noting that strict hostname validation is enabled by default in the production compose.

---

## 7. CI/CD & Ways of Working — Score: 8.0/10 (+1.5)

### Improvements Verified

- **Docker build + Trivy scanning** stage added to CI (triggered on main after build/security tests pass).
- **Code coverage** collection with Cobertura format, posted to GitHub PR summary.
- **NuGet vulnerability scanning** with blocking exit code on findings.
- **Proper dependency chaining:** `needs: [build-and-test, security-audit]`.

### Remaining Findings

**[CI-003] No integration test stage in CI** — Severity: Medium — Status: OPEN

CI still runs only unit tests. No docker-compose-based integration test job.

---

**[CI-004] No SAST/DAST tooling** — Severity: Low — Status: OPEN

No CodeQL or OWASP ZAP configured.

---

## All Findings Summary

### New Bugs Introduced (5)

| ID      | Severity | Finding |
| ------- | -------- | ------- |
| CFG-004 | **High** | DEPLOYMENT.md Nginx example uses `proxy_pass https://` to HTTP upstream — will fail |
| DOCKER-006 | **Medium** | Production Keycloak has no healthcheck — race condition on startup |
| CQ-006  | Low      | DashboardEndpoints returns raw 500 instead of structured ApiError JSON |
| CFG-005 | Low      | DEPLOYMENT.md Caddy example has unnecessary tls_insecure_skip_verify |
| TEST-006 | Low     | SecurityTests uses fake share tokens instead of real seeded shares |

### Additional New Observations (3)

| ID         | Severity | Finding |
| ---------- | -------- | ------- |
| SEC-001    | Low      | FileMagicValidator broad container format matching |
| DOCKER-007 | Low      | Mailpit uses floating `latest` tag |
| CFG-006    | Low      | KC_HOSTNAME_STRICT not documented in .env.template |

### Still Open from Initial Audit (8)

| ID         | Severity | Finding |
| ---------- | -------- | ------- |
| CI-003     | Medium   | No integration test stage in CI |
| ARC-001    | Medium   | Large constructor injection in some services |
| ARC-002    | Low      | Collection hierarchy depth not validated at creation |
| ARC-003    | Low      | Polymorphic FK on Share entity |
| CQ-004     | Low      | Enum converter lacks graceful fallback |
| CQ-005     | Low      | Nested LINQ subqueries |
| DOCKER-004 | Low      | Keycloak admin credentials as env vars |
| DOCKER-005 | Low      | Dev HTTPS cert generation undocumented |
| CFG-003    | Low      | Keycloak `--import-realm` on every production start |
| CI-004     | Low      | No SAST/DAST tooling |
| TEST-005   | Low      | E2E tests not in CI |

---

## Priority Remediation Roadmap

### Immediate (Fix Before Deployment)

| ID         | Action |
| ---------- | ------ |
| CFG-004    | Fix Nginx example: change `proxy_pass https://assethub;` to `proxy_pass http://assethub;` in DEPLOYMENT.md:202 |
| DOCKER-006 | Add healthcheck to production Keycloak service in docker-compose.prod.yml |

### Short-Term (Within 2 Weeks)

| ID      | Action |
| ------- | ------ |
| CQ-006  | Replace `Results.StatusCode(500)` with `result.ToHttpResult()` in DashboardEndpoints.cs |
| CFG-005 | Simplify Caddy example in DEPLOYMENT.md |
| CI-003  | Add integration test stage to CI |

### Long-Term (Within 3 Months)

| ID         | Action |
| ---------- | ------ |
| ARC-001    | Continue decomposing large services as they grow |
| ARC-002    | Add depth check on collection creation |
| CQ-004     | Add enum fallback strategy |
| CQ-005     | Profile and optimize nested LINQ queries |
| TEST-005   | Integrate E2E tests into CI |
| TEST-006   | Use real share tokens in SecurityTests |
| CI-004     | Add CodeQL SAST |
| SEC-001    | Improve container format magic byte validation |
| DOCKER-007 | Pin Mailpit version |
| CFG-006    | Document KC_HOSTNAME_STRICT in .env.template |

---

## Conclusion

The remediation effort has been **highly effective**. Of the 28 original findings, 14 have been fully resolved — including all Critical and High severity items from the initial audit. The codebase now has comprehensive exception handling, input validation on all DTOs, 85+ new tests covering resilience/concurrency/security scenarios, and CI includes container vulnerability scanning with code coverage tracking.

**5 new issues were found** during re-audit. The most significant are:
1. **CFG-004 (High):** The Nginx proxy example in DEPLOYMENT.md will cause deployment failures — a one-line fix.
2. **DOCKER-006 (Medium):** Missing Keycloak healthcheck in production creates a startup race condition.

The remaining open findings are predominantly Low severity and represent refinements rather than risks. The application is **production-ready** once the two immediate items are addressed.
