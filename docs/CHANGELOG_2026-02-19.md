# AssetHub Changes — 2026-02-19 (Production Hardening & Code Review)

**Date**: 2026-02-19
**Focus**: Security hardening (signed access tokens), code quality (constant extraction, bug fixes), and infrastructure cleanup (pg_trgm migration).
**Build**: 0 warnings, 0 errors
**Tests**: 161 backend + bUnit tests passing

---

## Summary

| # | Change | Type | Files Modified |
|---|--------|------|----------------|
| 1 | Signed access tokens for share downloads | Security | 7 files |
| 2 | Access token bug fixes (rate limit + redundant hash) | Bug fix | 1 file |
| 3 | pg_trgm moved to EF migration | Infrastructure | 2 files (1 new) |
| 4 | Hangfire worker count constants | Code quality | 3 files |
| 5 | UserLookupService log statement bug fix | Bug fix | 1 file |
| 6 | MemoryCache size limit constant extraction | Code quality | 2 files |

---

## 1. Signed Access Tokens for Share Downloads

**Problem**: Share endpoints for download/preview accepted `?password=` query parameters for `<img>`, `<video>`, and `<a>` element URLs (which cannot set HTTP headers). Passwords in query strings appear in server logs, browser history, referrer headers, and proxy/CDN logs.

**Solution**: After initial password authentication (via `X-Share-Password` header), the client obtains a short-lived, DataProtection-signed access token. All subsequent resource URLs use `?accessToken=` instead of `?password=`. The token embeds the share's token hash and an expiry timestamp, signed with ASP.NET DataProtection.

**Token flow**:
1. User enters password → `POST /api/shares/{token}/access-token` (password in `X-Share-Password` header)
2. Server validates password, returns `{ accessToken, expiresInSeconds }` (30-minute lifetime)
3. Client uses `?accessToken=...` for all `<img src>`, `<video src>`, and download links
4. Server validates token signature + expiry on each request (no BCrypt cost)

### Files Modified

| File | Changes |
|------|---------|
| `src/Dam.Application/Constants.cs` | Added `ShareAccessTokenProtector` purpose string, `ShareAccessTokenLifetimeMinutes = 30` |
| `src/Dam.Application/Dtos/ShareDtos.cs` | Added `ShareAccessTokenResponse` DTO (AccessToken, ExpiresInSeconds) |
| `src/Dam.Application/Services/IShareAccessService.cs` | Added `CreateAccessTokenAsync(token, password, ct)` |
| `src/Dam.Infrastructure/Services/ShareAccessService.cs` | Injected `IDataProtectionProvider`. Implemented `CreateAccessTokenAsync` (DataProtection signing). Added `IsValidAccessToken` helper. Modified `ValidateAndGetShareAsync` to accept access tokens as alternative to BCrypt passwords. |
| `src/AssetHub/Endpoints/ShareEndpoints.cs` | Added `POST {token}/access-token` endpoint with `SharePassword` rate limiting. Download/preview endpoints now accept `?accessToken=` query param instead of `?password=`. |
| `src/Dam.Ui/Services/AssetHubApiClient.cs` | Added `GetShareAccessTokenAsync(token, password)` method |
| `src/Dam.Ui/Pages/Share.razor` | Added `_accessToken` field. After password auth, fetches access token. All URL builders use `accessToken` instead of `password` in query strings. |

---

## 2. Access Token Bug Fixes

Two bugs found during self-review of the access token implementation:

### 2a. Wrong rate limit on access-token endpoint
**Bug**: `POST {token}/access-token` used `ShareAnonymous` rate limit (30 req/min) instead of `SharePassword` (10 req/5min).
**Impact**: Endpoint validates passwords — should have the stricter rate limit to prevent brute-force.
**Fix**: Changed `.RequireRateLimiting("ShareAnonymous")` → `.RequireRateLimiting("SharePassword")`.

### 2b. Redundant tokenHash computation
**Bug**: `CreateAccessTokenAsync` computed `ShareHelpers.ComputeTokenHash(token)` to build the signed payload, but the share entity (already loaded and validated) has `share!.TokenHash` available.
**Impact**: Unnecessary SHA-256 computation on every token creation.
**Fix**: Replaced `ShareHelpers.ComputeTokenHash(token)` with `share!.TokenHash`.

**File**: `src/Dam.Infrastructure/Services/ShareAccessService.cs`

---

## 3. pg_trgm Moved to EF Migration

**Problem**: `CREATE EXTENSION IF NOT EXISTS pg_trgm` was executed in `WebApplicationExtensions.MigrateDatabaseAsync()` on every startup. This DDL belongs in the migration history so it runs exactly once and is tracked.

**Changes**:
- Created migration `20260218233901_AddPgTrgmExtension.cs` with `Up` (CREATE EXTENSION) and `Down` (DROP EXTENSION)
- Removed the `ExecuteSqlAsync` call from `WebApplicationExtensions.cs`

**Files**:
| File | Change |
|------|--------|
| `src/Dam.Infrastructure/Migrations/20260218233901_AddPgTrgmExtension.cs` | New — EF migration for pg_trgm |
| `src/AssetHub/Extensions/WebApplicationExtensions.cs` | Removed startup `ExecuteSqlAsync` call |

---

## 4. Hangfire Worker Count Constants

**Problem**: `Math.Max(2, Math.Min(Environment.ProcessorCount, 8))` was duplicated verbatim in `ServiceCollectionExtensions.cs` (API host) and `Worker/Program.cs` (dedicated worker). The two hosts have different workload profiles and should be independently tunable.

**Solution**: Added 4 named constants to `Constants.Limits`:
- `ApiMinHangfireWorkers = 2`, `ApiMaxHangfireWorkers = 8`
- `WorkerMinHangfireWorkers = 2`, `WorkerMaxHangfireWorkers = 8`

**Files**:
| File | Change |
|------|--------|
| `src/Dam.Application/Constants.cs` | Added 4 constants in `Limits` class |
| `src/AssetHub/Extensions/ServiceCollectionExtensions.cs` | Uses `ApiMin/ApiMax` constants |
| `src/Dam.Worker/Program.cs` | Uses `WorkerMin/WorkerMax` constants |

---

## 5. UserLookupService Log Statement Bug

**Bug**: Line 69 of `UserLookupService.cs` logged cache hits as `result.Count - idsToFetch.Count + (result.Count - idsToFetch.Count)`, which is `2 × (result.Count - idsToFetch.Count)` — doubling the reported cache hit count.

**Fix**: Changed to `result.Count - idsToFetch.Count`.

**File**: `src/Dam.Infrastructure/Services/UserLookupService.cs`

---

## 6. MemoryCache Size Limit Constant

**Change**: Extracted the hardcoded `10_000` cache size limit to `Constants.Limits.MemoryCacheSizeLimit` for discoverability alongside other application limits.

**Files**:
| File | Change |
|------|--------|
| `src/Dam.Application/Constants.cs` | Added `MemoryCacheSizeLimit = 10_000` |
| `src/AssetHub/Extensions/ServiceCollectionExtensions.cs` | References `Constants.Limits.MemoryCacheSizeLimit` |

---

## Code Review Notes (No Changes)

The following items were reviewed and determined to need no changes:

| File | Line | Finding | Reason No Change Needed |
|------|------|---------|------------------------|
| `helpers.js` | 47 | Hardcoded polling config (360 attempts × 5s) | Single use case, both callers want same timeout, 30-min ceiling reasonable for ZIP builds |
| `ZipBuildService.cs` | 210 | `Path.GetTempPath()` without disk space check | Pre-flight check would be a race condition; existing `catch` block handles `IOException` gracefully |
| `MinIOAdapter.cs` | 52 | `81920` buffer constant | It's the .NET default for `CopyToAsync` — redundant but harmless |
| `CollectionRepository.cs` | 127 | Unbounded `GetCollectionNamesForAssetsAsync` | All callers are already paginated, no real risk |
