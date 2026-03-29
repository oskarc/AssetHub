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

### 1.3 Media Preview Logic Duplicated

**Files:** `Pages/AssetDetail.razor`, `Components/AssetDetailPanel.razor`, `Pages/Share.razor`

**Problem:** The conditional rendering logic for media previews (image vs video vs PDF vs fallback) is implemented separately in `AssetDetail.razor` (inline) and `AssetDetailPanel.razor` (component), with slightly different URL construction. `Share.razor` has its own URL logic too.

**Impact:** Bug fixes to preview behavior need to be applied in multiple places.

**Implementation Plan:**

1. Make `AssetDetailPanel.razor` the single source of truth for media preview rendering.
2. Add parameters to `AssetDetailPanel` for action buttons (download, share, edit, delete) via `RenderFragment` parameters:
   ```razor
   [Parameter] public RenderFragment? Actions { get; set; }
   [Parameter] public RenderFragment? DownloadActions { get; set; }
   ```
3. Refactor `AssetDetail.razor` to use `<AssetDetailPanel>` instead of inline preview markup. Pass action buttons as child content.
4. Ensure `Share.razor` continues to use `AssetDetailPanel` (it already does) but verify URL construction is consistent.
5. Remove ~80-100 lines of duplicate preview markup from `AssetDetail.razor`.

**Effort:** Medium (2-3 hours)

---

### 1.4 Dialog Parameter Ceremony Repeated 10+ Times

**Files:** `Pages/Assets.razor`, `Pages/AssetDetail.razor`, `Components/AssetGrid.razor`, `Components/AdminUsersTab.razor`, and others.

**Problem:** The same boilerplate for opening MudBlazor dialogs appears everywhere:
```csharp
var parameters = new DialogParameters<SomeDialog>
{
    { x => x.Prop1, val1 },
    { x => x.Prop2, val2 }
};
var dialog = await DialogService.ShowAsync<SomeDialog>("Title", parameters);
var result = await dialog.Result;
if (!result.Canceled) { ... }
```

**Impact:** Noise that obscures the actual intent. Easy to forget the `result.Canceled` check.

**Implementation Plan:**

1. Create `Services/DialogExtensions.cs` with typed extension methods:
   ```csharp
   public static class DialogExtensions
   {
       public static async Task<bool> ShowCreateCollectionAsync(
           this IDialogService dialog) { ... }

       public static async Task<(bool confirmed, string action)> ShowDeleteAssetAsync(
           this IDialogService dialog, Guid assetId, string title, Guid? collectionId) { ... }

       public static async Task<bool> ShowEditAssetAsync(
           this IDialogService dialog, AssetResponseDto asset) { ... }

       public static async Task<ShareResponseDto?> ShowCreateShareAsync(
           this IDialogService dialog, string targetType, Guid targetId) { ... }
   }
   ```
2. Each extension method encapsulates parameter construction, dialog invocation, and result handling.
3. Callers become one-liners: `if (await DialogService.ShowCreateCollectionAsync()) await LoadCollections();`
4. Migrate existing dialog calls one at a time — no big-bang refactor needed.

**Effort:** Medium (3-4 hours for all dialogs)

---

### 1.5 ~~Localization Helper Delegation Repeated ~15 Times~~ ✅ DONE

**Files:** `Pages/Home.razor`, `Pages/Share.razor`, `Pages/AssetDetail.razor`, `Components/AdminSharesTab.razor`, `Components/AdminAuditTab.razor`, `Components/SharePasswordDialog.razor`, `Components/SharedCollectionView.razor`

**Resolution:** Created `Services/LocalizedDisplayService.cs` — a scoped service wrapping `IStringLocalizer<CommonResource>` with methods `Role()`, `AssetType()`, `ContentType()`, `ScopeType()`. Registered in DI and injected as `@inject LocalizedDisplayService Display` in all 7 affected components. Removed ~15 one-liner wrapper methods (`GetLocalizedRole`, `GetAssetTypeLabel`, `GetFriendlyAssetType`, `GetFriendlyContentType`, `GetLocalizedScopeType`, `GetLocalizedTargetType`) and replaced all call sites with `Display.*` calls.
4. Replace all local delegation methods with `Display.GetRole(role)`, etc.
5. Remove ~3-5 lines from each of the ~15 components that currently define these wrappers.

**Effort:** Small (1-2 hours)

---

## 2. Oversized Components & Services

### 2.1 Home.razor (616 lines) — Dashboard Page

**Problem:** Single component handles stats rendering, chart building, recent assets, collections, shares, activity feed, dark mode, and localization helpers. Hard to test, hard to reuse sections.

**Implementation Plan:**

1. Extract `Components/Dashboard/DashboardStatsGrid.razor` (~80 lines):
   - Parameters: `DashboardDto Dashboard`
   - Renders the 6 stat cards (total assets, storage, collections, users, shares, audit events)
   - Owns the formatting logic for numbers and storage sizes

2. Extract `Components/Dashboard/StorageChart.razor` (~60 lines):
   - Parameters: `Dictionary<string, long> StorageByType`, `bool IsDarkMode`
   - Renders the MudChart pie chart
   - Owns `BuildStorageChart()` logic and color computation

3. Extract `Components/Dashboard/RecentAssetsGrid.razor` (~50 lines):
   - Parameters: `List<AssetResponseDto> Assets`
   - Renders the recent assets card grid with thumbnails

4. Extract `Components/Dashboard/ActivityTimeline.razor` (~70 lines):
   - Parameters: `List<AuditEventDto> Events`
   - Renders the activity timeline with event icons and relative timestamps
   - Owns `FormatTimeAgo()` and event color logic

5. Extract `Components/Dashboard/ActiveSharesList.razor` (~50 lines):
   - Parameters: `List<ShareResponseDto> Shares`
   - Renders the active shares sidebar with status chips

6. Refactor `Home.razor` to be an orchestrator (~150 lines):
   - Fetches dashboard data
   - Passes data down to sub-components
   - Handles error/retry state
   - No rendering logic beyond layout

**Effort:** Medium (4-5 hours)

---

### 2.2 Assets.razor (~970 lines) — Collection Browser + Asset Grid

**Problem:** Manages two distinct views (collection selection and asset browsing) with 20+ private fields, collection CRUD, asset search/filter, bulk actions, download progress, and view mode persistence.

**Implementation Plan:**

1. Extract `Components/CollectionBrowser.razor` (~200 lines):
   - Parameters: `List<CollectionResponseDto> Collections`, `EventCallback<Guid> OnSelected`
   - Owns collection search, sort, grid/table toggle, and collection selection mode
   - Owns bulk collection selection and `OpenBulkActionsAsync()`
   - Emits `OnSelected` when a collection is clicked

2. Extract `Components/AssetToolbar.razor` (~80 lines):
   - Parameters: `string SearchQuery`, `string? FilterType`, `string ViewMode`, plus `EventCallback` variants
   - Renders search field, type filter dropdown, grid/list toggle
   - Owns debounce logic for search

3. Extract `Components/CollectionHeader.razor` (~100 lines):
   - Parameters: `CollectionResponseDto Collection`, `string UserRole`
   - Renders breadcrumbs, collection info banner, action buttons (edit, delete, share, manage access, download all)
   - Emits callbacks for each action

4. Keep `Assets.razor` as the page orchestrator (~250 lines):
   - Manages which view is active (collection browser vs asset view)
   - Fetches collections and passes to `CollectionBrowser`
   - Handles routing/query parameters
   - Coordinates between toolbar, header, and grid

**Effort:** Large (6-8 hours)

---

### 2.3 AssetGrid.razor (420 lines) — Dual View Mode

**Problem:** Implements both grid view (MudCard thumbnails) and table view (MudTable rows) in a single component with shared state but divergent markup.

**Implementation Plan:**

1. Extract `Components/AssetCardGrid.razor` (~120 lines):
   - Parameters: `List<AssetResponseDto> Assets`, `bool SelectionMode`, `HashSet<Guid> SelectedIds`, `string UserRole`, plus callbacks
   - Renders only the MudGrid with MudCards
   - Handles card click (select or navigate)

2. Extract `Components/AssetTable.razor` (~120 lines):
   - Parameters: Same as AssetCardGrid
   - Renders only the MudTable with columns
   - Handles row click (select or navigate)

3. Keep `AssetGrid.razor` as the data-loading wrapper (~180 lines):
   - Owns API calls, pagination, search state
   - Renders `<AssetCardGrid>` or `<AssetTable>` based on `ViewMode`
   - Exposes public `RefreshAsync()` and `GetSelectedAssets()` methods

**Effort:** Medium (3-4 hours)

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

### 2.6 ShareAccessService.cs (479 lines) — Dual Interface

**Files:** `Infrastructure/Services/ShareAccessService.cs`

**Problem:** Implements both `IPublicShareAccessService` (anonymous access) and `IAuthenticatedShareAccessService` (create/revoke/update) in one class with 18 dependencies. These are fundamentally different concerns — one faces anonymous users, the other faces authenticated managers.

**Implementation Plan:**

1. Create `Services/PublicShareAccessService.cs` (~200 lines):
   - Implements `IPublicShareAccessService`
   - Methods: `GetSharedContentAsync`, `GetDownloadUrlAsync`, `GetPreviewUrlAsync`, `EnqueueDownloadAllAsync`, `CreateAccessTokenAsync`
   - Dependencies: share repository, MinIO adapter, data protection, asset repository

2. Create `Services/AuthenticatedShareAccessService.cs` (~180 lines):
   - Implements `IAuthenticatedShareAccessService`
   - Methods: `CreateShareAsync`, `RevokeShareAsync`, `UpdateSharePasswordAsync`
   - Dependencies: share repository, collection auth service, audit service, data protection

3. Extract shared helpers into `Services/ShareHelpers.cs` (~50 lines):
   - Token hashing, password encryption, share validation
   - Used by both services

4. Delete `ShareAccessService.cs`.
5. Update DI registration.

**Effort:** Medium (3-4 hours)

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

### 4.1 No Pagination in Admin Tabs (Critical)

**Files:** `Components/AdminUsersTab.razor` (356 lines), `Components/AdminCollectionAccessTab.razor` (289 lines)

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

### 4.2 No Virtual Scrolling / Lazy Thumbnail Loading

**File:** `Components/AssetGrid.razor`

**Problem:** All thumbnails in the current page load eagerly. With 24 items per page this is manageable, but the "Load More" pattern means after several loads, hundreds of images are in the DOM simultaneously.

**Implementation Plan:**

1. Add `loading="lazy"` to all `<MudImage>` thumbnail elements:
   ```razor
   <MudImage Src="@GetThumbnailUrl(asset)" loading="lazy" ... />
   ```
   This is a one-line change per image tag and gives native browser lazy loading.

2. For large collections, replace "Load More" with `MudVirtualize`:
   ```razor
   <MudVirtualize Items="_assets" Context="asset" OverscanCount="4">
       <AssetCard Asset="asset" ... />
   </MudVirtualize>
   ```
   This only renders visible items plus a small buffer.

3. Add an `ItemsProvider` to `MudVirtualize` that fetches pages on demand:
   ```csharp
   private async ValueTask<ItemsProviderResult<AssetResponseDto>> LoadItems(
       ItemsProviderRequest request)
   {
       var assets = await Api.GetAssetsAsync(collectionId, request.StartIndex, request.Count);
       return new ItemsProviderResult<AssetResponseDto>(assets.Items, assets.Total);
   }
   ```

**Effort:** Small for lazy loading (30 minutes), Medium for virtual scrolling (3-4 hours)

---

### 4.3 No Optimistic UI Updates

**Files:** Multiple pages and dialogs

**Problem:** After every mutation (create collection, edit asset, delete, etc.), the entire dataset is re-fetched from the API. This causes a visible loading flash and feels sluggish.

**Implementation Plan:**

1. For collection creation:
   ```csharp
   // In Assets.razor after CreateCollectionDialog returns
   var newCollection = dialog.Result.Data as CollectionResponseDto;
   _allCollections.Add(newCollection);  // Optimistic add
   StateHasChanged();
   _ = LoadAllCollections();  // Background refresh for consistency
   ```

2. For asset deletion:
   ```csharp
   // In AssetGrid after delete
   _assets.RemoveAll(a => a.Id == deletedId);
   _total--;
   StateHasChanged();
   // No need to re-fetch unless we want to verify
   ```

3. For metadata edits:
   ```csharp
   // In AssetDetail after edit dialog
   var updated = dialog.Result.Data as AssetResponseDto;
   _asset = updated;  // Apply returned data immediately
   StateHasChanged();
   ```

4. Apply this pattern to the 5-6 most common mutations first (create/edit/delete collection, edit/delete asset, create share).

**Effort:** Medium (3-4 hours across all mutation points)

---

### 4.4 No Retry on Transient Failures

**File:** `Services/UserFeedbackService.cs`

**Problem:** `ExecuteWithFeedbackAsync()` catches errors and shows a snackbar, but offers no retry mechanism. For uploads, saves, and downloads, a transient network error requires the user to manually redo the entire action.

**Implementation Plan:**

1. Extend `UserFeedbackService` with a retry-capable variant:
   ```csharp
   public async Task<T?> ExecuteWithRetryAsync<T>(
       Func<Task<T>> action,
       string successMessage,
       string errorContext,
       int maxRetries = 1)
   {
       for (int attempt = 0; attempt <= maxRetries; attempt++)
       {
           try
           {
               var result = await action();
               ShowSuccess(successMessage);
               return result;
           }
           catch (Exception ex) when (attempt < maxRetries && IsTransient(ex))
           {
               ShowWarning($"Retrying... ({attempt + 1}/{maxRetries})");
               await Task.Delay(1000 * (attempt + 1));
           }
           catch (Exception ex)
           {
               HandleError(ex, errorContext);
               return default;
           }
       }
       return default;
   }

   private static bool IsTransient(Exception ex) =>
       ex is HttpRequestException or TaskCanceledException or IOException;
   ```

2. Add a "Retry" action to error snackbars for key operations:
   ```csharp
   Snackbar.Add(errorMessage, Severity.Error, config =>
   {
       config.Action = "Retry";
       config.Onclick = _ => retryCallback();
   });
   ```

3. Apply to upload confirmation, asset save, collection CRUD, and share creation.

**Effort:** Medium (2-3 hours)

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

### 4.7 No Orphaned Asset Warning on Collection Delete

**File:** `Pages/Assets.razor` — `DeleteCollectionAsync()` method

**Problem:** When deleting a collection, the confirmation dialog shows the asset count but doesn't warn if any of those assets exist ONLY in this collection (and will become orphaned / permanently deleted).

**Implementation Plan:**

1. Add an API endpoint or extend the existing delete:
   ```
   GET /api/v1/collections/{id}/deletion-context
   Response: { assetCount: 42, orphanedAssetCount: 3 }
   ```
2. Backend implementation in `CollectionService`:
   ```csharp
   public async Task<CollectionDeletionContextDto> GetDeletionContextAsync(Guid id, CancellationToken ct)
   {
       var assetCount = await _repo.GetAssetCountAsync(id, ct);
       var orphanedCount = await _repo.GetOrphanedAssetCountAsync(id, ct);
       return new(assetCount, orphanedCount);
   }
   ```
   Where `GetOrphanedAssetCountAsync` counts assets that belong to ONLY this collection.

3. Update the delete confirmation dialog to show:
   ```
   "This collection contains 42 assets. 3 assets exist only in this collection and will be permanently deleted."
   ```

4. Style the orphan warning with `Color.Error` to make it prominent.

**Effort:** Medium (2-3 hours)

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

### 5.1 Error Handling Patterns Vary Across Components

**Problem:** Three different error handling patterns are used interchangeably:

1. `try/catch` with `Feedback.HandleError(ex, "context")` — most common
2. `await Feedback.ExecuteWithFeedbackAsync(async () => { ... }, "success", "error")` — used in some places
3. No error handling at all — a few components skip it

**Implementation Plan:**

1. Standardize on `ExecuteWithFeedbackAsync` for all user-initiated actions (button clicks, form submits):
   ```csharp
   private async Task SaveAsync()
   {
       await Feedback.ExecuteWithFeedbackAsync(
           async () => { await Api.UpdateAssetAsync(...); },
           Loc["AssetSaved"],
           Loc["SaveError"]);
   }
   ```

2. Use `try/catch` with `HandleError` only for initialization/loading where there's no success message.

3. Audit all components for missing error handling. Add error handling to:
   - `AssetDetailPanel.razor` — currently has zero error handling
   - Any `OnInitializedAsync` that calls APIs

4. Document the convention in `CONTRIBUTING.md`:
   - User actions: `ExecuteWithFeedbackAsync`
   - Data loading: `try/catch` with `HandleError`
   - Never: bare API calls without error handling

**Effort:** Medium (3-4 hours to audit and fix all components)

---

### 5.2 Inconsistent Async Disposal

**Problem:** Some components implement `IAsyncDisposable` for JS module cleanup (Home, Assets, AssetUpload, AssetDetail), but `Share.razor` and others that create JS module references or `DotNetObjectReference` instances may not dispose them safely in all paths.

**Implementation Plan:**

1. Audit all components that use `IJSRuntime` or create `DotNetObjectReference`.
2. Ensure all implement `IAsyncDisposable` with the safe pattern:
   ```csharp
   public async ValueTask DisposeAsync()
   {
       try
       {
           if (_jsModule is not null)
               await _jsModule.DisposeAsync();
       }
       catch (JSDisconnectedException) { }
       _dotNetRef?.Dispose();
       _cts?.Cancel();
       _cts?.Dispose();
   }
   ```
3. Check that `NavigationManager.LocationChanged` event handlers are unsubscribed.
4. Verify CancellationTokenSource instances are disposed.

**Effort:** Small (1-2 hours)

---

### 5.3 ~~Task Re-Await Pattern~~ ✅ DONE

**Files:** `CollectionAclService.cs`

**Resolution:** Replaced redundant `await` after `Task.WhenAll` with direct `.Result` access in `CollectionAclService.cs` (both `GetCollectionAclsAsync` and `SetAclAsync` methods).

---

## 6. Performance Issues

### 6.1 SearchUsersForAcl Loads All Users

**File:** `Infrastructure/Services/CollectionAclService.cs` — `SearchUsersForAclAsync()`

**Problem:** Fetches all Keycloak users, then filters in memory, then returns top 50. With thousands of users, this is wasteful.

**Implementation Plan:**

1. Use Keycloak Admin REST API's built-in search:
   ```csharp
   // Instead of: GetAllUsers() then filter
   // Use: GET /admin/realms/{realm}/users?search={query}&first=0&max=50
   var users = await _keycloakClient.GetUsersAsync(search: query, first: 0, max: 50, ct);
   ```

2. Update `IKeycloakUserService` to accept search parameters:
   ```csharp
   Task<List<UserDto>> SearchUsersAsync(string query, int skip, int take, CancellationToken ct);
   ```

3. Remove the client-side filtering logic from `SearchUsersForAclAsync`.

4. Update the autocomplete in `AdminCollectionAccessTab.razor` to call the new paginated search.

**Effort:** Medium (2-3 hours)

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
| 7.1 | Hangfire media-processing queue missing from API host | Bug fix (critical) |
| 7.2 | Upload polling ignores failed/deleted assets | Bug fix |
| 1.1 | Extract shared collection form | Reduce duplication |
| 1.2 | Consolidate dark mode into ThemeService | Maintainability |
| 4.8 | Remove unwired collection tree menu button | UX clarity |
| 5.3 | Fix task re-await pattern | Code quality |
| 6.2 | Add `loading="lazy"` to thumbnails | Performance |
| 6.3 | Cache SVG placeholders | Performance |
| 4.6 | Share.razor disposal verified correct | Memory safety |
| 1.5 | Create LocalizedDisplayService | Reduce duplication |

### Immediate (< 1 day)
| # | Item | Effort | Impact |
|---|------|--------|--------|
| — | *(All immediate items completed)* | — | — |

### Short-term (1-3 days)
| # | Item | Effort | Impact |
|---|------|--------|--------|
| 4.1 | Admin tab pagination | 6-8 hrs | Critical for scale |
| 2.1 | Break up Home.razor | 4-5 hrs | Maintainability |
| 2.2 | Break up Assets.razor | 6-8 hrs | Maintainability |
| 2.6 | Split ShareAccessService | 3-4 hrs | Architecture |
| 5.1 | Standardize error handling | 3-4 hrs | Consistency |
| 4.3 | Optimistic UI updates | 3-4 hrs | UX |
| 4.4 | Retry on transient failures | 2-3 hrs | UX |

### Medium-term (1-2 weeks)
| # | Item | Effort | Impact |
|---|------|--------|--------|
| 2.3 | Split AssetGrid into grid/table | 3-4 hrs | Architecture |
| 2.4 | Decompose AssetDetail.razor | 4-5 hrs | Maintainability |
| 2.5 | Extract TagEditor/MetadataEditor | 3-4 hrs | Reusability |
| 1.4 | Dialog extension methods | 3-4 hrs | Reduce boilerplate |
| 4.2 | Virtual scrolling for large collections | 3-4 hrs | Performance |
| 4.7 | Orphaned asset warning | 2-3 hrs | Data safety |
| 6.1 | Server-side user search | 2-3 hrs | Performance |
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

### 7.1 ~~Hangfire Media-Processing Queue Missing from API Host~~ ✅ DONE

**Files:** `Api/Extensions/ServiceCollectionExtensions.cs`, `Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`

**Problem:** Image and video processing jobs are enqueued to the `"media-processing"` Hangfire queue (via `[Queue("media-processing")]` on `ImageProcessingService` and `VideoProcessingService`). The Worker host correctly configured `Queues = ["default", "media-processing"]`, but the API host's Hangfire server used the default queue configuration (only `"default"`). When running without the Worker service (typical local development), media-processing jobs sat in the queue indefinitely — assets remained in `Processing` status, thumbnails were never generated, and the UI polling loop eventually timed out silently.

**Resolution:** Added `options.Queues = ["default", "media-processing"]` to the API host's `AddHangfireServer()` call. Updated the infrastructure comment to reflect both hosts now process media jobs. In production where both API and Worker run, Hangfire's distributed locking prevents duplicate execution.

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
