# AssetHub Changes — 2026-02-21 (Testing Completion & Negative Tests)

**Date**: 2026-02-21
**Focus**: Comprehensive negative/anti-test coverage, service-layer integration tests, project rename completion, raw string constants cleanup.
**Build**: 0 warnings, 0 errors
**Tests**: 334 backend + 221 bUnit = 555 tests passing (+ ~173 Playwright E2E)

---

## Summary

| # | Change | Type | Files Modified |
|---|--------|------|----------------|
| 1 | Project rename Dam.* → AssetHub.* | Infrastructure | All projects, csproj, Dockerfiles, compose, namespaces |
| 2 | Phase 6.1: Silent-pass test fixes | Testing | 4 test files |
| 3 | Phase 6.2: Service-layer integration tests (49 tests) | Testing | 4 new test files |
| 4 | Phase 6.3: API endpoint integration tests (42 → 81 tests) | Testing | 5 test files (1 new) |
| 5 | Phase 6.4: Negative test coverage audit (73 anti-tests) | Testing | 4 test files |
| 6 | Raw string constants → typed constants cleanup | Code quality | 10 test files |
| 7 | Production bug fixes (DashboardService, Keycloak mock) | Bug fix | 2 files |

---

## 1. Project Rename: Dam.* → AssetHub.*

Aligned all project names with the **AssetHub** product name. The legacy `Dam.*` prefix has been fully replaced.

| Old Name | New Name |
|----------|----------|
| `AssetHub` (API host) | `AssetHub.Api` |
| `Dam.Application` | `AssetHub.Application` |
| `Dam.Domain` | `AssetHub.Domain` |
| `Dam.Infrastructure` | `AssetHub.Infrastructure` |
| `Dam.Ui` | `AssetHub.Ui` |
| `Dam.Worker` | `AssetHub.Worker` |
| `Dam.Tests` | `AssetHub.Tests` |
| `Dam.Ui.Tests` | `AssetHub.Ui.Tests` |

**Scope**: `.csproj` files, `<RootNamespace>`, `<ProjectReference>` paths, `using` directives across all `.cs` / `.razor` files, `Dockerfile` / `Dockerfile.Worker` build paths, `docker-compose*.yml` service references, `AssetHub.sln` entries.

---

## 2. Phase 6.1: Silent-Pass Test Fixes

**Problem**: Several tests used `// Should not throw` comments followed by `await method()` — which silently passes even if the method was never called or had no assertion.

**Solution**: Replaced with `Record.ExceptionAsync` assertions that explicitly verify no exception is thrown. Also fixed E2E tests that used `if (isVisible)` guards instead of `expect().toBeVisible()` assertions.

### Files Modified

| File | Changes |
|------|---------|
| `AssetRepositoryTests.cs` | `Record.ExceptionAsync` for delete/update operations |
| `CollectionAclRepositoryTests.cs` | `Record.ExceptionAsync` for revoke no-op scenarios |
| `ShareRepositoryTests.cs` | `Record.ExceptionAsync` for increment/delete operations |
| E2E specs | `if (isVisible)` → `expect().toBeVisible()` |

---

## 3. Phase 6.2: Service-Layer Integration Tests (49 tests)

New integration tests for services using Testcontainers (real PostgreSQL) + Moq for MinIO/Keycloak.

| Test File | Tests | Coverage |
|-----------|-------|----------|
| `CollectionServiceTests.cs` | 19 | CRUD, validation, authorization, empty names, long names, hierarchy |
| `CollectionAclServiceTests.cs` | 16 | Grant, revoke, escalation prevention, role hierarchy, admin bypass |
| `DashboardServiceTests.cs` | 6 | Global stats, admin vs viewer, empty data |
| `AssetDeletionServiceTests.cs` | 8 | Exclusive deletion, shared assets, orphan cleanup, MinIO integration |
| **Total** | **49** | |

---

## 4. Phase 6.3: API Endpoint Integration Tests (81 tests)

Expanded API integration test coverage from 42 to 81 tests, including a new `ShareEndpointTests.cs` file.

| Test File | Before | After | Added |
|-----------|--------|-------|-------|
| `AssetEndpointTests.cs` | 15 | 39 | +24 negative tests |
| `CollectionEndpointTests.cs` | 12 | 28 | +16 negative tests |
| `AdminEndpointTests.cs` | 10 | 26 | +16 negative tests |
| `ShareEndpointTests.cs` | 0 (new) | 17 | +17 (2 positive + 15 negative) |
| `DashboardEndpointTests.cs` | 3 | 3 | (unchanged) |
| **Total** | **42** | **81** | **+73** |

All tests use `CustomWebApplicationFactory` with `TestAuthHandler` for authenticated requests and mocked external services (MinIO, Keycloak, Email, Media).

---

## 5. Phase 6.4: Negative Test Coverage Audit (73 anti-tests)

Comprehensive audit identified ~90+ missing negative test scenarios. Implemented 73 new negative tests across 4 endpoint test files:

### Share Endpoint Negatives (15 tests)
- Non-existent share tokens (404)
- Viewer blocked from create/revoke/password update (403)
- Invalid scope type, wrong scope ID
- Non-existent share by ID

### Asset Endpoint Negatives (24 tests)
- Viewer forbidden on update, delete, renditions, upload, add-to-collection (403)
- Non-existent asset on get, update, delete, collections, renditions, thumbnail, download, upload, deletion context (404)
- Collection access checks for assets in inaccessible collections

### Collection Endpoint Negatives (16 tests)
- Empty name validation on create and update (400)
- Viewer can't create subcollections, update, delete, or manage ACLs (403)
- Non-existent collection on get, update, delete, children, ACLs, access operations (404)

### Admin Endpoint Negatives (16 tests)
- Viewer blocked from all 9 admin routes (403)
- Validation: username too short, special characters, invalid email, missing first name (400)
- Non-existent user/share operations (404)
- Keycloak exception propagation (500)

### Coverage Summary
| Metric | Before | After |
|--------|--------|-------|
| Total backend tests | 261 | 334 |
| Negative tests | 74 (~29%) | 147 (~44%) |
| Endpoint tests | 42 | 81 |

---

## 6. Raw String Constants → Typed Constants

Replaced raw string literals in test files with typed constants from `RoleHierarchy.Roles.*`, `Constants.PrincipalTypes.*`, and `Constants.ScopeTypes.*`.

### Files Modified (10 test files)
- `AssetEndpointTests.cs`, `CollectionEndpointTests.cs`, `AdminEndpointTests.cs`, `ShareEndpointTests.cs`
- `CollectionServiceTests.cs`, `CollectionAclServiceTests.cs`, `DashboardServiceTests.cs`, `AssetDeletionServiceTests.cs`
- `SmartDeletionServiceTests.cs`, `DashboardEndpointTests.cs`

---

## 7. Production Bug Fixes

### DashboardService Concurrent DbContext
**Problem**: `Task.WhenAll` with multiple EF Core queries caused `InvalidOperationException` — a second operation was started on a DbContext before a previous one completed.
**Fix**: Changed to sequential `await` calls.

### Missing Keycloak Mock
**Problem**: `GetRealmRoleMemberIdsAsync` was not mocked in `CustomWebApplicationFactory`, causing admin endpoint tests to fail.
**Fix**: Added mock setup returning empty dictionary.

---

## Test Breakdown (334 backend tests)

| Category | Tests |
|----------|-------|
| Asset repository | 37 |
| Collection repository | 17 |
| Asset-collection repository | 16 |
| Collection ACL repository | 11 |
| Share repository | 20 |
| Service-layer integration | 49 |
| API endpoints | 81 |
| Edge cases | 64 |
| Smart deletion (service) | 9 |
| Dashboard endpoints | 3 |
| Collection ACL inheritance | 18 |
| Multi-collection access | 10 |
| **Total** | **334** |
