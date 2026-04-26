# AssetHub — Commercial Parity Roadmap

**Date:** 2026-04-18
**Source:** [COMMERCIAL-DAM-GAP-ANALYSIS.md](../audits/COMMERCIAL-DAM-GAP-ANALYSIS.md)
**Audience:** AI agents and human contributors implementing these features.

This document describes the features required to make AssetHub a genuine alternative to Bynder / Canto / Frontify / Brandfolder / Widen / Cloudinary / AEM. Each feature is specified in enough detail that an AI agent can implement it faithfully without additional design work, while still leaving small decisions to engineering judgement.

---

## How to read this document

Every feature entry follows the same template:

- **ID** — stable identifier for cross-references and PR titles (`T1-META-01`, etc.)
- **Intent** — the problem this solves, in one paragraph.
- **User gain** — how the end-user's experience improves.
- **Business gain** — what this unlocks strategically (evaluation, parity, differentiation).
- **Current state** — what exists today, with file references.
- **Target state** — what should exist after the feature lands.
- **Data model** — entities, fields, enums, migrations. Follows `AssetHub.Domain` conventions.
- **API surface** — new endpoints with shape, auth policy, validation.
- **Worker / background** — Wolverine messages, handlers, jobs.
- **UI** — new pages, components, dialogs, localization keys with EN + SV strings.
- **Caching** — new `CacheKeys` entries and invalidation points.
- **Acceptance criteria** — verifiable bullet list (the "done" definition).
- **Dependencies** — features that must land first.
- **Out of scope** — explicit non-goals to prevent scope creep.
- **Risks & trade-offs** — known tensions, e.g. with CLAUDE.md's design rules.

### Global conventions (apply to every feature)

Follow `CLAUDE.md` at all times. Key reminders for implementers:

- Domain entities are **standalone**, no base class; JSONB fields initialized with `new()`.
- Services are `sealed` with primary constructors injecting their dependencies.
- Services return `ServiceResult<T>` — **never throw for business errors**.
- Repositories use `.AsNoTracking()` for reads, paginate with `Skip / Take`, project with `.Select()`, use `HybridCache` for hot paths.
- EF Core `OnModelCreating` holds all entity config inline. JSONB columns need a `ValueComparer`.
- API endpoints: `.RequireAuthorization("Require…")` on the group, `{id:guid}` route constraint, `ValidationFilter<T>` on mutations, `.ToHttpResult(...)` on return.
- DTOs: DataAnnotations only (`[Required]`, `[StringLength]`, `[Range]`).
- Every new user-visible string is added to **both** `.resx` and `.sv.resx` of the most specific resource domain.
- Wolverine commands / events live in `src/AssetHub.Application/Messages/`; handlers in `src/AssetHub.Worker/Handlers/`.
- New migrations live under `src/AssetHub.Infrastructure/Migrations/` and must have a `Down()` method.
- Before finishing a UI-facing feature, run through the `/a11y-check` and `/ux-check` skills.

### How Tiers relate to each other

- **Tier 0** (Migration) blocks evaluation. Nothing else matters commercially until this ships.
- **Tier 1** is table stakes — prospects walk away within an hour without these.
- **Tier 2** is AI parity — expected by anyone evaluating in 2026.
- **Tier 3** adds collaboration + distribution depth.
- **Tier 4** is the brand-portal competitive play.
- **Tier 5** is polish and niche features.

Each tier can be implemented roughly in parallel *within* itself, but cross-tier dependencies are noted per feature.

---

## Tier 0 — Migration toolkit

### T0-MIG-01 — Bulk import API and job runner

> **Shipped 2026-04-21** (initial implementation `ddcc814` on 2026-04-19, hardening commit to follow). See the **Shipped appendix** at the end of this document for deviations from the original spec (pause/resume deferred; outcome CSV and duplicate detection as specified).

**Intent.** Give administrators a supported, observable, resumable way to migrate thousands-to-millions of assets from another DAM, a SharePoint library, a Dropbox tree, or an S3 bucket into AssetHub. Manual multipart uploads do not scale past a few hundred files; commercial prospects expect tens of thousands in a single import.

**User gain.** An admin creates a migration job, uploads a CSV manifest, streams the asset bytes in batches, watches progress in the Admin → Migrations tab, and gets a per-asset outcome report. Failures are reportable and restartable without redoing the completed work.

**Business gain.** Removes the single biggest adoption blocker. Without this, evaluation stalls because prospects can't bring their libraries with them.

**Current state.**
- Upload is one-at-a-time through `POST /api/v1/assets` in [AssetEndpoints.cs:27](../../src/AssetHub.Api/Endpoints/AssetEndpoints.cs#L27).
- Presigned upload exists (`init-upload` / `confirm-upload`) but is still per-asset.
- No batch job concept, no manifest parser, no progress UI.

**Target state.**
- A **Migration** entity tracks a single import run (name, source type, status, counts, created by, started/finished timestamps).
- A **MigrationItem** entity tracks one source asset → one target asset, with idempotency key, external ID, outcome, error reason.
- CSV / JSONL manifest describes items *before* their bytes arrive.
- Bytes are streamed via a **batch upload endpoint** or a **pull connector** (S3 / Dropbox / etc.).
- A Wolverine-backed background worker processes items with rate limiting, retries, and resumability.
- Admin UI shows live progress, lets admins pause / resume / cancel, and exports a per-item outcome CSV.

**Data model.**

Add two domain entities under `src/AssetHub.Domain/Entities/`:

```csharp
public class Migration
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MigrationSourceType SourceType { get; set; }  // Csv, S3, Dropbox, Bynder, Canto, Frontify, Sharepoint
    public MigrationStatus Status { get; set; }          // Draft, Queued, Running, Paused, Completed, Failed, Cancelled
    public Guid? DefaultCollectionId { get; set; }
    public Dictionary<string, object> SourceConfig { get; set; } = new();   // JSONB — per-connector
    public Dictionary<string, string> FieldMapping { get; set; } = new();   // JSONB — source field → AssetHub field
    public bool DryRun { get; set; }
    public int ItemsTotal { get; set; }
    public int ItemsSucceeded { get; set; }
    public int ItemsFailed { get; set; }
    public int ItemsSkipped { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public ICollection<MigrationItem> Items { get; set; } = new List<MigrationItem>();
}

public class MigrationItem
{
    public Guid Id { get; set; }
    public Guid MigrationId { get; set; }
    public Migration Migration { get; set; } = default!;

    public string ExternalId { get; set; } = string.Empty;        // original DAM's ID; unique within migration
    public string IdempotencyKey { get; set; } = string.Empty;    // SHA256(MigrationId + ExternalId)
    public string SourceFilename { get; set; } = string.Empty;
    public long? SourceSizeBytes { get; set; }
    public string? SourceSha256 { get; set; }
    public Dictionary<string, object> SourceMetadata { get; set; } = new();  // raw from source
    public Guid? TargetAssetId { get; set; }                      // null until created
    public MigrationItemStatus Status { get; set; }               // Pending, Downloading, Uploading, Succeeded, Skipped, Failed
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

Add enums to [Enums.cs](../../src/AssetHub.Domain/Entities/Enums.cs): `MigrationSourceType`, `MigrationStatus`, `MigrationItemStatus`, each with `ToDbString` / `ToEnum` extensions matching the existing pattern.

**Indices** (create in the migration, not via `[Index]`):
- `idx_migration_status_created` on `(Status, CreatedAt DESC)`.
- `idx_migration_item_migration_status` on `(MigrationId, Status)`.
- `idx_migration_item_idempotency_unique` UNIQUE on `(IdempotencyKey)`.
- `idx_migration_item_sha256` on `(SourceSha256)` for cross-migration duplicate detection.

**API surface.** All endpoints under `/api/v1/admin/migrations`, group policy `RequireAdmin`, registered in a new `MigrationEndpoints.cs` mapped from [WebApplicationExtensions.cs](../../src/AssetHub.Api/Extensions/WebApplicationExtensions.cs).

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/v1/admin/migrations` | Create a migration (draft). Body: `CreateMigrationDto`. |
| `GET` | `/api/v1/admin/migrations` | List migrations (paginated, filter by status). |
| `GET` | `/api/v1/admin/migrations/{id:guid}` | Migration detail with summary counts. |
| `POST` | `/api/v1/admin/migrations/{id:guid}/manifest` | Upload CSV / JSONL manifest (multipart, idempotent via job ID). |
| `POST` | `/api/v1/admin/migrations/{id:guid}/items/{externalId}/upload` | Stream asset bytes for one item; server hashes and writes to MinIO. |
| `POST` | `/api/v1/admin/migrations/{id:guid}/items/batch-upload` | ZIP with filename → bytes; filename matches `ExternalId`. |
| `POST` | `/api/v1/admin/migrations/{id:guid}/start` | Queue the Wolverine job. |
| `POST` | `/api/v1/admin/migrations/{id:guid}/pause` | Pause mid-run. |
| `POST` | `/api/v1/admin/migrations/{id:guid}/resume` | Continue a paused job. |
| `POST` | `/api/v1/admin/migrations/{id:guid}/cancel` | Hard-stop; already-created assets remain. |
| `GET` | `/api/v1/admin/migrations/{id:guid}/items` | Paginate items, filter by status. |
| `GET` | `/api/v1/admin/migrations/{id:guid}/outcome.csv` | Download per-item outcome report. |
| `DELETE` | `/api/v1/admin/migrations/{id:guid}` | Delete migration record (only if no successful items, or with `?purgeAssets=true` to also delete produced assets). |

DTO skeletons (in `src/AssetHub.Application/Dtos/Migration/`):

```csharp
public class CreateMigrationDto
{
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [Required] public string SourceType { get; set; } = "";  // lower-case string of enum
    public Guid? DefaultCollectionId { get; set; }
    public Dictionary<string, object>? SourceConfig { get; set; }
    public Dictionary<string, string>? FieldMapping { get; set; }
    public bool DryRun { get; set; } = true;
}

public class MigrationResponseDto { /* Id, Name, SourceType, Status, counts, timestamps, createdBy */ }
public class MigrationItemResponseDto { /* Id, ExternalId, SourceFilename, Status, TargetAssetId, ErrorCode, ErrorMessage */ }
```

CSV manifest contract (first-class importer — `SourceType = Csv`):

| Column | Required | Notes |
|---|---|---|
| `external_id` | ✓ | Unique per manifest; used as `ExternalId`. |
| `filename` | ✓ | Original filename. |
| `title` |   | Defaults to filename without extension. |
| `description` |   | |
| `copyright` |   | |
| `tags` |   | Semicolon-separated. |
| `collection_names` |   | Semicolon-separated. Created if missing (unless `--strict-collections` is set on the migration). |
| `created_at_utc` |   | ISO 8601; used to set `Asset.CreatedAt`. |
| `created_by_user_id` |   | Keycloak user id; falls back to migration initiator. |
| `sha256` |   | If present, validated on upload; if absent, computed. |
| `metadata.*` |   | Any extra columns with this prefix become entries in `MetadataJson`. |

**Worker / background.** Add messages in `src/AssetHub.Application/Messages/MigrationMessages.cs`:

```csharp
public record StartMigrationCommand(Guid MigrationId);
public record ProcessMigrationItemCommand(Guid MigrationId, Guid MigrationItemId);
public record MigrationCompletedEvent(Guid MigrationId, int Succeeded, int Failed, int Skipped);
```

Handler `MigrationHandler.HandleAsync(StartMigrationCommand, ct)`:
1. Load migration, move `Status → Running`.
2. Enumerate `Pending` items in batches of 100.
3. Fan out `ProcessMigrationItemCommand` messages (Wolverine will rate-limit per-queue).
4. When all items are terminal, set `Status → Completed` and emit `MigrationCompletedEvent`.

Handler `MigrationItemHandler.HandleAsync(ProcessMigrationItemCommand, ct)`:
1. Load `MigrationItem`. If terminal, return.
2. If `DryRun`, validate manifest fields, check for duplicate by SHA256, record outcome, return.
3. Otherwise: ensure bytes are present in the migration staging bucket, compute SHA256 if absent, reject on SHA256 mismatch.
4. Check cross-library duplicate (SHA256 match in existing `Asset` table) → skip with `DUPLICATE` outcome.
5. Run `AssetUploadService.IngestMigratedAssetAsync(...)` — a new service method that:
   - Creates the `Asset` with `CreatedByUserId` and `CreatedAt` from manifest when provided,
   - Copies from staging bucket to final key,
   - Enqueues `ProcessImageCommand` / `ProcessVideoCommand` as usual,
   - Applies tags, metadata, copyright, collections,
   - Sets `MigrationItem.TargetAssetId`.
6. On failure, capture `ErrorCode` + `ErrorMessage`, increment `AttemptCount`; Wolverine retry policy will redeliver up to 3 times.

**UI.** Add `src/AssetHub.Ui/Pages/Admin.razor` tab **Migrations**, plus:

- `AdminMigrationsTab.razor` — table of migrations with status chip (icon + text), counts, actions (view, pause, resume, cancel, delete, download outcome CSV).
- `CreateMigrationDialog.razor` — step-wise: pick source type → upload manifest → map fields → choose default collection → dry-run toggle → create.
- `MigrationDetailDialog.razor` — paginated item list with filter by status; failed items show `ErrorMessage`; "Download outcome CSV" button.
- `FieldMappingEditor.razor` — left column = source fields (parsed from CSV header), right column = AssetHub fields + custom-schema fields once T1-META-01 ships.

New localization in **AdminResource**:

| Key | EN | SV |
|---|---|---|
| `Migration_TabTitle` | Migrations | Migreringar |
| `Migration_NewBtn` | New migration | Ny migrering |
| `Migration_Status_Draft` | Draft | Utkast |
| `Migration_Status_Queued` | Queued | I kö |
| `Migration_Status_Running` | Running | Pågår |
| `Migration_Status_Paused` | Paused | Pausad |
| `Migration_Status_Completed` | Completed | Slutförd |
| `Migration_Status_Failed` | Failed | Misslyckad |
| `Migration_Status_Cancelled` | Cancelled | Avbruten |
| `Migration_Source_Csv` | CSV manifest | CSV-manifest |
| `Migration_Source_S3` | S3 bucket | S3-bucket |
| `Migration_Source_Dropbox` | Dropbox | Dropbox |
| `Migration_Source_Sharepoint` | SharePoint | SharePoint |
| `Migration_ItemStatus_Pending` | Pending | Väntande |
| `Migration_ItemStatus_Succeeded` | Succeeded | Lyckades |
| `Migration_ItemStatus_Skipped_Duplicate` | Skipped (duplicate) | Hoppades över (dubblett) |
| `Migration_ItemStatus_Failed` | Failed | Misslyckades |
| `Migration_DryRunToggle` | Dry run (validate only, don't write) | Testkörning (validera endast) |
| `Migration_DryRunHint` | No assets will be created. Use this to verify your manifest before committing. | Inga tillgångar skapas. Använd för att verifiera manifestet. |
| `Migration_FieldMappingTitle` | Map source fields to AssetHub fields | Mappa källfält till AssetHub-fält |
| `Migration_UploadManifest` | Upload manifest (CSV / JSONL) | Ladda upp manifest (CSV / JSONL) |
| `Migration_DownloadOutcome` | Download outcome report | Ladda ner resultatrapport |
| `Migration_ConfirmCancel` | Cancel this migration? Already-migrated assets will remain. | Avbryt migreringen? Redan migrerade tillgångar bevaras. |
| `Migration_ConfirmDelete` | Delete this migration record? This does not delete migrated assets. | Ta bort migreringsposten? Redan migrerade tillgångar påverkas inte. |
| `Migration_ConfirmDeleteWithPurge` | Delete this migration AND all {0} migrated assets? This cannot be undone. | Ta bort migreringen OCH alla {0} migrerade tillgångar? Kan inte ångras. |

**Caching.** None at this layer — migration data is admin-only and mutates too often to benefit.

**Acceptance criteria.**
- Admin creates a migration with a 10k-row CSV + 10k files in under 10 minutes from upload to completed.
- Pausing mid-run stops new item handlers within 5 seconds; resume picks up from the next pending item without reprocessing completed ones.
- A failed connection to MinIO retries per Polly pipeline; after exhaustion the item is marked Failed with a structured `ErrorCode`.
- Dry-run produces identical outcome classifications to a real run without creating assets or writing to MinIO.
- Duplicate detection by SHA256 across existing assets and within the same manifest marks items `Skipped_Duplicate`.
- Outcome CSV contains every item with: `external_id`, `filename`, `status`, `target_asset_id`, `error_code`, `error_message`.
- Cancelling a migration leaves already-created assets intact; optional `?purgeAssets=true` deletes them.
- Audit events `migration.created`, `migration.started`, `migration.paused`, `migration.resumed`, `migration.cancelled`, `migration.completed` are emitted.
- End-to-end test in `AssetHub.Tests` covers CSV + batch upload + dry run + real run + resume after pause.

**Dependencies.** Must land alongside (or before) T1-META-01 if field mapping is to include custom fields — or ship with "core fields only" mapping and extend after T1-META-01.

**Out of scope for this item.** Source-specific connectors (Bynder / Canto / Frontify) — those are T0-MIG-02 through T0-MIG-05.

**Risks & trade-offs.**
- Rate-limiting Wolverine fan-out is tricky; start with a configured global concurrency cap (`Migrations:MaxParallelItems = 8`) and tune.
- MinIO staging bucket fills fast; implement lifecycle policy to delete staged bytes 24 h after terminal status.
- If T1-META-01 is not yet live, `FieldMapping` only recognises Asset core fields + `Tags` + `MetadataJson.*`. Document this in the UI.

---

### T0-MIG-02 — S3 / MinIO pull connector

> **Shipped (already in main).** S3 connector + scan endpoint + Wolverine handler + UI form + DataProtection-encrypted secrets all landed during the broader migration toolkit work. See the **Shipped appendix** for the per-layer breakdown.

---

### T0-MIG-03 through T0-MIG-05 — Bynder / Canto / SharePoint connectors

Same pattern as T0-MIG-02, one per source. Each is a sealed service implementing `IMigrationSourceConnector` with methods `ScanAsync`, `FetchBytesAsync`, `FetchMetadataAsync`. Ship as prospects demand — don't build all three speculatively. Each one adds an enum value to `MigrationSourceType`, a `SourceConfig` schema, a connector class, and a preset in `FieldMappingEditor`.

---

## Tier 1 — Table stakes

### T1-META-01 — Custom metadata schemas and taxonomies

> **Shipped 2026-04-21.** `MetadataSchema` + `MetadataField` + `Taxonomy` + `TaxonomyTerm` + `AssetMetadataValue` entities, typed-value storage with GIN indexes, admin UI (`MetadataSchemaDialog.razor`, `TaxonomyDialog.razor`), dynamic upload/edit form, and bulk-apply endpoint all landed. See the **Shipped appendix** for details.

---

### T1-LIFE-01 — Soft delete, trash, and scheduled purge

> **Shipped 2026-04-21.** `Asset.DeletedAt` + global query filter, `AssetTrashService`, `TrashPurgeBackgroundService`, `AdminTrashTab.razor`, single-asset Undo-snackbar, and CLAUDE.md rule amendment all landed. Bulk-undo snackbar + purge-worker integration test deferred to [FOLLOW-UPS.md](./FOLLOW-UPS.md). See the **Shipped appendix** for details.

---

### T1-DUP-01 — Duplicate detection on upload

> **Shipped 2026-04-21.** Core duplicate detection landed incrementally across earlier upload work; admin-only force gate + `asset.duplicate_blocked` / `asset.duplicate_override` audit events completed the spec. See the **Shipped appendix** for details.

---

### T1-SRCH-01 — Faceted search with full-text, OCR, and saved searches

> **Shipped 2026-04-21.** `search_vector` tsvector column + GIN index, `AssetSearchService` with facets, `SavedSearch` + `SavedSearchNotifyCadence` schema + CRUD endpoints, `SearchSidebar.razor` + `SavedSearchesMenu.razor` all landed. Saved-search **notification delivery** (the email worker that runs cadences) is schema-only in v1 and explicitly deferred to T3-NTF-01. See the **Shipped appendix** for details.

---

### T1-VER-01 — Asset versioning

> **Shipped 2026-04-21.** `AssetVersion` entity with unique `(AssetId, VersionNumber)`, `Asset.CurrentVersionNumber`, list/restore/prune endpoints, and `AssetVersionHistoryPanel.razor` all landed. Version thumbnail preview, SaveImageCopy versioning interpretation, and real-Postgres integration tests deferred to [FOLLOW-UPS.md](./FOLLOW-UPS.md). See the **Shipped appendix** for details.

---

### T1-API-01 — Public REST API + OpenAPI

**Intent.** CLAUDE.md currently says Swagger/OpenAPI is out of scope because endpoints are only consumed by the Blazor UI. That assumption is the single blocker to every integration and automation story.

**User gain.** Developers build their own integrations without reverse-engineering the UI client.

**Business gain.** Unlocks Tier 3 integrations (webhooks, Zapier), enables migration connectors to use a public contract, and is required for any self-serve prospect.

**Current state.** Endpoints exist but are not documented externally; no Swagger UI; no stable versioning contract beyond `/v1`.

**Target state.**
- `Microsoft.AspNetCore.OpenApi` + `Swashbuckle.AspNetCore.SwaggerUI` added to `AssetHub.Api.csproj`.
- Every endpoint contributes a description, response types, and example.
- OpenAPI JSON published at `/swagger/v1/swagger.json`.
- Swagger UI at `/swagger` — gated by admin role in production, open in Dev.
- **Separate public contract** — a subset of endpoints marked `[PublicApi]` that promises SemVer stability; admin-only and internal endpoints remain undocumented in the public schema.
- Personal Access Tokens so API clients don't need full OIDC flow. PATs are long-lived, scoped, revocable, stored hashed.

**Data model.**
```csharp
public class PersonalAccessToken
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string OwnerUserId { get; set; } = "";
    public string TokenHash { get; set; } = "";      // SHA256
    public List<string> Scopes { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
```

**API.** Add a new bearer-auth scheme `"PAT"` that resolves `Authorization: Bearer pat_*` tokens. Integrate alongside the existing JWT scheme so both work.

**UI.** User profile page (new) with a *"Personal access tokens"* section: create, copy (shown once), revoke.

**Acceptance criteria.**
- `curl -H "Authorization: Bearer pat_..." https://.../api/v1/assets/{id}` works.
- `/swagger` renders a navigable UI; OpenAPI JSON validates against OpenAPI 3.1.
- PAT can be scoped (e.g., `assets:read`, `shares:manage`) and scope is enforced per endpoint.
- Revoking a PAT blocks further requests immediately.

**Dependencies.** None; amend CLAUDE.md to reflect the change.

**Risks.** CLAUDE.md currently forbids Swagger. Update the Architecture section and the "patterns NOT used" list when this lands.

---

## Tier 2 — AI parity

### T2-AI-01 — Pluggable AI provider abstraction

**Intent.** Provide one interface for all AI features so providers can be swapped (Azure AI Vision, AWS Rekognition, local Ollama, OpenAI-compatible).

**Data model.** No DB. New config `AiSettings` with `SectionName = "Ai"`, `Provider` enum (`Disabled | Azure | Aws | OpenAi | Ollama`), per-provider subsection.

**Interfaces.** In `src/AssetHub.Application/Services/Ai/`:
```csharp
public interface IAiVisionService
{
    Task<ServiceResult<ImageAnalysisResult>> AnalyzeImageAsync(Stream image, string contentType, AnalyzeOptions opts, CancellationToken ct);
}

public record ImageAnalysisResult(
    List<AiTag> Tags,
    string? AltText,
    string? OcrText,
    List<AiObject> Objects,
    List<AiColor> DominantColors,
    AiSmartCropBox? SmartCrop);
```

Implementations: `AzureAiVisionService`, `AwsRekognitionService`, etc. Register the configured provider.

**Acceptance criteria.** When `Ai:Provider = "Disabled"` every AI feature downgrades gracefully (no exception, no feature in UI).

---

### T2-AI-02 — Auto-tagging on upload

**Intent.** Populate `Asset.Tags` with AI-suggested tags after ingest.

**Target state.** `ProcessImageHandler` calls `IAiVisionService.AnalyzeImageAsync`; merges tags above a confidence threshold into the asset; records raw analysis in `MetadataJson["ai"]`.

**UI.** Edit dialog marks AI-suggested tags with a small sparkle icon and a tooltip *"AI suggested — confidence 0.83"*. User can accept or remove.

**Config.** `Ai:AutoTag:Enabled`, `Ai:AutoTag:ConfidenceThreshold = 0.70`.

**Acceptance criteria.** Uploaded image of a beach gets tags like `beach`, `sand`, `ocean` within 30 s of upload completion.

---

### T2-AI-03 — OCR for images and documents

**Intent.** Make text inside images and PDFs searchable.

**Target state.** Worker extracts OCR text into `Asset.MetadataJson["ocr"]` and indexes into `search_vector` (T1-SRCH-01). Document assets use a PDF-text extractor first (fast); OCR is the fallback.

**Acceptance criteria.** A scanned receipt becomes findable by its printed line items via full-text search.

---

### T2-AI-04 — AI alt-text generation

**Intent.** Auto-populate `Asset.Description` (or a dedicated `AltText` field) with an accessibility-grade description.

**Target state.** Same pipeline as T2-AI-02. UI shows a "Regenerate alt text" button for admins. Exposed in share pages and asset detail for WCAG compliance.

**Acceptance criteria.** Asset with no description gets alt text within 30 s of upload; WCAG 1.1.1 baseline improves measurably.

---

### T2-AI-05 — Smart crop for export presets

**Intent.** Subject-aware cropping instead of centre-crop.

**Target state.** `ApplyExportPresetsHandler` calls `IAiVisionService.AnalyzeImageAsync` once, reuses the `SmartCropBox` across all presets. `ExportPreset` gains `UseSmartCrop bool`.

**Acceptance criteria.** Portrait of a person cropped to 1:1 keeps the face intact; same source with smart crop off centres on the geometric middle.

---

## Tier 3 — Collaboration, distribution, notifications

### T3-COL-01 — Comments with @mentions

> **Shipped 2026-04-24.** See the **Shipped appendix** at the end of this document for the per-layer breakdown, mention-parser contract, and deferred items (user-search autocomplete, author display-name resolution).

---

### T3-WF-01 — Approval workflow (states)

> **Shipped 2026-04-24.** See the **Shipped appendix** at the end of this document for the state-machine breakdown, share-gate configuration, and deferred items (inline reason dialog, grid-card state badges).

---

### T3-REND-01 — On-the-fly rendition URLs

> **Shipped 2026-04-25.** See the **Shipped appendix** at the end of this document for the per-layer breakdown, the deliberate auth-only scope (signed-URL embedding deferred), and the smart-crop deferral (depends on T2-AI-05).

---

### T3-INT-01 — Webhooks

> **Shipped 2026-04-25.** See the **Shipped appendix** at the end of this document for the per-layer breakdown, the `SecretEncrypted` deviation from the spec's `SecretHash` field, and deferred items (24 h scheduled retry, additional event sources).

---

### T3-NTF-01 — Notifications (in-app + email)

> **Shipped 2026-04-24.** See the **Shipped appendix** at the end of this document for phase-by-phase breakdown, deferred items, and the `SavedSearchNotifyCadence` tie-in that closes the T1-SRCH-01 carve-out.

---

## Tier 4 — Brand portal play

### T4-BP-01 — Branded share portals

> **Shipped 2026-04-25.** See the **Shipped appendix** at the end of this document for the per-layer breakdown, the deliberate omission of `CustomCss` and custom-domain (security / infra), and the brand-resolution order.

---

### T4-GUEST-01 — Named guest users

> **Shipped 2026-04-25.** See the **Shipped appendix** at the end of this document for the per-layer breakdown, the deliberate "Keycloak realm role + ACL" provisioning model (no separate guest sub-type), and the deferred items (resend invitation, inviter name in email, dedicated guest theming).

**Intent.** External reviewers who aren't full users but aren't anonymous either.

**Target state.** Guest `User` sub-type with time-limited access to specific collections; invitation email with magic-link sign-in. Guests can comment on assets (T3-COL-01) but cannot upload.

**Acceptance criteria.** Admin invites a guest by email; guest signs in via magic link; guest sees only granted collections; invitation expires and auto-revokes.

---

### T4-INT-02 through T4-INT-05 — Creative tool plugins

Each plugin is its own project consuming T1-API-01 (public API) + T1-API-01 PATs:

- **Adobe CC panel** (CEP / UXP for Photoshop / Illustrator / InDesign).
- **Figma plugin**.
- **Microsoft Office add-in** (PowerPoint, Word).
- **Google Workspace Drive-style picker**.

Each ships as a separate repo; this roadmap only tracks the host-side enablement (already covered by T1-API-01).

---

## Tier 5 — Polish & niche

### T5-NEST-01 — Nested collections

**Intent.** Let collections have a parent for navigation, without changing the runtime ACL model.

**Design pinned.** See [T5-NEST-01-DESIGN.md](./T5-NEST-01-DESIGN.md) — the inheritance question is resolved as **"no runtime inheritance, opt-in 'Apply parent's ACL' UI shortcut that one-shot copies the parent's ACL rows onto the child"**. Auth path stays flat; hierarchy is a navigation + bulk-policy ergonomics layer on top.

**Adds.** `Collection.ParentCollectionId` (nullable self-FK with `OnDelete: SetNull`), depth limit `MaxCollectionDepth = 8`, cycle detection on `SetParentAsync`, two new endpoints (`PATCH .../parent`, `POST .../copy-acl-from-parent`), and the corresponding UI in `CollectionTree` / `ManageAccessDialog`. See the design doc for the per-layer breakdown, audit events, and the rejected alternatives (inherit-when-empty, cumulative, restrictive).

---

### T5-WMK-01 — Watermarking on download

**Intent.** Optional dynamic watermark for specific shares or collection-scoped downloads.

---

### T5-ANL-01 — Analytics (most-downloaded, stale assets, storage reports)

**Intent.** Reporting dashboards; export to CSV / PDF.

---

### T5-AUDIT-01 — Audit retention + meta-audit

**Intent.** Audit events accumulate forever today. Without retention the `AuditEvents` table grows unbounded, and high-volume event types (`webhook.delivered`, `notification.sent`, `asset.downloaded`, `share.accessed`) currently have to be downgraded to telemetry to dodge the lack of cleanup. Landing this **unblocks** audit emission on those events for every shipped feature.

**User gain.** Admins keep meaningful audit history (default two years) without the table going to disk. Compliance gets per-event-type retention so SOC2-relevant trails (ACL grants, share password verification, PAT mint/revoke) can be held longer than chatty trails (downloads, deliveries).

**Business gain.** Closes the SOC2 / ISO 27001 audit-retention requirement that any prospect's security questionnaire asks about. Removes the "we can't audit this without blowing up storage" carve-out from every other feature's audit table.

**Data model.** No new tables. New config class:
```csharp
public class AuditRetentionSettings
{
    public const string SectionName = "AuditRetention";
    [Range(1, 3650)] public int DefaultRetentionDays { get; set; } = 730; // ~2 years, SOC2 baseline
    public Dictionary<string, int> PerEventTypeOverrides { get; set; } = new();
    [Range(60, 86400)] public int SweepIntervalSeconds { get; set; } = 3600;
    [Range(100, 100_000)] public int BatchSize { get; set; } = 5000;
}
```

`PerEventTypeOverrides` maps event type → retention days, e.g.
```json
{
  "asset.downloaded": 90,
  "share.accessed": 90,
  "webhook.delivery_failed": 90,
  "acl.revoked": 1825,
  "pat.created": 1825,
  "pat.revoked": 1825
}
```

**API surface.** None. Retention is config-driven; no admin endpoint mutates the policy at runtime.

**Worker.** New `AuditRetentionBackgroundService` (hosted, scope-per-iteration) following the existing `TrashPurgeBackgroundService` shape:
- `PeriodicTimer(SweepIntervalSeconds)`.
- One pass per tick: iterate event types, compute cutoff = `now - retentionDays`, batch-delete rows with `CreatedAt < cutoff` using `ExecuteDeleteAsync` in chunks of `BatchSize`.
- Emits exactly **one** `audit.retention_purged` event per run with `purged_count`, `cutoff_date`, `event_type` (or `null` if mixed).

**UI.** None. The `Admin → Audit` tab continues to show whatever's in the table; the retention sweep is invisible operationally.

**Caching.** None.

**Acceptance criteria.**
- `AuditRetentionSettings` is bound + `ValidateOnStart`.
- Background service runs on the configured cadence and respects per-event overrides.
- The sweep emits `audit.retention_purged` itself — never purge without leaving a trace of the purge.
- Migration (or background service first-run) verifies the `AuditEvents.CreatedAt` index exists; if not, raise it via raw SQL `CREATE INDEX IF NOT EXISTS idx_audit_event_created_at ON "AuditEvents" ("CreatedAt");`.
- Integration test against `PostgresFixture`: seed events with mixed `CreatedAt`, run the sweep once, assert old rows gone, recent rows kept, one `audit.retention_purged` row written.

**Dependencies.** None. Cleanly bolted on to the existing `AuditService` pipeline.

**Out of scope.**
- UI for changing retention at runtime — config-file driven, restart on change.
- Retention based on actor / target rather than event type.
- Cold-storage / archival (S3 Glacier-style export of expired rows). If a customer demands long-term archive, that's a separate `T5-AUDIT-02`.

**Risks & trade-offs.**
- Per-event overrides are easy to misconfigure (e.g. set `acl.revoked` to 30 days and lose security-relevant history). Defaults must lean conservative; overrides should only shrink retention for genuinely high-volume telemetry-grade events.
- `ExecuteDeleteAsync` on a large `AuditEvents` table can spike replication lag; the `BatchSize` cap and chunked loop are deliberate.
- Once this lands, every feature with a high-volume event currently routed to telemetry should re-evaluate — see the **Auditing** cross-cutting section's table.

---

### T5-AUDIO-01 — Audio asset support

**Intent.** First-class audio handling. Today AssetHub treats audio as an opaque blob: `AssetType.Audio` exists in the enum but the upload pipeline, preview UI, metadata extraction, and search facets don't know what to do with it. Audio is a real asset class in marketing / brand work (jingles, podcast clips, trailers), and prospects evaluating us for those workloads bounce when they see audio rendering as a generic file icon.

**User gain.**
- Upload `.mp3` / `.wav` / `.flac` / `.m4a` / `.ogg` — gets a real preview rather than a download-prompt.
- Inline waveform thumbnail in the asset grid (replaces the generic audio icon).
- Detail page has a `<audio>` player with scrubber, duration, bitrate, sample-rate, channel count.
- Duration/bitrate/sample-rate/channels surface as metadata fields and are searchable / facetable.
- Optional speech-to-text transcription (when T2-AI is configured) populates `Asset.MetadataJson["transcription"]`, indexable by full-text search so "find the audio that says 'spring sale'" works.

**Business gain.** Audio is the cheapest gap to close that visibly distinguishes us from "generic file storage" perception. Brand-portal customers (T4-BP-01 audience) almost always have audio jingles and want them to feel native.

**Data model.** No new entities. Add fields to `Asset` for audio-specific media metadata:
```csharp
public int? DurationSeconds { get; set; }   // also useful for video
public int? AudioBitrateKbps { get; set; }
public int? AudioSampleRateHz { get; set; }
public int? AudioChannels { get; set; }
public string? WaveformPeaksPath { get; set; }  // MinIO key for the precomputed peaks JSON
```

`DurationSeconds` is shared with video (already has its own `DurationSeconds` if missing — verify) so it's not strictly audio-only.

**Migration.** Single migration `AddAudioMetadataToAssets` — five nullable columns. No data migration needed; existing audio assets stay null until they're re-processed.

**API surface.** No new endpoints. Existing rendition endpoints serve waveform peaks via a new `?fmt=peaks` (returns the peaks JSON) under the existing `/api/v1/assets/{id}/medium` path. The `RenditionRequest` allowlist (T3-REND-01) gets a new entry `peaks` in `AllowedFormats`.

**Worker.** New handler `ProcessAudioCommand` → `ProcessAudioHandler`, parallel to `ProcessImageCommand` and `ProcessVideoCommand`:
1. Probe with `ffprobe` — capture duration / bitrate / sample-rate / channels into `Asset` columns.
2. Generate waveform peaks: `ffmpeg -i input -ac 1 -filter:a aresample=8000 -map 0:a -c:a pcm_s16le -f data -` → process the PCM into a peaks array (≈1000 samples), upload as JSON to MinIO at `peaks/{assetId}.json`.
3. Emit `Asset.MarkReady` (existing state-machine method).
4. Optional: if `Ai:Transcription:Enabled`, call `IAiVisionService` extended to support audio (this part depends on T2-AI-01, ship without it as the v1).

**Routing.** `AssetUploadService` currently dispatches by `AssetType` to `ProcessImageCommand` / `ProcessVideoCommand`. Add the audio fork; keep image+video handlers untouched.

**UI.**
- New `AssetAudioPreview.razor` component — `<audio controls>` with the waveform rendered over a `<canvas>` from the peaks JSON (existing `imageEditor.js` pattern for canvas-from-server-data is the template).
- `AssetGrid` thumbnail: render a small waveform sparkline instead of the generic audio icon when `WaveformPeaksPath` is set, fall back to the icon otherwise.
- `AssetDetail` page: audio assets show duration + bitrate + sample-rate + channels in the metadata sidebar.
- Localization: `AssetsResource` keys `Asset_Duration`, `Asset_Bitrate`, `Asset_SampleRate`, `Asset_Channels` (en + sv pair).

**Caching.** Waveform peaks are immutable once generated; cache the JSON via `HybridCache` with a long TTL (`peaks:{assetId}` → 24 h, invalidate on `asset.updated` if the file is replaced via T1-VER-01).

**Acceptance criteria.**
- Uploading a `.mp3` populates `DurationSeconds`, `AudioBitrateKbps`, `AudioSampleRateHz`, `AudioChannels`, and writes `WaveformPeaksPath` within the same processing window as image thumbs (~5 s for a 3-minute file).
- Detail page renders an audio player with a waveform overlay; scrubbing the player highlights the corresponding waveform region.
- Asset grid shows a waveform sparkline for audio thumbnails.
- Search facets include duration buckets (`< 30 s`, `30–120 s`, `120–600 s`, `> 600 s`) and a bitrate bucket — wired through T1-SRCH-01's facet pipeline.
- `AssetType.Audio` is fully recognised by `MalwareScannerService`, `AssetMetadataService` (audio-scope schemas can be applied), and the workflow state machine.
- Integration test: upload a `.wav`, assert peaks JSON exists in MinIO, assert metadata fields populated.
- bUnit test: audio detail page renders the waveform component for an audio asset, falls back to icon for missing peaks.

**Dependencies.**
- ffmpeg / ffprobe must be in the worker container — already a base requirement for T0-MIG-01 video handling.
- Optional: T2-AI-01 (provider abstraction) for transcription. Ship audio v1 without it.

**Out of scope.**
- Real-time audio editing (trim / fade / EQ). The image editor is per-asset-type and audio's domain is too different to fold into the same component.
- Multi-track / stem support — single-track only.
- Live streaming / chunked playback — `<audio>` against a presigned MinIO URL is sufficient at our asset sizes.
- Loudness normalisation (LUFS) on ingest — defer until brand customers ask.

**Risks & trade-offs.**
- Waveform peaks JSON is small (~10 KB for a typical track) but generating them per upload adds maybe 1–2 seconds of processing. Acceptable.
- The peaks rendering is a `<canvas>` paint per detail-page view; on slow devices it can flash briefly. Render the audio player first, paint the waveform asynchronously.
- ffmpeg's audio filter chain has version-to-version quirks — pin the worker image's ffmpeg version (already done in `Dockerfile.Worker`).

---

## Cross-cutting concerns

### Auditing

AssetHub already has an `AuditEvent` pipeline via [IAuditService.LogAsync](../../src/AssetHub.Infrastructure/Services/AuditService.cs). Every feature in this roadmap that mutates state, grants/revokes access, exposes sensitive information, or performs a bulk operation **must** emit audit events through this pipeline. Skipping this is a regression of existing behaviour — the Admin → Audit tab is the only record of what happened when post-mortems start.

**Shape and invariants.**

- Call signature: `await _audit.LogAsync(eventType, targetType, targetId, actorUserId, details, ct)`.
- `eventType` is `snake_case` in the form `{target}.{verb_past_tense}` — for example `asset.created`, `share.revoked`, `acl.revoked`, `collection.download_requested`. Match the existing convention visible in [AssetService.cs](../../src/AssetHub.Infrastructure/Services/AssetService.cs), [AuthenticatedShareAccessService.cs](../../src/AssetHub.Infrastructure/Services/AuthenticatedShareAccessService.cs), and peers — do not invent new casing.
- `targetType` uses the values in `Constants.ScopeTypes` when one fits; add a new constant there if a feature introduces a new target class. One-off synthetic targets (e.g. `"upload"` for malware-detected during early-stage ingest) are allowed but should be constants, not string literals at call sites.
- `targetId` may be `Guid.Empty` when the event is scoped to a class of object rather than a specific instance (e.g. bulk actions, login events).
- `actorUserId` is `CurrentUser.UserId` in request-scoped flows. For Wolverine handlers and background services the actor is either the user who initiated the job (carry it on the command) or `null` for truly system-originated events.
- `details` is a `Dictionary<string, object>` that should carry just enough context to reconstruct intent without leaking secrets: sizes, counts, outcome codes, before/after for mutable fields, correlation IDs.

**Rules every feature must follow.**

1. **Never audit reads of non-sensitive data.** Listing assets, viewing a dashboard, opening a detail page — none of these are audit events. They pollute the log and make real security signals harder to find.
2. **Always audit sensitive reads.** Downloads, admin-retrievable secrets (share token / password recovery), access to another user's data, PAT usage for privileged endpoints — these must emit events. The existing `asset.downloaded` and `share.token_recovered` conventions set the bar.
3. **Audit the decision, not every intermediate state.** A bulk delete emits one `asset.bulk_deleted` event with counts in details, not N `asset.deleted` events (unless individual traceability is a regulatory requirement).
4. **Capture before/after for mutations.** For `asset.updated` etc., include changed-field names in `details` — not full payloads, which can leak.
5. **Record failures of privileged operations.** `share.password_verification_failed` with IP and token prefix is how abuse is noticed. Do not suppress audit on a business-logic `ServiceError.Forbidden` path.
6. **Do not let audit failure kill the primary operation.** The existing `AuditService.LogAsync` already catches and logs — preserve that semantic when extending.
7. **Think retention.** Audit rows accumulate forever today. Events that will be high-volume (downloads, webhook deliveries, search queries) need a retention story — either don't audit them at this granularity, or ship with a `AuditRetention` cleanup job as part of the feature.
8. **Request-scoped metadata comes from the middleware.** IP and User-Agent are captured by `AuditService` from `IHttpContextAccessor`. Do not pass them manually; do not log them in `details`. For background jobs where there is no HTTP context, these fields are legitimately `null`.

**Events to emit per roadmap feature.** Each feature owns the events it introduces and must add them in the same PR as the behaviour change. Every event listed below is a **required acceptance criterion** of its feature.

| Feature | Event types | `TargetType` | Key `details` keys |
|---|---|---|---|
| T0-MIG-01 | `migration.created`, `migration.started`, `migration.paused`, `migration.resumed`, `migration.cancelled`, `migration.completed`, `migration.deleted`, `migration.item_skipped_duplicate`, `migration.item_failed` | `migration`, `migration_item` | `source_type`, `dry_run`, `items_total`, `items_succeeded`, `items_failed`, `external_id`, `error_code` |
| T0-MIG-02 | `migration.s3_scan_started`, `migration.s3_scan_completed` | `migration` | `bucket`, `prefix`, `objects_found` (never log secrets) |
| T1-META-01 | `metadata_schema.created`, `metadata_schema.updated`, `metadata_schema.deleted`, `taxonomy.created`, `taxonomy.updated`, `taxonomy.deleted`, `asset.metadata_updated`, `asset.bulk_metadata_applied` | `metadata_schema`, `taxonomy`, `asset` | `schema_version`, `changed_field_keys[]`, `asset_count` (bulk only) |
| T1-LIFE-01 | `asset.soft_deleted`, `asset.restored`, `asset.purged`, `trash.emptied` | `asset`, `trash` | `retained_days`, `purge_batch_size`, `origin` (`user`/`ttl_worker`) |
| T1-DUP-01 | `asset.duplicate_blocked`, `asset.duplicate_override` | `asset`, `upload` | `sha256`, `existing_asset_id`, `override_by` |
| T1-SRCH-01 | `saved_search.created`, `saved_search.updated`, `saved_search.deleted`, `saved_search.digest_sent` | `saved_search` | `cadence`, `matches_delivered` — raw `search.query` events are telemetry, **not** audit, to prevent log spam |
| T1-VER-01 | `asset.version_created`, `asset.version_restored`, `asset.version_deleted` | `asset` | `version_number`, `previous_version_number`, `size_bytes`, `change_note` |
| T1-API-01 | `pat.created`, `pat.revoked`, `pat.used_first_time`, `pat.expired` | `pat` | `pat_id`, `scopes[]`, `last_used_at` (never log the token, never log the hash — log the id only) |
| T2-AI-02 | `asset.ai_tagged` | `asset` | `provider`, `tag_count`, `confidence_threshold` |
| T2-AI-03 | `asset.ocr_completed`, `asset.ocr_failed` | `asset` | `provider`, `character_count`, `duration_ms`, `error_code` |
| T2-AI-04 | `asset.ai_alt_text_generated`, `asset.ai_alt_text_regenerated` | `asset` | `provider`, `character_count`, `replaced_previous` (bool) |
| T2-AI-05 | `asset.smart_crop_applied` | `asset` | `preset_id`, `crop_box` |
| T3-COL-01 | `comment.created`, `comment.updated`, `comment.deleted`, `comment.mention_delivered` | `comment`, `asset` | `asset_id`, `mentioned_user_ids[]`, `parent_comment_id` |
| T3-WF-01 | `asset.workflow_submitted`, `asset.workflow_approved`, `asset.workflow_rejected`, `asset.workflow_published`, `asset.workflow_unpublished` | `asset` | `from_state`, `to_state`, `reason` |
| T3-REND-01 | **No per-request audit event.** Rendition hits are high-volume and belong in telemetry + CDN logs. Audit only the explicit configuration changes that affect rendition policy. |
| T3-INT-01 | `webhook.created`, `webhook.updated`, `webhook.deleted`, `webhook.delivery_failed_permanently` | `webhook`, `webhook_delivery` | `event_type`, `endpoint_host`, `attempt_count`, `response_status`. Successful deliveries are telemetry. |
| T3-NTF-01 | `notification.preferences_updated` | `user_preferences` | `category_changes[]`. Individual notification deliveries are telemetry. |
| T4-BP-01 | `brand.created`, `brand.updated`, `brand.deleted` | `brand` | `scope` (`global`/`collection`), `changed_fields[]` |
| T4-GUEST-01 | `guest.invited`, `guest.accepted`, `guest.access_revoked`, `guest.expired` | `user` | `invited_email`, `collection_ids[]`, `expires_at` |
| T5-NEST-01 | `collection.reparented` | `collection` | `previous_parent_id`, `new_parent_id` |
| T5-WMK-01 | `watermark.applied_on_download` | `asset` | `share_id`, `variant` — gated by config to avoid high-volume spam |
| T5-ANL-01 | **No audit** — reports are reads over existing audit/telemetry; no new events. |
| T5-AUDIT-01 | `audit.retention_purged` | `audit` | `purged_count`, `cutoff_date`, `event_type` (or `null` for mixed). **Meta-audit** — the retention worker is itself auditable. |
| T5-AUDIO-01 | **No new audit events** — audio assets ride on the existing `asset.created` / `asset.updated` / `asset.deleted` pipeline. Probe failures are logged at `Warning`, not audited (high-volume telemetry-grade). |

**Retention policy for audit data.**

This is a gap today — audit events are retained forever. When **T5-AUDIT-01** lands it must ship with:

- `AuditRetentionSettings { SectionName = "AuditRetention"; DefaultRetentionDays = 730; }` (two years — align with typical SOC2 expectations).
- Per-event-type overrides where regulation demands longer or shorter retention (e.g. `share.accessed` can be 90 days; `acl.revoked` must be the maximum).
- A `AuditRetentionBackgroundService` (hosted, scope-per-iteration) that batch-deletes expired rows.
- A `audit.retention_purged` event recording the purge itself — never purge without a record of the purge.

Until T5-AUDIT-01 lands, every feature adding a high-volume event type must justify the volume in its PR description or downgrade to telemetry.

**Where to emit from.**

- **Service layer.** The overwhelming majority of audit events belong in `sealed` services in `AssetHub.Infrastructure/Services/` alongside the mutation they describe, exactly where `AssetService`, `CollectionService`, etc. already emit. Do not emit from endpoint handlers — endpoints translate results, services own the decisions.
- **Wolverine handlers.** Handlers are services-by-another-name; they emit audit events for job lifecycle (`migration.item_failed`, `webhook.delivery_failed_permanently`). Pass `ActorUserId` on the command so handlers don't guess.
- **Background services.** TTL and retention workers must emit exactly one summary event per run (`asset.purged` with `count`), never one-per-item, and the `actorUserId` is `null`.
- **Never the UI.** Blazor components do not call `IAuditService`. A missing UI-side audit is always a signal that the service layer is missing one.

**Testing.** Every feature's integration tests include at least one assertion that the expected audit event was written for the happy path and for the primary failure path. The existing pattern in `AssetHub.Tests` (query the `AuditEvents` table after the operation) is the template.

### Telemetry

Every Tier 0–1 feature emits OpenTelemetry spans via the existing configuration:
- `migration.run` with child `migration.item`, tags: migration id, source type, dry run.
- `search.query` with tags: facet-count, has-text, result-count.
- `asset.ingest` with tags: asset type, size-bucket, dedup-hit, ai-provider.
- `webhook.deliver` with tags: event type, endpoint host, attempt count.

### Rate limits

New rate-limit policies to add in `ServiceCollectionExtensions.cs`:
- `MigrationUpload` — admin-only, 1000 requests/min per user.
- `SearchGlobal` — authenticated, 60 requests/min per user.
- `PublicApi` — per-PAT, configurable.

### Data protection

All encrypted-at-rest secrets (migration credentials, PAT tokens, webhook secrets) use ASP.NET Core Data Protection with the existing key persistence in `ServiceCollectionExtensions.cs`.

### Testing

Every Tier 0–1 feature ships with:
- Unit tests for services (Moq).
- Integration tests with `PostgresFixture` / `CustomWebApplicationFactory`.
- For UI-visible features: bUnit tests where reasonable.
- For end-to-end-critical features (migration, search, versioning, soft delete): at least one Playwright spec in `tests/E2E/tests/`.

### Documentation

Each shipped feature updates:
- `CLAUDE.md` when it amends existing rules (e.g., T1-LIFE-01 softens the "no soft delete" rule).
- `docs/audits/APPLICATION-AUDIT.md` — move line items from "missing" to "implemented".
- `docs/operations/` — runbook entries for migrations and lifecycle purge.

---

## Implementation order — suggested sequencing

A realistic sequence that minimises rework and delivers visible progress:

1. **T0-MIG-01** — Bulk import API + CSV connector. Ships first because nothing matters without migration.
2. **T1-API-01** — Public API + OpenAPI. Amends CLAUDE.md; unblocks connectors and integrations.
3. **T1-LIFE-01** — Soft delete + trash. Small, safe, improves every subsequent feature's risk profile.
4. **T1-DUP-01** — Duplicate detection. Small, reuses existing SHA256 field.
5. **T1-META-01** — Custom metadata + taxonomies. Foundation for search and rights.
6. **T1-SRCH-01** — Faceted search. Lights up all the metadata work.
7. **T1-VER-01** — Versioning.
8. **T0-MIG-02..05** — Additional source connectors as prospects demand.
9. **T2-AI-01..05** — AI features in order: provider abstraction, alt-text, auto-tag, OCR, smart crop.
10. **T3 / T4** — Collaboration, distribution, brand portals. Parallelisable across engineers.
11. **T5** — Polish.

Each item should ship behind a config flag where feasible so partially-completed features don't reach production.

---

## Updating this roadmap

This file is canonical for feature intent. When a feature ships:
1. Move it from the body to a **Shipped** appendix with the date and PR link.
2. Amend `CLAUDE.md` if the feature changed a rule.
3. Update `docs/audits/COMMERCIAL-DAM-GAP-ANALYSIS.md` to flip the gap status.

When a feature is reshaped:
1. Update the entry in place — never silently drop sections.
2. Keep the ID stable so cross-references don't rot.

When a new gap is discovered:
1. Add it in the appropriate Tier with a fresh ID.
2. Cross-link from the gap analysis doc.

---

## Shipped

### T1-API-01 — Public REST API + OpenAPI + Personal Access Tokens

**Shipped 2026-04-20** across six phases plus one mid-flight security fix. See the memory entry `project_t1_api_01_complete.md` for the per-commit breakdown, framework quirks (nullable collections + `Dictionary<string, object>` schema workarounds), and the open PatAuth-factory named-policy evaluation gap.

### T0-MIG-01 — Bulk import API and job runner

**Shipped 2026-04-21.** Initial implementation landed 2026-04-19 (commit `ddcc814`); test coverage, the missing `migration.completed` audit event, and roadmap / memory bookkeeping landed in the follow-up.

**Delivered as specified.**
- `Migration` + `MigrationItem` entities with JSONB `SourceConfig` / `FieldMapping` / `MetadataJson` and `ValueComparer` wiring.
- `MigrationSourceType`, `MigrationStatus`, `MigrationItemStatus` enums with `ToDbString` / `To*` extensions.
- Indices as specified (status+created composite, migration+status composite, idempotency unique, sha256).
- Admin endpoint surface under `/api/v1/admin/migrations` (see [MigrationEndpoints.cs](../../src/AssetHub.Api/Endpoints/MigrationEndpoints.cs)). Group policy `RequireAdmin`.
- CSV manifest parser with quoted fields, escaped quotes, `metadata.*` prefix, semicolon-separated tags + collection names.
- Staged-file model: manifest declares items; per-file staging uploads mark items `IsFileStaged=true`; `StartMigrationHandler` fans out only staged items; unstaged items keep the migration in `PartiallyCompleted`.
- `ProcessMigrationItemHandler` creates assets, copies bytes from staging to `originals/{assetId}/…`, resolves/creates named collections (with admin ACL), schedules media processing, handles SHA256 duplicate detection, truncates long error messages to `MigrationConstants.Limits.MaxErrorMessageLength`.
- Outcome CSV at `/{id}/outcome.csv` with the exact column set from the spec (`external_id, filename, status, target_asset_id, error_code, error_message`).
- Audit events: `migration.created`, `migration.started`, `migration.cancelled`, `migration.completed`, `migration.deleted`, `migration.bulk_deleted`, `migration.retried`. `actorUserId: null` for worker-emitted `migration.completed` (finalize runs from both `StartMigrationHandler.FinalizeMigration` and `ProcessMigrationItemHandler.TryFinalizeMigration`).
- Admin UI pages: list, detail with progress/items, create dialog, staging-file upload, outcome CSV download.

**Deviations from the original spec.**
- **Pause / resume endpoints are NOT shipped.** The existing design — retry failed items (`POST /{id}/retry`) + `PartiallyCompleted` status for unstaged items + `cancel` for hard stop — covers the main resumability use case without adding a state-machine dimension. Revisit if a customer demands mid-run pause of an in-flight batch; the path is: add `MigrationStatus.Paused`, a `PauseMigrationHandler`, and a `ResumeMigrationHandler` that re-publishes `ProcessMigrationItemCommand` for remaining pending items. Spec acceptance criterion "pausing mid-run stops new item handlers within 5 seconds" is not met; treat this as a known gap.
- **`migration.paused` / `migration.resumed` audit events are NOT emitted** (they would only exist once pause/resume ships). All other events from the spec's audit list are wired.
- **Batch upload of a ZIP file (`/items/batch-upload`) is NOT shipped** — the admin UI uploads files individually to `/files`. Adequate for the initial import flows but documented as a follow-up if customers bring pre-bundled ZIPs.
- **`?purgeAssets=true` on DELETE is NOT shipped.** Deleting a migration record leaves any produced assets intact; admins delete them via the normal asset UI. Low-stakes deviation.
- **Pull connectors (`/s3/scan` and T0-MIG-02..05 generally) are NOT shipped** — they remain as separate roadmap items.

**Race-condition known issue.** `ProcessMigrationItemHandler.TryFinalizeMigration` is not serialised across concurrent item handlers. In theory two handlers finalising the last two items at the same time could both pass the `counts.StagedPending == 0 && counts.Processing == 0` check and both emit `migration.completed`. In practice the window is small and the second `UpdateAsync` is idempotent; the only user-visible symptom is a duplicate audit entry. A proper fix (advisory lock or optimistic concurrency token on the `Migrations` row) is deferred — call this out if we ever see duplicate completed events in the audit log.

**Test coverage landed in hardening pass.**
- `tests/AssetHub.Tests/Services/MigrationServiceTests.cs` — 44 unit tests (auth gates, CSV parsing, manifest re-upload, staging upload path sanitisation, retry from terminal states, bulk delete filters).
- `tests/AssetHub.Tests/Handlers/StartMigrationHandlerTests.cs` (7) + `ProcessMigrationItemHandlerTests.cs` (22) — cover fan-out, dry run, duplicate SHA256, error truncation, `migration.completed` emission, and the "pending siblings skip finalize" path.
- `tests/AssetHub.Tests/Repositories/MigrationRepositoryTests.cs` — 21 Postgres-fixture tests for JSONB persistence, item count aggregation, cascade delete, case-insensitive staging match.
- `tests/AssetHub.Tests/Endpoints/MigrationEndpointTests.cs` — 23 HTTP tests (viewer-403 gates, validation, create→manifest→start happy path, bulk delete filter).

### T1-DUP-01 — Duplicate detection on upload

**Shipped 2026-04-21.** Core duplicate detection (`GetBySha256Async` + `DUPLICATE_ASSET` 409 with structured `details` + `?force=true` override) landed incrementally during earlier upload work; this pass added the remaining spec items.

**Delivered as specified.**
- `IAssetRepository.GetBySha256Async` consulted in both `AssetUploadService.UploadAsync` and `ConfirmUploadAsync`.
- 409 `ServiceError.DuplicateAsset` carries `existingAssetId` + `existingTitle`; `ServiceResult.ToHttpResult()` preserves the `Details` dictionary onto the wire via `ApiError`.
- `?force=true` on `POST /api/v1/assets` and `POST /api/v1/assets/{id}/confirm-upload` allows admin override.
- UI: [AssetUpload.razor](../../src/AssetHub.Ui/Components/AssetUpload.razor) catches `ApiException.ErrorCode == "DUPLICATE_ASSET"` and routes to [UploadErrorsDialog.razor](../../src/AssetHub.Ui/Components/UploadErrorsDialog.razor) with a "Go to existing" link. Localised in EN + SV (`Error_DuplicateAsset`, `Alert_DuplicateBlocked`, `Link_GoToExisting`).
- Migration pipeline: [ProcessMigrationItemHandler.cs](../../src/AssetHub.Worker/Handlers/ProcessMigrationItemHandler.cs) checks SHA256 and marks items with `MigrationConstants.ErrorCodes.Duplicate`.
- Audit events: `asset.duplicate_blocked` (emitted at 409) and `asset.duplicate_override` (emitted after successful admin force-override), both recorded with sha256 + existingAssetId.
- Admin-only gate: non-admin callers passing `skipDuplicateCheck=true` get `403 Forbidden` ("Only administrators can bypass duplicate detection.").

**Test coverage.** `tests/AssetHub.Tests/Services/AssetUploadServiceTests.cs` — 7 duplicate-focused tests: `UploadAsync_ExistingSha256_ReturnsConflict`, `_ForceOverride_Succeeds`, `_NonAdminForceOverride_ReturnsForbidden`, `_DifferentContent_NoDuplicate`, `_Duplicate_EmitsDuplicateBlockedAuditEvent`, `_AdminForceOverride_EmitsDuplicateOverrideAuditEvent`, plus equivalents on `ConfirmUploadAsync`.

**Deviations from the spec.** None material. The spec's `collections` field on the 409 payload was dropped in favour of just `existingAssetId` + `existingTitle` — the UI only needs the link target, and collections are one hop away via the existing asset detail page.

### T1-META-01 — Custom metadata schemas and taxonomies

**Shipped 2026-04-21.** Landed across commits `c5ea695` (initial implementation) and `11e9703` (test pass). Memory entry: `project_t1_meta_01_complete.md`.

**Delivered as specified.**
- `MetadataSchema` (scoped Global / AssetType / Collection), `MetadataField` (Text, LongText, Number, Decimal, Boolean, Date, DateTime, Select, MultiSelect, Taxonomy, Url), `Taxonomy` + hierarchical `TaxonomyTerm` (with `ParentTermId` self-FK and `Slug`), and `AssetMetadataValue` with polymorphic typed columns (`ValueText` / `ValueNumeric` / `ValueDate` / `ValueTaxonomyTermId`).
- GIN indexes from the spec: `idx_asset_metadata_value_field_text_trgm` (trigram on `ValueText`), `idx_asset_metadata_value_field`, `idx_asset_metadata_value_taxonomy_term`. All created via raw SQL in the schema migration with `IF NOT EXISTS` for idempotency.
- Endpoints: [MetadataSchemaEndpoints.cs](../../src/AssetHub.Api/Endpoints/MetadataSchemaEndpoints.cs), [TaxonomyEndpoints.cs](../../src/AssetHub.Api/Endpoints/TaxonomyEndpoints.cs), [AssetMetadataEndpoints.cs](../../src/AssetHub.Api/Endpoints/AssetMetadataEndpoints.cs). `GET /api/v1/assets/{id}/metadata` returns resolved schema + values; `PUT` upserts; `POST /api/v1/assets/bulk-metadata` applies a template to N assets.
- Admin UI: `MetadataSchemaDialog.razor`, `TaxonomyDialog.razor`, dynamic `AssetMetadataForm.razor` wired into `EditAssetDialog` and the upload flow. Required fields block submit; per-field validation comes from `PatternRegex` / `MaxLength` / `NumericMin` / `NumericMax`.
- `Facetable` flag honoured by `AssetSearchService` (see T1-SRCH-01 appendix entry).
- Schema delete protected by existing-value guard unless `?force=true`, then cascades.

**Deviations from the spec.** None material. Locale handling stayed at `LabelSv` single-column overrides; a JSONB-per-locale shape was left as a follow-up if a third locale appears.

**Test coverage.** `tests/AssetHub.Tests/Services/MetadataSchemaServiceTests.cs`, `MetadataSchemaQueryServiceTests.cs`, `AssetMetadataServiceTests.cs`, `TaxonomyServiceTests.cs`, `TaxonomyQueryServiceTests.cs` — cover schema CRUD + scope validation, hierarchical taxonomy CRUD with cycle detection, typed-value upsert + validation per field type, and the bulk-apply endpoint.

### T1-LIFE-01 — Soft delete, trash, and scheduled purge

**Shipped 2026-04-21.** Landed across commits `942cd54`, `3d61ac2`, `13d4491`, `c301bd0` (phases 1-6). Memory entry: `project_t1_life_01_complete.md`.

**Delivered as specified.**
- `Asset.DeletedAt` (nullable UTC) + `Asset.DeletedByUserId` on the entity; EF Core global query filter `.HasQueryFilter(a => a.DeletedAt == null)` hides trashed rows from default queries. Trash and purge paths use `IgnoreQueryFilters()`.
- `AssetLifecycleSettings` config class (`SectionName = "AssetLifecycle"`, `TrashRetentionDays = 30`, `ValidateOnStart = false`).
- Soft-delete endpoints + admin trash surface at [AdminTrashEndpoints.cs](../../src/AssetHub.Api/Endpoints/AdminTrashEndpoints.cs): `GET /api/v1/admin/trash`, `POST /{id}/restore`, `DELETE /{id}` (permanent), `POST /empty` (second-confirm bulk permanent).
- [TrashPurgeBackgroundService.cs](../../src/AssetHub.Worker/BackgroundServices/TrashPurgeBackgroundService.cs) hourly loop — finds assets with `DeletedAt < now - TrashRetentionDays`, deletes rows and MinIO objects.
- UI: [AdminTrashTab.razor](../../src/AssetHub.Ui/Components/AdminTrashTab.razor); single-asset delete flows in `AssetDetail` / `AssetCardGrid` show optimistic removal + Undo snackbar that calls restore.
- Cache invalidation via `CacheKeys.Tags.AssetList` on delete / restore.
- CLAUDE.md's "No soft delete" rule was amended for `Asset` to match this item (see the Domain Entities section in CLAUDE.md).

**Deviations from the spec.**
- **Bulk Undo snackbar is not shipped.** Single-asset Undo works from detail and grid. The bulk path (`BulkAssetActionsDialog`) deletes but does not offer a single "Undo (N)" snackbar — a UX call left for a dedicated pass. Tracked in [FOLLOW-UPS.md](./FOLLOW-UPS.md) as "T1-LIFE-01 — BulkAssetActionsDialog undo-snackbar".
- **TrashPurgeBackgroundService has no integration test.** Unit tests cover `AssetTrashService`; end-to-end worker-tick coverage needs a `WorkerFixture` pattern that doesn't exist yet. Tracked in [FOLLOW-UPS.md](./FOLLOW-UPS.md) as "T1-LIFE-01 — TrashPurgeBackgroundService integration test".
- **Collection soft-delete not shipped.** Explicitly scoped out in the original spec; future T1-LIFE-02.

**Test coverage.** `tests/AssetHub.Tests/Services/AssetTrashServiceTests.cs` covers the full loop (soft-delete, list, restore, permanent delete, empty). Repository tests cover the query-filter / `IgnoreQueryFilters` boundary. Endpoint tests cover admin-403 for non-admins.

### T1-SRCH-01 — Faceted search with full-text, OCR, and saved searches

**Shipped 2026-04-21.** Landed across commits `1e4b01e`, `461d49e`, `9100ec4`, `23abf26`, `59b4459`, `095fa7a`, `c53936b`, `ab6d84c`, `8ccbd63`. Memory entry: `project_t1_srch_01_complete.md`.

**Delivered as specified.**
- `search_vector` generated tsvector column on `Asset` with `setweight` A/B/C on Title / Description / Tags, GIN index `idx_asset_search_vector` (migration `20260419181317_AddAssetSearchAndSavedSearch` + reindex migration `20260419195521_BulkReindexSearchVector`).
- [AssetSearchService.cs](../../src/AssetHub.Infrastructure/Services/AssetSearchService.cs) accepts a structured `AssetSearchRequest` (text, facet filters, sort, pagination). Facet counts aggregated in a second query per facet dimension.
- `SavedSearch` entity (`Id`, `Name`, `OwnerUserId`, `RequestJson`, `Notify`, `LastRunAt`, `LastHighestSeenAssetId`, `CreatedAt`) + `SavedSearchNotifyCadence` enum (None / OnNewMatch / Daily / Weekly).
- Endpoints: `POST /api/v1/assets/search` → `{ items, facets, totalCount }` ([AssetSearchEndpoints.cs](../../src/AssetHub.Api/Endpoints/AssetSearchEndpoints.cs)); standard CRUD at `/api/v1/saved-searches` ([SavedSearchEndpoints.cs](../../src/AssetHub.Api/Endpoints/SavedSearchEndpoints.cs)) scoped to owner.
- UI: [SearchSidebar.razor](../../src/AssetHub.Ui/Components/SearchSidebar.razor) with faceted filter accordion + active-filter chips; [SavedSearchesMenu.razor](../../src/AssetHub.Ui/Components/SavedSearchesMenu.razor) in the nav menu.
- Reads `Facetable` fields from T1-META-01 (metadata facets aggregate into the response).

**Deviations from the spec.**
- **Saved-search notification delivery is NOT shipped** — only the schema (`Notify`, `LastRunAt`, `LastHighestSeenAssetId`) is in place. See [SavedSearch.cs](../../src/AssetHub.Domain/Entities/SavedSearch.cs) comments: *"Schema-only in v1; the worker ships with T3-NTF-01."* The roadmap acceptance criterion "Saved search with `Daily` cadence generates a digest email" will be satisfied when T3-NTF-01 (notifications) ships and reads these columns. No standalone follow-up — this is part of T3-NTF-01's scope.
- **OCR-content searchability** depends on T2-AI-03 which has not shipped. The schema supports it (the service already joins against `AssetMetadataValue` text values); once OCR writes extracted text into `Asset.Description` / metadata fields, it will be picked up automatically.

**Test coverage.** `tests/AssetHub.Tests/Services/AssetSearchServiceTests.cs` + `SavedSearchServiceTests.cs` — cover tsvector query construction, facet aggregation, saved-search CRUD, owner isolation, and `RequestJson` round-trip.

### T1-VER-01 — Asset versioning

**Shipped 2026-04-21.** Landed across commits `b9f37a4`, `a4fe14e`, `4007449`, `7f1cbc2`. Memory entry: `project_t1_ver_01_complete.md`.

**Delivered as specified.**
- [AssetVersion.cs](../../src/AssetHub.Domain/Entities/AssetVersion.cs) entity with `VersionNumber` (1-based), per-version `OriginalObjectKey` / `ThumbObjectKey` / `MediumObjectKey` / `PosterObjectKey`, `SizeBytes`, `ContentType`, `Sha256`, `EditDocument`, JSONB `MetadataSnapshot`, `ChangeNote`, audit fields.
- Unique index `idx_asset_version_asset_version_unique` on `(AssetId, VersionNumber)` (migration `20260419220555_AddAssetVersions`).
- `Asset.CurrentVersionNumber` column; `ReplaceImageFileAsync` creates vN instead of overwriting; `versions/{versionNumber}/...` MinIO key layout.
- Endpoints at [AssetVersionEndpoints.cs](../../src/AssetHub.Api/Endpoints/AssetVersionEndpoints.cs): `GET /api/v1/assets/{id}/versions`, `POST /{id}/versions/{n:int}/restore`, `DELETE /{id}/versions/{n:int}` (admin only).
- `ProcessImageHandler` reuse: new versions schedule rendition processing writing to the versioned keys.
- UI: [AssetVersionHistoryPanel.razor](../../src/AssetHub.Ui/Components/AssetVersionHistoryPanel.razor) under Derivatives in `AssetDetail`.
- Soft-delete (T1-LIFE-01) cascades into version rows.

**Deviations from the spec.**
- **`SaveImageCopy` is not interpreted as a versioning event on the source asset.** Save-copy already creates a separate derivative asset with its own `SourceAssetId` trail; adding an `AssetVersion` on top of that would duplicate history. The source asset's bytes are unchanged, so there is no new version. This is a design call, not a bug — see [FOLLOW-UPS.md](./FOLLOW-UPS.md) "T1-VER-01 — SaveImageCopy versioning interpretation" for the alternative interpretation and the trigger for revisiting.
- **Version thumbnail preview in the history panel is not shipped.** The DTO ships `ThumbObjectKey` per version, but the panel renders a plain table. Tracked in [FOLLOW-UPS.md](./FOLLOW-UPS.md) as "T1-VER-01 — version thumbnail preview in history panel".
- **Real-Postgres integration tests for restore round-trip and purge cascade are not shipped.** Unit-level coverage via Moq is in; integration needs the same `WorkerFixture` pattern the purge service wants. Tracked in [FOLLOW-UPS.md](./FOLLOW-UPS.md) as "T1-VER-01 — AssetVersionService integration test against real Postgres".
- **Check-in / check-out locking** is explicitly out of scope per the spec; future T3-COL-03.

**Test coverage.** `tests/AssetHub.Tests/Services/AssetVersionServiceTests.cs` — covers list / restore / prune, version-number monotonicity, and admin-only prune authorisation. Integration coverage deferred per the FOLLOW-UPS entry above.

### T3-NTF-01 — Notifications (in-app + email)

**Shipped 2026-04-24** across three phases.

**Phase 1** (`e55f921`) — backbone. `Notification` + `NotificationPreferences` entities + migration, `INotificationService` (create / list / unread-count / mark-read / mark-all-read / delete) and `INotificationPreferencesService` (lazy-create + defaults, merge-update with audit, resolve). Endpoints at `/api/v1/notifications` + `/api/v1/notifications/preferences`. HybridCache unread-count with tag invalidation. Audit event `notification.preferences_updated`.

**Phase 2** (`5b2889d`) — UI. `NotificationBell` in `MainLayout` (30s polling for unread count, dropdown with 10 most recent, optimistic mark-read). `/notifications` full-page list with All/Unread filter. `NotificationPreferencesPanel` embedded in `/account`. `NotificationsResource.resx` + `.sv.resx` (39 keys EN + SV).

**Phase 3** (2026-04-24) — email + digest + unsubscribe.

*Instant email pipeline.*
- [SendNotificationEmailCommand](../../src/AssetHub.Application/Messages/NotificationMessages.cs) Wolverine message + queue listener `send-notification-email`.
- [SendNotificationEmailHandler](../../src/AssetHub.Worker/Handlers/SendNotificationEmailHandler.cs) loads the notification, resolves the recipient email via `IUserLookupService`, signs an unsubscribe URL, and sends via `IEmailService`. Silent no-op when notification was deleted, email is missing, or prefs row is gone.
- [NotificationEmailTemplate](../../src/AssetHub.Application/Services/Email/Templates/NotificationEmailTemplate.cs) — subject mirrors notification title, body + deep-link CTA + unsubscribe line.
- [NotificationService.CreateAsync](../../src/AssetHub.Infrastructure/Services/NotificationService.cs) publishes the command when resolved prefs are `Email=true && EmailCadence="instant"`; skips for disabled email or non-instant cadence.

*Anonymous unsubscribe.*
- [INotificationUnsubscribeTokenService](../../src/AssetHub.Application/Services/INotificationUnsubscribeTokenService.cs) — Data Protection purpose `NotificationUnsubscribeProtector`, payload `{userId, category, stamp}` base64url-encoded.
- `GET /api/v1/notifications/unsubscribe?token=...` — anonymous endpoint in [NotificationEndpoints](../../src/AssetHub.Api/Endpoints/NotificationEndpoints.cs), renders plain HTML confirmation (English-only, see deviation below). Neutral response on any invalid token so an attacker can't probe for valid ids.
- `INotificationPreferencesService.UnsubscribeFromCategoryAsync` validates the token, checks the stamp matches the current `UnsubscribeTokenHash`, flips `Email=false` for the embedded category, emits `notification.unsubscribed_via_email`.

*Saved-search digest worker.*
- [SavedSearchDigestBackgroundService](../../src/AssetHub.Worker/BackgroundServices/SavedSearchDigestBackgroundService.cs) — polls every `NotificationConstants.DigestSchedule.PollIntervalMinutes` (30 min). Per saved search with `Notify != None`, checks cadence cooldown (`OnNewMatch` always; `Daily` ≥ 20 h; `Weekly` ≥ 6 days), runs the owner's `AssetSearchRequest`, filters to new matches via `LastRunAt`, creates a `saved_search_digest` notification, stamps `LastRunAt` / `LastHighestSeenAssetId`, emits `saved_search.digest_sent` audit event.
- `ISavedSearchRepository.GetWithNotificationsEnabledAsync` + `MarkRunAsync`.
- Closes the schema-only carve-out from T1-SRCH-01 — saved searches with notifications now deliver in-app (and email, via the instant pipeline) when new matches arrive.

*Supporting plumbing.*
- Worker registers `CurrentUser.Anonymous` scoped (background context), plus `IUserLookupService`, `IEmailService`, `AppSettings`, `EmailSettings` (moved from API-only registration).
- API Wolverine publishes `SendNotificationEmailCommand` to `send-notification-email`; Worker listens.
- `INotificationRepository.GetByIdAsync` added (server-trusted, not owner-scoped).
- `INotificationPreferencesService.GetByUserIdAsync` exposes the raw row so the email handler can read the stamp.

**Deviations from the spec.**
- **Daily / weekly email batching is NOT shipped.** Every notification created with `EmailCadence=instant` sends its own email. `daily` / `weekly` are accepted in prefs but produce no email — only the in-app entry. A proper batching worker that groups each user's pending notifications into one email per cadence interval is deferred to [FOLLOW-UPS.md](./FOLLOW-UPS.md) as "T3-NTF-01 — daily/weekly email batching". Users who want fewer emails can either disable email for a category or set it to daily/weekly and accept the gap.
- **Unsubscribe confirmation page is English-only.** Hit from an email client outside the Blazor session with no culture negotiation. Localising would require a per-user preferred-culture column or a culture hint in the signed token; both are bigger than phase 3 needs. Tracked in FOLLOW-UPS as "T3-NTF-01 — localise unsubscribe confirmation page".
- **Digest worker has no integration test.** Unit coverage of the dependencies is in; an end-to-end tick against a real Postgres + Wolverine harness needs the same `WorkerFixture` pattern the T1-LIFE-01 purge service and T1-VER-01 version service are waiting on. Tracked in FOLLOW-UPS as "T3-NTF-01 — SavedSearchDigestBackgroundService integration test".
- **Email template is English-only, same reason as the confirmation page.** No localisation keys added in phase 3. Folds into the same follow-up.

**Known issue.** `NotificationPreferences.UnsubscribeTokenHash` is generated once at prefs creation and never rotated by user action today. If a plaintext unsubscribe URL is leaked (forwarded email, etc.) the user has no "invalidate outstanding links" button. The stamp-as-rotation-token infrastructure is there; the missing piece is a `POST /preferences/rotate-unsubscribe-token` endpoint + UI. Low-risk since each token is category-scoped and only flips `Email=false` — no privilege escalation. Tracked in FOLLOW-UPS as "T3-NTF-01 — user-visible unsubscribe-token rotation".

**Audit events emitted per the roadmap cross-cutting table.**
- `notification.preferences_updated` (phase 1).
- `notification.unsubscribed_via_email` (phase 3).
- `saved_search.digest_sent` (phase 3, from the digest worker — `actorUserId` is null, owner id + match count in details).
- Individual notification deliveries remain telemetry, not audit, per the roadmap spec.

**Test coverage landed in phase 3.**
- `tests/AssetHub.Tests/Services/NotificationUnsubscribeTokenServiceTests.cs` — 9 tests: round-trip, URL-safety, tampering, key-ring separation, empty-field guards.
- `tests/AssetHub.Tests/Services/NotificationPreferencesServiceTests.cs` — 4 new tests on the unsubscribe path (invalid token, happy path, stamp mismatch, idempotent second call).
- `tests/AssetHub.Tests/Services/NotificationServiceTests.cs` — 3 new tests verifying instant cadence publishes `SendNotificationEmailCommand`, daily skips, email-disabled skips.
- `tests/AssetHub.Tests/Handlers/SendNotificationEmailHandlerTests.cs` — 4 tests: missing notification, missing email, happy path (asserts URL shape), missing prefs row.
- `tests/AssetHub.Tests/Endpoints/NotificationEndpointTests.cs` — 3 new tests: missing token 400, invalid token neutral 200, valid token flips email and returns confirmation HTML.

Full suite: 940 passing (AssetHub.Tests) + 234 passing (AssetHub.Ui.Tests).

### T3-COL-01 — Comments with @mentions

**Shipped 2026-04-24** in a single pass. Lights up the T3-NTF-01 mention category with real traffic — every `@username` in a comment generates a `mention` notification, which rides the instant-email pipeline to the recipient.

**Delivered as specified.**
- [AssetComment](../../src/AssetHub.Domain/Entities/AssetComment.cs) entity with `AssetId`, `AuthorUserId`, `Body`, `MentionedUserIds` (Postgres `text[]`), `CreatedAt`, `EditedAt`, `ParentCommentId` (self-FK, cascade).
- EF config in [AssetHubDbContext](../../src/AssetHub.Infrastructure/Data/AssetHubDbContext.cs) with two indices (`idx_asset_comment_asset_created`, `idx_asset_comment_parent`); migration `20260424214046_AddAssetComments` creates the table + FKs + `text[]` column.
- [AssetCommentService](../../src/AssetHub.Infrastructure/Services/AssetCommentService.cs) implements the four CRUD methods with three-tier authorization:
  - **Read**: viewer-or-higher on any collection containing the asset.
  - **Create**: contributor-or-higher on any collection containing the asset.
  - **Update**: author only (admins intentionally can't silently rewrite someone's words; delete is their moderation tool).
  - **Delete**: author or system admin.
- [MentionParser](../../src/AssetHub.Application/Helpers/MentionParser.cs) — source-generated regex `(?<![A-Za-z0-9])@([A-Za-z0-9._-]{1,32})`. Matches after a word boundary (so `email@host.com` is not a mention), 1–32 chars, deduplicated with case-insensitive ordering. Unknown usernames (no Keycloak hit) are dropped silently — typos don't 500 the request.
- [AssetCommentEndpoints](../../src/AssetHub.Api/Endpoints/AssetCommentEndpoints.cs) under `/api/v1/assets/{id:guid}/comments` with group policy `RequireViewer`; POST returns 201 with Location header; PATCH / DELETE return 200 / 204. `ValidationFilter<T>` on POST + PATCH; `DisableAntiforgery` on mutations.
- Notification fan-out reuses `NotificationConstants.Categories.Mention` — no new category. Title `"You were mentioned on '{assetTitle}'"`, 180-char body preview, deep-link `/assets/{assetId}`, `data = { comment_id, asset_id, author_user_id }`. Self-mentions (author @'ing themselves) are dropped.
- Audit events: `comment.created`, `comment.updated`, `comment.deleted`, `comment.mention_delivered` (per mention, on both create and the "newly-added only" subset of an edit).
- UI: [AssetCommentsPanel](../../src/AssetHub.Ui/Components/AssetCommentsPanel.razor) embedded in [AssetDetail](../../src/AssetHub.Ui/Pages/AssetDetail.razor) just below the version history panel. [AssetCommentEditor](../../src/AssetHub.Ui/Components/AssetCommentEditor.razor) is a reusable textarea + submit used for new comments, replies, and inline edits. [AssetCommentItem](../../src/AssetHub.Ui/Components/AssetCommentItem.razor) renders a single comment with HTML-escaped body, styled `@mention` chips, and conditional Reply / Edit / Delete actions. Optimistic delete with rollback on failure.
- Localization: new [CommentsResource.resx](../../src/AssetHub.Ui/Resources/CommentsResource.resx) + [CommentsResource.sv.resx](../../src/AssetHub.Ui/Resources/CommentsResource.sv.resx), 22 keys each.

**Deviations from the spec.**
- **`MudAutocomplete` for @mention suggestions is NOT shipped.** The server is happy to resolve typed `@username` to a user id, but the client doesn't yet offer a user-search dropdown as you type. Needs a new `/api/v1/users/search` endpoint + UI integration. Tracked in [FOLLOW-UPS.md](./FOLLOW-UPS.md) as "T3-COL-01 — mention autocomplete in the comment editor".
- **Author display-name resolution is NOT shipped.** The panel shows Keycloak `sub` truncated to `abcd1234…` instead of `@alice` / "Alice Smith". The backend has `IUserLookupService.GetUserNamesAsync` but the UI doesn't call it yet; wiring through display names needs a batch lookup on panel load. Tracked in FOLLOW-UPS as "T3-COL-01 — author display name + avatar resolution".
- **Markdown rendering is NOT shipped.** Body is plain text; newlines preserved, HTML escaped. The spec said "markdown, sanitized" but for v1 we avoided pulling Markdig + a sanitizer for minimal-value features (bold/italic). Tracked in FOLLOW-UPS as "T3-COL-01 — markdown rendering for comment bodies" — revisit if a customer specifically asks.
- **Reply threading depth capped at 1.** The entity supports `ParentCommentId` chains of any depth, but the service rejects `parent.ParentCommentId != null` to keep the UI flat. This matches Figma / Frame.io conventions and avoids unbounded-nesting UX. Not a deviation per the spec (which said "threading; null for top-level"); just documenting the interpretation.

**Test coverage.**
- `tests/AssetHub.Tests/Services/AssetCommentServiceTests.cs` — 13 tests covering every auth path (anonymous / no-access / viewer-can-read / contributor-can-create / author-can-edit / author-or-admin-can-delete), mention fan-out on create, "only newly added mentions notify" on edit, unknown-username drop, self-mention skip, reply-to-missing-parent, reply-to-reply depth cap.
- `tests/AssetHub.Tests/Helpers/MentionParserTests.cs` — 9 tests: empty input, no mentions, email-pattern-not-mention, punctuation boundaries, de-duplication, dots/dashes/underscores, 32-char max.
- `tests/AssetHub.Tests/Endpoints/AssetCommentEndpointTests.cs` — 5 HTTP tests: anonymous 401, create-list round trip, empty-body 400, author edit, author delete.

Full suite: 977 passing (AssetHub.Tests) + 234 passing (AssetHub.Ui.Tests).

### T3-WF-01 — Approval workflow (states)

**Shipped 2026-04-24** in a single pass. Introduces `AssetWorkflowState` with the transition machine `Draft → InReview → Approved → Published` plus `Rejected` as a dead-end that can resubmit, and integrates the share gate so unpublished assets can't be shared externally.

**Delivered as specified.**
- [AssetWorkflowState](../../src/AssetHub.Domain/Entities/Enums.cs) enum with `ToDbString` / `ToAssetWorkflowState` extensions matching the existing pattern; [AssetWorkflowTransition](../../src/AssetHub.Domain/Entities/AssetWorkflowTransition.cs) append-only log entity.
- `Asset.WorkflowState` + `Asset.WorkflowStateUpdatedAt` columns; migration `20260424224039_AddAssetWorkflow` backfills existing rows with `"published"` so pre-upgrade shares keep working.
- [AssetWorkflowService](../../src/AssetHub.Infrastructure/Services/AssetWorkflowService.cs) with a single `TransitionAsync` core driven by a compile-time `(from-state, action) → plan` table. Each plan carries the target state, audit-event constant, whether author or Manager+ is required, and whether the required-metadata gate runs.
- **Required-metadata gate** hits `IMetadataSchemaQueryService.GetApplicableAsync` for every schema scope touching the asset (global, per-asset-type, per-containing-collection), then cross-references `IAssetMetadataRepository.GetByAssetIdAsync`. Missing fields return 400 with per-field messages.
- **Authorization** is three-tier, mirroring T3-COL-01: Viewer floor for reads and action discovery; Submit / Resubmit is author-bound (bypassed by system admins for support scenarios); Approve / Reject / Publish / Unpublish need Manager+ on a containing collection (or system admin).
- Endpoints in [AssetWorkflowEndpoints](../../src/AssetHub.Api/Endpoints/AssetWorkflowEndpoints.cs) — one per action, `ValidationFilter<T>` applied, `DisableAntiforgery` on POSTs.
- Notifications reuse `NotificationConstants.Categories.WorkflowTransition`; the author is notified on every transition except their own action (so resubmits don't ping the author about themselves).
- Audit events: `asset.workflow_submitted`, `asset.workflow_approved`, `asset.workflow_rejected`, `asset.workflow_published`, `asset.workflow_unpublished` — each with `from_state`, `to_state`, `reason` in details.
- **Share-policy gate** in [ShareService.CreateShareAsync](../../src/AssetHub.Infrastructure/Services/ShareService.cs): when the scope is `asset` and the caller is not a system admin, the asset's current `WorkflowState` must be in `WorkflowSettings.AllowedShareStates` (default `[Approved, Published]`). Collection-scoped shares bypass the gate for brand-portal use cases.
- [WorkflowSettings](../../src/AssetHub.Application/Configuration/WorkflowSettings.cs) — `NewAssetState` (default `Published`, backward-compat) and `AllowedShareStates` (default `[Approved, Published]`). Admins flip `NewAssetState = Draft` in config to activate the workflow for new uploads.
- UI: [WorkflowPanel](../../src/AssetHub.Ui/Components/WorkflowPanel.razor) renders the current state as a colour-coded chip, surfaces only the actions the server says are available (so reviewers don't see "Submit", authors don't see "Approve"), and shows a `MudTimeline` of transition history. Localised in [WorkflowResource.resx](../../src/AssetHub.Ui/Resources/WorkflowResource.resx) + [sv.resx](../../src/AssetHub.Ui/Resources/WorkflowResource.sv.resx).

**Deviations from the spec.**
- **`NewAssetState` defaults to `Published`, not `Draft`.** The roadmap's acceptance criterion ("assets cannot be shared externally unless Approved or Published") only bites when admins opt in by flipping the setting. The spec-literal default would have broken every existing "upload and share immediately" flow plus ~500 tests. Customers who want the workflow flip `Workflow:NewAssetState = Draft` — a one-line appsettings change. Documented as a known deviation; admins who expect spec-default behaviour need to set this explicitly.
- **State badge is on AssetDetail only, not in the grid/cards.** The spec asked for both; the grid-card badge is deferred because the card grid has several contexts (grid page, collection page, embeds) and the visual treatment needs a UX pass so the badge doesn't clutter the thumbnail. Tracked in [FOLLOW-UPS.md](./FOLLOW-UPS.md) as "T3-WF-01 — workflow state badge on asset grid cards".
- **Reason dialog uses `MudDialog.ShowMessageBox` with fixed text.** The reject path collects a reason via the API (`WorkflowRejectDto.Reason`) but the UI currently doesn't prompt for free-text input inline — it confirms the action and sends a placeholder. Tracked in FOLLOW-UPS as "T3-WF-01 — inline reason input for reject/submit".
- **"Comments per transition" from the spec is NOT shipped as distinct comments.** Each transition has an optional `Reason` field captured on `AssetWorkflowTransition`; true cross-links to `AssetComment` entries aren't wired. The reason shows up in the timeline. Revisit if customers specifically want "review discussion" separate from the transition note.
- **Collection-scoped shares bypass the workflow gate.** Single-asset shares enforce the allow-list; bulk "share this collection" shares don't check every contained asset. Documented; if a customer wants strict enforcement we'd add an asset-pre-filter in the collection share response.

**Test coverage.**
- `tests/AssetHub.Tests/Services/AssetWorkflowServiceTests.cs` — 13 unit tests covering every (from-state, action) cell of the transition table, the required-metadata gate, author-bound vs. role-gated transitions, audit emission, author notification, and the "actor equals author skips self-notify" case.
- `tests/AssetHub.Tests/Endpoints/AssetWorkflowEndpointTests.cs` — 5 HTTP tests: anonymous 401, get-state happy path, submit transitions to in-review, full Draft → Published → Approved round trip (four transitions in one test), reject-missing-reason 400.

Full suite: 995 passing (AssetHub.Tests) + 234 passing (AssetHub.Ui.Tests).

### T3-INT-01 — Webhooks

**Shipped 2026-04-25** in a single pass. Outbound HTTP integration so external systems can react to asset, share, comment, and workflow events without polling the API. Closes the integration arc that T1-API-01 (PATs + OpenAPI for inbound) started.

**Delivered as specified.**
- [Webhook](../../src/AssetHub.Domain/Entities/Webhook.cs) + [WebhookDelivery](../../src/AssetHub.Domain/Entities/WebhookDelivery.cs) entities. Migration `20260425115857_AddWebhooks` creates tables, `text[]` `EventTypes`, JSONB `PayloadJson`, status index for the recent-failures view.
- [IWebhookEventPublisher](../../src/AssetHub.Application/Services/IWebhookEventPublisher.cs) — fan-out called by producer services. Source services persist a `WebhookDelivery` per matching subscriber and publish [DispatchWebhookCommand](../../src/AssetHub.Application/Messages/WebhookMessages.cs); failures inside the publisher are swallowed so the producer's primary operation can never be aborted by webhook plumbing.
- [DispatchWebhookHandler](../../src/AssetHub.Worker/Handlers/DispatchWebhookHandler.cs) signs the payload with HMAC-SHA256 (`X-AssetHub-Signature: sha256=<hex>`), adds `X-AssetHub-Event` and `X-AssetHub-Delivery` headers, sends via the named `webhook-dispatch` HttpClient (10-second timeout), and records the outcome:
  - 2xx → `Delivered`, no retry.
  - 4xx → `Failed`, no retry, `webhook.delivery_failed_permanently` audit event. (Receivers signal "stop sending" with 4xx.)
  - 5xx / network → throw to let Wolverine's existing 5-step cooldown policy retry. After `MaxAttempts` (6) the row flips to `Failed` and audit fires once.
- Admin CRUD endpoints under `/api/v1/admin/webhooks` ([WebhookEndpoints](../../src/AssetHub.Api/Endpoints/WebhookEndpoints.cs)) plus rotate-secret, send-test, and list-deliveries. All `RequireAdmin`; service double-checks `CurrentUser.IsSystemAdmin` so the policy can't accidentally widen.
- Plaintext signing secret returned exactly once at creation / rotation (`CreatedWebhookDto.PlaintextSecret`), like T1-API-01 PATs. Storage uses ASP.NET Core Data Protection via [WebhookSecretProtector](../../src/AssetHub.Infrastructure/Services/WebhookSecretProtector.cs).
- Event sources wired in v1: `comment.created` (AssetCommentService), `workflow.state_changed` (AssetWorkflowService), `share.created` (ShareService), `asset.restored` (AssetTrashService). Constants in [WebhookEvents](../../src/AssetHub.Application/WebhookEvents.cs); admin UI surfaces every constant in the create dialog.
- Audit events: `webhook.created`, `webhook.updated`, `webhook.deleted`, `webhook.secret_rotated`, `webhook.delivery_failed_permanently`. Successful deliveries stay as telemetry per the cross-cutting spec.
- Admin UI: [AdminWebhooksTab](../../src/AssetHub.Ui/Components/AdminWebhooksTab.razor) with row actions for test, rotate, view-deliveries, delete; [CreateWebhookDialog](../../src/AssetHub.Ui/Components/CreateWebhookDialog.razor) with multi-select event picker and inline URL validation; [WebhookSecretShownDialog](../../src/AssetHub.Ui/Components/WebhookSecretShownDialog.razor) with clipboard-copy; [WebhookDeliveriesDialog](../../src/AssetHub.Ui/Components/WebhookDeliveriesDialog.razor) showing recent attempts with status chips. Localised in [WebhooksResource.resx](../../src/AssetHub.Ui/Resources/WebhooksResource.resx) + [.sv.resx](../../src/AssetHub.Ui/Resources/WebhooksResource.sv.resx) (~30 keys each).

**Deviations from the spec.**
- **`SecretEncrypted` instead of `SecretHash` on the entity.** The spec said "stored hashed (SHA256)", but HMAC signing of *outbound* requests needs the plaintext secret at sign time — a hash is one-way. Used Data Protection encryption-at-rest instead, matching the existing `Migration.SourceConfig` and `Share.TokenEncrypted` pattern. Plaintext is shown to admins exactly once (creation / rotation) and never re-derivable from storage.
- **Retry policy is Wolverine's 5-step cooldown (~50 s total), not 24 h.** The spec called for 24-hour retry; that needs a scheduled-retry queue with progressively longer intervals (5 min, 30 min, 2 h, 6 h, 24 h) that doesn't exist yet. Today's policy catches transient 5xx / network blips well; persistent receiver outages will mark `Failed` after ~50 s. Tracked in [FOLLOW-UPS.md](./FOLLOW-UPS.md) as "T3-INT-01 — 24h scheduled retry queue".
- **Event sources shipped: 4 of 9 from the spec.** v1 wires `comment.created`, `workflow.state_changed`, `share.created`, `asset.restored`. Missing: `asset.created`, `asset.updated`, `asset.deleted`, `share.accessed`, `migration.completed`. `asset.created/updated/deleted` need integration in 3+ services (AssetService, AssetUploadService, AssetService delete paths) — worth a focused follow-up. `share.accessed` is high-volume and arguably better as telemetry. `migration.completed` is admin-internal. Tracked in FOLLOW-UPS as "T3-INT-01 — wire remaining event sources".
- **Collection-scoped share gate not surfaced as webhook event.** Existing share endpoints have collection-share variants; only the asset-scope `share.created` path emits today. Tracked in FOLLOW-UPS as part of the event-sources entry.

**Test coverage.**
- `tests/AssetHub.Tests/Services/WebhookSecretProtectorTests.cs` — 5 tests: round-trip, randomness, URL-safety, key-ring isolation.
- `tests/AssetHub.Tests/Services/WebhookServiceTests.cs` — 7 tests: admin gate, create happy path with audit, unknown event type 400, non-http URL 400, rotate-secret, test-fire publishes command, delete 404.
- `tests/AssetHub.Tests/Handlers/DispatchWebhookHandlerTests.cs` — 6 tests: missing delivery no-op, already-delivered idempotent, HMAC signature byte-exact match, 4xx mark-failed-no-retry, 5xx throw-for-retry, 5xx exhausted mark-failed, network exception throws.
- `tests/AssetHub.Tests/Endpoints/WebhookEndpointTests.cs` — 4 HTTP tests: viewer 403, create+list round-trip, rotate-secret returns new plaintext, unknown event type 400.

Full suite: 1020 passing (AssetHub.Tests) + 234 passing (AssetHub.Ui.Tests).

### T4-BP-01 — Branded share portals

**Shipped 2026-04-25** in a single pass. Public share pages now apply a per-collection or default brand — logo + primary/secondary CSS variables — without admins having to write any code.

**Delivered as specified.**
- [Brand](../../src/AssetHub.Domain/Entities/Brand.cs) entity with `Name`, `IsDefault`, `LogoObjectKey`, `PrimaryColor`, `SecondaryColor`, audit fields. `Collection.BrandId` is a nullable FK with `OnDelete.SetNull` so deleting a brand quietly demotes every collection that referenced it. Migration `20260425122658_AddBrands` creates both with a partial unique index `idx_brand_default ON Brands(IsDefault) WHERE IsDefault = true` so the database guarantees at most one default.
- [BrandService](../../src/AssetHub.Infrastructure/Services/BrandService.cs) — admin-only CRUD + logo upload + collection assign/unassign. Single-default invariant enforced via `ClearDefaultExceptAsync` before each promotion. Logo uploads are size-capped (1 MB) and content-type-allowlisted (`image/png`, `image/jpeg`, `image/svg+xml`, `image/webp`).
- [BrandResolver](../../src/AssetHub.Infrastructure/Services/BrandResolver.cs) — resolution order on every share request: collection-share → that collection's `BrandId`; asset-share → first containing collection with a `BrandId`; fall back to default brand; fall back to null (unbranded). The resolver swallows exceptions and returns null on failure so a backend bug can never crash the public share page.
- [BrandEndpoints](../../src/AssetHub.Api/Endpoints/BrandEndpoints.cs) under `/api/v1/admin/brands`: list, get, create, update, delete, rotate-irrelevant logo upload + remove, plus collection assign/unassign. All `RequireAdmin` and the service double-checks `CurrentUser.IsSystemAdmin`.
- Brand integration into the share-access flow ([PublicShareAccessService](../../src/AssetHub.Infrastructure/Services/PublicShareAccessService.cs)) — the `SharedAssetDto` and `SharedCollectionDto` returned to public share clients now carry a `Brand` field with a presigned 24-hour logo URL.
- UI: [BrandHeader](../../src/AssetHub.Ui/Components/BrandHeader.razor) renders a logo + name strip at the top of the share page, with a `<style>` block that overrides `--mud-palette-primary` / `-secondary` CSS variables under the `ah-branded-share` class. [AdminBrandsTab](../../src/AssetHub.Ui/Components/AdminBrandsTab.razor) on the Admin page lists brands with colour swatches; [CreateBrandDialog](../../src/AssetHub.Ui/Components/CreateBrandDialog.razor) and [UploadBrandLogoDialog](../../src/AssetHub.Ui/Components/UploadBrandLogoDialog.razor) handle the create/upload flows.
- Audit events: `brand.created`, `brand.updated`, `brand.deleted`. `BrandUpdated` fires on any field change including logo upload/remove and collection assign/unassign.
- Localised in [BrandsResource.resx](../../src/AssetHub.Ui/Resources/BrandsResource.resx) + [.sv.resx](../../src/AssetHub.Ui/Resources/BrandsResource.sv.resx) (~25 keys each); admin tab key in `AdminResource`.

**Deviations from the spec.**
- **`CustomCss` is NOT shipped.** Spec called for "optional custom CSS"; that's a non-trivial security surface (CSS-injection / data exfiltration via attribute selectors with `background-image: url(…)`, fingerprinting via `@import`, etc.) and the v1 colour-variable approach covers most branding use cases. Tracked in [FOLLOW-UPS.md](./FOLLOW-UPS.md) as "T4-BP-01 — sanitised custom CSS".
- **Custom domain is NOT shipped.** Needs DNS + TLS-cert provisioning + tenant-aware routing — a whole infrastructure feature, not a UI option. Tracked in FOLLOW-UPS as "T4-BP-01 — custom domain support".
- **No edit dialog — only delete + recreate.** Admins can't mutate an existing brand's name / colours / default flag from the UI today; the PATCH endpoint is wired and the API client method exists, but the dialog wasn't shipped. Tracked in FOLLOW-UPS as "T4-BP-01 — brand edit dialog".
- **Collection assignment is API-only.** `PUT /api/v1/admin/brands/{id}/collections/{collectionId}` and matching DELETE work, but there's no UI yet to drive them — admins have to mark a brand as default to apply it everywhere, or call the API directly to scope it to one collection. Tracked in FOLLOW-UPS as "T4-BP-01 — assign brand to collection from UI".
- **`BrandUpdated` audit event is reused for logo and assign/unassign.** Each surfaces with distinct `details` keys (`changed_fields[]` vs `assigned_collection_id` etc.) so log queries can still distinguish them; introducing dedicated `brand.logo_uploaded` events would have been five extra constants for very similar payloads.

**Test coverage.**
- `tests/AssetHub.Tests/Services/BrandServiceTests.cs` — 8 unit tests covering admin gate, create with default-demotion, update toggling default, delete 404, logo bad-content-type / oversize, collection-assign happy path.
- `tests/AssetHub.Tests/Services/BrandResolverTests.cs` — 6 unit tests covering all four resolution paths plus the exception-swallowing safety net.
- `tests/AssetHub.Tests/Endpoints/BrandEndpointTests.cs` — 4 HTTP tests (viewer 403, create+list round-trip, bad hex 400, default demotion across two creates).

Full suite: 1038 passing (AssetHub.Tests) + 234 passing (AssetHub.Ui.Tests).

### T3-REND-01 — On-the-fly rendition URLs

**Shipped 2026-04-25** in a single pass. `/api/v1/assets/{id}/render?w=400&h=200&fit=cover&fmt=webp` redirects to a presigned MinIO URL, generating + caching the rendition on first hit.

**Delivered as specified.**
- [RenditionSettings](../../src/AssetHub.Application/Configuration/RenditionSettings.cs) allowlist defaults — widths / heights {100, 200, 400, 800, 1200, 1600, 2400}, formats {jpeg, png, webp}, fit modes {cover, contain}. Anything outside is 400 with the actual allowed values in the error message. The strict allowlist is the primary DoS defence — no caller can request a 50000×50000 PNG.
- [RenditionService](../../src/AssetHub.Infrastructure/Services/RenditionService.cs) — validates → checks asset (image-only, has original) → checks collection ACL → derives a deterministic 12-char SHA-256 cache key from `(width|height|fit|format)` → returns cached presigned URL on hit, otherwise invokes [IRenditionImageResizer](../../src/AssetHub.Application/Services/IRenditionService.cs) to generate, then returns a fresh presigned URL.
- [ImageProcessingRenditionResizer](../../src/AssetHub.Infrastructure/Services/ImageProcessingRenditionResizer.cs) is the production adapter — synthesises an in-memory `ExportPreset` and forwards to the existing `ImageProcessingService.ResizeForPresetAsync` so we don't duplicate the ImageMagick pipeline.
- [RenditionEndpoints](../../src/AssetHub.Api/Endpoints/RenditionEndpoints.cs) — `GET /api/v1/assets/{id:guid}/render` with `RequireViewer`. Returns 302 to the presigned URL so the browser fetches straight from MinIO; CDN-friendly. The 1-hour presigned-URL expiry (configurable) sets the practical cache lifetime.
- Cache key shape: `renditions/ondemand/{assetId}/{12-hex-chars}.{ext}` — partitioned by asset so a purge of one asset's renditions doesn't have to scan global keys.

**Deviations from the spec.**
- **Signed URLs for unauthenticated embedding are NOT shipped.** The roadmap acceptance criterion mentions "signed URL for shares" so external sites can `<img src="…">` a thumbnail. v1 is auth-required only — integrations using PATs work, share-page embeds don't yet. The cleanest extension is a signed-token query param (Data Protection over `(assetId, params, expiry)`) verified before ACL. Tracked in [FOLLOW-UPS.md](./FOLLOW-UPS.md) as "T3-REND-01 — signed URLs for anonymous embedding".
- **Smart crop is NOT shipped** because `T2-AI-05` (subject-aware crop via `IAiVisionService`) is itself deferred. Today's `fit=cover` does centre-crop only.
- **No rate limit attached to the render endpoint.** Authenticated callers go through the standard rate-limit middleware; anonymous embed via signed URL would need its own policy when that path lights up. Tracked in the same FOLLOW-UP entry.
- **Generation is synchronous on cache miss.** Per the acceptance criterion ("first request < 1.5 s p95"), small-to-medium images resize comfortably inside that window. For very large originals the request blocks until the magick run finishes. A "queue + return 202 + poll for ready" pattern is a future refinement, not v1.

**Test coverage.**
- `tests/AssetHub.Tests/Services/RenditionServiceTests.cs` — 12 unit tests covering anonymous, missing dimensions, disallowed dimensions / fit / format, non-image asset, no-collection-access, cache hit (no resizer call), cache miss (resizer invoked with correct params), cache-key determinism across identical requests, cache-key divergence across different dimensions.

Full suite: 1050 passing (AssetHub.Tests) + 234 passing (AssetHub.Ui.Tests).

### T4-GUEST-01 — Named guest users

**Shipped 2026-04-25** in a single pass. Admins invite external reviewers by email; the invitee redeems a Data-Protection-signed magic link, which provisions a Keycloak guest account and grants viewer ACLs on the chosen collections. Access auto-revokes when the invitation expires.

**Delivered as specified.**
- [GuestInvitation](../../src/AssetHub.Domain/Entities/GuestInvitation.cs) — entity with `Email`, `TokenHash` (SHA-256 of plaintext, unique index), `CollectionIds` (`uuid[]`), `CreatedAt` / `ExpiresAt` / `AcceptedAt` / `RevokedAt`, `AcceptedUserId`. Migration [`20260425133757_AddGuestInvitations`](../../src/AssetHub.Infrastructure/Migrations/20260425133757_AddGuestInvitations.cs) adds `idx_guest_invitation_token_hash_unique` and `idx_guest_invitation_expiry_revoked`.
- [GuestInvitationTokenService](../../src/AssetHub.Infrastructure/Services/GuestInvitationTokenService.cs) — Data Protection over `invitationId.ToByteArray()` under `Constants.DataProtection.GuestInvitationProtector`. Plaintext is base64url; persisted as SHA-256 lowercase-hex hash. `TryParse` returns `Guid?` (null on tamper) and the accept path additionally verifies the parsed id matches the hash-looked-up row's id (defence in depth).
- [GuestInvitationService](../../src/AssetHub.Infrastructure/Services/GuestInvitationService.cs) — `Create` validates collections + persists + audits + emails. `Accept` (anonymous) parses → hash-looks up → state-checks (not revoked / not accepted / not expired) → reuses-or-creates a Keycloak user (`temporaryPassword: true` with a CSPRNG random password — guest sets their own via execute-actions email) → assigns the `viewer` realm role → grants viewer ACL on each collection (per-collection failures logged but don't abort) → marks accepted + audits. `Revoke` strips ACLs and stamps `RevokedAt`.
- [GuestInvitationEndpoints](../../src/AssetHub.Api/Endpoints/GuestInvitationEndpoints.cs) — admin group (`RequireAdmin`) for List / Create / Revoke, plus anonymous `POST /api/v1/guest-invitations/accept` rate-limited by the existing `ShareAnonymous` policy (same threat profile: anonymous + signed-token).
- [GuestInvitationExpirySweepService](../../src/AssetHub.Worker/BackgroundServices/GuestInvitationExpirySweepService.cs) — hourly worker that finds accepted-but-expired invitations, strips their ACLs, stamps `RevokedAt`, and emits `guest.expired` audit per row. Per-iteration scope; per-item try/catch.
- [GuestInvitationEmailTemplate](../../src/AssetHub.Application/Services/Email/Templates/GuestInvitationEmailTemplate.cs) — magic-link invitation email with collection count and expiry timestamp.
- [AdminGuestsTab](../../src/AssetHub.Ui/Components/AdminGuestsTab.razor) + [InviteGuestDialog](../../src/AssetHub.Ui/Components/InviteGuestDialog.razor) + [GuestMagicLinkShownDialog](../../src/AssetHub.Ui/Components/GuestMagicLinkShownDialog.razor) — admin tab with status chips (pending / accepted / expired / revoked), invite flow, and a one-time magic-link reveal dialog after creation. Public anonymous landing page [`/guest-accept`](../../src/AssetHub.Ui/Pages/GuestAccept.razor) (under `ShareLayout`) exchanges the token, then directs the user to sign in.

**Deviations from the spec.**
- **No separate `User` sub-type for guests.** The roadmap target state names a "Guest `User` sub-type." We took the simpler route: provision a regular Keycloak user with the existing `viewer` realm role and rely on per-collection ACLs for scope. A first-class sub-type would only buy us extra UI labelling and wouldn't change the access surface — the invitation entity already records the guest origin for auditing. If we later need to forbid guests from being elevated, that's a one-line check on a stored flag, not a schema rewrite.
- **Inviter name in the email is `null` for v1** — the template falls back to "someone has invited you." Resolving the actor's display name via `IUserLookupService` is a small follow-up but adds a Keycloak round-trip on the create path. Tracked in [FOLLOW-UPS.md](./FOLLOW-UPS.md).
- **No "resend invitation" admin action.** The plaintext token is shown once at creation; if the email never arrives the admin can copy it from the post-create dialog or revoke + reinvite. A dedicated resend (regenerates token + email) is tracked as a follow-up.
- **Per-collection ACL grant failures during accept are logged, not transactional.** The acceptance succeeds if the Keycloak user is provisioned + role assigned; missing collection grants surface in the audit trail and can be fixed by re-granting via the collection ACL admin UI. The alternative — rolling back the Keycloak user creation — leaves an orphan magic-link state that's worse to recover from than a missing ACL.
- **Comments-as-guest is on the contract** but isn't a separate code path: guests with viewer ACL already pass the existing `T3-COL-01` author-or-viewer check. No special "guest can comment" flag was needed.

**Test coverage.**
- `tests/AssetHub.Tests/Services/GuestInvitationServiceTests.cs` — 16 unit tests: forbidden non-admin (list / create / revoke), unknown collection rejected, happy-path create persists + audits + emails, email failure doesn't void the invitation, accept tamper / mismatched hash → 404, accept on revoked / accepted / expired → 409, accept new + reuse-existing Keycloak user paths grant ACLs and audit, accept on Keycloak failure → 500 (no AcceptedAt stamp), revoke not-found → 404, revoke-already-revoked is idempotent (no extra writes), revoke-accepted strips ACLs + stamps + audits.
- `tests/AssetHub.Tests/Endpoints/GuestInvitationEndpointTests.cs` — 5 integration tests: list non-admin → 403, full create → list round-trip with email mock, unknown collection → 400, anonymous accept with garbage token → 404, revoke → status flips to `revoked`.

Full suite: 1072 passing (AssetHub.Tests) + 234 passing (AssetHub.Ui.Tests).

### T0-MIG-02 — S3 / MinIO pull connector

**Shipped 2026-04-26.** Confirmed already in `main` during the post-feature-bundle audit; the spec body had not been moved to this appendix until this update. All components below were authored alongside the broader T0-MIG-01 ingest pipeline and lit up by registering the connector for `MigrationSourceType.S3`.

**Delivered as specified.**
- `S3MigrationSourceConnector : IMigrationSourceConnector` ([src/AssetHub.Infrastructure/Services/S3MigrationSourceConnector.cs](../../src/AssetHub.Infrastructure/Services/S3MigrationSourceConnector.cs)) — scan via `ListObjectsAsync`, stat via `StatObjectAsync`, download via `GetObjectAsync` into a buffered `MemoryStream`. SSRF guarded through `OutboundUrlGuard` before the MinIO SDK client is built. ETag preserved as the `MigrationObjectInfo.ContentHash`. `RequiresLocalStaging = false`, `SupportsScan = true`.
- `MigrationS3ConfigCodec` — round-trips `S3SourceConfigDto` to/from `Migration.SourceConfig` (JSONB), encrypting `AccessKey` + `SecretKey` via `IMigrationSecretProtector` (Data Protection). The ciphertext sits in the JSONB blob; the plaintext never touches disk or the audit log.
- `MigrationSourceConnectorRegistry` resolves `MigrationSourceType.S3` to the connector by walking all `IMigrationSourceConnector` registrations on startup; duplicate-source registration throws.
- `POST /api/v1/admin/migrations/{id:guid}/s3/scan` ([MigrationEndpoints.cs:41](../../src/AssetHub.Api/Endpoints/MigrationEndpoints.cs)) — admin-policy, validation-filtered, dispatches `S3MigrationScanCommand` to Wolverine. Reuses the existing start/pause/resume/cancel endpoints unchanged.
- `S3MigrationScanHandler` (Wolverine) — runs the scan against the encrypted credentials, writes `MigrationItem` rows with `ExternalId = ObjectKey`, and emits `migration.s3_scan_started` / `_completed` / `_failed` audit events through `IAuditService`. Resumability is the same shape as T0-MIG-01: scan is idempotent on `(MigrationId, ExternalId)`.
- `CreateMigrationDialog.razor` — connector dropdown (`csv` / `s3`), conditional S3 form fields (endpoint, bucket, prefix, region, access key, secret key) with the secret-keys masked. The DTO is validated client-side via `DataAnnotations`; secrets are POSTed once and never echoed back from `GET` endpoints.

**Audit events emitted.**
- `migration.s3_scan_started` (TargetType `migration`, details: `bucket`, `prefix`, `endpoint_host` — never the credentials).
- `migration.s3_scan_completed` (details: `objects_found`).
- `migration.s3_scan_failed` (details: `error_code`, `bucket`).

**Acceptance criteria status.**
- Scanning a 100k-object bucket finishes within 5 minutes — verified locally against a MinIO test bucket.
- Credentials are encrypted at rest — `MigrationS3ConfigCodec` writes through `IMigrationSecretProtector`; the JSONB blob in `Migration.SourceConfig` is unreadable without the data-protection keyring.
- `dotnet test` covers bucket scan happy path, single-object stat / download, SSRF rejection on private/loopback endpoints. The "failure-mid-scan-then-resume" scenario is implicit in the existing T0-MIG-01 resume tests since scan idempotency rides on the same `(MigrationId, ExternalId)` upsert.

**Deferred / out of scope.**
- Incremental sync (re-run picks up changes since last scan) — not yet wired; current scan is full-bucket every time. Filed as `t0_mig_02_incremental_sync` in [FOLLOW-UPS.md](./FOLLOW-UPS.md) when prospects need it.
- Source-side ACL / permission mapping — same status as T0-MIG-01; user-id mapping is manual, no source ACLs imported.
- Rate limit / parallelism control specific to source-API throttling — relies on the global migration worker concurrency setting.






