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
| **GUEST** | Magic-link guest | Provisioned Keycloak *viewer* on invite accept; ACL-scoped, auto-expiring |
| **V** | Viewer (L1) | Read + download within ACL; comment if enabled |
| **C** | Contributor (L2) | + upload, edit metadata, share, submit for review |
| **M** | Manager (L3) | + delete, edit collections, manage per-collection ACL, approve/publish |
| **A** | Admin (L4) | + platform governance; bypasses all ACL checks |
| **API** | PAT / integration | Bearer `pat_*`; scope-constrained; cannot mint PATs |
| **SYS** | Background worker | Not a user, but produces user-visible outcomes (processing, sweeps, digests) |

### Dimension B — Domain area (WHAT)
Auth · Ingestion · Processing · Organization · Metadata · Discovery · View/Deliver · Editing ·
Versioning · Lifecycle · Collaboration · Notifications · Workflow/Review · Sharing · Branding ·
Guests · Watermarking · Administration · Public API · Webhooks · Migration · Analytics.

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
| UC-AUTH-07 | Accept a magic-link invite → provisioned viewer | ANON→GUEST | `/guest-accept` | ✅ |
| UC-AUTH-08 | Authenticate API calls with a PAT bearer token | API | `Authorization: Bearer pat_*` | ✅ |

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
| UC-ING-04 | Be blocked/warned on a duplicate (SHA-256) | C+ | upload flow | ✅ |
| UC-ING-05 | Admin force-create over a duplicate (audited) | A | upload flow | ✅ |
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
| UC-ORG-07 | Nest a collection under a parent (reparent) | A | `PATCH …/parent` | 🟡 reparent UI + recursive tree view deferred |
| UC-ORG-08 | Toggle / break ACL inheritance from parent | A | `PATCH …/inherit-acl` | ✅ |
| UC-ORG-09 | Copy parent ACL as a standalone snapshot | A | `POST …/copy-acl-from-parent` | ✅ |
| UC-ORG-10 | Download a whole collection as a ZIP (queued) | V+ | `POST …/download-all` | ✅ |
| UC-ORG-11 | Bulk-delete / bulk-set-access on collections | A | `/admin/collection-access` | ✅ |

### F. Metadata & taxonomies
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-META-01 | View resolved asset metadata | V+ | asset detail, `GET …/metadata` | ✅ |
| UC-META-02 | Edit metadata / tags / taxonomy values | C+ | `EditAssetDialog`, `PUT …/metadata` | ✅ |
| UC-META-03 | Bulk-set metadata across assets | A | `POST /assets/bulk-metadata` | ✅ |
| UC-META-04 | Define a metadata schema (fields, types, required) | A | `/admin/metadata-schemas` | ✅ |
| UC-META-05 | Manage taxonomies (controlled vocab + terms) | A | `/admin/taxonomies` | ✅ |
| UC-META-06 | Required-metadata gate enforced at workflow submit | C+ | workflow submit | ✅ |
| UC-META-07 | Schema scope resolution (asset-type / collection / global) | SYS | `IMetadataSchemaQueryService` | ✅ |

### G. Discovery (search)
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-DISC-01 | Faceted full-text search (type/status/date/metadata/tags) | V+ | `/search`, `POST /assets/search` | ✅ |
| UC-DISC-02 | Save a search with a name | V+ | `SaveSearchDialog` | ✅ |
| UC-DISC-03 | Re-run / load a saved search | V+ | `SavedSearchesMenu` | ✅ |
| UC-DISC-04 | Get notified of new matches to a saved search | V+ | digest worker | 🟡 delivery shipped; batching + localisation deferred |
| UC-DISC-05 | Search results scoped to my ACL | V+ | `IAssetSearchService` | ✅ |

### H. View, deliver & render
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-VIEW-01 | Open asset detail | V+ | `/assets/{id}` | ✅ |
| UC-VIEW-02 | Preview media inline (image/video/audio/doc) | V+ | `MediaPreview`, `…/preview` | ✅ |
| UC-VIEW-03 | Download the original | V+ | `…/download` | ✅ |
| UC-VIEW-04 | Get thumbnail / medium / poster renditions | V+ | `…/thumb` `…/medium` `…/poster` | ✅ |
| UC-VIEW-05 | Request on-the-fly rendition (w/h/fit/fmt) | V+/API | `GET …/render` | 🟡 auth-only; signed-URL embedding + async-202 (>50MP) deferred |
| UC-VIEW-06 | See an asset's derivatives | V+ | `DerivativesPanel`, `…/derivatives` | ✅ |
| UC-VIEW-07 | See which collections an asset belongs to | V+ | `…/collections` | ✅ |

### I. Editing & export presets
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-EDIT-01 | Edit an image (crop/resize/rotate/filter/watermark) | C+ | `/assets/{id}/edit` | ✅ |
| UC-EDIT-02 | Save the edit as a new copy (lineage) | C+ | `SaveImageCopyDialog`, `…/save-copy` | 🟡 versioning interpretation of save-copy deferred |
| UC-EDIT-03 | Replace the original with the edited file | C+ | `…/replace-file` | ✅ |
| UC-EDIT-04 | Apply export presets to generate derivatives | C+/A | `ApplyExportPresetsHandler` | ✅ |
| UC-EDIT-05 | Manage export presets | A | `/admin/export-presets` | ✅ |

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
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-COLLAB-01 | Read comments on an asset | V+ | `AssetCommentsPanel`, `GET …/comments` | ✅ |
| UC-COLLAB-02 | Post a comment | C+ | `POST …/comments` | ✅ |
| UC-COLLAB-03 | @mention a user → notify them | C+ | server-side regex | 🟡 autocomplete + display-name/avatar + markdown deferred |
| UC-COLLAB-04 | Reply (single-level thread) | C+ | comments panel | ✅ |
| UC-COLLAB-05 | Edit / delete own comment (admin deletes any) | C+/A | `PATCH/DELETE …/comments/{id}` | ✅ (edit re-notifies mention diff) |

### M. Notifications
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-NOTIF-01 | See unread badge + bell dropdown | V+ | `NotificationBell` | ✅ |
| UC-NOTIF-02 | Open notification center, filter unread/all | V+ | `/notifications` | ✅ |
| UC-NOTIF-03 | Mark read (single/all) / delete | V+ | `…/read`, `…/read-all` | ✅ |
| UC-NOTIF-04 | Manage preferences (category, cadence, email) | V+ | `PUT …/preferences` | ✅ |
| UC-NOTIF-05 | Receive instant email for events | V+ | `SendNotificationEmailHandler` | ✅ |
| UC-NOTIF-06 | One-click unsubscribe from an email (DP-signed) | V+/ANON | `GET …/unsubscribe` | 🟡 page localisation deferred |
| UC-NOTIF-07 | Rotate unsubscribe token | V+ | `…/rotate-unsubscribe-token` | ✅ (user-visible UI deferred) |

### N. Publishing workflow & review
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-WF-01 | View an asset's workflow state | V+ | `WorkflowPanel`, `GET …/workflow` | ✅ |
| UC-WF-02 | Submit a draft for review (metadata gate) | C+ | `…/workflow/submit` | ✅ |
| UC-WF-03 | Approve / reject with a reason | M+ | `…/approve`, `…/reject` + `RejectReasonDialog` | 🚧 inline reason dialog uncommitted |
| UC-WF-04 | Publish / unpublish an approved asset | M+ | `…/publish`, `…/unpublish` | ✅ |
| UC-WF-05 | Resubmit a rejected asset | C+ | `…/workflow/submit` | ✅ |
| UC-WF-06 | Work a review **queue** (pending, scoped, assigned/unassigned) | M+ | `/review` + `IAssetReviewQueryService` | 🚧 in progress (uncommitted) |
| UC-WF-07 | Browse review **history** (decisions) | M+ | `/review/history` | 🚧 in progress (uncommitted) |
| UC-WF-08 | See workflow-state badge on grid cards | V+ | asset grid | 🟡 deferred |

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
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-BRAND-01 | Create / edit / delete a brand (logo + colours) | A | `/admin/brands` | 🟡 edit dialog UI deferred (API exists) |
| UC-BRAND-02 | Upload / remove a brand logo | A | `…/brands/{id}/logo` | ✅ |
| UC-BRAND-03 | Assign a brand to a collection | A | `PUT …/brands/{id}/collections/{cid}` | 🟡 assign-from-UI deferred (API exists) |
| UC-BRAND-04 | See a share page themed by brand | ANON | `/share/{token}` + `IBrandResolver` | ✅ custom CSS + custom domain deferred |

### Q. Guest invitations
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-GUEST-01 | Invite a guest by email (magic link) | A | `/admin/guests` | ✅ |
| UC-GUEST-02 | See the generated magic link once | A | `GuestMagicLinkShownDialog` | ✅ |
| UC-GUEST-03 | Guest accepts → provisioned + ACL granted | ANON→GUEST | `/guest-accept` | ✅ |
| UC-GUEST-04 | Revoke an invitation | A | `…/revoke` | ✅ |
| UC-GUEST-05 | Guest access auto-expires | SYS | `GuestInvitationExpirySweepService` | ✅ |
| UC-GUEST-06 | Resend an invitation | A | — | 🟡 deferred |
| UC-GUEST-07 | Inviter name / brand theming in the email | A | — | 🟡 deferred |

### R. Watermarking & forensics
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-WMK-01 | Toggle watermarking on a collection | M | `PATCH …/collections/{id}/watermark` | ✅ |
| UC-WMK-02 | Override / clear watermarking on an asset | C+ | `AssetWatermarkOverridePanel` | ✅ |
| UC-WMK-03 | Override watermarking on a share | C+ | `ShareWatermarkOverrideField` | ✅ |
| UC-WMK-04 | Recipient-fingerprinted watermark on download | SYS | two-layer DCT-LSB | ✅ |
| UC-WMK-05 | Verify a leaked image → recipient/share/asset (audited) | A | `/admin/watermarks/verify` | ✅ |
| UC-WMK-06 | Watermark on the share **preview** path | — | — | 🟡 deferred |

### S. Administration (users, ACL, audit, PATs)
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-ADMIN-01 | Admin console grouped Access/Content/Operations/Insights | A | `/admin` | ✅ |
| UC-ADMIN-02 | List / create / edit / delete users (Keycloak) | A | `/admin/users` | ✅ |
| UC-ADMIN-03 | Send a password-reset email | A | `…/users/{id}/reset-password` | ✅ |
| UC-ADMIN-04 | Sync deleted users (dry-run / live) | A | `…/users/sync` | ✅ |
| UC-ADMIN-05 | Manage per-collection ACL (grant/revoke roles) | M/A | `/admin/collection-access` | ✅ |
| UC-ADMIN-06 | Search users for the ACL picker | M/A | `…/acl/users/search` | ✅ |
| UC-ADMIN-07 | View / filter / paginate the audit log | A | `/admin/audit` | ✅ |
| UC-ADMIN-08 | Audit retention auto-prune | SYS | `AuditRetentionService` | ✅ |
| UC-ADMIN-09 | Self-service PAT create / list / revoke | V+ | `/account`, `…/me/personal-access-tokens` | ✅ |
| UC-ADMIN-10 | PAT cannot mint/revoke PATs (escalation guard) | API | `pat_id` guard | ✅ |

### T. Public API & integration
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-API-01 | Read assets/collections via scoped PAT | API | `assets:read` / `collections:read` | ✅ |
| UC-API-02 | Write assets/collections via scoped PAT | API | `assets:write` / `collections:write` | ✅ |
| UC-API-03 | Search via PAT | API | `search:read` | ✅ |
| UC-API-04 | Manage shares via PAT | API | `shares:write` | ✅ |
| UC-API-05 | Discover the API via OpenAPI / Swagger | API/dev | `/swagger` | ✅ |
| UC-API-06 | Per-endpoint scope enforcement on every public route | API | `RequireScopeFilter` | ✅ |

### U. Webhooks
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-HOOK-01 | Create a webhook (events, HMAC secret shown once) | A | `/admin/webhooks` | ✅ |
| UC-HOOK-02 | Edit / delete / rotate-secret | A | `…/webhooks/{id}` | ✅ |
| UC-HOOK-03 | Send a test event | A | `…/webhooks/{id}/test` | ✅ |
| UC-HOOK-04 | View delivery history | A | `WebhookDeliveriesDialog` | ✅ |
| UC-HOOK-05 | Downstream receives signed event w/ retry split | SYS | `DispatchWebhookHandler` | ✅ |
| UC-HOOK-06 | Full event-source coverage + 24h scheduled retry | SYS | event publishers | 🟡 some sources (asset.created/updated/deleted, share.accessed, migration.completed) + 24h retry deferred |

### V. Bulk import / migration
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-MIG-01 | Create a migration job | A | `/admin/migrations` | ✅ |
| UC-MIG-02 | Upload a CSV manifest | A | `…/migrations/{id}/manifest` | ✅ |
| UC-MIG-03 | Scan a source S3 bucket | A | `…/migrations/{id}/s3/scan` | ✅ |
| UC-MIG-04 | Upload staging files (multipart) | A | `…/migrations/{id}/files` | ✅ |
| UC-MIG-05 | Start / cancel / retry-failed | A | `…/start` `…/cancel` `…/retry` | ✅ |
| UC-MIG-06 | Poll progress / view items by status | A | `MigrationDetailDialog` | ✅ |
| UC-MIG-07 | Download the outcome CSV | A | `…/outcome.csv` | ✅ |
| UC-MIG-08 | Unstage an item / bulk-delete migrations | A | `…/unstage`, `…/bulk` | ✅ |
| UC-MIG-09 | Import from Bynder / Canto / SharePoint | A | connector | ⬜ planned (T0-MIG-03/04/05) |

### W. Analytics & exposure
| ID | Use case | Persona | Surface | Status |
|----|----------|---------|---------|:------:|
| UC-ANL-01 | View analytics dashboard (downloads/storage/exposure) | A | `/admin/analytics` | ✅ |
| UC-ANL-02 | View daily download trends | A | `…/downloads/daily` | ✅ |
| UC-ANL-03 | Storage by collection / by asset type | A | `…/storage/by-*` | ✅ |
| UC-ANL-04 | Top watermark recipients (hash-grouped, no PII) | A | `…/exposure` | ✅ |
| UC-ANL-05 | Reveal a recipient's PII (audited) | A | `…/exposure/reveal` | ✅ |
| UC-ANL-06 | Export CSV / queue a PDF report | A | `…/export.csv`, `…/export-pdf` | ✅ |
| UC-ANL-07 | Manager-scoped analytics / custom date range | M | — | 🟡 deferred |
| UC-ANL-08 | Rollups self-heal / back-fill on start | SYS | `AnalyticsRollupBackgroundService` | ✅ |

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
| 🟡 Partial | ~16 | UI polish (reparent, brand edit/assign, badges), embedding/async fallbacks, deferred autocomplete/markdown, manager-scoped analytics, webhook event-source coverage |
| 🚧 In progress | 3 | The `/review` queue + history + inline reject dialog (uncommitted) |
| ⬜ Planned | ~8 | T2 AI suite, T6 HA suite, non-S3 migration connectors |

---

## 4. How to use this catalog

This is the *what*. The next step — "how each use case is implemented" — is best done by
extending each row with implementation columns. Suggested working schema per use case:

| Field | Meaning |
|-------|---------|
| **Entry point** | The page/endpoint already in the Surface column |
| **Service / handler** | Application service + Infrastructure impl that does the work |
| **Auth path** | Policy + (if applicable) collection ACL check + PAT scope |
| **Persistence / side-effects** | Tables touched, MinIO objects, cache tags invalidated, audit events emitted |
| **Async tail** | Wolverine messages / background jobs triggered |
| **Tests** | Covering xUnit / bUnit / E2E specs (or "gap") |
| **Notes / risks** | Edge cases, deferrals, known issues |

> **Coverage signal from the test sweep:** E2E is strong on the core loop (auth → browse → upload
> → share → revoke) and role-visibility, but several shipped features are **backend-tested only** —
> versioning UI, metadata schemas, workflow approval end-to-end, webhooks, migrations pause/resume,
> guest expiry, branded theming, renditions, trash→restore, and analytics export. Those are the
> highest-value targets if we want each use case demonstrably exercised through the UI.
