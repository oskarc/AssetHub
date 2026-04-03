# AssetHub Review Report

## Table of Contents

1. [Redundant Code](#1-redundant-code)
2. [Oversized Components & Services](#2-oversized-components--services)
3. [Out of Scope / Over-Engineered](#3-out-of-scope--over-engineered)
4. [Missing Features](#4-missing-features)
5. [Inconsistencies](#5-inconsistencies)
6. [Performance Issues](#6-performance-issues)
7. [Bug Fixes](#7-bug-fixes)

---

## 1. Redundant Code

### 1.1 ~~CreateCollectionDialog / EditCollectionDialog Are 95% Identical~~ ✅ DONE

**Files:** `Components/CreateCollectionDialog.razor`, `Components/EditCollectionDialog.razor`

**Resolution:** Extracted shared `Components/CollectionForm.razor` component. Both dialogs now use `<CollectionForm @ref="_collectionForm" />` as a shared form, reducing each dialog to ~40-50 lines. Validation is handled via `CollectionForm.ValidateAsync()`. Tests updated to verify the new structure.

---

### 1.2 ~~Duplicate Dark Mode Initialization~~ ✅ DONE

**Files:** `Layout/MainLayout.razor`, `Layout/ShareLayout.razor`, `Pages/Home.razor`

**Resolution:** Created `Services/ThemeService.cs` as a scoped service managing dark mode state centrally. `MainLayout` and `ShareLayout` inject `ThemeService`, call `InitializeFromCookies()` during `OnInitialized()` and `InitializeFromLocalStorageAsync()` in `OnAfterRenderAsync`. `Home.razor` removed all dark mode fields and initialization (~26 lines removed). Both layouts now implement `IDisposable` to unsubscribe from `ThemeService.OnChange`. DI registration added in `ServiceCollectionExtensions`.

---

### 1.3 ~~Media Preview Logic Duplicated~~ ✅ DONE

**Files:** `Components/MediaPreview.razor` (new), `Components/AssetDetailPanel.razor`, `Pages/AssetDetail.razor`

**Resolution:** Extracted `Components/MediaPreview.razor` — a shared component handling conditional rendering for image/video/PDF/fallback previews. Both `AssetDetailPanel.razor` and `AssetDetail.razor` now use `<MediaPreview>` instead of inline conditional markup. `AssetDetail.razor`'s `GetPreviewUrl()` unified to handle video/PDF/image URL selection in one method, removing `GetVideoUrl()` and `GetInlinePreviewUrl()`. ~25 lines of duplicate preview markup eliminated from each consumer.

---

### 1.4 ~~Dialog Parameter Ceremony Repeated 10+ Times~~ ✅ DONE

**Files:** `Pages/Assets.razor`, `Pages/AssetDetail.razor`, `Components/AssetGrid.razor`, `Components/AdminUsersTab.razor`, and others.

**Resolution:** Created `Services/DialogExtensions.cs` with two extension methods on `IDialogService`:
- `ShowConfirmAsync()` — encapsulates `ConfirmDialog` parameter construction and result checking, returns `bool`. Refactored 13 call sites across 10 components.
- `ShowShareFlowAsync()` — encapsulates the `CreateShareDialog` → `ShareLinkDialog` two-step flow, returns `ShareResponseDto?`. Refactored 4 call sites across 4 components.

One `ConfirmDialog` usage intentionally left as-is (`AssetUpload.razor` navigation guard) because it requires custom `DialogOptions` and a re-entrant navigation pattern.

**Files modified:** `AssetDetail.razor`, `AllAssets.razor`, `Assets.razor`, `AssetGrid.razor`, `AdminSharesTab.razor`, `AdminCollectionAccessTab.razor`, `AdminUsersTab.razor`, `BulkAssetActionsDialog.razor`, `BulkCollectionActionsDialog.razor`, `ManageAccessDialog.razor`, `ManageUserAccessDialog.razor`, `UserAccessDialog.razor`

---

### 1.5 ~~Localization Helper Delegation Repeated ~15 Times~~ ✅ DONE

**Files:** `Pages/Home.razor`, `Pages/Share.razor`, `Pages/AssetDetail.razor`, `Components/AdminSharesTab.razor`, `Components/AdminAuditTab.razor`, `Components/SharePasswordDialog.razor`, `Components/SharedCollectionView.razor`

**Resolution:** Created `Services/LocalizedDisplayService.cs` — a scoped service wrapping `IStringLocalizer<CommonResource>` with methods `Role()`, `AssetType()`, `ContentType()`, `ScopeType()`. Registered in DI and injected as `@inject LocalizedDisplayService Display` in all 7 affected components. Removed ~15 one-liner wrapper methods (`GetLocalizedRole`, `GetAssetTypeLabel`, `GetFriendlyAssetType`, `GetFriendlyContentType`, `GetLocalizedScopeType`, `GetLocalizedTargetType`) and replaced all call sites with `Display.*` calls.
4. Replace all local delegation methods with `Display.GetRole(role)`, etc.
5. Remove ~3-5 lines from each of the ~15 components that currently define these wrappers.

**Effort:** Small (1-2 hours)

---

## 2. Oversized Components & Services

### 2.1 ~~Home.razor (616 lines) — Dashboard Page~~ ✅ DONE

**Resolution:** Decomposed Home.razor from ~580 lines into 5 focused sub-components + a ~170-line orchestrator:

1. **`Components/Dashboard/DashboardStatsPanel.razor`** (~130 lines) — Admin platform stats cards (6 stat cards with asset/storage/collection/user/share/audit counts) + storage pie chart with theme-aware colors. Parameters: `DashboardStatsDto? Stats`, `bool Loading`. Owns `BuildStorageChart` logic in `OnParametersSet`.

2. **`Components/Dashboard/RecentAssetsGrid.razor`** (~70 lines) — Recent asset cards with thumbnails, creator names, and relative timestamps. Parameters: `List<AssetResponseDto> RecentAssets`, `bool Loading`, `EventCallback<Guid> OnAssetClicked`.

3. **`Components/Dashboard/QuickAccessCollections.razor`** (~75 lines) — Collection quick-access cards with folder icons, role chips, and asset counts. Parameters: `List<CollectionResponseDto> Collections`, `bool Loading`, `EventCallback<Guid> OnCollectionClicked`.

4. **`Components/Dashboard/ActiveSharesList.razor`** (~90 lines) — Active shares sidebar with status chips, password indicators, and share info dialog. Parameters: `List<DashboardShareDto> Shares`, `bool IsAdmin`. Owns `GetShareStatusColor/Label` and `ShowShareInfo` logic.

5. **`Components/Dashboard/ActivityTimeline.razor`** (~65 lines) — Activity timeline with event type colors, actor info, and relative timestamps. Parameters: `List<AuditEventDto> Events`, `bool IsAdmin`. Owns `GetLocalizedEventType` logic.

`Home.razor` now serves as a slim orchestrator: fetches dashboard data, determines visibility flags (`_isAdmin`, `_showShares`, `_showActivity`), and composes sub-components with parameters. Removed 4 helper methods (`FormatTimeAgo`, `BuildStorageChart`, `GetShareStatusColor/Label`, `GetAuditEventColor`, `GetLocalizedEventType`) — moved to sub-components or `LocalizedDisplayService.TimeAgo()`.

Also added `TimeAgo(DateTime utcTime)` to `LocalizedDisplayService` as a shared replacement for the private `FormatTimeAgo` method (used by 3 sub-components).

---

### 2.2 Assets.razor (~970 lines) — Collection Browser + Asset Grid ✅ DONE

**Problem:** Manages two distinct views (collection selection and asset browsing) with 20+ private fields, collection CRUD, asset search/filter, bulk actions, download progress, and view mode persistence.

**Resolution:** Extracted three sub-components:

1. **`Components/CollectionBrowser.razor`** (~160 lines) — Collection grid/table view with search, sort, selection checkboxes, and view mode toggle. Parameters: `AllCollections`, `Loading`, `SearchString`, `ViewMode`, `SelectionMode`, `SelectedCollectionIds`, `OnCollectionSelected`. Owns `FilteredCollections`, `Truncate`, card/row click, selection toggle.

2. **`Components/CollectionHeader.razor`** (~60 lines) — Collection info banner with description, role chip, edit/delete buttons. Parameters: `Collection`, `CurrentUserRole`, `OnEdit`, `OnDelete`. Owns `GetRoleDescription`.

3. **`Components/AssetToolbar.razor`** (~55 lines) — Search field, type filter dropdown, grid/list view toggle. Parameters: `SearchQuery`, `FilterType`, `ViewMode` with EventCallbacks. Owns debounce and filter change propagation.

`Assets.razor` is now an orchestrator (~480 lines) that manages routing, collection/asset state, CRUD dialog flows, download logic, and bulk actions. Inline collection browser/header/toolbar markup replaced with component tags.

`Assets.razor` is now an orchestrator (~480 lines) that manages routing, collection/asset state, CRUD dialog flows, download logic, and bulk actions. Inline collection browser/header/toolbar markup replaced with component tags.

---

### 2.3 ~~AssetGrid.razor (420 lines) — Dual View Mode~~ ✅ DONE

**Problem:** Implements both grid view (MudCard thumbnails) and table view (MudTable rows) in a single component with shared state but divergent markup.

**Resolution:** Extracted two view sub-components: `Components/AssetCardGrid.razor` (~100 lines, MudGrid/MudCard rendering) and `Components/AssetTable.razor` (~110 lines, MudTable rendering). Both share the same parameter surface: `Items`, `SelectionMode`, `SelectedAssetIds`, `UserRole`, plus `EventCallback`s for click, toggle, share, and delete. `AssetGrid.razor` reduced to ~230 lines as a data-loading wrapper that delegates to `<AssetCardGrid>` or `<AssetTable>` based on `ViewMode`. All 709 tests pass.

---

### 2.4 AssetDetail.razor (616 lines) — Single Asset Page

**Problem:** Mixes media preview rendering, metadata display, collection management, and action handling in one file. Duplicates preview logic from `AssetDetailPanel.razor`.

**Implementation Plan:**

1. Reuse `AssetDetailPanel.razor` for media preview (see 1.3).
2. Extract `Components/AssetCollectionsList.razor` (~80 lines):
   - Parameters: `List<CollectionResponseDto> Collections`, `Guid AssetId`, `string UserRole`
   - Renders collection chips with remove buttons
   - Owns "Add to collection" dialog flow
   - Emits `OnChanged` callback when collections are modified

3. Extract `Components/AssetMetadataDisplay.razor` (~60 lines):
   - Parameters: `Dictionary<string, object> Metadata`
   - Renders expandable metadata section with key-value table
   - Filters out "error" key consistently

4. Keep `AssetDetail.razor` as page orchestrator (~250 lines):
   - Loads asset by ID
   - Passes data to `AssetDetailPanel`, `AssetCollectionsList`, `AssetMetadataDisplay`
   - Handles action bar (download, share, edit, delete)
   - Manages cancellation tokens for navigation

**Effort:** Medium (4-5 hours)

---

### 2.5 EditAssetDialog.razor (375 lines) — Metadata Editor

**Problem:** Combines basic info form, tag management, and metadata editing in a single dialog with complex state.

**Implementation Plan:**

1. Extract `Components/TagEditor.razor` (~60 lines):
   - Parameters: `List<string> Tags`, `EventCallback<List<string>> TagsChanged`
   - Renders chip list with remove, input field with Enter-to-add
   - Reusable in any context that needs tag editing

2. Extract `Components/MetadataEditor.razor` (~80 lines):
   - Parameters: `List<MetadataField> Fields`, `EventCallback<List<MetadataField>> FieldsChanged`
   - Renders editable key-value table
   - Owns the "show empty fields" toggle and "add custom field" form
   - Reusable for any entity with metadata

3. Keep `EditAssetDialog.razor` as the dialog shell (~150 lines):
   - Basic info fields (title, description, copyright)
   - `<TagEditor>` and `<MetadataEditor>` components
   - Save/cancel logic

**Effort:** Medium (3-4 hours)

---

### 2.6 ~~ShareAccessService.cs (479 lines) — Dual Interface~~ ✅ DONE

**Files:** `Infrastructure/Services/ShareAccessService.cs`

**Resolution:** Split into two focused services:
- `PublicShareAccessService.cs` (~280 lines, 11 deps) — implements `IPublicShareAccessService` with 5 public methods + 6 private helpers for anonymous share access.
- `AuthenticatedShareAccessService.cs` (~120 lines, 7 deps) — implements `IAuthenticatedShareAccessService` with 3 public methods for share management.
- Shared helpers (token hashing, etc.) stayed with `PublicShareAccessService` as they're only used there.
- DI simplified from 4-line forwarding pattern to 2 direct registrations.
- Deleted original `ShareAccessService.cs`. All 28 share access tests pass with updated factory methods.

---

### 2.7 ServiceCollectionExtensions — Monolithic Registration

**Problem:** Single file registers 40+ services, configures rate limiting, Keycloak HTTP clients, health checks, and caching. Changes to one concern require navigating a large file.

**Implementation Plan:**

1. Extract `Extensions/KeycloakExtensions.cs`:
   - `AddKeycloakAuthentication()` — OIDC, JWT Bearer, cookie schemes
   - `AddKeycloakHttpClients()` — admin and user API typed clients with resilience

2. Extract `Extensions/RateLimitingExtensions.cs`:
   - `AddAppRateLimiting()` — all rate limiter policies

3. Extract `Extensions/CachingExtensions.cs`:
   - `AddAppCaching()` — Redis, HybridCache configuration

4. Keep `ServiceCollectionExtensions.cs` as the top-level orchestrator:
   ```csharp
   public static IServiceCollection AddInfrastructure(this IServiceCollection services, ...)
   {
       services.AddKeycloakAuthentication(config);
       services.AddKeycloakHttpClients(config);
       services.AddAppRateLimiting(env);
       services.AddAppCaching(config);
       services.AddRepositories();
       services.AddDomainServices();
       return services;
   }
   ```

**Effort:** Medium (2-3 hours)

---

## 3. Out of Scope / Over-Engineered

### 3.1 40 Seed Users in Keycloak Import

**File:** `keycloak/import/media-realm.json`

**Problem:** The Keycloak realm import creates 40 seed users for development. Only 2-3 are needed for typical dev work (an admin, a contributor, a viewer). The rest add startup time and noise.

**Implementation Plan:**

1. Reduce to 5 seed users:
   - `mediaadmin` — Admin role
   - `contributor1` — Contributor role
   - `viewer1` — Viewer role
   - `manager1` — Manager role
   - `testuser` — No role (for testing provisioning)
2. Create a separate `keycloak/import/media-realm.load-test.json` for load testing with 40+ users.
3. Add a `docker-compose.loadtest.yml` override that uses the load-test realm import.

**Effort:** Small (1 hour)

---

### 3.2 Documentation Overlap

**Files:** `SECURITY.md`, `SECURITY-AUDIT.md`, `APPLICATION-AUDIT.md`

**Problem:** Three documents cover overlapping security/audit territory. `SECURITY-AUDIT.md` is a point-in-time review that partially duplicates `SECURITY.md`. `APPLICATION-AUDIT.md` describes the audit trail feature, which is also covered in `SECURITY.md` under audit logging.

**Implementation Plan:**

1. Merge `APPLICATION-AUDIT.md` content into `SECURITY.md` under a dedicated "Audit Trail" section.
2. Rename `SECURITY-AUDIT.md` to `docs/audits/2025-security-review.md` to clarify it's a point-in-time artifact.
3. Add a one-line reference in `SECURITY.md`: "For the initial security review, see `docs/audits/2025-security-review.md`."
4. Delete `APPLICATION-AUDIT.md`.

**Effort:** Small (1 hour)

---

## 4. Missing Features

### 4.1 ~~No Pagination in Admin Tabs~~ ✅ DONE (AdminUsersTab)

**Files:** `Components/AdminUsersTab.razor`, `Components/AdminCollectionAccessTab.razor` (289 lines)

**Resolution (AdminUsersTab):** Implemented full server-side pagination. Added `PaginatedKeycloakUsersResponse` DTO with category counts, `GetKeycloakUsersPaginatedAsync` to `IUserAdminQueryService` + `UserAdminService` (server-side filtering/sorting/pagination), paginated endpoint `GET /api/v1/admin/keycloak-users/paginated`, and `AssetHubApiClient` method. Refactored `AdminUsersTab.razor` from client-side `Items`/`Filter` to `MudTable<T> ServerData` callback with `SortLabel`-based server sorting, exclusive category chip filters backed by server counts, and debounced search. Removed `LoadAsync()`, `UserFilter()`, 3 toggle methods. Category preference persisted to localStorage.

**Remaining (AdminCollectionAccessTab):** Collection access tab still uses client-side loading. Collection lists are typically smaller, but pagination can be added if needed.

**Problem:** Both tabs load ALL records into memory on initialization. `AdminUsersTab` calls `GetUsersAsync()` which returns every Keycloak user. `AdminCollectionAccessTab` calls `GetCollectionAccessTreeAsync()` which returns every collection with ACLs. At 1000+ users or collections, the page becomes unresponsive.

**Implementation Plan:**

**AdminUsersTab:**
1. Add server-side pagination to the API:
   - Modify `IUserAdminService.GetUsersAsync()` to accept `skip`, `take`, `search` parameters.
   - Update `AdminEndpoints` to expose paginated endpoint: `GET /api/v1/admin/users?skip=0&take=50&search=...`
2. Replace `_allUsers` with paginated state:
   ```csharp
   private List<UserDto> _users = new();
   private int _totalUsers;
   private int _skip;
   private const int _take = 50;
   ```
3. Use `MudTable` server-side pagination with `ServerData` callback:
   ```razor
   <MudTable ServerData="LoadUsersAsync" @ref="_table">
   ```
4. Move category filters (WithAccess, Admin, NoAccess) to API query parameters so filtering happens server-side.
5. Remove in-memory `UserFilter()` method.

**AdminCollectionAccessTab:**
1. Add `skip`/`take` to `GetCollectionAccessTreeAsync()` or implement a flat paginated list.
2. Use `MudTable` with server-side pagination for the collection list.
3. Keep the right-pane ACL editor as-is (ACL lists per collection are small).

**Effort:** Large (6-8 hours for both tabs + backend changes)

---

### 4.2 ~~Lazy Thumbnail Loading~~ ✅ DONE (partial — virtual scrolling remains open)

**File:** `Components/AssetGrid.razor`

**Resolution (lazy loading):** Already implemented — `MudCardMedia` uses `loading="lazy"` and `MudImage` uses `loading="lazy"` on all thumbnail elements in `AssetGrid.razor`. Native browser lazy loading is active.

**Remaining (virtual scrolling):** Replacing "Load More" with `MudVirtualize` + `ItemsProvider` for on-demand page fetching is still open. Medium effort (3-4 hours).

---

### 4.3 ~~No Optimistic UI Updates~~ ✅ DONE

**Files:** `Pages/Assets.razor`, `Pages/AssetDetail.razor`, `Components/AssetGrid.razor`

**Resolution:** Applied optimistic local-state updates to the most common mutations, eliminating visible loading flashes:

1. **Collection creation** (`Assets.razor CreateCollectionAsync`): Dialog returns `CollectionResponseDto` → added to `_allCollections` immediately + `StateHasChanged()` → background `LoadAllCollections()` for consistency.

2. **Collection editing** (`Assets.razor EditCollectionAsync`): Immediately refreshes selected collection details if it was the edited one → shows success feedback → background `LoadAllCollections()` instead of blocking await.

3. **Collection deletion** (`Assets.razor DeleteCollectionAsync`): After API delete, removes from `_allCollections` locally via `RemoveAll` + `StateHasChanged()` → background `LoadAllCollections()` for consistency.

4. **Asset deletion** (`AssetGrid.razor DeleteAsset`): Already optimistic — removes from `_assets` list, decrements `_total`, notifies parent via callback. No change needed.

5. **Asset editing** (`AssetDetail.razor EditAssetAsync`): Already optimistic — applies returned `AssetResponseDto` directly to `_asset`. No change needed.

6. **Remove from collection** (`AssetDetail.razor RemoveFromCollectionAsync`): Changed from full `LoadAssetAsync()` to local `_assetCollections.RemoveAll(c => c.Id == collection.Id)` + `StateHasChanged()`.

Pattern: mutation succeeds → update local state immediately → `StateHasChanged()` → fire-and-forget background refresh (`_ = LoadAllCollections()`) for eventual consistency.

---

### 4.4 ~~No Retry on Transient Failures~~ ✅ DONE

**File:** `Services/UserFeedbackService.cs`, `Services/IUserFeedbackService.cs`

**Resolution:** Added `maxRetries = 0` optional parameter to both `ExecuteWithFeedbackAsync` overloads (backward-compatible default). Implemented retry loop with exponential backoff (`1000 * (attempt + 1)` ms) and `IsTransient(Exception)` classifier (matches `HttpRequestException`, `TaskCanceledException`, `IOException`, and `ApiException` with 500/502/503/504/408 status codes). Shows localized warning snackbar on retry (`Feedback_Retrying` key in EN/SV). Updated `BunitTestBase` mock setup for the new 4-parameter signatures.

---

### 4.5 Missing Accessibility (a11y)

**Files:** All components

**Problem:** No ARIA labels on interactive elements, no keyboard navigation for custom components, no focus management on dialog open/close, color-only status indicators.

**Implementation Plan:**

**Phase 1 — Quick wins (2-3 hours):**
1. Add `aria-label` to all icon-only buttons:
   ```razor
   <MudIconButton Icon="@Icons.Material.Filled.Delete"
                  aria-label="@Loc["DeleteAsset"]" ... />
   ```
2. Add `role="status"` and `aria-live="polite"` to loading indicators.
3. Add `alt` text to all `<MudImage>` elements.

**Phase 2 — Dialog focus management (2 hours):**
1. Add `AutoFocus` to the first input in every dialog (some already have this).
2. Ensure Escape key closes all dialogs (MudBlazor handles this by default — verify).
3. Return focus to the trigger button when a dialog closes.

**Phase 3 — Color-independent indicators (1-2 hours):**
1. Add text labels or icons alongside color-coded status chips:
   ```razor
   <MudChip Color="Color.Success" Icon="@Icons.Material.Filled.Check">
       @Loc["Active"]
   </MudChip>
   ```
2. Ensure all status indicators have both color AND text/icon.

**Phase 4 — Keyboard navigation (3-4 hours):**
1. Add `tabindex` and `@onkeydown` handlers to `AssetGrid` cards.
2. Support arrow key navigation in the collection list.
3. Add skip-to-content link (MainLayout already has one — verify it works).

**Total Effort:** Large (8-11 hours across all phases)

---

### 4.6 ~~Missing IAsyncDisposable in Share.razor~~ ✅ DONE

**File:** `Pages/Share.razor`

**Resolution:** Audit confirmed the implementation is already correct. `Share.razor` has `try { await _jsModule.DisposeAsync(); } catch (JSDisconnectedException) { }`, which properly guards against circuit-disconnected disposal. All 5 page components with disposable resources (`Home`, `Share`, `Assets`, `AssetDetail`, `SharedCollectionView`) were verified to have correct `JSDisconnectedException` guards.

---

### 4.7 ~~No Orphaned Asset Warning on Collection Delete~~ ✅ DONE

**File:** `Pages/Assets.razor` — `DeleteCollectionAsync()` method

**Resolution:** Implemented full orphaned asset warning flow:

1. **New DTO**: `CollectionDeletionContextDto` with `TotalAssetCount` and `OrphanedAssetCount` in `CollectionDtos.cs`.
2. **New repo method**: `GetOrphanedAssetCountAsync(Guid collectionId)` in `CollectionRepository` — EF Core query that counts assets in the collection not present in any other collection.
3. **New service method**: `GetDeletionContextAsync(Guid id)` in `CollectionQueryService` — requires Manager role, returns asset + orphan counts.
4. **New endpoint**: `GET /api/v1/collections/{id}/deletion-context` in `CollectionEndpoints`.
5. **New API client method**: `GetCollectionDeletionContextAsync(Guid id)` in `AssetHubApiClient`.
6. **Updated delete dialog**: `DeleteCollectionAsync` in `Assets.razor` fetches context before showing confirmation. Shows total asset count and a separate orphan warning when orphaned assets exist. Gracefully degrades if context fetch fails.
7. **Localization**: Updated `ConfirmDeleteCollection` format, added `OrphanedAssetWarning` key in EN and SV resource files.

---

### 4.8 ~~CollectionTree Menu Button Not Wired Up~~ ✅ DONE

**File:** `Components/CollectionTree.razor`

**Resolution:** Removed the unwired three-dot `MudIconButton` (Option B). Collection actions are accessible from the collection header when a collection is selected. Test updated to assert `MoreVert` icon is absent.

---

### 4.9 No Batch Metadata Editing

**Problem:** Users can bulk-delete assets but cannot bulk-tag, bulk-move-to-collection, or bulk-edit metadata. For a DAM system, batch tagging is a core workflow.

**Implementation Plan:**

1. Add backend endpoint:
   ```
   PATCH /api/v1/assets/bulk-update
   Body: { assetIds: [...], addTags: [...], removeTags: [...], addToCollections: [...], removeFromCollections: [...] }
   ```

2. Create `BulkEditAssetsDialog.razor`:
   - Tabs: Tags | Collections
   - Tags tab: Add tags (shared `<TagEditor>` from 2.5), remove tags (checkboxes of existing)
   - Collections tab: Add to / remove from collections (autocomplete)
   - Preview: "Will update N assets"

3. Add "Bulk Edit" button alongside "Bulk Delete" in `AssetGrid` selection mode toolbar.

4. Backend `AssetService.BulkUpdateAsync()`:
   ```csharp
   public async Task<ServiceResult<BulkUpdateResultDto>> BulkUpdateAsync(
       BulkUpdateAssetsDto dto, string userId, CancellationToken ct)
   {
       var assets = await _repo.GetByIdsAsync(dto.AssetIds, ct);
       // Verify permissions for each asset's collections
       // Apply tag additions/removals
       // Apply collection additions/removals
       // Audit log each change
   }
   ```

**Effort:** Large (8-10 hours)

---

### 4.10 No Activity Feed Per Asset

**Problem:** The asset detail page shows metadata and collections, but not who downloaded it, when it was shared, or its change history. The audit log exists in the admin panel but isn't accessible per-asset.

**Implementation Plan:**

1. Add API endpoint:
   ```
   GET /api/v1/assets/{id}/activity?skip=0&take=20
   ```

2. Backend: Filter `AuditEvent` by `TargetType == "Asset"` and `TargetId == assetId`.

3. Create `Components/AssetActivityFeed.razor` (~80 lines):
   - Parameters: `Guid AssetId`
   - Renders a timeline of events (download, edit, share, collection changes)
   - Reuses the `ActivityTimeline` component from 2.1 (or shares its event rendering logic)

4. Add as a collapsible section on `AssetDetail.razor`, below metadata.

**Effort:** Medium (3-4 hours)

---

### 4.11 No Folder / Sub-Collection Hierarchy

**Problem:** Collections are flat. Users with many collections can't organize them into categories or nested folders. This is a common DAM expectation.

**Implementation Plan:**

1. Add `ParentCollectionId` nullable FK to `Collection` entity:
   ```csharp
   public Guid? ParentCollectionId { get; set; }
   public Collection? ParentCollection { get; set; }
   public ICollection<Collection> ChildCollections { get; set; }
   ```

2. Add EF migration with index on `ParentCollectionId`.

3. Update `CollectionQueryService` to support tree queries:
   - `GetRootCollectionsAsync()` — where `ParentCollectionId == null`
   - `GetChildCollectionsAsync(parentId)` — direct children
   - `GetBreadcrumbAsync(collectionId)` — ancestor chain for navigation

4. Update `CollectionTree.razor` to render as a `MudTreeView`:
   ```razor
   <MudTreeView Items="_rootCollections" @bind-SelectedValue="_selectedId">
       <ItemTemplate>
           <MudTreeViewItem Value="@context.Id" Text="@context.Name"
                            CanExpand="@(context.ChildCount > 0)">
           </MudTreeViewItem>
       </ItemTemplate>
   </MudTreeView>
   ```

5. Update `CreateCollectionDialog` to allow selecting a parent collection.

6. Update breadcrumbs in `Assets.razor` to show the full path.

**Effort:** Very Large (12-16 hours — touches domain, infrastructure, API, and UI)

---

### 4.12 No Asset Versioning

**Problem:** Uploading a replacement for an existing asset creates a new asset entirely. There's no way to update the file while preserving the asset's metadata, collections, shares, and audit history.

**Implementation Plan:**

1. Add `AssetVersion` entity:
   ```csharp
   public class AssetVersion
   {
       public Guid Id { get; set; }
       public Guid AssetId { get; set; }
       public int VersionNumber { get; set; }
       public string StorageKey { get; set; }
       public long FileSize { get; set; }
       public string ContentType { get; set; }
       public string Checksum { get; set; }
       public DateTime CreatedAt { get; set; }
       public string CreatedByUserId { get; set; }
   }
   ```

2. Modify `Asset` to have `CurrentVersionId` pointing to the active version.

3. Add API endpoints:
   ```
   POST /api/v1/assets/{id}/versions          — Upload new version
   GET  /api/v1/assets/{id}/versions          — List versions
   POST /api/v1/assets/{id}/versions/{vid}/restore  — Restore old version
   ```

4. Update upload flow: "Replace File" button on asset detail triggers the version upload.

5. Add `Components/AssetVersionHistory.razor` showing version list with download/restore/compare.

**Effort:** Very Large (16-20 hours — new entity, migration, storage logic, processing pipeline, UI)

---

## 5. Inconsistencies

### 5.1 ~~Error Handling Patterns Vary Across Components~~ ✅ DONE

**Resolution:** Audited all 38 Blazor components. No unhandled API calls found (category B clean). Standardized 12 user-action methods across 9 dialog/component files from raw `try/catch + HandleError` to `ExecuteWithFeedbackAsync` pattern:
- **Dialog methods (close on success):** `EditCollectionDialog.SaveAsync`, `CreateCollectionDialog.CreateAsync`, `EditAssetDialog.SaveAsync`, `AddToCollectionDialog.AddToCollectionAsync`, `CreateShareDialog.CreateShare`, `SharePasswordDialog.SavePassword`
- **Access management methods:** `ManageAccessDialog` (GrantAccessAsync, SaveRoleEditAsync, RevokeAccessAsync), `ManageUserAccessDialog` (AddCollectionAccess, SaveRoleEdit, RevokeAccess, SendPasswordResetEmail)

Methods with custom error branching (`AdminCollectionAccessTab.AddCollectionAccess` — "not found" check, `CreateUserDialog.Submit` — custom ApiException handling) and bulk operations (`BulkDeleteAsync`, `SyncDeletedUsers`) intentionally left as `try/catch` since they require non-standard error logic. Test infrastructure updated: `BunitTestBase.SetupFeedbackPassThrough()` configures `ExecuteWithFeedbackAsync` mock to invoke the provided action and delegate errors to `HandleError`.

---

### 5.2 ~~Inconsistent Async Disposal~~ ✅ DONE

**Resolution:** Audited all 10 Blazor components with disposable resources (JS modules, CancellationTokenSource, DotNetObjectReference, event subscriptions). All were found to be correct with one exception: `AssetUpload.razor`'s `DisposeAsync` was missing `JSDisconnectedException` guard on `_jsModule.DisposeAsync()` — fixed by wrapping in `try/catch (JSDisconnectedException)`. All event subscriptions (NavigationManager.LocationChanged, Theme.OnChange) properly unsubscribed. All CancellationTokenSource instances properly cancelled and disposed.

---

### 5.3 ~~Task Re-Await Pattern~~ ✅ DONE

**Files:** `CollectionAclService.cs`

**Resolution:** Replaced redundant `await` after `Task.WhenAll` with direct `.Result` access in `CollectionAclService.cs` (both `GetCollectionAclsAsync` and `SetAclAsync` methods).

---

## 6. Performance Issues

### 6.1 ~~SearchUsersForAcl Loads All Users~~ ✅ DONE

**File:** `Infrastructure/Services/CollectionAclService.cs` — `SearchUsersForAclAsync()`

**Resolution:** Added `SearchUsersAsync(string query, int maxResults, CancellationToken ct)` to `IUserLookupService` with parameterized SQL `WHERE username ILIKE @search OR email ILIKE @search`. Implemented in `UserLookupService` with direct Keycloak DB query. Updated `CollectionAclService.SearchUsersForAclAsync` to use server-side search when a query is provided (falls back to `GetAllUsersAsync` for empty queries). Updated `AdminCollectionAccessTab.razor` to call the backend search endpoint on each keystroke (via `Api.SearchUsersForAclAsync`) instead of loading all users upfront and filtering locally — removed `_allUsers` field, `LoadUsersAsync()` method, and in-memory `SearchUsersForAcl` callback. Changed autocomplete type from `KeycloakUserDto` to `UserSearchResultDto`.

---

### 6.2 ~~No Image Lazy Loading~~ ✅ DONE

**File:** `Components/AssetGrid.razor`

**Resolution:** Added `loading="lazy"` attribute to both grid-view `MudCardMedia` and table-view `MudImage` thumbnail elements.

---

### 6.3 ~~SVG Placeholder Data URLs Not Cached~~ ✅ DONE

**File:** `Services/AssetDisplayHelpers.cs`

**Resolution:** Added a `ConcurrentDictionary<string, string>` cache in `AssetDisplayHelpers`. `GetPlaceholderForType()` now caches by asset type key via `GetOrAdd()`, avoiding repeated SVG string construction.

---

## Priority Summary

### ✅ Completed
| # | Item | Category |
|---|------|----------|
| 7.1 | Hangfire media-processing queue (superseded by Wolverine) | Bug fix (critical) |
| 7.2 | Upload polling ignores failed/deleted assets | Bug fix |
| 1.1 | Extract shared collection form | Reduce duplication |
| 1.2 | Consolidate dark mode into ThemeService | Maintainability |
| 4.8 | Remove unwired collection tree menu button | UX clarity |
| 5.3 | Fix task re-await pattern | Code quality |
| 6.2 | Add `loading="lazy"` to thumbnails | Performance |
| 6.3 | Cache SVG placeholders | Performance |
| 4.6 | Share.razor disposal verified correct | Memory safety |
| 1.5 | Create LocalizedDisplayService | Reduce duplication |
| 1.3 | Extract MediaPreview component | Reduce duplication |
| 1.4 | Dialog extension methods (ShowConfirmAsync, ShowShareFlowAsync) | Reduce boilerplate |
| 4.2 | Lazy thumbnail loading (`loading="lazy"`) | Performance |
| 5.1 | Standardize error handling (ExecuteWithFeedbackAsync) | Consistency |
| 5.2 | Async disposal audit — one fix (AssetUpload JSDisconnectedException) | Memory safety |
| 6.1 | Server-side user search (SearchUsersAsync) | Performance |
| 2.6 | Split ShareAccessService into Public + Authenticated | Architecture |
| 4.1 | Admin tab server-side pagination (AdminUsersTab) | Performance |
| 4.4 | Retry on transient failures (ExecuteWithFeedbackAsync) | UX |
| 2.1 | Break up Home.razor into Dashboard sub-components | Maintainability |
| 4.3 | Optimistic UI updates for collection CRUD | UX |
| 2.2 | Break up Assets.razor into CollectionBrowser/Header/Toolbar | Maintainability |
| 4.7 | Orphaned asset warning on collection delete | Data safety |
| 2.3 | Split AssetGrid into grid/table sub-components | Maintainability |

### Immediate (< 1 day)
| # | Item | Effort | Impact |
|---|------|--------|--------|
| — | *(All immediate items completed)* | — | — |

### Short-term (1-3 days)
| # | Item | Effort | Impact |
|---|------|--------|--------|
| — | *(All short-term items completed)* | — | — |

### Medium-term (1-2 weeks)
| # | Item | Effort | Impact |
|---|------|--------|--------|
| 2.4 | Decompose AssetDetail.razor | 4-5 hrs | Maintainability |
| 2.5 | Extract TagEditor/MetadataEditor | 3-4 hrs | Reusability |
| 4.2 | Virtual scrolling for large collections | 3-4 hrs | Performance |
| 4.5 | Accessibility pass (phases 1-2) | 4-5 hrs | Compliance |

### Long-term (2+ weeks)
| # | Item | Effort | Impact |
|---|------|--------|--------|
| 4.9 | Batch metadata editing | 8-10 hrs | Core DAM feature |
| 4.10 | Per-asset activity feed | 3-4 hrs | UX |
| 4.11 | Sub-collection hierarchy | 12-16 hrs | Organization |
| 4.12 | Asset versioning | 16-20 hrs | Core DAM feature |
| 4.5 | Accessibility (phases 3-4) | 5-6 hrs | Compliance |

---

## 7. Bug Fixes

### 7.1 ~~Hangfire Media-Processing Queue Missing from API Host~~ ✅ DONE (Superseded)

> **Note:** This fix was valid at the time but has since been superseded by the migration from Hangfire to **Wolverine + RabbitMQ**. Media processing is now driven by Wolverine message consumers, and the Hangfire dependency has been fully removed.

**Files:** `Api/Extensions/ServiceCollectionExtensions.cs`, `Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`

**Original Problem:** Image and video processing jobs were enqueued to the `"media-processing"` Hangfire queue. The Worker host correctly configured the queue, but the API host's Hangfire server used the default queue configuration only. This was resolved by aligning the queue configuration, but has since been replaced entirely by Wolverine + RabbitMQ messaging.

---

### 7.2 ~~Upload Polling Ignores Failed/Deleted Assets~~ ✅ DONE

**File:** `Components/AssetUpload.razor`, `Resources/CommonResource.resx`, `Resources/CommonResource.sv.resx`

**Problem:** The `PollForProcessingCompletion` method only checked for `Processing` status — any asset that transitioned to `Failed` (e.g., ImageMagick error) or was deleted (e.g., malware detected during scan) was treated as "ready" and shown with a green success checkmark. On polling timeout (120s), all remaining processing uploads were also silently marked as completed.

**Resolution:**
- Polling now detects three terminal states: `null` (deleted), `Failed`, and `Ready`
- Deleted assets show error: "File was removed during security scanning"
- Failed assets show error: "Processing failed. Please try uploading again"
- Timeout marks remaining uploads as failed with: "Processing is taking longer than expected"
- `FinishProcessing` reports failure count via toast and only preloads thumbnails for successful assets
- Added 4 new localization keys in both English and Swedish resource files
