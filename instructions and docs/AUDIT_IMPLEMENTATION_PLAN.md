# Application Audit — Implementation Plan

**Date:** 2026-02-08
**Scope:** Full codebase audit of AssetHub — inconsistencies, bad practices, and inferior function flows.

---

## Legend

| Status | Meaning |
|--------|---------|
| ⬜ | Not started |
| 🔄 | In progress |
| ✅ | Completed |
| ⏭️ | Skipped (not needed) |

---

## #1 ✅ CRITICAL — Tags stored as comma-separated string (lossy serialization)

**File:** `src/Dam.Infrastructure/Data/AssetHubDbContext.cs`
**Problem:** `Tags` uses `string.Join(',', v)` / `v.Split(',')`. Tags containing commas silently split into multiple tags on read.
**Decision:** Migrate to `jsonb` column (JSON array). Requires an EF Core migration.
**Changes:**
- Update `AssetHubDbContext` value conversion from comma-split to JSON serialization
- Generate EF migration to alter column type to `jsonb`
- Verify existing tag data is compatible

---

## #2 ✅ CRITICAL — SearchAllAsync bypasses collection ACLs

**File:** `Endpoints/AssetEndpoints.cs`
**Problem:** `/api/assets/all` returns all ready assets regardless of the caller's collection permissions. Any authenticated user can see assets from collections they don't have access to.
**Decision:** Filter by user's accessible collections — query ACLs first, then restrict search results to those collections only.
**Changes:**
- Inject `ICollectionAuthorizationService` or `ICollectionAclRepository` into the endpoint
- Fetch user's accessible collection IDs
- Pass them to the repository query as a filter
- Update `IAssetRepository.SearchAsync` to accept `allowedCollectionIds` parameter

---

## #3 ✅ HIGH — N+1 query in GetAllAssets

**File:** `Endpoints/AssetEndpoints.cs`
**Problem:** `GetAllAssets` loads assets then loops each one calling `GetCollectionsForAssetAsync(asset.Id)` — producing N+1 queries.
**Decision:** Rewrite to load assets + their collections in one or two queries using JOINs.
**Changes:**
- Add repository method to batch-load collections for multiple assets
- Or include collection info in the main asset query via JOIN
- Update the endpoint to use the new method

---

## #4 ✅ HIGH — Repository two-query anti-pattern materializes all IDs

**File:** `src/Dam.Infrastructure/Repositories/AssetRepository.cs`
**Problem:** `GetByCollectionAsync` loads all join-table IDs into a `List<Guid>`, then uses `WHERE a.Id IN (...)`. `CountByCollectionAsync` loads all IDs into memory just to return `.Count`.
**Decision:** Rewrite both to use proper single-query JOINs.
**Changes:**
- `GetByCollectionAsync`: Single query with `JOIN AssetCollections` filtered by `CollectionId`
- `CountByCollectionAsync`: Single `SELECT COUNT(*) ... JOIN` query
- `SearchAsync`: Same JOIN optimization if applicable

---

## #5 ✅ HIGH — JS eval() for downloads (XSS vector)

**File:** `src/Dam.Ui/Pages/Share.razor`
**Problem:** `DownloadAsset` and `DownloadAllAssets` use `JS.InvokeVoidAsync("eval", ...)` with string interpolation, enabling potential XSS.
**Decision:** Create a named JS function in `wwwroot` and call it via proper JS interop.
**Changes:**
- Create `wwwroot/js/download.js` with a `downloadViaAnchor(url)` function
- Reference the script in the app
- Replace all `eval()` calls with `JS.InvokeVoidAsync("downloadViaAnchor", url)`

---

## #6 ✅ HIGH — Share passwords sent in URL query strings

**Files:** `src/Dam.Ui/Pages/Share.razor`, `Endpoints/ShareEndpoints.cs`
**Problem:** Passwords appended as `?password=...` appear in server logs, browser history, referrer headers, and CDN/proxy logs.
**Decision:** Move passwords to a custom HTTP header (e.g. `X-Share-Password`).
**Changes:**
- Update all share endpoints to read password from `X-Share-Password` header (with query string fallback for backward compat during transition, then remove)
- Update `Share.razor` to send password via header for API calls
- Update preview/download URL generation to not include password in URLs
- For `<img>`, `<video>`, `<iframe>` src attributes that can't set headers: use a token/session approach after initial auth

---

## #7 ✅ MEDIUM — Duplicate DTO definitions (Share page vs Endpoints)

**Files:** `Endpoints/ShareEndpoints.cs`, `src/Dam.Ui/Pages/Share.razor`
**Problem:** `SharedAssetDto`, `SharedCollectionDto`, `CreateShareDto`, `ShareResponseDto` defined inline in `ShareEndpoints.cs`. `Share.razor` redefines `SharedAssetDto` and `SharedCollectionDto` in its `@code` block (missing `Permissions` property).
**Decision:** Move all share DTOs into `Dam.Application/Dtos/` and reference from both.
**Changes:**
- Create `src/Dam.Application/Dtos/ShareDtos.cs` with all share DTOs
- Remove inline definitions from `ShareEndpoints.cs`
- Remove duplicate definitions from `Share.razor`
- Update `using` statements as needed

---

## #8 ✅ MEDIUM — Password verification copy-pasted 4 times

**File:** `Endpoints/ShareEndpoints.cs`
**Problem:** The pattern `if (share has password && wrong password) → return 401` is duplicated across public view, download, download-all, and preview endpoints (~12 lines each).
**Decision:** Extract to a `ValidateShareAccess` helper method.
**Changes:**
- Create a static helper that checks expiry, revocation, and password in one place
- Returns `IResult?` (null = valid, non-null = error response)
- Replace all 4 inline checks with the helper call

---

## #9 ✅ MEDIUM — 5 rendition endpoints with near-identical code

**File:** `Endpoints/AssetEndpoints.cs`
**Problem:** `/thumb`, `/medium`, `/poster`, `/preview`, `/download` all share identical auth check → load asset → get objectKey → presigned URL → redirect.
**Decision:** Extract a shared `ServeRendition` helper.
**Changes:**
- Create helper method that takes asset ID, objectKey selector, and auth context
- Replace each endpoint body with a single call to the helper
- Keep endpoint registrations distinct for OpenAPI documentation

---

## #10 ✅ MEDIUM — CollectionRepository.CreateAsync silently overwrites Id

**File:** `src/Dam.Infrastructure/Repositories/CollectionRepository.cs`
**Problem:** `collection.Id = Guid.NewGuid()` always runs, silently discarding any Id the caller provided.
**Decision:** Only assign if `collection.Id == Guid.Empty`.
**Changes:**
- Guard with `if (collection.Id == Guid.Empty)` before assigning `Guid.NewGuid()`

---

## #11 ✅ MEDIUM — AuditEvent entity is dead infrastructure

**Files:** `src/Dam.Domain/Entities/AuditEvent.cs`, `src/Dam.Infrastructure/Data/AssetHubDbContext.cs`
**Problem:** The entity and table exist but nothing writes audit events.
**Decision:** Implement audit logging for all major operations.
**Scope of audit events:**
- Asset: create, update, delete
- Collection: create, update, delete
- Share: create, revoke, password change
- ACL: grant, revoke, update
- User: create
**Changes:**
- Create `IAuditService` in `Dam.Application/Services/`
- Implement `AuditService` in `Dam.Infrastructure/Services/` using `IAuditRepository`
- Create `IAuditRepository` + `AuditRepository`
- Wire up audit calls in all endpoint handlers for the operations listed above
- Define audit event types as constants

---

## #12 ⏭️ MEDIUM — Share.Asset / Share.Collection navigation properties (SKIPPED — actually used)

**Files:** `src/Dam.Domain/Entities/Share.cs`, `src/Dam.Infrastructure/Data/AssetHubDbContext.cs`
**Problem:** Navigation properties exist but are `.Ignore()`d in DbContext — can never be populated.
**Decision:** Remove the dead navigation properties.
**Changes:**
- Remove `Asset?` and `Collection?` properties from `Share.cs`
- Remove the `.Ignore()` calls from `AssetHubDbContext`
- Update any code that references `share.Asset` or `share.Collection` (AdminEndpoints GetAllShares)
- Adjust `GetAllAsync` in ShareRepository to not try to include them

---

## #13 ✅ MEDIUM — Constants.ScopeTypes defined but not used

**Files:** `src/Dam.Application/Constants.cs`, `Endpoints/ShareEndpoints.cs`
**Problem:** `Constants.ScopeTypes.Asset` / `.Collection` exist but endpoints use hardcoded strings.
**Decision:** Replace all hardcoded scope-type strings with constants.
**Changes:**
- Find all `"asset"` and `"collection"` string literals in share-related code
- Replace with `Constants.ScopeTypes.Asset` / `Constants.ScopeTypes.Collection`

---

## #14 ✅ MEDIUM — Hardcoded limits: 100 assets per share, 1000 for download-all

**Files:** `Endpoints/ShareEndpoints.cs`, `Endpoints/CollectionEndpoints.cs`
**Problem:** Collection shares silently truncate at 100 assets. Download-all truncates at 1000. No indication to the user.
**Decision:** Implement proper pagination.
**Changes:**
- Add `skip`/`take` query parameters to collection share viewing
- Add total count in the response so the client knows there are more
- For download-all, add a warning header if truncated, or increase limit with streaming
- Return a `truncated: true` flag when results are limited

---

## #15 ✅ MEDIUM — CanCreateRootCollectionAsync allows any authenticated user

**File:** `src/Dam.Infrastructure/Services/CollectionAuthorizationService.cs`
**Problem:** Check is simply `!string.IsNullOrEmpty(userId)` — any authenticated user can create root collections.
**Decision:** Require at least manager role (global/Keycloak role).
**Changes:**
- Update `CanCreateRootCollectionAsync` to check for manager+ global role
- Pass `ClaimsPrincipal` to the method so it can check Keycloak realm roles
- Update interface `ICollectionAuthorizationService`
- Update callers

---

## #16 ✅ MEDIUM — Remove group ACL support

**Files:** `src/Dam.Domain/Entities/CollectionAcl.cs`, `src/Dam.Application/Constants.cs`, various
**Problem:** `PrincipalType` supports `"group"` but authorization only checks `"user"`. Group ACLs are stored but never enforced.
**Decision:** Remove group support entirely.
**Changes:**
- Remove `Constants.PrincipalTypes.Group`
- Remove `PrincipalType` regex allowing "group" from DTOs
- Simplify `CollectionAcl` if `PrincipalType` becomes always "user"
- Update admin endpoints to reject group type
- Keep `PrincipalType` column for future re-introduction but validate "user" only

---

## #17 ✅ MEDIUM — UI DTOs split across two layers

**Files:** `src/Dam.Ui/Services/` (5 files), `src/Dam.Application/Dtos/`
**Problem:** `AssetListResponse`, `AllAssetsListResponse`, `ShareResponse`, `AssetUploadResult`, `InitUploadResult` live in `Dam.Ui.Services` instead of `Dam.Application.Dtos`. Naming mismatch: API returns `InitUploadResponse` but UI uses `InitUploadResult`.
**Decision:** Consolidate into `Dam.Application.Dtos`.
**Changes:**
- Move all 5 classes into `Dam.Application/Dtos/`
- Rename to match API response names where inconsistent
- Update namespaces and `using` statements in `AssetHubApiClient` and components
- Delete the old files from `Dam.Ui/Services/`

---

## #18 ✅ MEDIUM — Bare catch in download-all ZIP streaming

**File:** `Endpoints/CollectionEndpoints.cs`
**Problem:** Individual asset download failures are swallowed by `catch { }`. Users get incomplete ZIPs with no indication.
**Decision:** Log failures and include an error text file in the ZIP.
**Changes:**
- Catch specific exceptions, log with asset ID
- Collect failed file names
- After streaming, add an `_errors.txt` entry listing failed files
- Or write the error file inline as each failure occurs

---

## #19 ✅ LOW — Deprecated .HasName() API usage

**File:** `src/Dam.Infrastructure/Data/AssetHubDbContext.cs`
**Problem:** 5 calls to deprecated `.HasName()` produce compiler warnings.
**Decision:** Replace with `.HasDatabaseName()`.
**Changes:**
- Find/replace `.HasName(` → `.HasDatabaseName(` in the DbContext

---

## #20 ✅ LOW — CollectionAcl.RoleEnum and HasAtLeastRole never used

**File:** `src/Dam.Domain/Entities/CollectionAcl.cs`
**Problem:** `RoleEnum` property and `HasAtLeastRole()` method are dead code — all authorization uses `RoleHierarchy` string comparisons.
**Decision:** Remove dead code.
**Changes:**
- Remove `RoleEnum` property, `CollectionRole` enum, and `HasAtLeastRole()` method

---

## #21 ✅ LOW — Bare catches in Keycloak role extraction

**File:** `Program.cs` (helper methods at bottom)
**Problem:** `GetRealmRoles` / `GetClientRoles` use `catch { return Array.Empty<string>(); }` — silently swallows malformed tokens.
**Decision:** Catch `JsonException` specifically and log a warning.
**Changes:**
- Replace bare `catch` with `catch (JsonException ex)`
- Add `ILogger` parameter or use static logger
- Log warning with exception details

---

## #22 ✅ LOW — PasswordGenerator modulo bias

**File:** `src/Dam.Application/Helpers/PasswordGenerator.cs`
**Problem:** `bytes[i] % chars.Length` creates slight bias — some characters are more likely than others.
**Decision:** Use `RandomNumberGenerator.GetInt32()` for unbiased selection.
**Changes:**
- Replace the byte-based approach with `RandomNumberGenerator.GetInt32(0, chars.Length)`

---

## #23 ✅ LOW — Dark mode preference not persisted

**File:** `src/Dam.Ui/Layout/MainLayout.razor`
**Problem:** `_isDarkMode` resets to `true` on every page load.
**Decision:** Store in `localStorage` via JS interop.
**Changes:**
- On init: read `localStorage.getItem("darkMode")` via JS interop
- On toggle: write `localStorage.setItem("darkMode", value)` via JS interop
- Default to `true` when no value stored

---

## #24 ✅ LOW — Swedish comments mixed with English

**File:** `Program.cs`
**Problem:** Several comments are in Swedish while the rest of the codebase is in English.
**Decision:** Translate all Swedish comments to English.
**Changes:**
- Identify and translate each Swedish comment

---

## #25 ✅ LOW — Anonymous response types in endpoints

**Files:** `Endpoints/AssetEndpoints.cs`, `Endpoints/AdminEndpoints.cs`, `Endpoints/CollectionEndpoints.cs`, `Endpoints/ShareEndpoints.cs`
**Problem:** Some endpoints return `new { message = "...", ... }` anonymous objects instead of typed DTOs. This makes OpenAPI docs incomplete and client deserialization fragile.
**Decision:** Replace with typed DTOs.
**Changes:**
- Create `OperationResult` / `MessageResponse` DTOs for simple message responses
- Replace anonymous objects throughout
- Add `.Produces<T>()` to endpoint registrations

---

## Post-Implementation Review Fixes

The following issues were found during code review and fixed immediately:

### R1 ✅ MinIO download streams not disposed in ZIP builders
**Files:** `Endpoints/CollectionEndpoints.cs`, `Endpoints/ShareEndpoints.cs`
**Problem:** `minioAdapter.DownloadAsync()` streams were never disposed, risking connection/handle exhaustion during large ZIP downloads.
**Fix:** Added `await using` to all MinIO download stream variables in both `DownloadAllAssets` and `DownloadAllSharedAssets`.

### R2 ✅ Admin share revocation missing audit log
**File:** `Endpoints/AdminEndpoints.cs`
**Problem:** `POST /api/admin/shares/{id}/revoke` did not inject `IAuditService` or log an audit event, unlike the user-level `RevokeShare`.
**Fix:** Added `IAuditService audit` and `HttpContext httpContext` parameters, logging `"share.revoked"` with `admin: true` detail.

### R3 ✅ CollectionEndpoints audit calls missing HttpContext (no IP/UserAgent)
**File:** `Endpoints/CollectionEndpoints.cs`
**Problem:** All 5 audit calls in collection CRUD + ACL methods passed `ct: ct` without `httpContext`, meaning audit events had no IP address or User-Agent. Other endpoint files consistently pass `httpContext`.
**Fix:** Added `HttpContext httpContext` parameter to `CreateCollection`, `CreateSubCollection`, `UpdateCollection`, `DeleteCollection`, `SetCollectionAccess`, and `RevokeCollectionAccess`. All audit calls now pass `httpContext`.

### R4 ✅ AddAssetToCollection still returned anonymous object
**File:** `Endpoints/AssetEndpoints.cs`
**Problem:** This endpoint was missed during #25 and still returned `new { assetId, collectionId, addedAt, message }`.
**Fix:** Created `AssetAddedToCollectionResponse` DTO and replaced the anonymous object.

### R5 ✅ Double dictionary lookup in GetKeycloakUsers
**File:** `Endpoints/AdminEndpoints.cs`
**Problem:** `userAclGroups.TryGetValue` was called twice per user with the same key (once for `CollectionCount`, once for `HighestRole`).
**Fix:** Refactored to a single `TryGetValue` call with the result reused for both properties.
