# Plan: Image Editor v2 — fabric.js + Server Pipeline

> **Status**: implemented. Filerobot has been fully replaced by fabric.js + server-side ImageSharp pipeline.
> **Author context**: written after a UX review surfaced that Filerobot is a poor fit (React-in-Blazor impedance, brittle theming, layout hacks, no export-presets, weak DAM lineage). This document is the full replacement plan.

---

## TL;DR

Replace the embedded Filerobot React editor with a **MudBlazor-native editor backed by fabric.js** for canvas/layer authoring and a **server-side ImageSharp pipeline** for derivative generation. The editor supports crop, rotate/flip, resize, layered authoring (text, shapes, image overlays, redaction), and a first-class **export-presets** feature that lets users save copies in any number of preconfigured ratios/sizes/formats in one action. Edits are stored as a durable JSON document on the produced asset so they can be re-opened and re-edited later. Derivative assets are linked to their source via `Asset.SourceAssetId`, giving AssetHub real lineage tracking.

The whole thing is shippable in independent slices — the server foundation alone is valuable even before the new client lands.

---

## Goals

1. **Eliminate the React-in-Blazor impedance mismatch** caused by Filerobot. No third-party UI framework inside the Blazor app.
2. **Native theming and localization** — the editor is MudBlazor components plus a thin canvas; theming and `IStringLocalizer<ImageEditorResource>` apply automatically.
3. **Layered authoring** — text, shapes, image overlays, and redactions as reorderable, editable layers.
4. **Export presets** — admins define a library of presets (1080×1080, 1920×1080, thumbnail 400px, etc.); users pick one or many at save time and the server produces N derivative assets in one action.
5. **Reproducible edits** — the edit document is persisted on the resulting asset so users can re-open and refine the edit later, not just the rendered pixels.
6. **Lineage** — every derivative asset links back to its source via `Asset.SourceAssetId`, surfaced in the UI as a "Derivatives" section on the source asset.
7. **No regression in role-based access control** — replacing requires manager+, copy creation requires contributor+, all checks go through `ICollectionAuthorizationService`.

## Non-goals (v1)

- Freehand pen drawing
- Color filters / adjustments / curves / levels
- Masks, gradients, blend modes, layer groups
- Vector path editing
- Animated GIF / video editing
- Custom font upload
- Multi-user collaborative editing

These can be added later. v1 is intentionally focused.

---

## Architectural decisions

### D1. Hybrid compositing — client renders once, server resizes N times

Three options were considered:

| Approach | Pros | Cons |
|---|---|---|
| Client renders all preset sizes | Simple server | N uploads per save; bandwidth waste; client pegs CPU on big batches |
| Server reconstructs layers from JSON | One upload; presets are cheap | Every fabric feature needs an ImageSharp equivalent; font/text rendering parity is hard |
| **Hybrid (chosen)** | One client render + cheap server resizes; no server-side font rendering needed | Server can't change layer content, only resize/encode |

**Decision**: client uploads (a) the rendered full-resolution PNG (canonical result) and (b) the edit JSON document (for audit/re-open). Server resizes the canonical PNG with ImageSharp for each selected preset.

### D2. Edit document is durable and versioned

The edit JSON is stored on the produced asset in a new `Asset.EditDocument` jsonb column. The document is versioned (`{"v": 1, ...}`) so the editor can refuse or migrate old documents in the future. This unlocks "re-open and re-edit" without storing the original layered project file separately.

### D3. Source linkage via `Asset.SourceAssetId`

Self-referencing nullable FK on `Asset`. Powers:
- "Derivatives" section in [`AssetDetail.razor`](../../src/AssetHub.Ui/Pages/AssetDetail.razor)
- Filtering ("show only derivatives" / "show only originals")
- Audit trail clarity
- Future: cascade or detach behavior on source delete

### D4. Export presets are a first-class entity, admin-managed

A library of named presets (`ExportPreset` table) maintained by admins. Not user-defined per save — a curated, reusable list. Users at save time pick zero or more presets to apply.

### D5. Worker handles preset batches asynchronously

Generating 6 preset derivatives synchronously inside an HTTP request is fine for small images and bad for big ones. Dispatch `ApplyExportPresetsCommand` to Wolverine; respond immediately with the new asset IDs in pending state. The standard processing pipeline (`ProcessImageHandler` → `AssetProcessingCompletedHandler`) takes over from there, so derivatives flow through the same status lifecycle and notifications as any other upload.

### D6. fabric.js, not Konva, not custom canvas

fabric.js is the most mature browser-side layer engine (used under the hood by TUI Image Editor and several commercial editors), MIT-licensed, plain JS (no React/Vue), and stable. Konva is a viable alternative with a slightly more modern API; fabric wins on stability and ecosystem. Pure custom `<canvas>` was rejected — text editing, hit-testing, and selection handles are not work worth doing from scratch.

### D7. Display proxy for huge images

Large originals (e.g. 6000×4000) are downscaled to a display proxy (max 2048px on the longest edge) for the canvas. The original URL is kept; on export, the operation is replayed against the original at full resolution in an offscreen canvas. This keeps browser memory bounded and the UI responsive without sacrificing output quality.

### D8. No undo/redo in v1

fabric has no built-in history. A JSON-snapshot stack is straightforward to add but adds complexity to every mutation path. v1 ships without undo; revisit after the rest is stable. (Cancel/discard the entire edit is always available.)

---

## Scope of editor operations (v1)

### Canvas-level
- **Crop** — free or fixed aspect ratios (Free, 1:1, 4:3, 3:2, 16:9, 9:16, custom W×H)
- **Rotate** — 90°/180°/270° quick buttons + free rotation slider
- **Flip** — horizontal, vertical
- **Resize** — width/height with optional locked aspect ratio (canvas dimensions; export resolution may differ via presets)

### Layer types
- **Text** — content, font family (allowlist of 4–6 web-safe fonts), size, color, bold/italic, alignment
- **Rectangle** — stroke color, stroke width, fill, fill opacity, corner radius
- **Ellipse** — stroke, fill, opacity
- **Line** — stroke color, stroke width
- **Arrow** — line + arrowhead, stroke color, stroke width
- **Image overlay** — uploaded file or picked from existing AssetHub assets (enables watermarking from a stored watermark asset)
- **Redaction** — semantically distinct from a rectangle; rendered as opaque black; flagged in audit so privacy redactions are queryable

### Layer operations
- Reorder (up / down / to top / to bottom, drag-to-reorder in panel)
- Show/hide
- Lock (prevent selection/movement)
- Delete
- Duplicate
- Select (single — no multi-select in v1)
- Edit properties via inspector panel

### Save modes
- **Replace original** — overwrites the source asset's object in MinIO; bumps `UpdatedAt`; stores edit document; re-runs the standard processing pipeline. Requires manager+ on the source collection.
- **Save as copy** — creates a new `Asset` with `SourceAssetId = original.Id`. Title defaults to `"<original> (edited)"`, user can change. Requires contributor+ on the destination collection.
- **Export with presets** — like Save as copy but additionally generates N derivative assets, one per selected preset, all linked to the edited copy as their source. Optionally, users can select "Replace original AND export presets" to do both in one action.

---

## Domain & data model changes

### `Asset` entity
```csharp
public Guid? SourceAssetId { get; set; }     // self-FK; null = original
public Asset? SourceAsset { get; set; }
public ICollection<Asset> Derivatives { get; set; } = new List<Asset>();
public string? EditDocument { get; set; }    // jsonb; v1 schema {"v":1,"layers":[...],"canvas":{...}}
```

### `ExportPreset` entity (new)
```csharp
public Guid Id { get; set; }
public string Name { get; set; }              // "Square 1080", "Story 9:16", "Email thumb"
public int? Width { get; set; }
public int? Height { get; set; }
public ExportPresetFitMode FitMode { get; set; }   // Contain | Cover | Stretch | Width | Height
public ExportPresetFormat Format { get; set; }     // Jpeg | Png | WebP | Original
public int Quality { get; set; }              // 1-100
public DateTime CreatedAt { get; set; }
public Guid CreatedByUserId { get; set; }
```

### Enums
`ExportPresetFitMode` and `ExportPresetFormat` with `ToDbString()` / parse extensions per CLAUDE.md conventions.

### Indexes
- `idx_assets_source_asset_id` on `Assets.SourceAssetId`
- `idx_export_presets_name_unique` on `ExportPresets.Name`

### Migration
Single migration adds the columns, table, and indexes. Idempotent. `Down()` reverses cleanly. JSONB column declared with `type: "jsonb"` and a `ValueComparer` in `OnModelCreating` per CLAUDE.md.

---

## Application layer

### DTOs (`Application/Dtos/`)
- `ExportPresetDto`, `CreateExportPresetDto`, `UpdateExportPresetDto`
- `ImageEditOperationDto` — opaque JSON edit document, validated as parseable + size-bounded (e.g. 256KB)
- `ImageEditRequestDto` — edit document + save mode (`Replace` | `Copy` | `CopyWithPresets`) + optional `Guid[] PresetIds` + optional new title
- `AssetDerivativeDto` — minimal asset projection used by the "Derivatives" panel

### Service interfaces (`Application/Services/`)
- `IExportPresetService` — admin commands (create, update, delete)
- `IExportPresetQueryService` — list (HybridCache-backed), get by id
- `IImageEditingService` — `ApplyEditAsync(Guid assetId, ImageEditRequestDto request, Stream renderedPng, CancellationToken ct)`

### Repository interfaces
- `IExportPresetRepository`

### Cache keys (`Application/CacheKeys.cs`)
- `ExportPresets()` — list
- `ExportPreset(Guid id)`
- Tag: `ExportPresets` for group invalidation

### Wolverine messages (`Application/Messages/`)
- `ApplyExportPresetsCommand(Guid sourceAssetId, Guid[] presetIds, Guid actorUserId)`
- Reuses existing `AssetProcessingCompletedEvent` once derivatives finish their standard processing.

---

## Infrastructure layer

### Repositories
- `ExportPresetRepository` — primary constructor with `AssetHubDbContext`, `HybridCache`, `ILogger<>`. `.AsNoTracking()` reads, tag-based cache invalidation on writes.

### Services
- `ExportPresetService` (commands) and `ExportPresetQueryService` (queries) — both `sealed`, primary constructors, return `ServiceResult<T>`.
- `ImageEditingService` — orchestrates the save modes:
  - **Replace**: ACL check (manager+ on source collection), upload PNG to MinIO via existing upload pipeline, update asset row (`UpdatedAt`, `EditDocument`), enqueue `ProcessImageCommand` to regenerate derivatives, emit `asset.replaced_via_edit` audit event.
  - **Copy**: ACL check (contributor+ on destination collection), create new `Asset` row with `SourceAssetId`, upload PNG, copy metadata, enqueue processing, emit `asset.copy_created_via_edit` audit event.
  - **CopyWithPresets**: do the Copy flow, then dispatch `ApplyExportPresetsCommand` to the worker. Return both the copy id and the (pending) derivative ids.

### Worker handler
- `ApplyExportPresetsHandler` (in `AssetHub.Worker/Handlers/`):
  - Loads the canonical edited PNG from MinIO.
  - For each preset: resize with ImageSharp using the preset's fit mode, encode to format/quality, upload as a new asset with `SourceAssetId` pointing at the parent.
  - Per-item try/catch (one failure doesn't stop the batch — CLAUDE.md worker rule).
  - Emits `asset.exported_with_preset` audit event per derivative.
  - Returns events triggering the standard `AssetProcessingCompletedEvent` flow.

### Polly resilience
- Reuse the existing `"minio"` pipeline for all blob uploads/downloads in `ImageEditingService` and `ApplyExportPresetsHandler`.

### DI registration
In `InfrastructureServiceExtensions`:
```csharp
services.AddScoped<IExportPresetRepository, ExportPresetRepository>();
services.AddScoped<IExportPresetService, ExportPresetService>();
services.AddScoped<IExportPresetQueryService, ExportPresetQueryService>();
services.AddScoped<IImageEditingService, ImageEditingService>();
```

---

## API layer

### `ExportPresetEndpoints` — `/api/v1/admin/export-presets`
- `GET` — list all
- `GET /{id:guid}` — get one
- `POST` — create (admin)
- `PUT /{id:guid}` — update (admin)
- `DELETE /{id:guid}` — delete (admin)
- All under `RequireAdmin`, `DisableAntiforgery()`, `ValidationFilter<T>` on writes, `ToHttpResult()` on every return.

### `ImageEditEndpoints` — `/api/v1/assets/{id:guid}/edit`
- `POST` multipart:
  - `image` (the rendered PNG, required)
  - `request` (JSON `ImageEditRequestDto`, required)
- Authorization: routed through `ICollectionAuthorizationService`. Endpoint allows the request through if the user is at least contributor on the source collection; the service enforces the stricter "manager for replace" rule and returns `ServiceError.Forbidden` if violated.
- `ValidationFilter<ImageEditRequestDto>`, `DisableAntiforgery()`, `ToHttpResult()`.

### Endpoint registration
Add to `WebApplicationExtensions.MapAssetHubEndpoints()`.

---

## Audit events

New event types (string constants), localized in `AdminResource.resx` / `.sv.resx` mirroring the existing `Event_asset.image_saved_as_copy` pattern:

| Event | Severity | When |
|---|---|---|
| `asset.edited` | Info | Any edit save (umbrella event) |
| `asset.replaced_via_edit` | Warning | Original was overwritten |
| `asset.copy_created_via_edit` | Info | New copy created from edit |
| `asset.exported_with_preset` | Info | Derivative asset created from a preset (one event per derivative) |
| `asset.redaction_applied` | Warning | Edit document contains a redaction layer (privacy-relevant) |

Add filter chips for the new events in [`Pages/Admin.razor`](../../src/AssetHub.Ui/Pages/Admin.razor) audit tab.

---

## Client implementation — fabric.js host module

### File: `wwwroot/js/imageEditor.js` (full rewrite)

Plain ES module exporting an `init` function that returns a controller object Blazor calls into via `IJSObjectReference`. Public surface:

```js
init(canvasContainerId, imageUrl, options) -> controllerHandle
loadEditDocument(handle, json)              // re-open existing edit
addText(handle, defaults)
addShape(handle, kind, defaults)            // 'rect' | 'ellipse' | 'line' | 'arrow'
addImageLayer(handle, blobUrl)
addRedaction(handle)
deleteSelected(handle)
duplicateSelected(handle)
reorderLayer(handle, layerId, direction)    // 'up' | 'down' | 'top' | 'bottom'
toggleLayerVisible(handle, layerId)
toggleLayerLocked(handle, layerId)
selectLayer(handle, layerId)
updateSelectedProps(handle, props)          // partial property bag
crop(handle, rect)                          // {x, y, w, h} in image coordinates
rotate(handle, degrees)
flip(handle, axis)                          // 'h' | 'v'
resize(handle, w, h)
getLayers(handle)                           // [{id, kind, label, visible, locked}]
exportPng(handle) -> Promise<Blob>          // full-resolution render via offscreen canvas
exportEditDocument(handle) -> json
dispose(handle)
```

### Internals
- One fabric `Canvas` per editor instance, mounted in a `<div>` provided by Blazor.
- Base image is a `fabric.Image` locked against deletion/movement; cropping uses a clipping rect on the canvas.
- Each layer carries `data: { id, kind, label }` for Blazor-side identification.
- Selection changes fire a `.NET` callback (`DotNetObjectReference.invokeMethodAsync('OnSelectionChanged', layerId)`) so the Blazor inspector and layer panel stay in sync.
- A `ResizeObserver` keeps the canvas filling its container.
- Display proxy logic per **D7**: load proxy onto canvas; on export, build an offscreen full-resolution canvas, replay the operation list against the original-size base image, and produce the PNG blob.
- No bundler — use fabric's UMD build loaded on demand.

### Bundle strategy
Lazy-load fabric only on the editor page (`<script>` injected on first render, not in `_Host.cshtml`). Bundle is ~300KB minified — acceptable on a page users opt into.

---

## Client implementation — Blazor page

### File: `Pages/ImageEditor.razor` (full rewrite)

Layout:

```
+----------------------------------------------+
| MudAppBar (existing app shell)               |
+----------------------------------------------+
| Toolbar:                                     |
|  Crop | Rotate ▾ | Flip ▾ | Resize           |
|  | Add Text | Shape ▾ | Image | Redact       |
|                                  Save ▾      |
+--------+-----------------------+-------------+
| Layers | Canvas (fabric host)  | Inspector   |
| panel  |                       | (selected   |
|        |                       |  layer's    |
|        |                       |  props)     |
+--------+-----------------------+-------------+
```

- **Toolbar**: MudBlazor buttons. Save is a split-button: Replace / Save as copy / Export with presets…
- **Layers panel**: `MudList` with drag-to-reorder, visibility toggle, lock toggle, delete. Driven by `getLayers()` snapshots refreshed on each mutation and on the `OnSelectionChanged` callback.
- **Canvas host**: `<div id="@_canvasId">` of flexible size; fabric mounts inside.
- **Inspector**: contextual MudBlazor form bound to the selected layer's properties; on change, calls `updateSelectedProps`.
- **Crop sub-mode**: toolbar toggles Apply / Cancel; aspect ratio dropdown.
- **Resize**: opens `ResizeDialog` (W × H + lock aspect).
- **Localization**: every string via `IStringLocalizer<ImageEditorResource>`. New keys added to both `.resx` and `.sv.resx`.

### Save flows

**Replace original**
1. Confirm dialog (copy from existing `ImageEditor.razor` confirmation copy).
2. `exportPng()` + `exportEditDocument()`.
3. `POST /api/v1/assets/{id}/edit` multipart with `mode=Replace`.
4. On success, navigate to asset detail and snackbar success.
5. On failure, show error via `IUserFeedbackService` and stay on the editor.

**Save as copy**
1. Small dialog asking for new title (default `"<original> (edited)"`).
2. Same export + POST with `mode=Copy`.
3. On success, navigate to the new asset's detail page.

**Export with presets**
1. Opens `ExportPresetsDialog`:
   - Lists all presets from `ExportPresetQueryService` (HybridCache-backed, near-instant).
   - Each preset shows name, dimensions, format, quality.
   - Multi-select with checkboxes.
   - Toggle: "Also replace the original" (admin/manager only).
2. Same export + POST with `mode=CopyWithPresets`, `presetIds`.
3. On success, snackbar "N copies queued" and navigate to the source collection where the new assets will appear in pending state and become ready as the worker processes them.

### Optimistic UI
Per CLAUDE.md:
- Layer panel mutations (add, remove, reorder, visibility, lock) update local state immediately, then call into fabric.
- Saves are **not** optimistic — file uploads have real progress.

### Cleanup
The editor disposes its fabric controller and any DotNetObjectReference in `IAsyncDisposable.DisposeAsync`.

---

## Client implementation — admin export-presets UI

### New Admin tab: "Export Presets"
- Add to [`Pages/Admin.razor`](../../src/AssetHub.Ui/Pages/Admin.razor) tab list.
- New component `Components/Admin/AdminExportPresetsTab.razor`.
- `MudTable` columns: Name, Dimensions, Format, Quality, Created by, Actions.
- Create/Edit dialog (`ExportPresetDialog.razor`) with DataAnnotations validation.
- Optimistic delete with rollback on failure.
- New `ExportPresetsResource.resx` + `.sv.resx` and a marker class in `Resources/ResourceMarkers.cs`.

---

## Client implementation — derivatives surface

### `Pages/AssetDetail.razor`
- Add a "Derivatives" section showing children where `SourceAssetId == this.Id`.
- Compact card list with thumbnails, dimensions, format, and a link to each derivative.
- Visible whenever the asset has at least one derivative.

### Re-open edit
- On any asset that has a non-null `EditDocument`, show a "Re-edit" button alongside "Edit Image".
- Re-edit loads the asset into `ImageEditor.razor` and immediately calls `loadEditDocument` to reconstruct layers.

---

## Testing strategy

### Unit / integration (`AssetHub.Tests`)
- `ExportPresetServiceTests` — CRUD happy paths + validation + duplicate name conflict.
- `ImageEditingServiceTests` — replace happy path, copy happy path, copy-with-presets dispatches the worker command, ACL denial for non-manager attempting replace, ACL denial for non-contributor attempting copy.
- `ApplyExportPresetsHandlerTests` — `[Collection("Database")]` with `PostgresFixture`, real ImageSharp resize, asserts derivative assets created with correct dimensions and `SourceAssetId`.
- `ImageEditEndpointsTests` — `[Collection("Api")]` with `CustomWebApplicationFactory`, exercises all save modes via multipart uploads.
- `TestData.CreateExportPreset()` factory helper.

### Component (`AssetHub.Ui.Tests`)
- bUnit tests for the editor toolbar (button visibility per role), layers panel (reorder events), `ExportPresetsDialog`, `AdminExportPresetsTab`.

### E2E (`tests/E2E`)
- `image-editor-crop.spec.ts` — open editor, crop, save as copy, verify new asset appears in collection.
- `image-editor-layers.spec.ts` — add text + shape, reorder, delete, save as copy, verify rendered output.
- `image-editor-presets.spec.ts` — admin creates a preset, manager exports an asset with two presets, verifies two derivative assets are created and linked.
- `image-editor-replace.spec.ts` — manager replaces original, verifies original is overwritten and audit event present.
- `image-editor-reedit.spec.ts` — save as copy, re-open the copy, verify layers reconstruct correctly.

---

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| fabric bundle size hurts page load | Lazy-load only on the editor page; not in `_Host.cshtml` |
| Cross-origin canvas tainting blocks `exportPng` | Verify MinIO serves images with appropriate CORS headers; add to operations checklist |
| Browser memory blowup on huge images | Display proxy + offscreen full-res render on export (D7) |
| Font rendering parity if we ever move to server-side compositing | Decision D1 avoids this entirely; if revisited, allowlist fonts and ship them server-side |
| Edit document schema drift | Version field (`{"v": 1, ...}`); editor refuses or migrates older versions |
| ACL nuance (replace vs copy) silently wrong | Explicit tests for both denial paths; helper method `CanReplace` / `CanCopy` on the service |
| Worker preset batch failures invisible to user | Per-derivative audit events; failed derivatives appear in the collection in failed state per existing pipeline |
| User adds redaction expecting irreversible privacy guarantee | Server stores only the rendered (redacted) PNG as the canonical image; the edit document is also stored, so a user with DB access could in principle retrieve the unredacted version. Document this clearly. If true privacy is required, add a "permanent redaction" mode that strips the redaction layer from the stored edit document |
| Undo missing in v1 | Documented; cancel-edit always available; revisit post-launch |

---

## Out-of-scope follow-ups (post-v1 backlog)

- Undo/redo via JSON-snapshot stack
- Color adjustments (brightness, contrast, saturation, curves)
- Filters (preset LUTs)
- Freehand pen drawing
- Layer groups and multi-select
- Custom fonts
- "Permanent redaction" mode that destroys the unredacted edit document
- User-defined private presets (currently only admin-curated)
- Edit history per asset (timeline of edits with re-open at any point)
- Batch edit (apply same crop/resize to N assets at once)
- Smart crop (face/saliency detection)

---

## Shipping order

Each step is independently shippable, reversible, and leaves the system in a working state. Filerobot stays in place until step 3.

1. **Server foundation** — domain changes, migration, repos, services, endpoints, worker handler, audit events, tests. Backend is fully exercised by integration tests; no UI changes yet.
2. **Admin export-presets UI** — small, validates the data model end-to-end, gives admins something useful immediately even before the new editor lands.
3. **fabric.js host module + minimal editor page** — crop, rotate, flip, save as copy, replace. Replaces Filerobot. No layers yet. Ship.
4. **Layers** — text, shapes, image overlay, redaction, layer panel, inspector. Ship.
5. **Export presets in the editor save flow** — the headline feature. Ship.
6. **Derivatives section + re-open edit** — lineage payoff in the asset detail page. Ship.
7. **Cleanup** — remove Filerobot package, theme palette JS, layout hacks in [`ImageEditor.razor.css`](../../src/AssetHub.Ui/Pages/ImageEditor.razor.css), orphaned `SaveImageDialog.razor`. Update CLAUDE.md with a short "Image editing pipeline" subsection.

---

## Files touched (anticipated)

### New
- `src/AssetHub.Domain/Entities/ExportPreset.cs`
- `src/AssetHub.Domain/Enums/ExportPresetFitMode.cs`
- `src/AssetHub.Domain/Enums/ExportPresetFormat.cs`
- `src/AssetHub.Application/Dtos/ExportPresetDto.cs`
- `src/AssetHub.Application/Dtos/CreateExportPresetDto.cs`
- `src/AssetHub.Application/Dtos/UpdateExportPresetDto.cs`
- `src/AssetHub.Application/Dtos/ImageEditOperationDto.cs`
- `src/AssetHub.Application/Dtos/ImageEditRequestDto.cs`
- `src/AssetHub.Application/Dtos/AssetDerivativeDto.cs`
- `src/AssetHub.Application/Services/IExportPresetService.cs`
- `src/AssetHub.Application/Services/IExportPresetQueryService.cs`
- `src/AssetHub.Application/Services/IImageEditingService.cs`
- `src/AssetHub.Application/Repositories/IExportPresetRepository.cs`
- `src/AssetHub.Application/Messages/ApplyExportPresetsCommand.cs`
- `src/AssetHub.Infrastructure/Repositories/ExportPresetRepository.cs`
- `src/AssetHub.Infrastructure/Services/ExportPresetService.cs`
- `src/AssetHub.Infrastructure/Services/ExportPresetQueryService.cs`
- `src/AssetHub.Infrastructure/Services/ImageEditingService.cs`
- `src/AssetHub.Infrastructure/Migrations/<timestamp>_AddImageEditorV2.cs`
- `src/AssetHub.Worker/Handlers/ApplyExportPresetsHandler.cs`
- `src/AssetHub.Api/Endpoints/ExportPresetEndpoints.cs`
- `src/AssetHub.Api/Endpoints/ImageEditEndpoints.cs`
- `src/AssetHub.Ui/Components/Admin/AdminExportPresetsTab.razor`
- `src/AssetHub.Ui/Components/Admin/ExportPresetDialog.razor`
- `src/AssetHub.Ui/Components/ImageEditor/LayersPanel.razor`
- `src/AssetHub.Ui/Components/ImageEditor/LayerInspector.razor`
- `src/AssetHub.Ui/Components/ImageEditor/EditorToolbar.razor`
- `src/AssetHub.Ui/Components/ImageEditor/ResizeDialog.razor`
- `src/AssetHub.Ui/Components/ImageEditor/ExportPresetsDialog.razor`
- `src/AssetHub.Ui/Resources/ExportPresetsResource.resx` + `.sv.resx`
- Test files mirroring the above per CLAUDE.md test layout

### Modified
- `src/AssetHub.Domain/Entities/Asset.cs` — `SourceAssetId`, `EditDocument`, navigation
- `src/AssetHub.Infrastructure/AssetHubDbContext.cs` — entity config, JSONB converter, indexes, FK
- `src/AssetHub.Application/CacheKeys.cs` — preset keys + tags
- `src/AssetHub.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs` — DI registration
- `src/AssetHub.Api/Extensions/WebApplicationExtensions.cs` — endpoint registration
- `src/AssetHub.Ui/Pages/ImageEditor.razor` — full rewrite
- `src/AssetHub.Ui/Pages/ImageEditor.razor.css` — strip layout hacks, simplify
- `src/AssetHub.Ui/wwwroot/js/imageEditor.js` — full rewrite (fabric controller)
- `src/AssetHub.Ui/Pages/AssetDetail.razor` — Derivatives section + Re-edit button
- `src/AssetHub.Ui/Pages/Admin.razor` — Export Presets tab
- `src/AssetHub.Ui/Resources/ImageEditorResource.resx` + `.sv.resx` — new keys
- `src/AssetHub.Ui/Resources/AdminResource.resx` + `.sv.resx` — new audit event labels
- `src/AssetHub.Ui/Resources/ResourceMarkers.cs` — `ExportPresetsResource` marker
- `CLAUDE.md` — short "Image editing pipeline" subsection

### Removed (in cleanup phase)
- Filerobot npm/CDN reference
- Filerobot theme palette overrides in `imageEditor.js` (replaced)
- `SaveImageDialog.razor` (already orphaned)
- Layout hacks in `ImageEditor.razor.css`

---

## Open questions for the team

1. **Pricing/license check**: confirmed fabric.js MIT is acceptable for the deployment.
2. **Watermark workflow**: should "image overlay from existing asset" be limited to a designated watermark collection, or any asset the user can read?
3. **Preset naming conventions**: case-sensitive uniqueness or case-insensitive? (Recommend case-insensitive.)
4. **Replace audit severity**: is `Warning` strong enough, or should it be `Critical` given irreversibility?
5. **Redaction guarantee**: do we want a "permanent redaction" mode in v1 that strips the redaction layer from the stored edit document, or defer to post-v1?
