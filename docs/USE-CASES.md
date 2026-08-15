# AssetHub — User Scenario / Use-Case Catalog

> Working artifact for reasoning about *what users can do* and *how each case is implemented*.
> Generated from a full sweep of UI pages, API endpoints, domain services, the test/E2E suite,
> and the kit manifest + docs. Status reflects the tree as of branch `main` on 2026-06-21.

---

## 1. The dimensions of the experience

Every use case below is positioned along four dimensions. Naming these first makes the table
navigable and makes coverage gaps visible.

### Dimension A — Persona (WHO)
The role hierarchy is cumulative: `viewer (1) < contributor (2) < manager (3) < admin (4)`,
plus two non-account principals and the system itself.

| Code | Persona | Notes |
|------|---------|-------|
| **ANON** | Anonymous share visitor | Reaches `/share/{token}`, no account |
| **V** | Viewer (L1) | Read + download within ACL |
| **C** | Contributor (L2) | + upload, edit metadata, share, submit for review |
| **M** | Manager (L3) | + delete, edit collections, manage per-collection ACL, approve/publish |
| **A** | Admin (L4) | + platform governance; bypasses all ACL checks |
| **SYS** | Background worker | Not a user, but produces user-visible outcomes (processing, sweeps) |

### Dimension B — Domain area (WHAT)
Auth · Ingestion · Processing · Organization · Metadata · Discovery · View/Deliver · Editing ·
Versioning · Lifecycle · Sharing ·
Administration · Migration.

### Dimension C — Journey stage (WHEN)
Onboarding → Daily work → Collaboration → Distribution → Governance → Integration.

### Dimension D — Surface (HOW it's reached)
A Blazor page (`/route`), a REST endpoint (`METHOD /path`), a dialog/component, or an
async/background outcome. Recorded per row so "how is this implemented?" starts from the entry point.

### Status legend
- ✅ **Shipped** — wired across all layers
- 🟡 **Partial** — core shipped; named gap/deferral
- 🚧 **In progress** — work present in the working tree, not yet committed
- ⬜ **Planned** — roadmap, not started

---

## 2. Master use-case table

### A. Authentication, session & access
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-AUTH-01 | Sign in via Keycloak OIDC | ANON→auth | `/login` → Keycloak | ✅ |
| UC-AUTH-02 | Sign out / clear session | all | App bar user menu | ✅ |
| UC-AUTH-03 | Redirected to login when hitting a protected page unauthenticated | ANON | any `[Authorize]` page | ✅ |
| UC-AUTH-04 | See only the nav/actions my role allows | all | `NavMenu`, role-gated buttons | ✅ |
| UC-AUTH-05 | Switch UI language (EN / SV) | all | App bar / `ShareLayout` | ✅ |
| UC-AUTH-06 | Toggle dark mode | all | App bar | ✅ |

### B. Home / dashboard
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-HOME-01 | View dashboard (counts, storage, recent assets, activity, active shares) | V+ | `/` , `GET /api/v1/dashboard` | ✅ |

### C. Ingestion (upload)
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-ING-01 | Upload a single asset (image/video/audio/doc) | C+ | `AssetUpload`, `POST /assets` | ✅ |
| UC-ING-02 | Upload many files with live progress | C+ | `AssetUpload` | ✅ |
| UC-ING-03 | Large-file presigned upload (init → confirm) | C+/API | `POST /assets/init-upload`, `/confirm-upload` | ✅ |
| UC-ING-06 | See failed uploads explained | C+ | `UploadErrorsDialog` | ✅ |

### D. Processing (async, user-visible outcome)
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-PROC-01 | Image → thumbnail + medium + EXIF, asset becomes Ready | SYS | `ProcessImageHandler` | ✅ |
| UC-PROC-02 | Video → poster frame + duration/codec | SYS | `ProcessVideoHandler` | ✅ |
| UC-PROC-03 | Audio → duration + waveform peaks | SYS | `ProcessAudioHandler` | ✅ |
| UC-PROC-04 | Malware scan blocks an infected upload | SYS | ClamAV adapter | ✅ |
| UC-PROC-05 | Failed processing surfaces error + retry | SYS/C | `MarkFailed` + Wolverine retry | ✅ |
| UC-PROC-06 | Abandoned/stale uploads cleaned up | SYS | `StaleUploadCleanupService` | ✅ |

### E. Organization (collections)
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-ORG-01 | Browse collections (tree / flat) | V+ | `/collections` | ✅ |
| UC-ORG-02 | Create a collection | C+ | `CreateCollectionDialog` | ✅ |
| UC-ORG-03 | Edit collection name/description | C+/M | `EditCollectionDialog` | ✅ |
| UC-ORG-04 | Delete a collection (with impact preview) | M/C | `…/deletion-context` | ✅ |
| UC-ORG-05 | Add / remove an asset to / from a collection | C+ | `AssetToolbar`, `POST/DELETE …/collections/{id}` | ✅ |
| UC-ORG-06 | Reach the same asset from each of its collections | V+ | — | ✅ |
| UC-ORG-10 | Download a whole collection as a ZIP (queued) | V+ | `POST …/download-all` | ✅ |
| UC-ORG-11 | Bulk-delete / bulk-set-access on collections | A | `/admin/collection-access` | ✅ |

**Nested collections removed 2026-08** by the reshape (contract-012) — collections
are flat. A collection's effective role is the direct ACL grant on it (plus the
system-admin bypass); there is no parent chain, no inheritance toggle, and no
copy-from-parent. Browsable on the `full-featured` branch / `pre-reshape` tag.

### F. Metadata & taxonomies
Removed 2026-08 by the reshape (contract-010) — the admin-defined schema/taxonomy
engine is outside the five-feature identity. Browsable on the `full-featured`
branch / `pre-reshape` tag.

**Breaking API change**: `GET`/`PUT /api/v1/assets/{id}/metadata` and
`POST /api/v1/assets/bulk-metadata` are gone. Free-form **tags** are the
replacement and are *not* equivalent — they carry no field identity, no type, no
validation and no controlled vocabulary. An integration writing structured
metadata has no drop-in substitute.

What survives on an asset: `Tags` (free-form, faceted, searchable) and
`MetadataJson` (technical metadata extracted from the file — EXIF, dimensions,
duration), both returned by `GET /api/v1/assets/{id}` and rendered on asset detail.

### G. Discovery (search)
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-DISC-01 | Faceted full-text search (type/status/collection/date/tags) | V+ | `/search`, `POST /assets/search` | ✅ |
| UC-DISC-05 | Search results scoped to my ACL | V+ | `IAssetSearchService` | ✅ |

**Saved searches removed 2026-08** by the reshape (contract-012). Search itself is
unchanged — full-text, tags and all four facets — but a search is no longer a
persistable object.

**Upload duplicate detection removed 2026-08** (contract-012). Uploads still hash
content into `Asset.Sha256`; nothing blocks an identical file, and the admin
force-override and its audit events are gone. Migration ingest *retains* its
SHA-256 dedup, so re-running an import still skips assets that already exist.

### H. View, deliver & render
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-VIEW-01 | Open asset detail | V+ | `/assets/{id}` | ✅ |
| UC-VIEW-02 | Preview media inline (image/video/audio/doc) | V+ | `MediaPreview`, `…/preview` | ✅ |
| UC-VIEW-03 | Download the original | V+ | `…/download` | ✅ |
| UC-VIEW-04 | Get thumbnail / medium / poster renditions | V+ | `…/thumb` `…/medium` `…/poster` | ✅ |
| UC-VIEW-07 | See which collections an asset belongs to | V+ | `…/collections` | ✅ |

### I. Editing & export presets
Removed 2026-08 by the reshape (contract-008) — outside the five-feature identity. Browsable on the `full-featured` branch / `pre-reshape` tag. The internal `…/save-copy` and `…/replace-file` endpoints survive as the API-only copy and version-minting path (no UI surface; § J versioning still applies).

### J. Versioning
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-VER-01 | View an asset's version history | V+ | `AssetVersionHistoryDialog`, `…/versions` | 🟡 per-version thumbnail preview deferred |
| UC-VER-02 | Restore a previous version (auto-snapshots current) | C+ | `…/versions/{n}/restore` | ✅ |
| UC-VER-03 | Prune an old version | A | `DELETE …/versions/{n}` | ✅ |

### K. Lifecycle (soft delete / trash / purge)
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-LIFE-01 | Smart-delete an asset (decides hard vs soft by access) | M/A | `DeleteAssetDialog` | ✅ |
| UC-LIFE-02 | Remove from one collection, preserved elsewhere | C+ | smart deletion | ✅ |
| UC-LIFE-03 | View trash (soft-deleted assets) | A | `/admin/trash` | ✅ |
| UC-LIFE-04 | Restore an asset from trash | A | `POST /admin/trash/{id}/restore` | ✅ |
| UC-LIFE-05 | Permanently purge / empty trash | A | `DELETE /admin/trash/{id}`, `…/empty` | ✅ |
| UC-LIFE-06 | Auto-purge after retention period | SYS | `TrashPurgeBackgroundService` | ✅ |
| UC-LIFE-07 | Undo a multi-asset bulk delete | C+ | `BulkAssetActionsDialog` | 🟡 deferred (N-asset undo snackbar) |
| UC-LIFE-08 | Orphaned storage objects swept (tombstones) | SYS | `OrphanedObjectsSweeperService` | ✅ |

### L. Collaboration (comments & mentions)
Removed 2026-08 by the reshape (contract-006) — outside the five-feature identity. Browsable on the `full-featured` branch / `pre-reshape` tag.

### M. Notifications
Removed 2026-08 by the reshape (contract-006) — same note as L. Email infrastructure (share emails) remains.

### N. Publishing workflow & review
Removed 2026-08 by the reshape (contract-007) — outside the five-feature identity. Browsable on the `full-featured` branch / `pre-reshape` tag.

### O. Sharing & distribution
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-SHARE-01 | Create a share for an asset/collection (password, expiry) | V+ | `CreateShareDialog`, `POST /shares` | ✅ |
| UC-SHARE-02 | Copy share URL / QR | V+ | `ShareLinkDialog` | ✅ |
| UC-SHARE-03 | Set / change / rotate share password | V+ | `PUT /shares/{id}/password` | ✅ |
| UC-SHARE-04 | Revoke a share | V+ | `DELETE /shares/{id}` | ✅ |
| UC-SHARE-05 | Open a share & enter password | ANON | `/share/{token}` | ✅ |
| UC-SHARE-06 | Download / ZIP-all from a share | ANON | `…/download`, `…/download-all` | ✅ |
| UC-SHARE-07 | Be blocked by an expired / revoked share | ANON | public share access | ✅ |
| UC-SHARE-08 | Admin manage all shares (reveal token/pw, bulk delete) | A | `/admin/shares` | ✅ |

### P. Branded portals
Removed 2026-08 by the reshape (contract-004) — outside the five-feature identity. Browsable on the `full-featured` branch / `pre-reshape` tag.

### Q. Guest invitations
Removed 2026-08 by the reshape (contract-004) — same note as P.

### R. Watermarking & forensics
Removed 2026-08 by the reshape (contract-003) — outside the five-feature identity. The
full implementation remains browsable on the `full-featured` branch / `pre-reshape` tag.

### S. Administration (users, ACL, audit, PATs)
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-ADMIN-01 | Admin console grouped Access/Content/Operations/Insights | A | `/admin` | ✅ |
| UC-ADMIN-02 | List / create / edit / delete users (Keycloak) | A | `/admin/users` | ✅ |
| UC-ADMIN-03b | Reset a user password (emailed single-use link) | A/V+ | `…/auth/forgot-password`, `/reset-password` | ✅ Identity only |
| UC-ADMIN-03 | Send a password-reset email | A | `…/users/{id}/reset-password` | ✅ |
| UC-ADMIN-05 | Manage per-collection ACL (grant/revoke roles) | M/A | `/admin/collection-access` | ✅ |
| UC-ADMIN-06 | Search users for the ACL picker | M/A | `…/acl/users/search` | ✅ |
| UC-ADMIN-07 | View / filter / paginate the audit log | A | `/admin/audit` | ✅ |
| UC-ADMIN-08 | Audit retention auto-prune | SYS | `AuditRetentionService` | ✅ |

### T. Public API & integration
Removed 2026-08 by the reshape (contract-011) — Personal Access Tokens, the
OpenAPI/Swagger document, and the `[PublicApi]` + scope-filter apparatus are gone.
AssetHub is no longer an integration platform. Browsable on the `full-featured`
branch / `pre-reshape` tag.

The REST endpoints still exist but are **internal**: they serve the Blazor UI's
browser-side fetches (media bytes, downloads, share media) behind cookie/JWT auth,
carry no SemVer promise, and are not documented.

### U. Webhooks
Removed 2026-08 by the reshape (contract-005) — outside the five-feature identity. Browsable on the `full-featured` branch / `pre-reshape` tag.

### V. Bulk import / migration
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-MIG-01 | Create a migration job | A | `/admin/migrations` | ✅ |
| UC-MIG-02 | Upload a CSV manifest | A | `…/migrations/{id}/manifest` | ✅ |
| UC-MIG-04 | Upload staging files (multipart) | A | `…/migrations/{id}/files` | ✅ |
| UC-MIG-05 | Start / cancel / retry-failed | A | `…/start` `…/cancel` `…/retry` | ✅ |
| UC-MIG-06 | Poll progress / view items by status | A | `MigrationDetailDialog` | ✅ |
| UC-MIG-07 | Download the outcome CSV | A | `…/outcome.csv` | ✅ |
| UC-MIG-08 | Unstage an item / bulk-delete migrations | A | `…/unstage`, `…/bulk` | ✅ |
| UC-MIG-09 | Import from a remote source (S3 / Bynder / Canto / SharePoint) | A | — | ❌ removed 2026-08 by the reshape (contract-008); import is CSV-manifest + staged-file only |

### W. Analytics & exposure
Removed 2026-08 by the reshape (contract-002) — outside the five-feature identity. The
full implementation remains browsable on the `full-featured` branch / `pre-reshape` tag.

### X. Planned tiers (not started)
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-AI-01..05 | AI auto-tagging, OCR, alt-text, smart-crop, provider abstraction | C+/A | — | ⬜ planned (T2) |
| UC-HA-01..03 | Horizontal scaling, MinIO federation, observability dashboards | A/ops | — | ⬜ planned (T6) |

---

## 3. Status roll-up

| Status | Count (approx.) | Where it clusters |
|--------|:---:|-------------------|
| ✅ Shipped | ~95 | All Tier 0–5 core paths |
| 🟡 Partial | ~16 | UI polish (reparent, badges), embedding/async fallbacks, deferred autocomplete/markdown |
| 🚧 In progress | 3 | The `/review` queue + history + inline reject dialog (uncommitted) |
| ⬜ Planned | ~7 | T2 AI suite, T6 HA suite |

---

## 4. How to use this catalog

This is the *what*. The next step — "how each use case is implemented" — is best done by
extending each row with implementation columns. Suggested working schema per use case:

| Field | Meaning |
|-------|---------|
| **Entry point** | The page/endpoint already in the Surface column |
| **Service / handler** | Application service + Infrastructure impl that does the work |
| **Auth path** | Policy + (if applicable) collection ACL check |
| **Persistence / side-effects** | Tables touched, MinIO objects, cache tags invalidated, audit events emitted |
| **Async tail** | Wolverine messages / background jobs triggered |
| **Tests** | Covering xUnit / bUnit / E2E specs (or "gap") |
| **Notes / risks** | Edge cases, deferrals, known issues |

> **Coverage signal from the test sweep:** E2E is strong on the core loop (auth → browse → upload
> → share → revoke) and role-visibility, but several shipped features are **backend-tested only** —
> versioning UI, metadata schemas, workflow approval end-to-end, migrations pause/resume,
> renditions and trash→restore. Those are the
> highest-value targets if we want each use case demonstrably exercised through the UI.
