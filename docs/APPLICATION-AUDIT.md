# AssetHub Application Audit Report

**Date:** 2026-02-23
**Scope:** Full application architecture, code quality, infrastructure, testing, and operational readiness review
**Application:** AssetHub (.NET 9, Blazor Server, Keycloak, PostgreSQL, MinIO, ClamAV, Hangfire)

---

## Executive Summary

AssetHub is a self-hosted Digital Asset Management system with a clean multi-layered architecture, strong security posture, and professional test suite. This audit examines architecture, code quality, Docker infrastructure, testing practices, configuration management, and CI/CD readiness.

**Overall Score: 8.3 / 10**

| Dimension                      | Score      | Verdict     |
| ------------------------------ | ---------- | ----------- |
| Architecture & Separation      | 8.5/10     | Excellent   |
| Code Quality & Patterns        | 8.0/10     | Strong      |
| Security Posture               | 8.1/10     | Strong      |
| Docker & Infrastructure        | 8.4/10     | Strong      |
| Testing & Coverage             | 8.0/10     | Strong      |
| Configuration & Best Practices | 8.5/10     | Excellent   |
| CI/CD & Ways of Working        | 6.5/10     | Needs Work  |

---

## 1. Architecture & Separation of Concerns — Score: 8.5/10

### Strengths

- **Clean Architecture with 5 distinct layers:** Domain → Application → Infrastructure → Api → Ui. Each layer depends only inward. The dependency flow is strictly `Api → Application ← Infrastructure` with `Domain` at the center.
- **ServiceResult pattern** — Services return `ServiceResult<T>` instead of throwing exceptions. Endpoints convert these to HTTP responses via `ToHttpResult()`. This makes error paths explicit, testable, and eliminates the exception-as-control-flow anti-pattern.
- **Request-scoped authorization cache** — `CollectionAuthorizationService` caches resolved roles per-request in a dictionary. This eliminates N+1 ACL lookups while guaranteeing fresh permissions on each new HTTP request. Prevents both performance degradation and stale permission bugs.
- **Shared infrastructure registration** — `InfrastructureServiceExtensions.AddSharedInfrastructure()` is shared between the API host and the Worker host, preventing service registration duplication and drift between the two processes.
- **Repository pattern** — All data access goes through interfaces (`IAssetRepository`, `ICollectionRepository`, etc.), enabling testing with mocks and keeping the infrastructure layer swappable.
- **DTO layer separation** — Rich DTOs with context-aware data (e.g., `AssetResponseDto.UserRole` for UI visibility decisions). Separate DTOs for create, update, and response operations.
- **Explicit role hierarchy** — `RoleHierarchy` centralizes role logic with numeric levels (Viewer=1, Contributor=2, Manager=3, Admin=4). All permission checks use `MeetsRequirement()` rather than scattered string comparisons.

### Findings

**[ARC-001] Large constructor injection in service classes** — Severity: Medium

Several services inject 10+ dependencies via constructor injection. While this is not incorrect, it signals that these services may be doing too much and could benefit from decomposition.

**Affected locations:**
- `AssetService` — orchestrates uploads, metadata, authorization, malware scanning, audit, media processing
- `CollectionService` — manages hierarchy, access, bulk downloads, ACL

**Impact:** Harder to test (requires many mocks), harder to understand at a glance, increased risk of unrelated changes causing regressions.

**Recommendation:** Consider introducing facade services or command/query handlers to distribute responsibilities. For example, extract `AssetUploadService` (handling the upload pipeline) and `AssetQueryService` (handling search and retrieval) from the monolithic `AssetService`.

---

**[ARC-002] Collection hierarchy depth not validated at creation time** — Severity: Low

The ACL inheritance traversal in `CollectionAuthorizationService` has a `const int maxDepth = 20` guard to prevent infinite loops from circular parent references. However, there is no validation during collection creation that prevents a user from creating collections deeper than 20 levels.

**Impact:** A deeply nested collection (21+ levels) would silently fail ACL inheritance. Users at the deepest levels would lose inherited permissions without any error message.

**Recommendation:** Add a depth validation check in `CollectionService.CreateAsync()` that walks the parent chain before persisting. Return a clear error if the maximum depth would be exceeded.

---

**[ARC-003] Polymorphic foreign key on Share entity** — Severity: Low

`Share.ScopeType` + `Share.ScopeId` implements a polymorphic relationship pointing to either an `Asset` or a `Collection`. There is no database-level foreign key constraint — referential integrity is enforced only at the application level.

**Impact:** If a bug bypasses the service layer (e.g., direct database manipulation, migration script error), orphaned shares could reference non-existent assets or collections. The current service implementation handles this correctly, so real-world risk is minimal.

**Recommendation:** Consider adding a database trigger or periodic cleanup job that detects and removes orphaned shares. Alternatively, document this as an accepted design trade-off since the application layer handles it correctly.

---

## 2. Code Quality & Patterns — Score: 8.0/10

### Strengths

- **Async-first throughout** — All I/O operations use `async Task<T>` with proper `CancellationToken` propagation. Zero instances of `.Result` or `.Wait()` found. No sync-over-async anti-patterns.
- **Nullable reference types enabled** — Project-wide `<Nullable>enable</Nullable>`. Proper `string?` usage, defensive null checks in constructors and configuration loading with `throw new InvalidOperationException()` for required config.
- **LINQ-to-SQL compilation** — Queries use EF Core's IQueryable pipeline for server-side evaluation. Proper use of `FirstOrDefaultAsync`, `AnyAsync`, `CountAsync`. No accidental client-side evaluation patterns found.
- **Zero raw SQL** — No instances of `ExecuteSqlRaw` or `FromSqlRaw`. All data access is parameterized via EF Core, eliminating SQL injection risks by construction.
- **Centralized constants** — All magic numbers live in `Constants.Limits` (pagination sizes, presigned URL expiry, ZIP limits, worker counts). No scattered literals.
- **Consistent naming** — Interfaces prefixed with `I`, clear `GetAsync`/`CreateAsync`/`DeleteAsync` patterns. Test methods follow `MethodName_Scenario_ExpectedResult`.

### Findings

**[CQ-001] No try-catch blocks in application source code** — Severity: Medium

A search for try-catch blocks across all source files returned zero results. All error handling relies on the `ServiceResult` pattern. While this is a clean approach for business logic errors, it means unhandled exceptions from external dependencies (MinIO HTTP calls, Keycloak API requests, SMTP delivery, ClamAV connections) will bubble up uncontrolled to the global exception handler.

**Impact:** When an external service is temporarily unavailable (e.g., MinIO returns a 503, Keycloak is restarting, ClamAV is loading definitions), the user receives a generic 500 error instead of a meaningful message like "Storage service temporarily unavailable." There is no opportunity for graceful degradation or retry logic.

**Recommendation:**
1. Add targeted try-catch blocks around external service calls in infrastructure services. Catch specific exceptions (e.g., `HttpRequestException`, `SocketException`, `MinioException`) and return appropriate `ServiceResult.Failure()` responses.
2. Add a structured global exception handling middleware for API routes that catches unhandled exceptions, logs them with full context, and returns a clean `ApiError` JSON response (not a stack trace).
3. Consider adding Polly retry policies for transient failures (MinIO, Keycloak HTTP calls).

---

**[CQ-002] Inconsistent logging coverage across services** — Severity: Medium

Only 46 `ILogger` usages were found across 155 source files (~30% of classes have any logging). Service-layer logging is concentrated in a few key services (KeycloakUserService, ClamAvScannerService), while repositories and many other services have little to no logging.

**Impact:** Production debugging of data-layer issues becomes harder. When a repository query returns unexpected results or a service silently returns an empty collection, there is no trace in the logs to diagnose the issue.

**Recommendation:**
1. Add `LogDebug` calls to repository methods for query parameters and result counts.
2. Add `LogInformation` calls to all service methods for significant operations (collection created, ACL changed, share revoked).
3. Add `LogWarning` for unexpected but non-fatal conditions (empty results where data was expected, cache misses).
4. Standardize on structured logging with consistent property names across all services.

---

**[CQ-003] Input validation class exists but is barely used** — Severity: Medium

`InputValidation.cs` provides centralized validation with regex-based validators for username, email, and password. However, only 2 explicit usages were found in the codebase. DTOs lack validation attributes (`[Required]`, `[StringLength]`, `[Range]`). No FluentValidation is integrated.

**Impact:** Invalid data can reach the service layer before being caught. While EF Core will reject null violations at the database level, business validation (string length limits, format requirements, range checks) is not consistently enforced at the API boundary.

**Recommendation:**
1. Add Data Annotation attributes to all input DTOs (`CreateCollectionDto`, `UpdateAssetDto`, etc.) with appropriate constraints.
2. Alternatively, integrate FluentValidation with automatic model validation in the request pipeline.
3. Ensure the existing `InputValidation` class is used wherever user-provided text is processed (not just in the 2 current locations).

---

**[CQ-004] Enum-to-string value converter lacks graceful fallback** — Severity: Low

`DomainEnumExtensions` converts between C# enums and lowercase database strings. If the database contains an unknown string value (e.g., from a migration or manual edit), the converter throws `ArgumentOutOfRangeException` with no fallback.

**Impact:** A single unexpected value in the database could cause an entire query to fail, affecting all results, not just the problematic row. This is especially risky during schema evolution where a new enum value might be added to the database before the application is updated.

**Recommendation:** Add a fallback strategy: either return a default enum value (e.g., `AssetStatus.Unknown`) or catch the exception and log a warning while skipping the problematic entity.

---

**[CQ-005] Nested LINQ subqueries in repository layer** — Severity: Low

Some repository queries use nested `Where` + `Contains` subqueries instead of explicit joins:
```csharp
.Where(a => dbContext.AssetCollections
    .Where(ac => ac.CollectionId == collectionId)
    .Select(ac => ac.AssetId)
    .Contains(a.Id))
```

**Impact:** While EF Core translates this to SQL correctly, explicit joins can produce more predictable query plans and are easier to optimize with indexes on large datasets.

**Recommendation:** For performance-critical queries (asset listing, search), consider refactoring to explicit `Join` syntax. Profile with `EXPLAIN ANALYZE` on representative data volumes to confirm whether this is a real issue or theoretical.

---

## 3. Docker & Infrastructure — Score: 8.4/10

### Strengths

- **Multi-stage Dockerfile builds** — SDK stage for compilation, slim runtime image for deployment. Minimizes attack surface and image size.
- **Pinned base images** — SDK `9.0.102-bookworm-slim` and runtime `9.0.2-bookworm-slim` pinned to patch versions.
- **Non-root execution** — Both Dockerfiles use `USER app` (default from aspnet base image).
- **Network isolation in production** — PostgreSQL, MinIO, Keycloak, and ClamAV have no externally exposed ports. Only the API binds to `127.0.0.1:7252`.
- **Comprehensive health checks** — PostgreSQL (`pg_isready`), MinIO (HTTP health endpoint), Keycloak (TCP check), ClamAV (`clamdcheck.sh`), API (HTTP `/health`). All services use `condition: service_healthy` dependency chains.
- **Resource limits** — Production compose sets memory limits: PostgreSQL 512M, MinIO 512M, Keycloak 768M, API 1G, Worker 1G, ClamAV 1G.
- **Database isolation** — Separate databases (`assethub` + `keycloak`) with dedicated credentials and auto-provisioned via `init-keycloak-db.sh`.
- **ImageMagick hardening** — Custom `policy.xml` disables 13+ dangerous coders (SVG, PDF, PS, MSL, MVG, URL handlers), limits resources (16K max dimensions, 128MP area, 256MiB memory, 2GiB disk, 120s timeout).

### Findings

**[DOCKER-001] No reverse proxy in production docker-compose** — Severity: Critical

`docker-compose.prod.yml` exposes the API on `127.0.0.1:7252` and assumes a reverse proxy exists in front, but no reverse proxy service (Nginx, Caddy, Traefik) is defined in the compose file. The Keycloak service sets `KC_PROXY_HEADERS: xforwarded` (expecting proxy headers), and the API enables `ForwardedHeaders` middleware, but the proxy itself is missing.

**Impact:** Without a reverse proxy, there is:
- No TLS termination or certificate management
- No HTTP→HTTPS redirect at the edge
- No rate limiting or request buffering at the proxy layer
- The application must handle all TLS directly, which is less performant and harder to manage
- New deployments will fail to work with HTTPS out of the box

**Recommendation:** Either:
1. Add an Nginx or Caddy service to `docker-compose.prod.yml` with TLS termination, certificate auto-renewal, and proper proxy headers. Example:
   ```yaml
   nginx:
     image: nginx:1.27-alpine
     ports:
       - "80:80"
       - "443:443"
     volumes:
       - ./nginx.conf:/etc/nginx/nginx.conf:ro
       - /etc/letsencrypt:/etc/letsencrypt:ro
     depends_on:
       api:
         condition: service_healthy
   ```
2. Or create a `DEPLOYMENT.md` document that explicitly details the external reverse proxy requirement with example configurations for Nginx, Caddy, and Traefik.

---

**[DOCKER-002] ClamAV health check start_period may be insufficient** — Severity: Medium

ClamAV is configured with `start_period: 120s` (2 minutes). On first startup, ClamAV must download and load virus definition databases, which can take 2-5 minutes depending on network speed and system resources.

**Impact:** On slower systems or first-time deployments, the ClamAV container may be marked unhealthy before it finishes loading definitions. Services depending on ClamAV (the API) may fail to start or report unhealthy status during this window. Subsequent restarts are faster because definitions are cached in the volume.

**Recommendation:** Increase to `start_period: 300s` (5 minutes). Consider mounting a named volume for `/var/lib/clamav` to persist definitions between container recreations and reduce subsequent startup times.

---

**[DOCKER-003] Image version mismatch between development and production** — Severity: Medium

Development `docker-compose.yml` uses `minio/minio:latest` while production pins `minio/minio:RELEASE.2025-01-20T14-49-07Z`. Similarly, PostgreSQL uses `postgres:16-alpine` without a patch version pin.

**Impact:** Developers may test against a different MinIO version than production, leading to behavioral differences that only surface during deployment. The `latest` tag can change at any time, breaking builds without any code changes.

**Recommendation:**
1. Pin MinIO to the same version in both compose files: `minio/minio:RELEASE.2025-01-20T14-49-07Z`
2. Pin PostgreSQL to a specific patch version: `postgres:16.6-alpine` (or latest stable 16.x)
3. Document version update procedures in a maintenance runbook.

---

**[DOCKER-004] Keycloak admin credentials exposed as environment variables** — Severity: Low

`KC_BOOTSTRAP_ADMIN_USERNAME` and `KC_BOOTSTRAP_ADMIN_PASSWORD` are passed as plain environment variables in both compose files. These are visible via `docker inspect` and in process listings.

**Impact:** Anyone with access to the Docker host can read the admin credentials. While these are only used for initial Keycloak bootstrap, they remain set in the container environment for the lifetime of the container.

**Recommendation:**
1. Document that initial admin credentials should be rotated via the Keycloak admin console immediately after first startup.
2. For higher security environments, use Docker secrets instead:
   ```yaml
   secrets:
     keycloak_admin_password:
       file: ./secrets/keycloak_admin_password
   ```
3. Consider unsetting the bootstrap environment variables after first startup (Keycloak 26.x supports this).

---

**[DOCKER-005] Development HTTPS certificate not documented** — Severity: Low

`docker-compose.yml` references `../certs/dev-cert.pfx` for both Kestrel and Keycloak HTTPS configuration, but this file is not in version control (correctly gitignored) and no generation script or setup instructions exist.

**Impact:** New developers cloning the repository will get container creation errors when the certificate file is missing. This creates a friction point in the onboarding experience.

**Recommendation:** Either:
1. Add a `scripts/generate-dev-cert.sh` script that creates the certificate and document it in the README.
2. Or make the dev certificate optional by falling back to HTTP-only in development when the cert file is absent.

---

## 4. Testing & Coverage — Score: 8.0/10

### Strengths

- **Real database testing** — `CustomWebApplicationFactory` uses Testcontainers to spin up a real PostgreSQL instance. Tests run against actual database behavior, not an in-memory fake. This catches real SQL issues, index behavior, and constraint violations.
- **Comprehensive mock setup** — External services (MinIO, Keycloak, Email, ClamAV) are mocked with sensible defaults. The `CustomWebApplicationFactory` exposes `MockMinIO`, `MockUserLookup`, etc. for per-test customization.
- **Flexible test authentication** — `TestAuthHandler` with `TestClaimsProvider` supports `Default()` (Viewer), `Admin()`, and `WithUser(userId, username, role)` for per-test claim override without modifying global state.
- **Excellent edge case coverage** — `SmartDeletionServiceTests` tests 6 sophisticated scenarios (exclusive deletion, partial access unlink, system admin bypass, forbidden). `CollectionAclInheritanceTests` tests 18 scenarios including 5-level deep hierarchies and closest-ancestor-wins behavior.
- **Test data builders** — `TestData.CreateAsset()` and `TestData.CreateCollection()` use optional parameters with sensible defaults, reducing test boilerplate while allowing per-test customization.
- **Strong test isolation** — Each test gets a unique database name, `EnsureDeletedAsync` cleans up after each test, no shared mutable state between test classes.
- **E2E coverage** — 15 Playwright specs covering authentication, navigation, collections, assets, shares, admin, ACL, responsive design, accessibility, i18n, and multi-step workflows.
- **Consistent naming** — All tests follow `MethodName_Scenario_ExpectedResult` convention.

### Findings

**[TEST-001] No error recovery or resilience tests** — Severity: Medium

No tests verify service behavior when external dependencies fail. There are no tests for MinIO unavailability, Keycloak timeout, ClamAV connection refused, SMTP delivery failure, or database connection pool exhaustion.

**Impact:** Without resilience tests, the application's behavior during partial outages is unknown. If MinIO goes down during an upload, does the user get a clear error or a stack trace? If ClamAV is restarting, are uploads blocked or does the scan gracefully degrade? These questions cannot be answered without dedicated tests.

**Recommendation:**
1. Add tests that configure mocks to throw `HttpRequestException`, `SocketException`, `TimeoutException` and verify the service returns an appropriate `ServiceResult.Failure()`.
2. Test that ClamAV unavailability with `ClamAV:Enabled = true` returns a clear error, not a crash.
3. Test database connection timeout behavior.

---

**[TEST-002] Limited concurrency and race condition tests** — Severity: Medium

Only 1 test addresses concurrent modification: `UpdateAsync_ConcurrentModification_LastWriteWins`. No tests exist for simultaneous deletion of the same asset, concurrent ACL modifications, race conditions on share access counting, or parallel upload confirmation.

**Impact:** Race conditions may exist in production under concurrent load. The `AssetDeletionService` could potentially double-delete an asset if two requests arrive simultaneously. Share access counters could lose increments under high concurrency.

**Recommendation:**
1. Add tests that execute the same operation from multiple threads using `Task.WhenAll()`.
2. Test simultaneous deletion of the same asset from two different collection contexts.
3. Test concurrent share access with password verification.
4. Consider adding optimistic concurrency tokens (`RowVersion`) to entities that are frequently updated.

---

**[TEST-003] No security-specific test cases** — Severity: Low

While the application uses EF Core (which prevents SQL injection by construction) and has strong authorization checks, there are no explicit security tests that verify:
- Parameter tampering (modifying IDs in requests to access other users' resources)
- Authorization bypass attempts (accessing admin endpoints with viewer role)
- XSS vector injection in asset titles/descriptions
- Path traversal in file names

**Impact:** Security properties are implicitly tested through integration tests but not explicitly verified. A refactoring that accidentally removes an authorization check would not be caught by any dedicated security test.

**Recommendation:**
1. Add explicit authorization tests: verify that a Viewer cannot access `RequireAdmin` endpoints (returns 403).
2. Add parameter tampering tests: verify that User A cannot access User B's collections by guessing collection IDs.
3. Add input sanitization tests: verify that HTML/script content in asset titles is properly escaped in API responses.

---

**[TEST-004] No code coverage metrics configured** — Severity: Low

No coverage tool (Coverlet, dotCover) is configured in the test projects or CI pipeline. The estimated coverage based on test density is 50-70%, but there is no way to track this over time or enforce a minimum threshold.

**Impact:** Coverage may silently decrease over time as new features are added without proportional test additions. There is no visibility into which code paths are untested.

**Recommendation:**
1. Add Coverlet to the test projects: `dotnet add package coverlet.msbuild`
2. Configure CI to generate coverage reports: `dotnet test --collect:"XPlat Code Coverage"`
3. Set a baseline threshold (e.g., 70%) and fail the build if coverage drops below it.
4. Generate coverage reports as CI artifacts for developer review.

---

**[TEST-005] E2E tests not integrated into CI pipeline** — Severity: Low

15 well-structured Playwright E2E specs exist but are not triggered by the CI workflow (`.github/workflows/ci.yml`). They run only locally.

**Impact:** E2E regressions are caught only when a developer remembers to run the E2E suite manually. UI-breaking changes can merge to main without detection.

**Recommendation:**
1. Add a CI job that starts the application stack via docker-compose, waits for health checks, then runs the Playwright suite.
2. Use the existing Playwright configuration's JUnit XML output for CI test result reporting.
3. Consider running E2E tests on a schedule (nightly) rather than on every push if they are too slow for PR workflows.

---

## 5. Configuration & Best Practices — Score: 8.5/10

### Strengths

- **Layered configuration** — `appsettings.json` → `appsettings.{Environment}.json` → environment variables → `appsettings.Local.json` (gitignored). Clear precedence chain.
- **No hardcoded secrets** — All credentials are empty strings in base config, overridden via environment variables in Docker.
- **Options validation at startup** — `ValidateDataAnnotations()` + `ValidateOnStart()` catches missing or invalid configuration immediately, preventing runtime surprises.
- **Environment-appropriate defaults** — Development allows HTTP, disables HTTPS metadata; Production restricts `AllowedHosts`, uses compact JSON logging (Warning level), enables HSTS.
- **Certificates gitignored** — `*.pfx`, `*.pem`, `*.key`, `*.crt` all excluded from version control.
- **Clear environment template** — `.env.template` (126 lines) documents every variable with descriptions and safe defaults.
- **Structured logging** — Serilog with environment/machine name/thread ID enrichment. Production uses compact JSON format for log aggregation.

### Findings

**[CFG-001] No DEPLOYMENT.md documentation** — Severity: Medium

The production compose file assumes knowledge of reverse proxy configuration, certificate generation, environment variable setup, Keycloak hardening, and backup strategy. No deployment guide exists.

**Impact:** New operators deploying to production must reverse-engineer the requirements from docker-compose files and configuration templates. This increases deployment errors and security misconfigurations.

**Recommendation:** Create a `docs/DEPLOYMENT.md` covering:
1. Prerequisites (Docker, DNS, TLS certificates)
2. Environment variable configuration (walking through `.env.template`)
3. Reverse proxy setup (with examples for Nginx, Caddy)
4. Initial Keycloak configuration (admin credential rotation, realm import)
5. Backup strategy (PostgreSQL dumps, MinIO data, ClamAV definitions)
6. Health check monitoring endpoints
7. Log aggregation setup
8. Upgrade procedures

---

**[CFG-002] `DangerousAcceptAnyServerCertificateValidator` scope too broad** — Severity: Medium

The TLS validation bypass appears in 4 locations and is gated by `!environment.IsProduction()`. This means it is active not only in Development but also in Staging or any custom environment name.

**Affected locations:**
- `AuthenticationExtensions.cs:51-55` (JWT Bearer backchannel)
- `AuthenticationExtensions.cs:146-150` (OIDC backchannel)
- `ServiceCollectionExtensions.cs:173-176` (Keycloak HttpClient)
- `ServiceCollectionExtensions.cs:209-211` (UI HttpClient)

**Impact:** If a staging environment is configured with `ASPNETCORE_ENVIRONMENT=Staging`, all TLS validation is disabled. A man-in-the-middle attack could intercept Keycloak tokens or API calls in staging.

**Recommendation:** Restrict to `environment.IsDevelopment()` only (not `!IsProduction()`). This ensures TLS validation is only disabled in the explicitly named "Development" environment.

---

**[CFG-003] Keycloak realm import runs on every production start** — Severity: Low

`docker-compose.prod.yml` includes `--import-realm` in the Keycloak command. Keycloak safely skips existing realms, so this is functionally harmless. However, it performs unnecessary file parsing and validation on every container restart.

**Impact:** Minimal — a few seconds of wasted startup time. No data risk since Keycloak's import is idempotent.

**Recommendation:** Use a separate init container or one-time script for realm import, then remove `--import-realm` from the production command. This makes the production startup cleaner and more intentional.

---

## 6. CI/CD & Ways of Working — Score: 6.5/10

### Strengths

- **Automated build and test** — CI triggers on push to main/develop and on pull requests.
- **.NET 9 build in Release mode** — Catches compilation errors that only appear in Release (e.g., trimming, AOT).
- **Test result artifacts** — TRX output uploaded as GitHub Actions artifacts for review.
- **NuGet vulnerability audit** — `dotnet list package --vulnerable` runs as a CI step.
- **Docker image build with caching** — GitHub Actions cache used for Docker layer caching.

### Findings

**[CI-001] No deployment stage — images built but never pushed** — Severity: High

Docker images are built in CI with `push: false`. There is no continuous deployment to any environment. Production deployments must be done manually.

**Impact:** Manual deployments are error-prone and inconsistent. There is no automated path from a merged PR to a running production instance. This slows down the release cycle and increases the risk of deployment mistakes.

**Recommendation:**
1. Add a deployment stage that pushes Docker images to a container registry (GitHub Container Registry, Docker Hub, or a private registry) on merge to main.
2. Consider separate deployment jobs for staging (automatic on merge) and production (manual approval gate).
3. Tag images with both the commit SHA and a semantic version for traceability.

---

**[CI-002] No container image vulnerability scanning** — Severity: Medium

Built Docker images are not scanned for known vulnerabilities. The base images (`aspnet:9.0.2-bookworm-slim`) and installed system packages (ImageMagick, FFmpeg, libvips) may contain CVEs.

**Impact:** Vulnerabilities in system packages or base images could be deployed to production without detection. These are particularly risky because they are outside the application code and not covered by NuGet vulnerability checks.

**Recommendation:**
1. Add Trivy as a CI step after Docker image build:
   ```yaml
   - name: Scan image
     uses: aquasecurity/trivy-action@master
     with:
       image-ref: assethub-api:latest
       severity: CRITICAL,HIGH
       exit-code: 1
   ```
2. Consider also scanning the Worker image.
3. Set a policy: fail the build on CRITICAL vulnerabilities, warn on HIGH.

---

**[CI-003] No integration test stage in CI** — Severity: Medium

CI runs only unit tests. There are no docker-compose-based integration tests that verify the full stack (API + PostgreSQL + MinIO + Keycloak) works together.

**Impact:** Integration issues (database migration failures, MinIO connectivity, Keycloak token validation with real OIDC flows) are caught only during manual testing or in production.

**Recommendation:**
1. Add a CI job that starts the development docker-compose stack.
2. Wait for all health checks to pass.
3. Run a subset of integration tests against the running stack.
4. This can run as a separate workflow on a schedule (nightly) if it's too slow for every PR.

---

**[CI-004] No SAST or DAST tooling** — Severity: Low

No static application security testing (CodeQL, SonarQube, Semgrep) or dynamic application security testing (OWASP ZAP) is configured.

**Impact:** Security regressions (e.g., accidentally removing an authorization check, introducing an injection vulnerability) are not automatically detected.

**Recommendation:**
1. Enable GitHub CodeQL for C# analysis (free for public and private repositories):
   ```yaml
   - name: Initialize CodeQL
     uses: github/codeql-action/init@v3
     with:
       languages: csharp
   ```
2. Consider adding OWASP ZAP as a DAST step against the running integration test environment.

---

## 7. Additional Findings

### Error Handling & Resilience

**[RES-001] No global exception handling middleware for API routes** — Severity: Medium

The current exception handler catches `UnauthorizedAccessException` but does not produce structured `ApiError` JSON for other unhandled exceptions. An unexpected null reference or network timeout produces a raw 500 response.

**Impact:** API consumers receive inconsistent error formats — sometimes a structured `ApiError` (from ServiceResult failures) and sometimes a raw server error. This makes client-side error handling unreliable.

**Recommendation:** Add middleware that catches all unhandled exceptions on `/api/*` routes, logs the full exception with context, and returns a consistent `ApiError` response:
```csharp
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ApiError
        {
            Code = "INTERNAL_ERROR",
            Message = "An unexpected error occurred"
        });
    });
});
```

---

### Performance

**[PERF-001] Presigned URL generation not cached** — Severity: Low

Each request for an asset download, preview, or thumbnail generates a new presigned MinIO URL via an HTTP call to MinIO. Under high traffic, this creates significant overhead.

**Impact:** For a page displaying 50 thumbnails, 50 separate presigned URL requests are made to MinIO. This adds latency to page loads and puts unnecessary load on the MinIO service.

**Recommendation:** Consider caching presigned URLs with a TTL shorter than their expiry time. For example, if URLs expire in 60 minutes, cache them for 45 minutes. Use `IMemoryCache` with the object key as the cache key.

---

**[PERF-002] Keycloak token acquisition serialized via SemaphoreSlim** — Severity: Low

`KeycloakUserService` uses a `SemaphoreSlim(1, 1)` to serialize all token acquisition requests. When the cached token expires, all concurrent admin API calls queue up waiting for a single token refresh.

**Impact:** Under high concurrency (e.g., bulk user provisioning), this becomes a bottleneck. Only one token refresh can happen at a time while all other requests wait.

**Recommendation:** This is currently an acceptable trade-off for correctness (prevents duplicate token requests). If it becomes a bottleneck, consider using a `Lazy<Task<T>>` pattern with atomic swap for lock-free token caching.

---

## Score Summary

| Area                             | Score      | Issues Found |
| -------------------------------- | ---------- | ------------ |
| Architecture & Separation        | 8.5/10     | 3            |
| Code Quality & Patterns          | 8.0/10     | 5            |
| Docker & Infrastructure          | 8.4/10     | 5            |
| Testing & Coverage               | 8.0/10     | 5            |
| Configuration & Best Practices   | 8.5/10     | 3            |
| CI/CD & Ways of Working          | 6.5/10     | 4            |
| Error Handling & Resilience      | 7.0/10     | 1            |
| Performance                      | 8.0/10     | 2            |
| **Overall**                      | **8.3/10** | **28**       |

---

## Priority Remediation Roadmap

### Critical (Address Immediately)

| ID         | Finding                                                    |
| ---------- | ---------------------------------------------------------- |
| DOCKER-001 | Add reverse proxy to production compose or document requirement |

### High Priority (Address Within 2 Weeks)

| ID      | Finding                                              |
| ------- | ---------------------------------------------------- |
| CI-001  | Add deployment stage to CI — push images to registry |
| CQ-001  | Add targeted exception handling around external service calls |
| RES-001 | Add global exception handling middleware for API routes |

### Medium Priority (Address Within 1 Month)

| ID         | Finding                                                       |
| ---------- | ------------------------------------------------------------- |
| CQ-002     | Standardize logging coverage across all services              |
| CQ-003     | Expand input validation on DTOs                               |
| CI-002     | Add container vulnerability scanning (Trivy)                  |
| CI-003     | Add integration test stage to CI                              |
| CFG-001    | Create DEPLOYMENT.md documentation                            |
| CFG-002    | Restrict TLS bypass to `IsDevelopment()` only                 |
| TEST-001   | Add error recovery and resilience tests                       |
| TEST-002   | Add concurrency and race condition tests                      |
| DOCKER-002 | Increase ClamAV start_period to 300s                          |
| DOCKER-003 | Pin all container image versions consistently across environments |

### Low Priority (Address Within 3 Months)

| ID         | Finding                                                     |
| ---------- | ----------------------------------------------------------- |
| ARC-001    | Decompose large service classes                             |
| ARC-002    | Add collection hierarchy depth validation at creation time  |
| ARC-003    | Add orphaned share cleanup mechanism                        |
| CQ-004     | Add graceful fallback for enum-to-string conversion         |
| CQ-005     | Optimize nested LINQ subqueries with explicit joins         |
| DOCKER-004 | Document Keycloak admin credential rotation                 |
| DOCKER-005 | Document or automate development certificate generation     |
| TEST-003   | Add explicit security test cases                            |
| TEST-004   | Configure code coverage metrics and thresholds              |
| TEST-005   | Integrate E2E tests into CI pipeline                        |
| CI-004     | Add SAST/DAST tooling (CodeQL, OWASP ZAP)                  |
| CFG-003    | Remove `--import-realm` from production Keycloak command    |
| PERF-001   | Cache presigned MinIO URLs                                  |
| PERF-002   | Optimize Keycloak token caching under high concurrency      |

---

## Conclusion

AssetHub demonstrates **professional, enterprise-grade software engineering** across all dimensions. The clean architecture separation is exemplary, the security posture includes proper defense-in-depth, and the test suite covers sophisticated edge cases with real database integration. The Docker infrastructure is well-configured with health checks, network isolation, and resource limits.

The primary gaps are operational: **missing deployment documentation**, **no CD pipeline**, and **no reverse proxy in the production compose file**. On the code side, the main opportunities are around **error recovery resilience**, **input validation coverage**, and **logging consistency**. These are all tractable improvements on an otherwise strong foundation.

This audit should be revisited quarterly or after significant architectural changes.
