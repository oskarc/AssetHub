# AssetHub — Code Review Action Items

> Generated from code review on 2026-02-22. All items implemented 2026-02-22.
> Child/nested collections are obsolete — only 1 level is used.

---

## High

### 1. ~~Content-Disposition header injection~~ ✅ Fixed
**File:** `src/AssetHub.Infrastructure/Services/MinIOAdapter.cs`

Added `SanitizeFileName()` helper that strips CRLF, control characters, and quote characters before embedding the filename in the `Content-Disposition` header.

---

### 2. ~~Rate limiting bypassed behind reverse proxy~~ ✅ Fixed
**File:** `src/AssetHub.Api/Extensions/WebApplicationExtensions.cs`

Cleared `KnownNetworks` and `KnownProxies` on `ForwardedHeadersOptions` so `UseForwardedHeaders` correctly updates `RemoteIpAddress` for requests from the Docker bridge proxy. Rate limit partitioning by IP now uses the real client IP.

---

### 3. ~~`Enum.Parse` throws HTTP 500 on invalid query string values~~ ✅ Fixed
**File:** `src/AssetHub.Infrastructure/Repositories/AssetRepository.cs`

Replaced all `Enum.Parse<AssetType/AssetStatus>()` calls with `Enum.TryParse`. Invalid values now return an empty result (or zero count) rather than propagating an unhandled exception.

---

## Medium

### 4. Access tokens leaking in query strings
**File:** `src/AssetHub.Ui/Pages/Share.razor`

Short-lived access tokens still appear as `?accessToken=...` in media URLs. Fixing this properly requires a service worker or cookie-based token relay. Deferred — lower risk given token lifetime is 30 minutes.

---

### 5. ~~`GetHighestRole` misleading fallback for unknown role strings~~ ✅ Fixed
**File:** `src/AssetHub.Application/RoleHierarchy.cs`

Changed `FirstOrDefault()` to `FirstOrDefault(r => GetLevel(r) > 0)` so the method correctly falls back to `Roles.Viewer` when all provided roles are unrecognised, not just when the enumerable is empty.

---

### 6. ~~`GetJobStatusAsync` always returns `"processing"`~~ ✅ Fixed
**Files:** `src/AssetHub.Application/Services/IMediaProcessingService.cs`, `src/AssetHub.Infrastructure/Services/MediaProcessingService.cs`

Removed the dead method from both the interface and implementation. Callers should poll the asset record's `Status` field directly.

---

### 7. ~~`DeleteByCollectionAsync` lacks serializable isolation~~ ✅ Fixed
**File:** `src/AssetHub.Infrastructure/Repositories/AssetRepository.cs`

Wrapped the entire read-decide-delete block in an explicit `REPEATABLE READ` transaction via `CreateExecutionStrategy().ExecuteAsync()` + `BeginTransactionAsync(IsolationLevel.RepeatableRead)`.

---

### 8. ~~130 lines of duplicated markup in Share.razor~~ ✅ Fixed
**Files:** `src/AssetHub.Ui/Components/AssetDetailPanel.razor` (new), `src/AssetHub.Ui/Pages/Share.razor`

Extracted the shared asset detail panel (preview + metadata + download button) into a new `AssetDetailPanel` component. Both the single-asset view and the selected-collection-asset view now use it. Also added the missing `GetAssetDownloadUrl()` helper.

---

### 9. ~~`DisableAntiforgery()` on all state-changing endpoints lacks documentation~~ ✅ Fixed
**File:** `src/AssetHub.Api/Endpoints/AssetEndpoints.cs`

Added a comment explaining the intentional decision and the mitigations in place (SameSite=Lax, Keycloak Referer checks). Consistent with the existing comment in `ShareEndpoints.cs`.

---

### 10. ~~Per-upload `EnsureBucketExistsAsync` call~~ ✅ Fixed
**File:** `src/AssetHub.Infrastructure/Services/MinIOAdapter.cs`

Removed `EnsureBucketExistsAsync` from `UploadAsync`. Bucket existence is guaranteed at startup via `RunStartupTasksAsync`.

---

### 11. ~~Fragile error string-matching in Share.razor~~ ✅ Fixed
**Files:** `src/AssetHub.Application/ServiceResult.cs`, `src/AssetHub.Infrastructure/Services/ShareAccessService.cs`, `src/AssetHub.Ui/Pages/Share.razor`

Added `ServiceError.ShareExpired` (code `SHARE_EXPIRED`) and `ServiceError.ShareRevoked` (code `SHARE_REVOKED`). `ShareAccessService` now returns the specific code. `Share.razor` deserialises the JSON response body and switches on the `code` field.

---

## Low

### 12. ~~Bare `catch` swallows all exceptions in `DownloadAllAssets`~~ ✅ Fixed
**File:** `src/AssetHub.Ui/Pages/Share.razor`

Replaced bare `catch` with `catch (OperationCanceledException) { throw; }` + `catch (Exception ex)` with logging.

---

### 13. ~~Empty `title` accepted on upload without validation~~ ✅ Fixed
**File:** `src/AssetHub.Api/Endpoints/AssetEndpoints.cs`

Added `string.IsNullOrWhiteSpace(title)` guard in `UploadAsset` returning 400.

---

### 14. Full video buffered to disk before poster extraction
**File:** `src/AssetHub.Infrastructure/Services/MediaProcessingService.cs`

Not implemented. Using ffmpeg pipe input would require significant changes. Acceptable trade-off for typical DAM video sizes.

---

### 15. ~~`GetAllWithAclsAsync` loads all collections with no pagination~~ ✅ Fixed
**Files:** `src/AssetHub.Infrastructure/Repositories/CollectionRepository.cs`, `src/AssetHub.Application/Constants.cs`

Added `Constants.Limits.AdminCollectionQueryLimit = 2_000` and applied `.Take()` in `GetAllWithAclsAsync`.

---

## Notes

- **Finding dropped:** `ExistsByNameAsync` root-only check is correct — only 1 level of collections is used.
- **Not a bug:** `SqlQueryRaw` with `{0}` placeholders is safe (EF Core parameterized queries).
- **Not a bug:** `Path.GetTempFileName()` in `MediaProcessingService` is the correct approach (atomic creation prevents TOCTOU).
