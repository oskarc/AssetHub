# SPEC: On-prem Bildbank (DAM light) i .NET + Blazor – Gratis lokalt (Docker)

## 0) Målbild och principer

### Mål
Bygg en intern/extern delningsplattform för bilder, video och vissa dokument (pdf/pptx utan preview) med:
- Rollbaserad åtkomst (per collection, primärt).
- Metadata och sök/filter.
- Extern delning via tidsbegränsade länkar och/eller externa orgs.
- Snabb upplevelse (thumbnails, streaming utan att proxy:a allt).
- Enkel att förstå och underhålla (“DAM light”, inte enterprise-monster).
- On-prem och körbar lokalt utan licenser/kostnader.

### Grundprinciper (styr design)
1. **Collections-first behörighet**: ACL på collection som standard. Asset override är valfritt och ska vara “advanced”.
2. **Object Storage för filer, DB för metadata**: Filer i MinIO, metadata/ACL/shares/audit i Postgres.
3. **API styr åtkomst, MinIO levererar bytes**: API genererar kortlivade presigned URLs så klienten hämtar thumbnails/original direkt från MinIO.
4. **Allt tungt är asynkront**: Thumbnails, video metadata, poster frame, virus-scan (om används) körs i Worker via job queue.
5. **Snabb UI = små payloads**: Grid listar bara nödvändiga fält + thumbnail-url; detaljsida hämtas separat.
6. **Säkerhet före bekvämlighet**: Share tokens hashade, TTL kort, audit på delning och nedladdning, rate limiting för share-endpoints.
7. **Bygg för att byta komponenter**: interfaces för storage, job queue, search.

---

## 1) Scope

### In scope (MVP + “nästa nivå”)
- Blazor UI:
  - Browse collections och assets (grid, filter, sort).
  - Asset detail med preview (bild/video) + metadata + share.
  - Upload (multi, drag&drop) + progress.
  - Admin: skapa collections, tilldela roller, hantera shares.
- Backend API:
  - AuthN/AuthZ (policy-based).
  - CRUD för collections/assets/metadata.
  - Share-länkar (extern access) med expiry/revoke.
  - Presigned URLs för thumbnails/original/video.
  - Auditlogg (miniminivå).
- Worker:
  - Thumbnail + medium för bilder.
  - Video metadata (duration/dimensioner) + poster frame.
  - (Dokument) metadata enbart.
- Storage:
  - MinIO buckets/paths.
- DB:
  - Postgres schema med index.
- Lokalt:
  - Docker Compose som spinnar upp Postgres/MinIO/API/Worker (+ ev Keycloak).

### Out of scope (initialt)
- PDF/PPTX preview/rendering.
- Video transcoding till HLS/DASH (kan komma senare).
- Avancerad DLP/klassning, watermarking.
- Full text search cluster (OpenSearch) – börja med Postgres sök.

---

## 2) Användarroller och rättigheter (RBAC)

### Roller (baseline)
- Viewer:
  - Se assets i tillåtna collections
  - Se metadata
  - Spela upp video/bild
  - Ladda ner (om policy tillåter)
- Contributor:
  - Upload i tillåtna collections
  - Redigera metadata på assets i tillåtna collections
- Manager:
  - Skapa/ändra collections (inom tenant)
  - Tilldela roller (inom tenant)
  - Skapa/hantera shares
- Admin:
  - Full access tenant
  - Globala policies, retention, audit export

### Behörighetsmodell (viktig)
- Primär åtkomst sker via:
  - `principal` (user eller group) + `role` på `collection`.
- Policy checks i API:
  - `CanViewCollection(collectionId)`
  - `CanUploadToCollection(collectionId)`
  - `CanEditMetadata(collectionId)`
  - `CanShareExternally(collectionId)`
  - `CanManagePermissions(collectionId or tenant)`
- Välj strategi:
  - **MVP**: endast collection ACL.
  - **Option**: asset-level override (tabell `asset_acl`) men avstängt tills behov.

### Extern åtkomst
Två lägen:
1) Share-länk (token):
   - Kräver ej inloggning
   - Token har TTL, scope (asset/collection), och capabilities (view/download)
2) Extern organisation som loggar in:
   - Lokalt gratis: Keycloak + OIDC (eller lokala konton via ASP.NET Identity).
   - Detta är “next” efter share-links.

---

## 3) Domänobjekt (koncept)

### Tenant (valfritt i MVP)
- Om du vill stödja “olika organisationer” som separata rum:
  - `Tenant` med `TenantId`
  - Alla data radas med `TenantId`
- Om du vill hålla det enklare:
  - En enda tenant i MVP, men behåll kolumnen `tenant_id` ändå.

### Collection
- `id, tenant_id, name, description, parent_id (optional)`
- “Folder” / “Album” logik.

### Asset
- `id, tenant_id, collection_id, type (image/video/document), status, created_at`
- Filinfo:
  - `original_object_key`, `content_type`, `size_bytes`, `sha256 (optional)`
- Renditions:
  - `thumb_object_key`, `medium_object_key`, `poster_object_key (video)`
- Metadata:
  - JSONB + definierade fält (titel, beskrivning) för bra index.

### Share
- `id, tenant_id, token_hash, scope_type, scope_id, expires_at, permissions, password_hash (optional)`
- `permissions` = view/download flags, optional watermarking later.

### AuditEvent
- Append-only:
  - `event_type, actor_user_id (nullable), ip, user_agent, target_type, target_id, created_at, details_json`

---

## 4) Lösningsstruktur (Solution/Projects)

### Rekommenderad solution layout
- `/src`
  - `Dam.Domain`
    - Entities, value objects, domain services, enums
  - `Dam.Application`
    - Use cases, DTOs, validators, interfaces (ports)
  - `Dam.Infrastructure`
    - Postgres repos (EF Core), MinIO adapter, job queue, media processing adapters
  - `Dam.Api`
    - ASP.NET Core API, AuthN/AuthZ, controllers/minimal APIs
  - `Dam.Worker`
    - Background processing (jobs), ffmpeg/image tools, scheduled cleanup
  - `Dam.Ui`
    - Blazor UI (Server eller WASM)
  - `Dam.Shared`
    - Shared contracts, common utils, errors

### Port/Adapter interfaces (för utbytbarhet)
- `IObjectStorage`
  - `GetPresignedGetUrl(objectKey, ttl)`
  - `GetPresignedPutUrl(objectKey, ttl)`
  - `PutObject(stream...)` (om du inte vill presign PUT i MVP)
- `IAssetRepository`, `ICollectionRepository`, `IShareRepository`, `IAuditRepository`
- `IJobQueue`
  - `Enqueue(jobType, payloadJson)`
  - `Dequeue(batchSize)`
  - `Complete(jobId)`
- `IMediaProcessor`
  - `CreateImageRenditions(originalPath) -> (thumb, medium, metadata)`
  - `ExtractVideoMetadata(originalPath) -> (duration, w, h, codec)`
  - `CreateVideoPosterFrame(originalPath) -> image`
- `IClock`, `IIdGenerator` (testbarhet)

---

## 5) Teknikval (gratis lokalt)

### Core
- .NET 8/9 (valfri, men håll dig till LTS om möjligt)
- Blazor Server (MVP) eller Blazor WASM (senare)

### Storage
- MinIO (S3-compatible), Docker

### DB
- PostgreSQL, Docker

### Auth (lokalt)
Alternativ A (minst friktion): ASP.NET Core Identity (lokala konton)
Alternativ B (mer enterprise-likt): Keycloak OIDC (Docker), gratis

### Media tooling
- ffmpeg/ffprobe via container (CLI) för video metadata + poster frame
- Bildrenditions:
  - Rekommenderat för minsta licensrisk: ImageMagick CLI i container
  - Alternativ: SkiaSharp (kolla licensvillkor för din användning)
  - OBS: Var tydlig i README: “val av bildlib kräver licenskontroll för produktion”.

### Job queue
- MVP: Postgres tabell `jobs` + worker poll
- Alternativ: Hangfire (community) med Postgres-storage

---

## 6) Data och DB-schema (PostgreSQL)

### Tabeller (MVP)
#### tenants
- tenant_id (uuid pk)
- name (text)
- created_at (timestamptz)

#### users (om lokala konton)
- user_id (uuid pk)
- tenant_id (uuid fk)
- username (text unique per tenant)
- password_hash (text)
- display_name (text)
- created_at

(Om OIDC: user-tabell kan ersättas av “external_id + claims cache” eller minimal mapping.)

#### groups
- group_id (uuid pk)
- tenant_id
- name

#### group_members
- group_id, user_id (composite pk)

#### collections
- collection_id (uuid pk)
- tenant_id
- parent_id (uuid nullable)
- name (text)
- description (text)
- created_at
- created_by_user_id

Index:
- (tenant_id, parent_id)
- (tenant_id, name)

#### collection_acl
- acl_id (uuid pk)
- tenant_id
- collection_id
- principal_type (text: 'user' | 'group')
- principal_id (uuid)
- role (text: viewer|contributor|manager|admin)
- created_at

Index:
- (tenant_id, collection_id)
- (tenant_id, principal_type, principal_id)

#### assets
- asset_id (uuid pk)
- tenant_id
- collection_id
- asset_type (text: image|video|document)
- status (text: processing|ready|failed)
- title (text)
- description (text)
- tags (text[] optional) OR store in metadata json
- metadata_json (jsonb)
- content_type (text)
- size_bytes (bigint)
- sha256 (text nullable)
- original_object_key (text)
- thumb_object_key (text nullable)
- medium_object_key (text nullable)
- poster_object_key (text nullable)
- created_at
- created_by_user_id
- updated_at

Index:
- (tenant_id, collection_id, created_at desc)
- (tenant_id, asset_type)
- GIN index on metadata_json (selective)
- trigram index on title/description for search (optional extension)

#### shares
- share_id (uuid pk)
- tenant_id
- token_hash (text unique)  -- store hash, never plaintext
- scope_type (text: asset|collection)
- scope_id (uuid)
- permissions_json (jsonb)  -- { "view": true, "download": false }
- expires_at (timestamptz)
- revoked_at (timestamptz nullable)
- password_hash (text nullable)
- created_at
- created_by_user_id
- last_accessed_at (timestamptz nullable)
- access_count (int default 0)

Index:
- (tenant_id, scope_type, scope_id)
- (token_hash) unique

#### audit_events
- audit_id (uuid pk)
- tenant_id
- event_type (text) -- e.g. SHARE_CREATED, SHARE_ACCESSED, DOWNLOAD, UPLOAD_COMPLETED
- actor_user_id (uuid nullable) -- null for anonymous share access
- ip (text nullable)
- user_agent (text nullable)
- target_type (text)
- target_id (uuid nullable)
- created_at
- details_json (jsonb)

Index:
- (tenant_id, created_at desc)
- (tenant_id, event_type, created_at desc)

#### jobs (om Postgres-queue)
- job_id (uuid pk)
- tenant_id
- job_type (text: PROCESS_IMAGE, PROCESS_VIDEO, EXTRACT_METADATA, etc.)
- payload_json (jsonb)
- status (text: queued|processing|completed|failed)
- attempts (int)
- locked_until (timestamptz nullable)
- last_error (text nullable)
- created_at
- updated_at

Index:
- (status, created_at)
- (locked_until)

### Data constraints
- All rows måste ha tenant_id (även om single-tenant i MVP) för framtidssäkring.
- All access måste filtrera på tenant_id.

---

## 7) Object Storage (MinIO) layout

### Bucket strategy
- En bucket per environment: `dam-dev`, `dam-prod`
- Alternativ: per tenant bucket (mer admin), men enkelt: en bucket + prefix per tenant.

### Object keys
- `tenant/{tenantId}/assets/{assetId}/original`
- `tenant/{tenantId}/assets/{assetId}/thumb.jpg`
- `tenant/{tenantId}/assets/{assetId}/medium.jpg`
- `tenant/{tenantId}/assets/{assetId}/poster.jpg` (video)

### Presigned URL policy
- GET url TTL: 60–300 sekunder (kort)
- PUT url TTL: 5–15 minuter (upload)
- Varje presign kräver authz-check i API (för share-token eller inloggad user).

---

## 8) API-design (endpoints och payloads)

### Auth
- OIDC login (Keycloak) eller cookie auth (Identity).
- All API kräver auth utom share endpoints.

### Collections
- GET `/api/collections?parentId=...`
- POST `/api/collections`
- PATCH `/api/collections/{id}`
- DELETE `/api/collections/{id}` (soft delete rekommenderas)
- GET `/api/collections/{id}/permissions`
- POST `/api/collections/{id}/permissions` (add/replace)
- DELETE `/api/collections/{id}/permissions/{aclId}`

### Assets
- GET `/api/assets?collectionId=...&q=...&type=...&sort=...&page=...`
  - Returnerar: `assetId, title, type, status, createdAt, thumbUrl (presigned), posterUrl (presigned for video grid), tags`
- GET `/api/assets/{id}`
  - Returnerar detalj + presigned URLs (original/medium/thumb/video)
- POST `/api/assets/init-upload`
  - Input: `collectionId, filename, contentType, sizeBytes, assetType`
  - Output: `assetId, uploadMode, presignedPutUrl OR multipart instructions`
- POST `/api/assets/{id}/complete-upload`
  - Triggar job creation
- PATCH `/api/assets/{id}/metadata`
  - Title/desc/tags/metadata_json
- POST `/api/assets/batch/metadata` (batch edit)
- POST `/api/assets/{id}/move` (to another collection)

### Shares (extern delning)
- POST `/api/shares`
  - Input: `scopeType, scopeId, expiresInHours, canDownload, password(optional)`
  - Output: `shareUrl` (token i URL), `expiresAt`
- GET `/api/shares?scopeType=...&scopeId=...`
- POST `/api/shares/{id}/revoke`

### Share access (anonymous)
- GET `/share/{token}` (UI route) -> server-side calls:
- GET `/api/public/shares/{token}/assets?collectionId=...` (om share scope = collection)
- GET `/api/public/shares/{token}/asset/{assetId}`
  - Returnerar presigned urls för visning
- Optional: POST `/api/public/shares/{token}/validate-password`

### Audit
- GET `/api/audit?from=...&to=...&type=...` (admin)

### API Non-functional requirements
- Rate limiting på `/api/public/*`
- Strict validation av content types och storlek
- Antivirus-scan hook (optional)
- CORS: endast om UI separerat; om Blazor Server kan du minimera.

---

## 9) UI/UX-spec (Blazor)

### Browse page
- Left: collection tree (lazy load)
- Main: asset grid:
  - thumbnails (img) / poster (video)
  - badges: type, status
- Filter:
  - search text (title/desc/tags)
  - type filter
  - created date range
  - tags (autocomplete)
- Sort:
  - newest, oldest, title
- Virtualize grid + incremental loading.

### Asset details
- Preview:
  - Image: show medium, click to original
  - Video: HTML5 video tag with presigned URL
  - Document: icon + download
- Metadata editor (permissions-based)
- Share panel:
  - list shares, create new, revoke
- Activity/audit summary (optional snippet)

### Upload
- Drag&drop multi-file
- For each file:
  - progress
  - status: uploading, processing, ready
- For video: chunked upload (phase 2 if needed)

### Admin
- Manage collections
- Manage permissions (assign group/user)
- Manage groups (om lokal)

---

## 10) Worker & Job flow

### Job types
- PROCESS_IMAGE:
  - Download original from MinIO -> local temp
  - Generate thumb/medium via ImageMagick
  - Extract EXIF (optional)
  - Upload renditions to MinIO
  - Update DB asset keys, status=ready
- PROCESS_VIDEO:
  - ffprobe: duration, w/h, codec
  - ffmpeg: generate poster frame at t=1s (configurable)
  - Upload poster
  - Update DB asset metadata_json, poster_object_key, status=ready
- PROCESS_DOCUMENT:
  - No preview; just confirm file ok; status=ready

### Failure handling
- On failure:
  - status=failed
  - job attempts++
  - store last_error
- Retry policy:
  - 3 attempts, exponential backoff
- Cleanup:
  - temp files deleted always (finally)

### Worker concurrency
- Config: `MAX_CONCURRENT_JOBS=2` for dev
- Use lock in DB (jobs.locked_until) to prevent multiple workers processing same job.

---

## 11) Security spec (must-have)

### Token handling
- Share token:
  - Generate random 32+ bytes
  - Store `token_hash = SHA256(token + server_salt)`
  - Only return plaintext token once (in shareUrl)
- Optional password:
  - Store bcrypt/argon hash
  - Do not log plaintext
- Revoke:
  - set revoked_at
- Validate:
  - token hash lookup + expiry + revoked

### Presigned URLs
- Only generate after authorization check:
  - Normal user: collection ACL
  - Share user: share scope + permissions
- TTL short for GET (60-300s)
- Use HTTPS in real env.

### Audit
- Log at least:
  - UPLOAD_INITIATED, UPLOAD_COMPLETED, ASSET_READY/FAILED
  - SHARE_CREATED, SHARE_REVOKED, SHARE_ACCESSED
  - DOWNLOAD
- Include IP + user_agent for anonymous share access

### Input validation
- Allow list av content-types:
  - image/jpeg, image/png, image/webp
  - video/mp4, video/webm (choose)
  - application/pdf
  - application/vnd.openxmlformats-officedocument.presentationml.presentation
- Validate file extension + MIME + sniffing (optional advanced)
- Max file size per type (config)

### Hardening
- Rate limit share endpoints
- Limit share attempts per IP (basic)
- Avoid SSRF: presign endpoints never accept arbitrary URLs.

---

## 12) Performance spec (must-have)

### UI performance
- Grid: thumbnails only (small)
- Virtualized list
- Server-side paging

### Storage performance
- Use presigned GET so MinIO serves bytes directly
- Cache thumbnails with Cache-Control (if safe)

### DB performance
- Index on (tenant, collection, created_at desc)
- Use “seek pagination” (created_at + id) om du vill skala

### Background processing
- Thumbnails precomputed; never compute on-request.

---

## 13) Local Dev (Docker Compose) – blueprint

Goal: start everything with `docker compose up` and test.

Services:
- postgres
- minio
- api
- worker
- optional: keycloak

Basic env vars:
- `POSTGRES_CONN`
- `MINIO_ENDPOINT`, `MINIO_ACCESS_KEY`, `MINIO_SECRET_KEY`, `MINIO_BUCKET`
- `AUTH_MODE=LocalIdentity|Keycloak`
- `PRESIGN_TTL_SECONDS=120`
- `UPLOAD_MAX_BYTES_*`
- `FFMPEG_PATH` (if running inside worker container, it’s in PATH)

---

## 14) Minimal “build order” (implementation roadmap)

### Phase 1 (walking skeleton)
1. Compose: Postgres + MinIO + Api + Ui (hello auth)
2. DB migrations (tenants, collections, assets, acl)
3. Create collections + ACL assign (hardcoded admin)
4. Upload image (store original), list in grid (no thumbnail yet)
5. Worker: generate thumbnail, update asset ready
6. Presigned GET for thumbnails, UI shows grid fast

### Phase 2 (sharing & video)
7. Shares: create token, public endpoints, audit share access
8. Video processing: ffprobe + poster frame
9. Download rules per share and per role

### Phase 3 (polish)
10. Batch metadata edit, tags, filters
11. Better search (Postgres trigram)
12. Admin UX for groups/roles

---

## 15) Debugging & AI-guidance instructions (hur en AI ska resonera)

När du (AI) får ett problem eller ska föreslå riktning:
1. Klassificera problemet i en kategori:
   - Auth/AuthZ
   - Storage/MinIO presign
   - DB/schema/query performance
   - Worker/media processing
   - UI/performance/state
   - Share token security
2. Ställ 3 kontrollfrågor till systemet (inte användaren) genom att:
   - läsa loggar (API, Worker)
   - inspektera DB status (assets.status, jobs, shares)
   - verifiera object keys i MinIO
3. Föreslå minst två åtgärdsspår:
   - snabb fix (MVP)
   - robust fix (production)
4. Alltid ange:
   - vilken modul (Api/Worker/Ui/Infrastructure) ändringen hör hemma i
   - vilka testfall som verifierar lösningen
5. Vid prestanda:
   - identifiera om bottleneck är DB, MinIO, API, UI
   - föreslå mätning (timing logs, tracing)
6. Vid säkerhet:
   - kontrollera token hash-lagring, TTL, authz-check före presign
   - logga och rate limit public endpoints

### Exempel: “Video laddar långsamt”
AI bör:
- Kontrollera om API proxy:ar bytes (då blir det segt)
- Säkerställ att UI använder presigned URL direkt mot MinIO
- Överväg range requests support (MinIO + HTML5 video)
- Föreslå HLS först senare om behov

### Exempel: “Användare ser fel collection”
AI bör:
- Kontrollera tenant_id filter i queries
- Kontrollera ACL join (principal_type + id)
- Verifiera claims mapping (userId/groupId)

---

## 16) Testplan (MVP)

### Access control
- Viewer kan se men inte edit
- Contributor kan upload + edit i sin collection
- Manager kan skapa share, revoke, och hantera ACL
- Extern share kan endast se scope och kan ej navigera utanför

### Upload/processing
- Upload image -> status processing -> ready -> thumb visible
- Upload video -> poster generated -> playable
- Upload pdf/pptx -> downloadable, no preview

### Security
- Share token:
  - invalid token -> 404
  - expired -> 410 (eller 404)
  - revoked -> 410
- Presigned URL TTL:
  - after expiry should fail (403 from MinIO)

### Performance
- Browse grid 1000 assets:
  - API list under X ms (target: <300ms local)
  - thumbnails load progressively

---

## 17) Open questions (design decisions att låsa)

1) Auth mode for production:
   - AD/LDAP? Keycloak? Entra on-prem? (OIDC provider)
2) Multi-tenant:
   - behöver du flera orgs med hård isolering eller räcker shares?
3) Video format policy:
   - tillåt mp4 endast? (minskar komplexitet)
4) Metadata schema:
   - fri JSON + tags räcker? eller behöver “fältkatalog”?

---

## 18) “Definition of Done” (MVP)

En build anses klar när:
- Collections + ACL fungerar.
- Upload av bilder/video/dokument fungerar.
- Worker gör thumbnails + video poster + metadata.
- UI visar snabbt grid med thumbnails och detaljer med preview.
- Share-länkar fungerar med TTL och revoke.
- Audit loggar share och download.
- Allt startar lokalt via Docker Compose utan licenskostnad.
