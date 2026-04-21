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

**Intent.** Many enterprises have asset libraries on S3-compatible storage. A pull connector reads an S3 bucket directly instead of forcing users to download-then-upload.

**User gain.** Admin enters bucket name, prefix, access key, secret. Migration lists objects and migrates them server-side; no local bandwidth required.

**Business gain.** Lowers friction for any prospect using AWS S3, Backblaze B2, Wasabi, Cloudflare R2, or MinIO.

**Data model.** No new tables. `Migration.SourceConfig` schema for `S3`:
```json
{ "endpoint": "https://s3.eu-west-1.amazonaws.com", "bucket": "my-dam", "prefix": "images/", "accessKey": "...", "secretKey": "...", "region": "eu-west-1" }
```
Secrets persisted with ASP.NET Core Data Protection (see existing encryption of `Share.TokenEncrypted`).

**API surface.**
- `POST /api/v1/admin/migrations/{id:guid}/s3/scan` — enumerates objects, creates `MigrationItem` rows with `ExternalId = ObjectKey`.
- Reuses existing start/pause/resume/cancel endpoints.

**Worker.** New handler `S3MigrationScanHandler` uses the existing `MinIOAdapter` (already S3-compatible) with per-migration credentials.

**UI.** New connector form in `CreateMigrationDialog` with fields per the `SourceConfig` schema; secrets masked on display.

**Acceptance criteria.**
- Scanning a 100k-object bucket finishes within 5 minutes and populates `MigrationItem` rows.
- Credentials are encrypted at rest.
- `dotnet test` covers bucket scan, single-object ingest, and a failure-mid-scan-then-resume scenario.

**Dependencies.** T0-MIG-01.

---

### T0-MIG-03 through T0-MIG-05 — Bynder / Canto / SharePoint connectors

Same pattern as T0-MIG-02, one per source. Each is a sealed service implementing `IMigrationSourceConnector` with methods `ScanAsync`, `FetchBytesAsync`, `FetchMetadataAsync`. Ship as prospects demand — don't build all three speculatively. Each one adds an enum value to `MigrationSourceType`, a `SourceConfig` schema, a connector class, and a preset in `FieldMappingEditor`.

---

## Tier 1 — Table stakes

### T1-META-01 — Custom metadata schemas and taxonomies

**Intent.** Let admins define structured metadata per asset (or per collection, or per asset-type) beyond the hard-coded Title/Description/Copyright/Tags. This is the biggest competitive gap vs. Bynder "metaproperties" / Brandfolder custom fields.

**User gain.** A marketing admin defines fields like *Campaign*, *Market*, *Usage Rights*, *Expiry Date*. Contributors fill them on upload; everyone searches by them; renditions can embed them.

**Business gain.** Unlocks faceted search (T1-SRCH-01), rights tracking (implicit part of compliance), and required-metadata gates before publish.

**Current state.** `Asset.MetadataJson` is a free-form dictionary with no schema, UI, or search integration.

**Target state.**
- Admins define a **MetadataSchema** with typed **MetadataField** entries.
- Contributors fill fields via a dynamic form on upload and asset-edit dialogs.
- Taxonomy fields reference a **Taxonomy** tree with **TaxonomyTerm** nodes.
- Search can filter by any indexed field.

**Data model.**

```csharp
public class MetadataSchema
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public MetadataSchemaScope Scope { get; set; }    // Global, AssetType, Collection
    public AssetType? AssetType { get; set; }         // null unless Scope == AssetType
    public Guid? CollectionId { get; set; }           // null unless Scope == Collection
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public ICollection<MetadataField> Fields { get; set; } = new List<MetadataField>();
}

public class MetadataField
{
    public Guid Id { get; set; }
    public Guid MetadataSchemaId { get; set; }
    public string Key { get; set; } = "";             // machine key, snake_case, unique per schema
    public string Label { get; set; } = "";           // display label, EN
    public string? LabelSv { get; set; }              // optional per-locale override; more languages → add columns or go JSONB
    public MetadataFieldType Type { get; set; }       // Text, LongText, Number, Decimal, Boolean, Date, DateTime, Select, MultiSelect, Taxonomy, Url
    public bool Required { get; set; }
    public bool Searchable { get; set; } = true;
    public bool Facetable { get; set; }
    public string? PatternRegex { get; set; }
    public int? MaxLength { get; set; }
    public decimal? NumericMin { get; set; }
    public decimal? NumericMax { get; set; }
    public List<string> SelectOptions { get; set; } = new();
    public Guid? TaxonomyId { get; set; }
    public int SortOrder { get; set; }
}

public class Taxonomy
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public ICollection<TaxonomyTerm> Terms { get; set; } = new List<TaxonomyTerm>();
}

public class TaxonomyTerm
{
    public Guid Id { get; set; }
    public Guid TaxonomyId { get; set; }
    public Guid? ParentTermId { get; set; }
    public string Label { get; set; } = "";
    public string? LabelSv { get; set; }
    public string Slug { get; set; } = "";            // used in URLs / facet filters
    public int SortOrder { get; set; }
    public ICollection<TaxonomyTerm> Children { get; set; } = new List<TaxonomyTerm>();
}
```

Extend `Asset`:
```csharp
// Already has Dictionary<string, object> MetadataJson — keep it for unstructured overflow.
// Add a typed storage table for searchable fields:
public class AssetMetadataValue
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Guid MetadataFieldId { get; set; }
    public string? ValueText { get; set; }
    public decimal? ValueNumeric { get; set; }
    public DateTime? ValueDate { get; set; }
    public Guid? ValueTaxonomyTermId { get; set; }
}
```

GIN indices (raw SQL in migration):
- `idx_asset_metadata_value_field_text_trgm` `USING gin (value_text gin_trgm_ops)` where value_text is not null.
- `idx_asset_metadata_value_field` on `(metadata_field_id, asset_id)`.
- `idx_asset_metadata_value_taxonomy_term` on `(value_taxonomy_term_id, asset_id) where value_taxonomy_term_id is not null`.

**API surface.** `/api/v1/admin/metadata-schemas`, `/api/v1/admin/taxonomies`, plus:
- `GET /api/v1/assets/{id}/metadata` — returns resolved schema + values.
- `PUT /api/v1/assets/{id}/metadata` — upsert values; validates against schema.
- `POST /api/v1/assets/bulk-metadata` — apply a metadata template to N assets.

**UI.** `AdminMetadataTab.razor`, `MetadataSchemaEditor.razor`, `TaxonomyEditor.razor`. Dynamic form component `AssetMetadataForm.razor` rendered inside `EditAssetDialog` and the upload flow.

**Acceptance criteria.**
- Admin creates a schema scoped to Image with 5 fields including a Taxonomy field.
- Uploading an Image shows the 5 fields in the upload dialog; Required fields block submit.
- Editing an asset round-trips values correctly for all field types.
- Search facets (T1-SRCH-01) read from `Facetable` fields.
- Deleting a schema with existing values is blocked unless `?force=true` (and then cascades).

**Dependencies.** None; it's a foundation for T1-SRCH-01, T0-MIG-01 field mapping extension, T1-VER-01 metadata versioning, T3-WF-01 required-to-publish rules.

**Out of scope.** Multi-language values beyond `LabelSv` (consider JSONB per-locale later if demand exceeds two locales).

---

### T1-LIFE-01 — Soft delete, trash, and scheduled purge

**Intent.** Replace hard delete with soft delete + recycle-bin with a TTL. Prevents accidental permanent loss, meets enterprise expectation, and aligns with ConfirmDialog UX.

**User gain.** Deleted assets move to Trash with a 30-day countdown; admins can restore. Bulk permanent delete still exists but goes through a second confirm with explicit count (already partially there in `BulkAssetActionsDialog`).

**Business gain.** Removes the "we got burned by a bad delete" objection; commercial DAMs all have this.

**Current state.** `Asset` has no `DeletedAt`; `AssetService.DeleteAsync` performs hard delete. CLAUDE.md says *"No soft delete — use status-based lifecycle or hard delete"* — this item **amends that rule** for `Asset` and `Collection`.

**Target state.**
- `Asset.DeletedAt` (nullable UTC) and `Asset.DeletedByUserId` added.
- All queries filter `DeletedAt IS NULL` by default (EF Core global query filter).
- Delete endpoints set `DeletedAt = now` instead of removing the row.
- **Trash** page (`/admin/trash`) lists deleted assets with restore / permanent-delete actions.
- Background worker purges assets older than configured TTL (default 30 days).

**Data model.** Add columns; update `OnModelCreating` with `.HasQueryFilter(a => a.DeletedAt == null)`. Add `AssetLifecycleSettings` config class with `const string SectionName = "AssetLifecycle"`, `TrashRetentionDays = 30`, `ValidateOnStart = false`.

**API surface.**
- `GET /api/v1/admin/trash` — lists soft-deleted assets (`IgnoreQueryFilters` in the repo).
- `POST /api/v1/admin/trash/{id:guid}/restore` — clears `DeletedAt`.
- `DELETE /api/v1/admin/trash/{id:guid}` — permanent delete (real row + MinIO cleanup).
- `POST /api/v1/admin/trash/empty` — permanent delete all trashed items (second confirm with count).

**Worker.** New `TrashPurgeBackgroundService` (hosted, per CLAUDE.md's BackgroundService pattern): every hour, finds assets with `DeletedAt < now - TrashRetentionDays`, deletes them and their MinIO objects.

**UI.** `AdminTrashTab.razor`. Delete flow in `AssetDetail`, `AssetCardGrid`, `BulkAssetActionsDialog` now shows optimistic removal + a **Snackbar with Undo** that calls restore within 10 s.

**Caching.** Add `CacheKeys.Tags.AssetList` invalidation when an asset is soft-deleted or restored (existing pattern already in [AssetService.cs](../../src/AssetHub.Infrastructure/Services/AssetService.cs)).

**Acceptance criteria.**
- Deleted asset disappears from listings but appears in Trash.
- Restore returns it to its original collections.
- Asset purged after TTL; MinIO objects deleted.
- Undo-toast works from both detail and grid.
- `AssetHub.Tests` covers the full loop including TTL purge (use shortened TTL via config).

**Dependencies.** None.

**Out of scope.** Soft delete for `Collection` — ship as T1-LIFE-02 separately.

---

### T1-DUP-01 — Duplicate detection on upload

**Intent.** `Asset.Sha256` is computed and stored but never consulted. Duplicates silently accumulate.

**User gain.** On upload, if the same file is already in AssetHub, the user sees *"This file is already in collection X as Y"* with a "Go to existing" button; import is blocked (or `?force=true` allowed for admins).

**Business gain.** Storage savings, cleaner libraries, table-stakes parity.

**Current state.** `AssetUploadService.UploadAsync` computes SHA256 during validation but doesn't query for duplicates.

**Target state.** Before persisting, run `IAssetRepository.FindBySha256Async(sha256)`; if present and not soft-deleted, return a `ServiceError.Conflict` with `Code = "DUPLICATE"` and a payload including existing asset id + title + collection names.

**API / DTO.** `ServiceError.Conflict(message, data: { existingAssetId, existingTitle, collections })`. Upload endpoint surfaces this as a 409 with the payload intact (extend `ServiceResult.ToHttpResult()` to preserve structured error data).

**UI.** `AssetUpload.razor` handles the 409 and shows a Duplicate card with "Go to existing" / "Upload anyway (admin only)". Also applies to `T0-MIG-01` migration item processing (skips with `Skipped_Duplicate`).

**Acceptance criteria.**
- Uploading the same file twice yields a clear duplicate message.
- Existing test `AssetUploadServiceTests` gains a `Upload_ExistingSha256_ReturnsConflict` case.
- Admin override via `?force=true` creates a second asset pointing to the same SHA256 (document this explicitly).

**Dependencies.** None.

---

### T1-SRCH-01 — Faceted search with full-text, OCR, and saved searches

**Intent.** Real DAM search: combine filters, search across title + description + tags + metadata + OCR (when T2-AI-03 ships) + transcripts (T2-AI-04), save the result, re-run later with notifications when new matches appear.

**User gain.** Contributors find assets in seconds instead of scrolling collections. Marketers save *"Brand-approved outdoor photos, EU market, last 90 days"* and re-run weekly.

**Business gain.** The feature prospects demo first after upload.

**Current state.** [AssetQueryService.cs](../../src/AssetHub.Infrastructure/Services/AssetQueryService.cs) uses a single `EF.Functions.ILike` on title with type + collection filters.

**Target state.**
- A new `AssetSearchService` accepts a structured `AssetSearchRequest` with text, facet filters, sort, pagination.
- Full-text uses Postgres tsvector + GIN index on title, description, tags, extracted OCR, metadata text values.
- Facets are aggregated in a second query (count per value for each facet dimension).
- `SavedSearch` entity stores request JSON + owner + optional notification cadence.

**Data model.**

Add a `search_vector` generated column to `Asset` (raw SQL migration):

```sql
ALTER TABLE "Assets" ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (
    setweight(to_tsvector('simple', coalesce("Title", '')), 'A') ||
    setweight(to_tsvector('simple', coalesce("Description", '')), 'B') ||
    setweight(to_tsvector('simple', array_to_string("Tags", ' ', '')), 'C')
) STORED;
CREATE INDEX idx_asset_search_vector ON "Assets" USING gin (search_vector);
```

`SavedSearch`:
```csharp
public class SavedSearch
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string OwnerUserId { get; set; } = "";
    public string RequestJson { get; set; } = "";   // AssetSearchRequest serialized
    public SavedSearchNotifyCadence Notify { get; set; } // None, OnNewMatch, Daily, Weekly
    public DateTime? LastRunAt { get; set; }
    public Guid? LastHighestSeenAssetId { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**API surface.**
- `POST /api/v1/assets/search` — body = `AssetSearchRequest`, returns `{ items, facets, totalCount }`.
- `/api/v1/saved-searches` — standard CRUD scoped to owner.

**UI.** New `SearchSidebar.razor` with faceted filter accordion. Chips showing active filters above the result grid. "Save this search" button. `SavedSearchesMenu.razor` in nav menu under Collections.

**Acceptance criteria.**
- 100k-asset library returns search results < 300 ms p95 with 3 facet filters active.
- Adding an OCR-indexed asset (T2-AI-03) is findable by OCR text within the same request cycle.
- Saved search with `Daily` cadence generates a digest email (T3-NTF-01) listing new matches since `LastHighestSeenAssetId`.

**Dependencies.** Benefits heavily from T1-META-01 (facetable fields) and T2-AI-03 (OCR content).

---

### T1-VER-01 — Asset versioning

**Intent.** Stop overwriting in `ReplaceImageFile`. Keep every version; allow restore.

**User gain.** Designers replace an asset; history shows v1, v2, v3 with thumbnails and "restore" buttons. Share links can pin to a version.

**Business gain.** Data safety; enterprise expectation; unblocks check-in/check-out (later).

**Current state.** [AssetUploadService.cs](../../src/AssetHub.Infrastructure/Services/AssetUploadService.cs) `ReplaceImageFileAsync` writes over the original MinIO object; `EditDocument` is the only "history" and it's single-value.

**Target state.**
- New `AssetVersion` entity stores prior bytes, metadata snapshot, and edit document snapshot.
- `Asset` gains `CurrentVersionNumber` + reverse nav to versions.
- Replace + image-save-copy create a new version; restore marks a prior version as current.

**Data model.**
```csharp
public class AssetVersion
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Asset Asset { get; set; } = default!;
    public int VersionNumber { get; set; }           // 1-based, unique per AssetId
    public string OriginalObjectKey { get; set; } = "";
    public string? ThumbObjectKey { get; set; }
    public string? MediumObjectKey { get; set; }
    public string? PosterObjectKey { get; set; }
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string? EditDocument { get; set; }
    public Dictionary<string, object> MetadataSnapshot { get; set; } = new();
    public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? ChangeNote { get; set; }
}
```
Index: `idx_asset_version_asset_version_unique` UNIQUE on `(AssetId, VersionNumber)`.

`Asset` gains `public int CurrentVersionNumber { get; set; } = 1;`.

**API.**
- `GET /api/v1/assets/{id}/versions` — list versions.
- `POST /api/v1/assets/{id}/versions/{n:int}/restore` — mark vN as current.
- `DELETE /api/v1/assets/{id}/versions/{n:int}` — prune (admin only).

**Worker.** Reuse `ProcessImageHandler` — when a new version is uploaded, process it like a fresh asset but write renditions to `versions/{versionNumber}/...` keys.

**UI.** `AssetVersionHistoryPanel.razor` inside `AssetDetail.razor`, under Derivatives.

**Acceptance criteria.**
- Replace-file creates v2; original stays accessible in the history panel.
- Restore of v1 flips current version; thumbnails update.
- Storage growth is linear in versions; admins can prune.

**Dependencies.** None — but T1-LIFE-01 soft-delete should cover version rows too.

**Out of scope.** Check-in / check-out locking (see T3-COL-03 later).

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

**Intent.** Asset-level discussion threads. Review feedback without leaving the DAM.

**Data model.**
```csharp
public class AssetComment
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public string AuthorUserId { get; set; } = "";
    public string Body { get; set; } = "";           // markdown, sanitized
    public List<string> MentionedUserIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public Guid? ParentCommentId { get; set; }       // threading; null for top-level
}
```

**API.** Standard CRUD under `/api/v1/assets/{id}/comments`.

**UI.** `AssetCommentsPanel.razor` in `AssetDetail`. `MudAutocomplete` for @mention suggestions.

**Notifications.** Mentions trigger T3-NTF-01 events.

**Acceptance criteria.** Mentioning a user delivers an in-app and email notification within 60 s.

---

### T3-WF-01 — Approval workflow (states)

**Intent.** Draft → In Review → Approved → Published, with comments per transition.

**Data model.** Add `Asset.WorkflowState` enum and `AssetWorkflowTransition` audit rows.

**Gates.** *Required-metadata* fields (T1-META-01 `Required=true`) must be filled before "Submit for review"; reviewers approve or reject with a reason.

**UI.** State badge on asset detail + grid; workflow actions in a `WorkflowPanel.razor`.

**Acceptance criteria.** Assets cannot be shared externally unless state is `Approved` or `Published` (configurable per share policy).

---

### T3-REND-01 — On-the-fly rendition URLs

**Intent.** `/api/v1/assets/{id}/render?w=400&h=300&fit=cover&fmt=webp&crop=smart` returns (or redirects to) an image rendered on demand, cached.

**Target state.** New endpoint validates params against a sane allowlist (prevent DoS-by-massive-resize). Cached result is stored under `renditions/ondemand/{hash}.{ext}` in MinIO. Signed URLs for embedding.

**API.** Public endpoint with rate limit; auth required for private assets, signed URL for shares.

**Acceptance criteria.** First request produces in < 1.5 s (p95); subsequent requests for the same URL serve from MinIO in < 150 ms.

---

### T3-INT-01 — Webhooks

**Intent.** Outbound HTTP on events: `asset.created`, `asset.updated`, `asset.deleted`, `asset.restored`, `share.created`, `share.accessed`, `migration.completed`, `workflow.state_changed`, `comment.created`.

**Data model.**
```csharp
public class Webhook
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string SecretHash { get; set; } = "";     // HMAC signing key, SHA256 hashed
    public List<string> EventTypes { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = "";
}
public class WebhookDelivery
{
    public Guid Id { get; set; }
    public Guid WebhookId { get; set; }
    public string EventType { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public int? ResponseStatus { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}
```

**Worker.** `WebhookDispatchHandler` retries with exponential backoff; signs payload with HMAC-SHA256 `X-AssetHub-Signature: sha256=...`.

**UI.** Admin → Integrations → Webhooks.

**Acceptance criteria.** Webhook delivery has at-least-once semantics; failures are retried up to 24 h; dashboard shows failing endpoints.

---

### T3-NTF-01 — Notifications (in-app + email)

**Intent.** Unified notification pipeline: mentions, approvals, workflow transitions, saved-search digests, share activity.

**Target state.** `Notification` entity + per-user preferences; in-app bell icon with unread count; digest emails via `SmtpEmailService`.

**Acceptance criteria.** Users control per-category preferences; instant vs digest; email unsubscribe honoured.

---

## Tier 4 — Brand portal play

### T4-BP-01 — Branded share portals

**Intent.** Custom-branded share pages with logo, colours, optional custom domain.

**Target state.** `Brand` entity (per-collection override + global default) with logo object key, primary/secondary colors, optional custom CSS. `ShareLayout` reads the brand from the share context.

**Acceptance criteria.** Share URL renders with the owning collection's brand; default brand applied otherwise.

---

### T4-GUEST-01 — Named guest users

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

**Intent.** Let collections have a parent.

**Risks.** ACL implications: does a child inherit from its parent? CLAUDE.md currently treats collections as flat; document the inheritance model explicitly before implementing.

---

### T5-WMK-01 — Watermarking on download

**Intent.** Optional dynamic watermark for specific shares or collection-scoped downloads.

---

### T5-ANL-01 — Analytics (most-downloaded, stale assets, storage reports)

**Intent.** Reporting dashboards; export to CSV / PDF.

---

### T5-AUD-01 — Audio support

**Intent.** Wire audio through properly: `AssetType.Audio`, preview with waveform, optional transcription (T2-AI).

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
| T5-AUD-01 | `audit.retention_purged` | `audit` | `purged_count`, `cutoff_date`. **Meta-audit** — the retention worker is itself auditable. |

**Retention policy for audit data.**

This is a gap today — audit events are retained forever. When **T5-AUD-01** lands it must ship with:

- `AuditRetentionSettings { SectionName = "AuditRetention"; DefaultRetentionDays = 730; }` (two years — align with typical SOC2 expectations).
- Per-event-type overrides where regulation demands longer or shorter retention (e.g. `share.accessed` can be 90 days; `acl.revoked` must be the maximum).
- A `AuditRetentionBackgroundService` (hosted, scope-per-iteration) that batch-deletes expired rows.
- A `audit.retention_purged` event recording the purge itself — never purge without a record of the purge.

Until T5-AUD-01 lands, every feature adding a high-volume event type must justify the volume in its PR description or downgrade to telemetry.

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

