# AssetHub vs Commercial DAMs — Gap Analysis

**Date:** 2026-04-18
**Scope:** Compare AssetHub feature set with commercial digital asset management systems to identify what is missing to be genuinely useful as a replacement.

**Benchmark set:** Bynder, Canto, Frontify, Brandfolder, Widen (Acquia), MediaValet, Aprimo, Cloudinary, Adobe AEM Assets.

---

## Current feature inventory (strengths)

- **Storage & ingest.** MinIO backed, magic-byte validated, ClamAV scanned, presigned uploads, 1.5 GB max file size.
- **Asset types.** Image / Video / Document (PDF). Audio is declared in UI strings but not wired through the domain enum.
- **Renditions.** Thumb / Medium / Poster, image editor (Fabric.js — layers, crop, redact), export presets with fit modes (contain, cover, stretch, width, height).
- **Collections.** Flat, many-to-many to assets, per-collection ACLs with a 4-role hierarchy (Viewer → Contributor → Manager → Admin).
- **Shares.** Public share links with password, expiration, access tokens, access count. Admin-retrievable encrypted token + password.
- **Governance.** Audit events, RBAC, Keycloak SSO, rate limits, presigned URL TTLs.
- **Dashboard.** Recent assets, active shares, activity timeline, quick-access collections.
- **i18n.** EN/SV parity, culture-bound UI.

---

## Gaps that define the difference between "working DAM" and "really useful DAM"

### 1. Migration from other DAMs (🔴 critical — new, Tier 0)

Commercial DAMs understand that buyers won't evaluate a tool they can't migrate into. Every serious DAM offers:

**Bulk import API surface**
- CSV manifest + batch file upload.
- JSON manifest for complex / nested metadata.
- ZIP with sidecar files (filename + matching `.json` / `.xmp` metadata).
- Resumable uploads — partial failures don't restart the whole job.
- Idempotency keys so retries don't duplicate.
- External-ID preservation for rollback or two-way sync.

**Source-specific importers**
- Direct connectors to Bynder, Canto, Frontify, SharePoint, Google Drive, Dropbox, Box, S3-compatible buckets, local NAS.
- OAuth / API-key authentication per connector.
- Incremental sync — re-run to pick up changes since the last import.

**Metadata mapping layer**
- UI to map source fields → AssetHub fields / custom schema.
- Transform rules (split on delimiter, regex, default values, enum remapping).
- Per-connector presets that can be saved and re-used across tenants.

**Structure mapping**
- Source folder tree → AssetHub collections (flat today; nested if Tier 5 lands).
- Permission / ACL mapping from source principals to local principals (or a staging area for manual reconciliation).

**Migration job runner**
- Asynchronous background job with resumable state.
- Dry-run mode that emits a report without writing.
- Chunked execution with rate limiting to avoid overwhelming source APIs or local infra.
- Progress UI: *"1,240 / 5,000 assets migrated, 12 failed — see report"*.
- Per-asset outcome log with reason-for-failure.
- Admin-only, audit-logged.

**Data preservation**
- Preserve original filename, `createdAt`, `createdBy` (mapped from source user IDs).
- Preserve SHA256 for duplicate detection across batches.
- Preserve XMP / IPTC sidecar data.

**Rollback**
- Tag migrated assets with the migration job ID.
- Bulk delete / revert by job if something went wrong.

**AssetHub today: none of the above.** The only path is `POST /api/v1/assets` one file at a time through the multipart endpoint, which is fine for tens of assets and unusable for the 50k–500k asset libraries typical of Bynder / Canto customers.

Without migration, the rest of this analysis is academic — prospects comparing to Bynder won't hand-rebuild their library. This is the **single highest-leverage gap**; see Tier 0 below.

### 2. AI / automation (🔴 critical)

Commercial DAMs have lived or died on this in the last 3–4 years.

| Capability | Commercial | AssetHub |
|---|---|---|
| Auto-tagging (vision) | Bynder, Canto, Widen, Cloudinary, AEM | None |
| OCR on documents/images | All major | None |
| Face detection / named-person tagging | Bynder, Canto | None |
| Smart crop (subject-aware) | Cloudinary, Bynder | None |
| Duplicate detection | All | SHA256 is **stored but never checked** |
| Alt-text generation | Widen, AEM, newer Bynder | None |
| Color palette extraction / color search | Cloudinary, Canto | None |
| Transcription (video/audio) | Cloudinary, MediaValet | None |

**Quick wins:** duplicate detection on upload (SHA256 already exists — reject or warn), AI alt-text via a pluggable vision provider (Azure AI Vision, AWS Rekognition, or a local model).

### 3. Metadata model (🔴 critical)

AssetHub has Title / Description / Copyright / Tags (list) / `MetadataJson` dictionary. Commercial DAMs provide:

- **Custom fields / metadata schemas per asset type or collection** (Bynder "metaproperties", Brandfolder custom fields, Canto custom fields). — Missing.
- **Controlled vocabularies / taxonomies** (hierarchical tags, "Campaign > Spring 2026 > Outdoor"). — Missing.
- **Required metadata gates** (can't publish until fields filled). — Missing.
- **Bulk metadata edit / find-and-replace**. — No bulk edit UI; `BulkDeleteAssets` only.
- **Metadata templates** (apply a preset set of metadata on upload). — Missing.
- **IPTC / XMP round-trip on export.** — Extract only; writeback would reassure enterprise users.

This is the single biggest gap for anyone replacing Bynder / Canto.

### 4. Organization & navigation (🟡 partial)

| | Commercial | AssetHub |
|---|---|---|
| Folder / collection hierarchy | Nested, drag-to-reorganise | Flat only (`Collection.cs` — no parent) |
| Smart collections / saved searches | Yes | None |
| Favorites / lightbox / workspaces | Yes | None |
| Pinboards for campaigns | Bynder, Brandfolder | None |

### 5. Search (🔴 critical)

Current: single ILIKE pattern on title + basic filter by type / collection.

Commercial baseline:

- **Faceted search** (type, tags, collection, date, rights, status, file format, dimensions) in a filter sidebar. AssetHub shows filters but only on a single facet at a time.
- **Full-text across metadata + extracted OCR + transcripts.** AssetHub only searches title.
- **Reverse image search / visual similarity** ("find images like this").
- **Saved searches** with notifications when new matches appear.
- **Search-within-results, boolean operators, field-qualified queries** (`tag:outdoor type:video`).
- **Quick "recent" and "popular" facets.**

### 6. Versioning (🔴 critical)

| | Commercial | AssetHub |
|---|---|---|
| True version history (every upload kept) | Widen, Brandfolder, Bynder | No — `ReplaceImageFile` **overwrites** the MinIO object |
| Restore previous version | Yes | No |
| Check-in / check-out lock | Bynder, AEM | No |
| Edit document history | Source/derivative chain exists | `EditDocument` is single-latest-version only |

The existing `SourceAsset` / `Derivatives` graph is a scaffold; turning it into proper versions (each upload → new row with `version_number`, `previous_version_id`, original retained) is a modest schema change with high perceived value.

### 7. Rights & compliance (🟡 partial)

- **License fields**: usage type (internal-only, web, print, social, broadcast), territory, channel, license start/end, model release, property release, photographer credit. AssetHub has only a "Copyright" string.
- **Expiration at asset level**: "remove this photo from all shares after 2026-06-01". Missing.
- **Watermarking on download** (Cloudinary, Widen, AEM). Missing.
- **Approval workflows** (DAM manager approves before publish). Missing.
- **Usage logging** ("where has this asset been downloaded / embedded"). Partial — share access count exists; asset-level usage tracking doesn't.

### 8. Collaboration (🔴 critical)

Effectively **zero** today. Commercial baseline:

- Comments / discussions on assets, with `@mentions`.
- Annotations / pin callouts on images (review-and-approve UX).
- Approval workflows (submit → review → approve / reject with reason).
- In-app notifications.
- Slack / Teams notifications on activity.
- Activity feed for the *user*, not just global audit.

### 9. Integrations (🔴 critical)

Commercial DAMs live in an ecosystem. AssetHub has none outside Keycloak / SMTP:

- **Creative tools**: Adobe Creative Cloud panel (Photoshop, Illustrator, InDesign), Figma plugin, Canva.
- **Office**: Microsoft Office / Google Workspace insert-from-DAM.
- **Web / CMS**: WordPress, Drupal, Contentful, Sanity, Sitecore, Shopify.
- **Messaging / productivity**: Slack, Teams, Notion.
- **Outbound**: Webhooks, Zapier / Make.
- **Delivery**: CDN integration (Cloudflare, Fastly, AWS CloudFront) for public renditions.

Even one SDK-style public REST API with OpenAPI / Swagger would open the door. CLAUDE.md currently bans Swagger because there are no external clients — that assumption is the blocker.

### 10. Brand portals / guest access (🔴 critical)

- **Branded portal pages** (custom colors, logo, domain) for external agencies / press. Frontify and Brandfolder built their whole pitch on this.
- **Guest users** (named external accounts with scoped access, not anonymous share links).
- **Brand guidelines** (logo kits, colors, fonts, do's and don'ts) served alongside assets.
- **Download gating** (agree-to-terms, registration, captcha).

The share system is close but only supports anonymous tokens, with no branding hooks.

### 11. Lifecycle (🔴 critical)

| | Commercial | AssetHub |
|---|---|---|
| Soft delete / Trash / Recycle Bin (30-day restore) | Standard | Hard delete only |
| Retention / archive policies | Yes | None |
| "Unused assets" report | Canto, Widen | None |
| Scheduled expiry / sunset | Yes | None |

The CLAUDE.md design note *"No soft delete — use status-based lifecycle or hard delete"* trades simplicity for data loss. Soft delete with a `DeletedAt` + scheduled worker purge is roughly a day's work.

### 12. Analytics (🟡 partial)

Dashboard today is descriptive (recent items, activity). Commercial DAMs offer:

- Most-downloaded / most-viewed over time.
- Downloads per share, per asset, per user.
- Storage growth, per-collection / per-user quota.
- Asset performance (views → downloads conversion).
- Orphaned assets, stale assets (> N days without access).
- Export reports to CSV / PDF.

### 13. Distribution (🟡 partial)

- **Embed codes / oEmbed** for CMS embedding.
- **Hotlink tokens** with referrer / domain allowlist.
- **Resize-on-the-fly URLs** (`?w=400&fmt=webp`) — export presets are pre-built, not on-the-fly.
- **Public CDN with signed URLs** for scale.
- **Scheduled publishing** (asset goes public at 09:00 Thu).

### 14. Audio support (🟡 partial)

`AssetType` enum has Image, Video, Document only, but the UI localization has `AssetType_Audio`. Either wire it through (ingest, preview with waveform, transcription) or remove the orphan localization.

### 15. Mobile (🟡 partial)

Responsive Blazor UI is adequate for browsing, but commercial DAMs typically ship a mobile capture app (photo / video → direct upload with location & metadata). Optional for AssetHub, but expected if pitching marketing / creative teams.

### 16. Enterprise readiness (🟡 partial)

- **SCIM user provisioning** (not just JIT provisioning from Keycloak).
- **Multi-tenancy** (single org today — all users share all collections subject to ACL).
- **White-labeling** of the main app, not just share pages.
- **Data residency / region pinning**.
- **SOC2 / ISO 27001 documentation pack**.
- **Billing / subscription plane** if this ever becomes SaaS.

---

## Priority recommendations

### Tier 0 — unlock evaluation entirely
0. **Bulk migration toolkit.** `POST /api/v1/admin/migrations` with a CSV / JSON manifest, file batch endpoint, resumable job runner, dry-run, per-asset outcome log, rollback by migration ID. Start with a generic CSV importer (covers 80% of "export from old DAM → import here" cases); add source-specific connectors (Bynder, Canto, SharePoint, Dropbox) as the prospect list demands. **Without this, the rest is academic.**

### Tier 1 — must fix before positioning as a commercial alternative
1. **Custom metadata schemas + taxonomies** — biggest competitive gap; unlocks everything downstream (search facets, rights, templates).
2. **Soft delete + trash / recycle bin with TTL** — low effort, huge perceived-safety win; prevents the "oh no I just lost 200 assets" call.
3. **Duplicate detection on upload** — SHA256 already stored; reject or warn on match.
4. **Faceted search UI** — combine multiple filters, save searches, search OCR / metadata / tags together.
5. **Asset versioning** — stop overwriting in `ReplaceImageFile`; store `version_number`, retain originals, show history panel.
6. **Public REST API + OpenAPI** — removes CLAUDE.md's "no external clients" premise; unblocks all integrations and the migration toolkit.

### Tier 2 — the AI parity push
7. **AI auto-tagging** (pluggable — Azure AI Vision / AWS Rekognition / Ollama-local).
8. **AI alt-text generation** — covers WCAG liability and auto-populates searchable description.
9. **OCR** for PDFs and scanned images.
10. **Smart crop on export presets** (subject-aware; replaces the center-crop default).

### Tier 3 — collaboration + distribution
11. **Comments + `@mentions` on assets** — single thread per asset is enough for v1.
12. **Approval workflow** (Draft → In review → Approved → Published).
13. **On-the-fly rendition URLs** (`/api/v1/assets/{id}/render?w=400&fmt=webp&crop=smart`) — unblocks CDN / CMS embedding.
14. **Webhooks** (asset.created, asset.updated, share.accessed) — minimum viable integration layer.

### Tier 4 — brand portal play
15. **Branded share portals** (per-collection logo, colors, optional domain) — competes with Frontify / Brandfolder.
16. **Adobe CC / Figma / Microsoft Office plugins** — sales hook for creative teams.
17. **Named guest users** beyond anonymous share tokens.

### Tier 5 — nice-to-haves
18. **Nested collections / folders** (architectural — may conflict with current flat design; consider carefully).
19. **Watermarking on download**.
20. **Usage analytics / most-downloaded reports**.
21. **Audio support** (remove the `AssetType_Audio` orphan strings or fully wire audio through).

---

## Strategic take

AssetHub's foundation is **above average** for an open-source DAM — clean architecture, solid security, proper RBAC, audit, image editor, export presets, i18n. What it lacks is everything that turns a DAM from a "file library with thumbnails" into an indispensable marketing / creative-ops tool: **bulk migration, structured metadata, real search, AI assist, versioning, lifecycle safety, and an ecosystem**.

If this ever competes with Bynder / Canto / Frontify:

- **Tier 0 is the price of admission.** Without migration, no one gets past evaluation.
- **Tiers 1 and 2 are table stakes.** Without them, a mid-market buyer comparing to Brandfolder hits the metadata / search / versioning wall within an hour of evaluation.
- **Tiers 3 and 4 are differentiators.** These are where a product wins deals rather than merely avoiding losing them.

The commercial DAMs win on AI, integrations, and brand portals; AssetHub's plausible wedge is **self-hosting + data sovereignty + Clean Architecture hackability** (a "Mattermost of DAM" positioning) — which only matters if the Tier 0 and Tier 1 gaps are closed first.
