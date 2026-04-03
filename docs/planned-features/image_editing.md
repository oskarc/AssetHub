# Plan: In-App Image Editor

## TL;DR
Add a full-featured image editor to AssetHub, accessible via `/assets/{id}/edit`, using **Filerobot Image Editor** (MIT, actively maintained by Scaleflex) embedded in a Blazor page via JS interop. Supports crop, rotate, flip, resize, filters, brightness/contrast/saturation adjustment, text overlay, drawing, and shapes. Save flow offers "Replace original" (re-upload + re-process) and "Save as copy" (create new asset). Uses the existing presigned-upload infrastructure for blob transfer to MinIO.

## Why Filerobot Image Editor
- **Complete out-of-the-box**: crop, resize, rotate, flip, filters, fine-tune (brightness/contrast/saturation/warmth/blur/etc.), annotations (text, shapes, arrows, pen draw), watermark — matches the "full editor" requirement without building custom UI
- **Vanilla JS API**: `new FilerobotImageEditor(container, config)` — clean interop surface, no React/Vue runtime needed
- **Actively maintained**: MIT license, backed by Scaleflex, frequent releases
- **Themeable**: CSS variables for color customization to match MudBlazor dark/light theme
- **Robust UX**: Built-in undo/redo, zoom, responsive layout, keyboard shortcuts, touch support
- **Export control**: Produces Blob (JPEG/PNG/WebP) with configurable quality — fits existing presigned upload pattern

**Alternative considered**: Fabric.js with custom MudBlazor toolbar — offers more UI control but requires building undo/redo, crop UX, filter pipeline, annotation tools, zoom handling from scratch. Higher risk for a "stable and robust" requirement.

---

## Steps

### Phase 1: Foundation — Editor Page & JS Module

1. **Install Filerobot Image Editor** via npm or CDN reference. Add to `src/AssetHub.Ui/wwwroot/js/` or load from CDN. If npm: add build step to bundle it. If CDN: add `<script>` tag in `_Host.cshtml` / `App.razor`. *(Decision: CDN is simpler and avoids build tooling; npm gives version pinning — recommend npm + copy to wwwroot for self-hosted deployments)*

2. **Create JS interop module** `src/AssetHub.Ui/wwwroot/js/imageEditor.js` — wraps Filerobot lifecycle:
   - `initEditor(containerId, imageUrl, theme, dotNetHelper)` — creates editor instance, loads image from presigned URL, wires save callback
   - `destroyEditor()` — cleanup
   - `getEditedImage(format, quality)` — triggers export, returns Blob via interop
   - `uploadBlob(presignedUrl, blob)` — direct PUT to MinIO (reuse pattern from `fileUpload.js`)
   - Event callbacks to .NET: `onSave`, `onError`, `onClose`

3. **Create Blazor page** `src/AssetHub.Ui/Pages/ImageEditor.razor` at route `/assets/{Id:guid}/edit`:
   - `@attribute [Authorize]` — requires authentication
   - Load asset details via `AssetHubApiClient.GetAssetAsync(Id)` — validate it's an image and status is `Ready`
   - Permission check: require Contributor+ role (`RolePermissions.CanEdit()`)
   - Fetch presigned preview URL (original, not medium) for loading into editor
   - Render `<div id="image-editor-container">` as the editor mount point
   - Show `MudOverlay` loading state while editor initializes
   - On `OnAfterRenderAsync(firstRender)`: import JS module, call `initEditor()`
   - `IAsyncDisposable` — call `destroyEditor()` and dispose JS module
   - Navigation guard: warn if unsaved changes (reuse pattern from `AssetUpload.razor`)

4. **Add "Edit Image" button** to `AssetDetail.razor`:
   - Visible only for image assets in `Ready` status with Contributor+ role
   - `NavigationManager.NavigateTo($"/assets/{Id}/edit")` on click

5. **Theme integration**: Pass MudBlazor theme colors (primary, surface, text) to Filerobot's theme config via JS interop for visual consistency.

*Depends on: nothing. Can start immediately.*

### Phase 2: Save Flow — Replace Original

6. **New API endpoint** `POST /api/v1/assets/{id:guid}/replace-file` in `AssetEndpoints.cs`:
   - Requires Contributor+ authorization
   - Returns `InitUploadResponse` (presigned PUT URL + new object key)
   - Flow: validates asset exists + is image + is Ready → generates new original object key → creates presigned PUT URL → returns response
   - After browser uploads, **new confirm endpoint** `POST /api/v1/assets/{id:guid}/confirm-replace`:
     - Validates file exists in MinIO at the new key
     - Archives old original to `archived/{assetId}/{timestamp}.{ext}` (safety net)
     - Updates asset: `OriginalObjectKey`, `SizeBytes`, `ContentType`, `Sha256`, `UpdatedAt`
     - Clears existing rendition keys (`ThumbObjectKey`, `MediumObjectKey`)
     - Sets status to `Processing`
     - Publishes `ProcessImageCommand` to re-generate thumbnails/medium
     - Returns updated `AssetResponseDto`

7. **New service methods** in `AssetService.cs`:
   - `InitReplaceFileAsync(Guid assetId, string contentType, long fileSize, CancellationToken ct) → ServiceResult<InitUploadResponse>`
   - `ConfirmReplaceFileAsync(Guid assetId, CancellationToken ct) → ServiceResult<AssetResponseDto>`
   - Permission check: `CanAccessAssetAsync(assetId, Contributor)`
   - Validation: asset must be image type, status must be Ready (prevent concurrent edits)
   - Audit log: "asset.file_replaced"

8. **Blazor save handler** in `ImageEditor.razor`:
   - On Filerobot "save" callback → JS calls `dotNetHelper.invokeMethodAsync('OnEditorSave', imageInfo)`
   - .NET initiates replace flow: call `AssetHubApiClient.InitReplaceFileAsync(id, contentType, size)`
   - Pass presigned URL back to JS → `uploadBlob(presignedUrl, blob)`
   - On upload complete → call `AssetHubApiClient.ConfirmReplaceFileAsync(id)`
   - Show progress: "Uploading edited image..." → "Processing..." → redirect to asset detail on success

*Depends on: Phase 1 (editor page exists).*

### Phase 3: Save Flow — Save as Copy

9. **New API endpoint** `POST /api/v1/assets/{id:guid}/save-copy` in `AssetEndpoints.cs`:
   - Returns `InitUploadResponse` with a **new asset ID** + presigned URL
   - Flow: validates source asset → creates new Asset entity (copies title with " (edited)" suffix, description, tags, copyright from source) → generates original object key for new asset → presigned URL
   - Confirm via existing `POST /api/v1/assets/{newId}/confirm-upload`
   - The new asset is added to the **same collections** as the source asset

10. **New service method** `InitSaveCopyAsync(Guid sourceAssetId, string contentType, long fileSize, CancellationToken ct) → ServiceResult<InitUploadResponse>` in `AssetUploadService.cs`:
    - Permission: Contributor+ on source asset
    - Creates new asset with metadata copied from source
    - Audit log: "asset.copy_created" with source asset reference

11. **Blazor "Save as copy" handler** in `ImageEditor.razor`:
    - Save dialog offers two buttons: "Replace original" and "Save as copy"
    - "Save as copy" flow: initiate copy → upload blob → confirm → navigate to **new** asset detail page

*Depends on: Phase 1. Parallel with Phase 2.*

### Phase 4: Error Handling, Guidance & UX Polish

12. **Editor page error states** (all with localized messages):
    - Asset not found → "This asset doesn't exist or has been deleted." + back button
    - Not an image → "Only image assets can be edited." + back button
    - Asset not ready (processing/failed/uploading) → "This image is currently being processed. Please try again later."
    - No permission → "You don't have permission to edit this asset." (Forbidden)
    - Editor load failure (JS error) → "The image editor failed to load. Please refresh the page or try again later." + retry button
    - Image too large to load in browser → Client-side check for `SizeBytes` against a configurable limit (e.g., 50MB for browser editing) → "This image is too large to edit in the browser ({size}). Consider downloading, editing locally, and re-uploading."
    - Save failure (upload/network) → "Failed to save the edited image. Your changes have not been lost — please try saving again." + retry button (editor stays open)
    - Concurrent edit detection → If someone else replaces the file while editing, confirm-replace returns Conflict (409) → "This image was modified by another user while you were editing. Please reload and try again."

13. **Guidance elements**:
    - First-time tooltip/banner: "Tip: Your original image is preserved when you choose 'Save as copy'."
    - Unsaved changes guard: browser `beforeunload` event (reuse pattern from `AssetUpload.razor`) + Blazor `NavigationManager.RegisterLocationChangingHandler`
    - Processing status after save: show skeleton/spinner on asset detail until `Ready` status (reuse existing processing poll pattern)
    - Toolbar documentation: add help icon linking to a brief in-app help panel or tooltip describing each tool

14. **MudBlazor save dialog** — `ImageEditorSaveDialog.razor`:
    - Title: "Save edited image"
    - Two options presented clearly with descriptions:
      - "Replace original" — "Overwrite the current image. The previous version will be archived."
      - "Save as copy" — "Create a new asset with the edited image. The original stays untouched."
    - Cancel button to return to editor
    - Loading state on save button to prevent double-clicks

*Depends on: Phases 2 & 3.*

### Phase 5: Localization

15. **New resource file** `ImageEditorResource.resx` + `ImageEditorResource.sv.resx` with marker class:
    - `Editor_Title` — "Edit Image" / "Redigera bild"
    - `Editor_Loading` — "Loading editor..." / "Laddar redigerare..."
    - `Editor_SaveDialogTitle` — "Save edited image" / "Spara redigerad bild"
    - `Btn_ReplaceOriginal` — "Replace original" / "Ersätt original"
    - `Btn_SaveAsCopy` — "Save as copy" / "Spara som kopia"
    - `Btn_BackToAsset` — "Back to asset" / "Tillbaka till tillgång"
    - `Text_ReplaceDescription` — "Overwrite the current image. The previous version will be archived." / ...
    - `Text_CopyDescription` — "Create a new asset with the edited image. The original stays untouched." / ...
    - `Error_NotAnImage`, `Error_NotReady`, `Error_TooLarge`, `Error_SaveFailed`, `Error_ConcurrentEdit`, `Error_EditorLoadFailed`, `Error_AssetNotFound`, `Error_NoPermission`
    - `Text_UnsavedChanges` — "You have unsaved changes. Are you sure you want to leave?"
    - `Progress_Uploading` — "Uploading edited image..."
    - `Progress_Processing` — "Processing..."
    - `Tip_OriginalPreserved` — "Tip: Your original image is preserved when you choose 'Save as copy'."

*Parallel with all phases.*

### Phase 6: Testing

16. **Unit tests** for new service methods:
    - `InitReplaceFileAsync_AssetNotFound_ReturnsNotFound`
    - `InitReplaceFileAsync_NotAnImage_ReturnsBadRequest`
    - `InitReplaceFileAsync_NotReady_ReturnsBadRequest`
    - `InitReplaceFileAsync_NoPermission_ReturnsForbidden`
    - `InitReplaceFileAsync_ValidImage_ReturnsPresignedUrl`
    - `ConfirmReplaceFileAsync_FileNotInMinIO_ReturnsBadRequest`
    - `ConfirmReplaceFileAsync_Success_ArchivesOldAndUpdatesAsset`
    - `ConfirmReplaceFileAsync_ConcurrentEdit_ReturnsConflict`
    - `InitSaveCopyAsync_CopiesMetadataFromSource`
    - `InitSaveCopyAsync_AddsToSameCollections`

17. **Integration tests** (endpoint-level via `CustomWebApplicationFactory`):
    - `ReplaceFile_Unauthorized_Returns401`
    - `ReplaceFile_ViewerRole_Returns403`
    - `ReplaceFile_ContributorRole_Returns200`
    - `ReplaceFile_NonImageAsset_Returns400`
    - `SaveCopy_CreatesNewAssetWithCopiedMetadata`

18. **E2E tests** (Playwright, if in scope):
    - Navigate to image → click Edit → verify editor loads
    - Apply crop → save as copy → verify new asset created
    - Test error state: navigate to non-image → verify error message shown

*Depends on: Phases 2 & 3.*

---

## Relevant Files

**New files to create:**
- `src/AssetHub.Ui/Pages/ImageEditor.razor` — full-page editor route
- `src/AssetHub.Ui/Components/ImageEditorSaveDialog.razor` — save mode chooser dialog
- `src/AssetHub.Ui/wwwroot/js/imageEditor.js` — Filerobot JS wrapper + upload logic
- `src/AssetHub.Ui/Resources/ImageEditorResource.resx` + `.sv.resx` — localization
- `src/AssetHub.Ui/Resources/ResourceMarkers.cs` — add `ImageEditorResource` marker class

**Files to modify:**
- `src/AssetHub.Api/Endpoints/AssetEndpoints.cs` — add `replace-file`, `confirm-replace`, `save-copy` endpoints, reuse `GetRendition()` pattern for presigned URLs, add `MapGroup` routes
- `src/AssetHub.Infrastructure/Services/AssetService.cs` — add `InitReplaceFileAsync()`, `ConfirmReplaceFileAsync()`, reuse `CanAccessAssetAsync()` for permission checks
- `src/AssetHub.Infrastructure/Services/AssetUploadService.cs` — add `InitSaveCopyAsync()`, reuse `InitUploadAsync()` pattern for presigned URL generation
- `src/AssetHub.Application/Services/IAssetService.cs` — add new method signatures to interface
- `src/AssetHub.Application/Services/IAssetUploadService.cs` — add `InitSaveCopyAsync` to interface
- `src/AssetHub.Application/Dtos/PresignedUploadDtos.cs` — add `ReplaceFileRequest` DTO if needed (contentType + fileSize)
- `src/AssetHub.Application/Constants.cs` — add `ArchivedOriginals` storage prefix
- `src/AssetHub.Ui/Pages/AssetDetail.razor` — add "Edit Image" button for image assets in Ready status
- `src/AssetHub.Ui/Services/AssetHubApiClient.cs` — add `InitReplaceFileAsync()`, `ConfirmReplaceFileAsync()`, `InitSaveCopyAsync()` methods
- `src/AssetHub.Ui/AssetHub.Ui.csproj` — no changes needed (Filerobot loaded via JS)

**Reference patterns (read but don't modify):**
- `src/AssetHub.Ui/Components/AssetUpload.razor` — JS interop lifecycle, navigation guard, upload flow, `IJSObjectReference` pattern
- `src/AssetHub.Ui/wwwroot/js/fileUpload.js` — `uploadFile()` function for direct-to-MinIO PUT via XMLHttpRequest, use as template for `uploadBlob()`
- `src/AssetHub.Infrastructure/Services/AssetUploadService.cs` — `InitUploadAsync()` + `ConfirmUploadAsync()` pattern for presigned flow
- `src/AssetHub.Ui/Components/EditAssetDialog.razor` — dialog parameter/result pattern
- `src/AssetHub.Ui/Services/DialogExtensions.cs` — reusable dialog display pattern

---

## Verification

1. **Build**: `dotnet build --configuration Release` must pass with zero warnings after all changes
2. **Unit tests**: `dotnet test --filter "Category=ImageEditor"` — all new service tests pass
3. **Integration tests**: `dotnet test --filter "Category=ImageEditorEndpoints"` — all endpoint tests pass
4. **Manual test**: Run app locally, navigate to an image asset, click Edit → verify editor loads with correct image → crop → save as copy → verify new asset appears in collection → navigate to new asset → verify thumbnail and medium generated
5. **Manual test**: Replace original → verify old original archived → verify new thumbnails generated → verify asset detail shows updated image
6. **Error scenarios**: Test each error state from Phase 4 step 12 manually — asset not found, no permission, not-an-image, editor load failure
7. **Browser navigation guard**: Edit image → make changes → try to navigate away → confirm warning dialog appears
8. **Accessibility**: Verify editor page has proper heading hierarchy, "Edit Image" button is keyboard accessible, save dialog has focus management, error messages are associated with ARIA
9. **Localization**: Switch language to Swedish → verify all new strings appear correctly
10. **SonarQube**: Run analysis on all modified files — no new issues

---

## Decisions

- **Library: Filerobot Image Editor** over Fabric.js — prioritizes stability, complete UX, and reduced implementation risk. The trade-off is less visual consistency with MudBlazor (the editor has its own UI skin), but this is acceptable for a full-page editor that owns its viewport.
- **Archive on replace**: When "Replace original" is chosen, the old file is moved to `archived/{assetId}/{timestamp}.{ext}` rather than hard-deleted. Provides a safety net. Archived files can be cleaned up by a future background job (out of scope).
- **Browser size limit**: Images over ~50MB may be slow/unusable in the browser canvas. Show a warning but don't hard-block (user can decide).
- **Edited image format**: Default export as JPEG (configurable quality) matching the existing processing pipeline. PNG option for transparency preservation.
- **Scope included**: All Filerobot features (crop, resize, rotate, flip, filters, adjustments, annotations, pen draw), replace original, save as copy, error handling, localization, tests.
- **Scope excluded**: Version history UI (archived originals exist but no UI to browse/restore them), batch editing, video/document editing, collaborative editing.

---

## Further Considerations

1. **Filerobot delivery method**: Bundle via npm + copy to wwwroot (recommended for self-hosted/air-gapped deployments) vs. CDN link (simpler setup). Recommend npm for consistency with the self-hosted philosophy of AssetHub.
2. **Archived originals cleanup**: A future `CleanupArchivedOriginalsJob` background job could purge archived files older than N days. Not needed for v1 but worth noting as a follow-up.
3. **Image size guard**: For very large images (>50MB / >8000x8000px), the browser canvas may struggle. Consider offering a server-side resize-before-edit option or simply showing a clear warning. Recommend: warning + "Download and edit locally" fallback guidance for v1, server-side pre-resize as a v2 enhancement.
