# Follow-ups — known deferred work from shipped features

Tracks pieces of shipped roadmap items that were deliberately scoped out, plus
small refinements identified during review that aren't worth their own roadmap
entry. Each item names the parent feature, what was deferred, the reason, and a
sketch of the suggested approach so future-us doesn't have to rediscover the
context.

When picking these up, move the entry to the parent feature's `Out of scope` /
shipped notes — or, if the work is broad enough, promote it to a fresh roadmap
ID.

---

## How to add a new entry

```
### <feature-id> — <short title>

**Deferred from**: <commit-hash or PR>
**Why deferred**: <one sentence — context the next picker needs>
**Sketch**: <a few bullets, including any specific files / interfaces / tests
to touch>
**Acceptance**: <what "done" looks like, brief>
```

Keep entries terse. Anything bigger than a paragraph belongs in `ROADMAP.md`.

---

## Open follow-ups

### T1-LIFE-01 — BulkAssetActionsDialog undo-snackbar

**Deferred from**: `13d4491` (T1-LIFE-01 phase 4-5)
**Why deferred**: Single-asset undo (AssetGrid, AssetDetail) needs one snackbar
+ one restore call. Bulk delete of N assets needs different UX — one snackbar
that restores all, or a list of N pending restores — and that design call is
worth its own pass rather than being shoe-horned into the same commit.
**Sketch**:
- Decide: single "Undo (N)" snackbar that restores the whole batch, vs.
  per-item snackbars (probably the former — N=20 single snackbars is noise).
- `IUserFeedbackService.ShowActionableInfo` already supports a generic
  callback; the bulk handler in `BulkAssetActionsDialog` would capture the
  list of deleted ids and the callback would loop `Api.RestoreFromTrashAsync`.
- Server already supports per-id restore — no API changes needed.
**Acceptance**: bulk delete shows one snackbar; clicking Undo restores every
asset that was just deleted; partial-failure surfaces as a separate
`Feedback.ShowError` listing the ids that didn't restore.

### T1-LIFE-01 — TrashPurgeBackgroundService integration test

**Deferred from**: `c301bd0` (T1-LIFE-01 phase 6)
**Why deferred**: Unit-test coverage on `AssetTrashService` is in place. End-
to-end test of the worker would need a hosted-service fixture (start the
worker against a Testcontainers Postgres with `TrashRetentionDays = 0` and
assert rows disappear after one tick). That fixture pattern doesn't exist
yet and is broader than this feature.
**Sketch**:
- New `WorkerFixture` (or extension to `PostgresFixture`) that builds a Host
  with the worker registered, runs `StartAsync`, ticks once, asserts.
- Test: seed an asset with `DeletedAt = now - 1 day`, set retention = 0 days,
  start worker, wait for one purge cycle (or invoke `RunPurgeAsync` directly
  via reflection / friend assembly), assert row is hard-deleted +
  `IMinIOAdapter.DeleteAssetObjectsAsync` was called.
- Will also serve `StaleUploadCleanupService`, `OrphanedSharesCleanupService`,
  `AuditRetentionService` — they currently have no integration coverage.
**Acceptance**: a `[Collection("Database")]` test class exercises one purge
cycle end-to-end and pins behaviour against a regression in the trigger /
filter / repo-method chain.

### T1-VER-01 — version thumbnail preview in history panel

**Deferred from**: `4007449` (T1-VER-01 phase 4-5)
**Why deferred**: The backend exposes `ThumbObjectKey` per version and the DTO
ships it, but `AssetVersionHistoryPanel` currently renders the timeline as a
plain table. Visual scanability is one small avatar away.
**Sketch**:
- Add a `MudAvatar` with `Image="@AssetDisplayHelpers.GetThumbnailUrl(assetId, version.ThumbObjectKey, assetType, version.PosterObjectKey)"`
  to the version column in `AssetVersionHistoryPanel.razor`.
- Asset type isn't on `AssetVersionDto` today — either add it (tiny API change)
  or pass the parent asset's type through as a panel parameter (cheaper).
**Acceptance**: each row in the panel shows a 40 px thumbnail of that
version's bytes; current version is visually identical to the asset page's
main thumbnail.

### T1-VER-01 — SaveImageCopy versioning interpretation

**Deferred from**: `a4fe14e` (T1-VER-01 phase 2-3)
**Why deferred**: The roadmap line "Replace + image-save-copy create a new
version" was interpreted as Replace only, because save-copy already creates a
separate derivative asset (with its own `SourceAssetId` trail) and versioning
on top would duplicate history. Flag a design decision, not a bug.
**Sketch**:
- If the intent is "save-copy also snapshots the source", then before writing
  the derivative, capture the source's current state to an AssetVersion on
  the SOURCE asset — same pattern as ReplaceImageFile.
- If the current interpretation stays, no work needed; document it under
  T1-VER-01's `Out of scope` section in ROADMAP.md when promoting this entry.
**Acceptance**: either the save-copy path creates a source-asset AssetVersion
alongside the derivative, or the ROADMAP entry explicitly confirms
single-interpretation.

### T1-VER-01 — AssetVersionService integration test against real Postgres

**Deferred from**: `4007449` (T1-VER-01 phase 4-5)
**Why deferred**: Same fixture gap as the T1-LIFE-01 purge-worker test.
Unit-test coverage via Moq is in; the cascade-on-purge invariant and the
restore-of-same-keys-shared-with-live-asset scenario are better exercised
against a real DbContext.
**Sketch**:
- Use `PostgresFixture.CreateMigratedDbContextAsync` (already exists from
  T1-SRCH-01) so the AssetVersions table and its unique index are live.
- Test: seed asset with 3 versions, restore v1, assert the asset row's keys
  match v1 AND a v4 row was created capturing the pre-restore state.
- Test: purge an asset with versions, assert version rows cascade-deleted
  AND MinIO was called for version-only keys (not shared ones).
**Acceptance**: `[Collection("Database")]` test class covers restore
round-trip and purge cascade, both against real EF with migrations applied.

### T3-NTF-01 — daily/weekly email batching

**Deferred from**: phase 3 (2026-04-24)
**Why deferred**: Phase 3 only sends email for `EmailCadence=instant`. A real
batching worker needs a per-user queue of pending notifications + a cadence
scheduler + a digest-email template that summarises many events into one
mail. That's an extra feature, not a refinement.
**Sketch**:
- Extend `Notification` with a `EmailDeliveryStatus` column (`pending` /
  `sent` / `skipped`). On create with non-instant cadence stamp `pending`.
- Add a `NotificationEmailDigestBackgroundService` that every N minutes
  groups `pending` notifications by user, renders a digest email template
  (grouped by category), sends, and flips them to `sent`. Respect per-user
  cadence (`daily` / `weekly`) via a `LastDigestAt` column on
  `NotificationPreferences`.
- Digest template already sketched by
  `NotificationEmailTemplate` — generalise to accept a list of items.
**Acceptance**: user sets `EmailCadence=daily` for `mention`; three mentions
arrive in a day; they get one email with three lines, not three emails.

### T3-NTF-01 — localise unsubscribe confirmation page + email template

**Deferred from**: phase 3 (2026-04-24)
**Why deferred**: Both the email body and the unsubscribe confirmation page
are English-only today because they're rendered outside any Blazor session
(worker host + email-client GET). Plumbing culture through requires either
storing a preferred culture per user in Keycloak / prefs, or embedding a
`lang` hint in the signed unsubscribe token (no user ref required for the
email template since it runs in worker context — needs the same stored
preference).
**Sketch**:
- Add `NotificationPreferences.PreferredCulture` (nullable string, e.g.
  `"sv-SE"`). Default null → fall back to `App:DefaultCulture` or `"en"`.
- Load + apply culture in `SendNotificationEmailHandler` before constructing
  `NotificationEmailTemplate`.
- For the unsubscribe endpoint, either embed culture in the signed payload
  or read from Keycloak by `userId` after unprotecting the token.
- Move the HTML in `NotificationEndpoints.UnsubscribeHtml` + the email
  template strings into a new `EmailsResource.{resx,sv.resx}`.
**Acceptance**: Swedish user receives mention notification in Swedish; the
unsubscribe page renders Swedish; Playwright spec covers both for EN + SV.

### T3-NTF-01 — SavedSearchDigestBackgroundService integration test

**Deferred from**: phase 3 (2026-04-24)
**Why deferred**: Same fixture gap as T1-LIFE-01 purge-worker and T1-VER-01
version-service. Unit dependencies are mocked; a real end-to-end tick
through Postgres + Wolverine + `IAssetSearchService` needs the shared
`WorkerFixture` pattern that does not yet exist.
**Sketch**:
- Same `WorkerFixture` as the other two items (Host with the hosted service,
  Testcontainers Postgres, Wolverine test harness).
- Seed an `Asset`, a `SavedSearch` with `Notify=Daily` and `LastRunAt=null`,
  tick the worker, assert a `Notification` row landed for the owner and
  `saved_search.digest_sent` was written.
**Acceptance**: `[Collection("Database")]` test class proves the digest
path end-to-end, pinned against regressions in the RequestJson
deserialisation / cadence gate / search-by-owner logic.

### T3-NTF-01 — user-visible unsubscribe-token rotation

**Deferred from**: phase 3 (2026-04-24)
**Why deferred**: `UnsubscribeTokenHash` is generated once at prefs creation
and never rotated. If a plaintext unsubscribe URL leaks (forwarded email,
screenshot of an email body) the user has no "invalidate outstanding
links" button. Low-risk because each token is category-scoped and only
flips `Email=false` — no privilege escalation — but the rotation infra is
already there; just missing the endpoint + UI.
**Sketch**:
- `POST /api/v1/notifications/preferences/rotate-unsubscribe-token` —
  authenticated, regenerates `UnsubscribeTokenHash`, invalidates every
  outstanding link.
- `NotificationPreferencesPanel.razor` in `/account` gets a "Rotate
  unsubscribe links" secondary button with a confirm dialog.
- Audit event `notification.unsubscribe_token_rotated`.
**Acceptance**: rotating invalidates previously-generated tokens (the
Unsubscribe endpoint returns the neutral "link not valid" page for an old
URL) and subsequent emails embed new URLs that work.

### T3-COL-01 — mention autocomplete in the comment editor

**Deferred from**: T3-COL-01 phase (2026-04-24)
**Why deferred**: Typing `@foo` resolves server-side, but there is no
suggestion dropdown while composing. Roadmap called for a MudAutocomplete
here; the missing piece is a client-facing user-search endpoint and wiring
in the editor.
**Sketch**:
- New `GET /api/v1/users/search?q={prefix}&take=N` — authenticated, viewer+,
  rate-limited, wraps `IUserLookupService.SearchUsersAsync` with a max of
  10 results. Returns `{ id, username, email }`.
- `AssetCommentEditor.razor` detects `@` at the caret, grabs the prefix,
  calls the endpoint, shows `MudAutocomplete` anchored at the caret. On
  select, replace the partial mention with the full `@username`.
- Debounce 150 ms; cancel the in-flight request on each keystroke.
**Acceptance**: typing `@al` while writing a comment shows a dropdown of
matching users, Enter inserts the full username, posting creates the
notification as today.

### T3-COL-01 — author display name + avatar resolution

**Deferred from**: T3-COL-01 phase (2026-04-24)
**Why deferred**: The comments panel shows `abcd1234…` (truncated Keycloak
sub) instead of `Alice Smith`. Backend already exposes
`IUserLookupService.GetUserNamesAsync`; UI just doesn't call it.
**Sketch**:
- On `AssetCommentsPanel.LoadAsync`, collect every `AuthorUserId` +
  `MentionedUserIds` and call a new `Api.GetUserNamesAsync(ids)` wrapper
  that proxies `IUserLookupService.GetUserNamesAsync`.
- Pass the `Dictionary<string,string>` down to `AssetCommentItem` and
  show the friendly name + initials-based avatar instead of the truncated
  sub.
- Cache per panel instance (don't re-fetch on every re-render); HybridCache
  on the server already memoises username lookups.
**Acceptance**: comment list renders `Alice Smith` + "AS" avatar for every
known author; unknown subs fall back to today's truncated form.

### T3-COL-01 — markdown rendering for comment bodies

**Deferred from**: T3-COL-01 phase (2026-04-24)
**Why deferred**: Spec said "markdown, sanitized" but phase 1 stuck to
plain text with newlines + HTML escaping (+ `@mention` chips). Pulling in
Markdig + an HTML sanitizer for bold / italic / links is a non-trivial
dependency for small user value unless a customer asks.
**Sketch**:
- Add `Markdig` + `Ganss.Xss` packages. Render on the *client* so we
  don't re-parse on every page load.
- Replace `RenderBodyWithMentions` with: escape → Markdig → sanitize
  output against a minimal allowlist (a, b, i, strong, em, code, ul, ol,
  li, p, br) → overlay mention regex on the safe HTML.
- Acceptance tests for XSS vectors: `<script>`, `<img onerror>`, javascript:
  hrefs, data: URIs, embedded HTML in code blocks.
**Acceptance**: `**bold**`, `_italic_`, inline `` `code` ``, and
`[label](https://…)` render as expected; no XSS vector from
`t3-col-01-xss-fuzz.spec.ts`.

### T3-WF-01 — workflow state badge on asset grid cards

**Deferred from**: T3-WF-01 phase (2026-04-24)
**Why deferred**: Badge is on AssetDetail only. Grid-card badge needs a
visual design pass so it doesn't crowd the thumbnail, and the card
component is reused in several contexts (grid page, collection page,
embedded lists). Worth its own UX review.
**Sketch**:
- Pass `WorkflowState` through `AssetResponseDto` (already in the
  backend DTO, just surface it to the grid mapper).
- Add an overlay chip on `AssetCard.razor` when state is not `Published` —
  small, top-right corner, same colour scheme as `WorkflowPanel.razor`.
- Make the badge optional via a panel parameter so admin / asset-detail
  callers can turn it off (they already show full state).
**Acceptance**: grid cards show a distinct visual for Draft / InReview /
Rejected / Approved; Published renders unchanged.

### T3-WF-01 — inline reason input for reject / submit

**Deferred from**: T3-WF-01 phase (2026-04-24)
**Why deferred**: `MudDialog.ShowMessageBox` can't embed a free-text
input — the UI currently confirms the action and sends a placeholder.
Workaround: reviewers currently write the reason in a comment
(T3-COL-01) before rejecting.
**Sketch**:
- New `WorkflowReasonDialog.razor` — `MudDialog` with a multiline
  `MudTextField` and Confirm / Cancel buttons, bound to `Reason`.
- In `WorkflowPanel.InvokeAsync`, swap the `ShowMessageBox` call for
  `DialogService.ShowAsync<WorkflowReasonDialog>` when the action is
  reject or when the server indicates a reason is accepted.
- Localisation keys already exist (`Dialog_Reason_Label`,
  `Dialog_Reason_Required_Label`).
**Acceptance**: Reject button opens a dialog with a reason field; Confirm
sends the typed text to the API and shows up in the transition history.

### T3-INT-01 — 24h scheduled retry queue for failed webhook deliveries

**Deferred from**: T3-INT-01 phase (2026-04-25)
**Why deferred**: Roadmap acceptance criterion "failures are retried up to
24 h" needs a scheduled-retry queue with progressively longer intervals
(5 min, 30 min, 2 h, 6 h, 24 h). Today's implementation uses Wolverine's
existing 5-step cooldown (~50 s total) — catches transient blips well,
but a multi-hour receiver outage marks Failed after under a minute.
**Sketch**:
- Background service that wakes every N minutes, finds `WebhookDelivery`
  rows in `Failed` state where `LastAttemptAt + nextBackoff(AttemptCount)`
  has elapsed and the attempt count is below a hard ceiling (e.g. 12).
- Re-publishes `DispatchWebhookCommand` for those rows; the existing
  handler picks up where it left off (idempotency comes from the
  Status check at top of HandleAsync).
- Track ceiling via a config setting (`Webhook:MaxRetryAge = 24h`).
- Audit when the ceiling is hit — distinct from the existing
  `webhook.delivery_failed_permanently` so dashboards can distinguish
  "5 quick retries lost" from "24 hours of unreachable".
**Acceptance**: a webhook receiver returning 503 for 23 hours then 200
delivers successfully; same receiver returning 503 for 25 hours stops
trying and records the give-up event.

### T3-INT-01 — wire remaining event sources

**Deferred from**: T3-INT-01 phase (2026-04-25)
**Why deferred**: v1 wires 4 of the 9 spec'd event sources
(`comment.created`, `workflow.state_changed`, `share.created`,
`asset.restored`). Missing: `asset.created`, `asset.updated`,
`asset.deleted`, plus `share.accessed` (high-volume — probably should
remain telemetry) and `migration.completed`. The asset CRUD trio touches
3+ different services (AssetService, AssetUploadService, AssetService
delete paths, AssetTrashService) — worth a focused pass rather than
grafting into the initial ship.
**Sketch**:
- `asset.created`: emit from `AssetUploadService` after the asset row is
  finalised (`MarkReady` path, not at upload-start, so subscribers don't
  see processing assets).
- `asset.updated`: emit from `AssetService.UpdateAsync` after audit.
  Include `changedFields[]` in the payload, similar to the audit
  details, so subscribers can no-op on irrelevant edits.
- `asset.deleted`: emit from the soft-delete path that already audits
  `asset.deleted` (currently in `AssetService` and the implicit-orphan
  branch). Skip the trash-purge path — that's a separate `asset.purged`
  event for FOLLOW-UPS later.
- Skip `share.accessed` (every public-share GET would emit; volume too
  high for webhook reliability targets).
- Skip `migration.completed` until customer demand — admin-internal
  notification already covers it via T3-NTF-01.
**Acceptance**: 3 new event types appear in `WebhookEvents.All` and the
admin create-webhook dropdown; subscribers receive distinct events for
upload, edit, and soft-delete; tests cover the new emit points.

### T4-BP-01 — sanitised custom CSS

**Deferred from**: T4-BP-01 phase (2026-04-25)
**Why deferred**: Spec called for optional custom CSS in `Brand`; v1
shipped only colour variables + logo. CSS injection is a real attack
surface — `@import` fetches arbitrary stylesheets, attribute selectors
with `background-image: url(…)` exfiltrate form values, font-family can
fingerprint, etc. A naive `<style>{Brand.CustomCss}</style>` is XSS-grade
risk. Worth a dedicated security pass before shipping.
**Sketch**:
- Add `Brand.CustomCss` (`string?`, max ~10 KB).
- Pull in a CSS parser (`AngleSharp.Css` or hand-rolled) and an
  allowlist: drop `@import`, `@font-face`, `url(http*://*)` (only
  `url(/...)` allowed and validated), restrict to a specific subset
  of properties (color, background-color, font-family from a fixed
  list, border-radius, padding/margin, transitions).
- Render via `<style nonce="…">` with CSP `style-src` adjusted.
- Test corpus: every CSS-injection example from
  https://portswigger.net/research/css-injection.
**Acceptance**: pasting any of the test corpus into the field results in
either a sanitised pass-through or a 400 with a specific reason; the
sanitised CSS only mutates the brand's colour palette and typography on
the share page.

### T4-BP-01 — custom domain support

**Deferred from**: T4-BP-01 phase (2026-04-25)
**Why deferred**: Spec mentioned "optional custom domain". That's not a
UI feature, it's a piece of infrastructure: DNS routing, TLS cert
provisioning (Let's Encrypt or AWS ACM), tenant-aware request routing
in the reverse proxy, ownership verification flow. A whole feature of
its own, probably its own roadmap entry rather than a follow-up.
**Sketch**:
- New `BrandDomain` entity (brand id, fqdn, verification token, status,
  cert ARN/path).
- Verification: TXT record at `_assethub-verify.<domain>`.
- Cert provisioning: pluggable `ICertProvider` (LetsEncrypt impl + an
  AWS ACM impl).
- Reverse proxy (Nginx / Traefik) reads brand domain table to route
  inbound traffic to the right brand context.
**Acceptance**: admin adds `brand.example.com` to a brand, follows the
verification steps, and a public share opened at `brand.example.com/share/{token}`
renders with the brand applied + a valid TLS cert.

### T4-BP-01 — brand edit dialog

**Deferred from**: T4-BP-01 phase (2026-04-25)
**Why deferred**: API supports PATCH (`UpdateBrandDto` with all
nullable fields), API client method exists, but no dialog was shipped.
Admins today can change a brand's appearance only by deleting and
recreating it.
**Sketch**:
- New `EditBrandDialog.razor` modelled on `CreateBrandDialog.razor`.
- Wire from a row-level "edit" icon button on `AdminBrandsTab`.
- Reuse the same hex-validation regex constant.
**Acceptance**: clicking Edit opens a populated dialog; saving issues
PATCH; the row updates in place without a full reload.

### T4-BP-01 — assign brand to collection from UI

**Deferred from**: T4-BP-01 phase (2026-04-25)
**Why deferred**: API endpoints (`PUT/DELETE /api/v1/admin/brands/{id}/collections/{cid}`)
and client methods are wired, but there's no UI surface yet. Admins
who need brand-per-collection have to mark a brand as default (which
applies to everything) or call the API directly.
**Sketch**:
- Add a "Brand" column to `AdminCollectionAccessTab` with a
  `MudSelect<Guid?>` populated from the brands list. On change, call
  the assign / unassign API.
- Or: a "Brand assignments" sub-panel inside the brand detail showing
  every collection that currently uses this brand, with a multi-select
  to add more.
**Acceptance**: admin picks a brand from a dropdown next to a
collection name, hits Save, and a public share of an asset in that
collection renders with the chosen brand.

### Test infra — full Release suite EF flake

**Deferred from**: noted across multiple sessions
**Why deferred**: Running the full `dotnet test --configuration Release` suite
intermittently produces ~159 cascading failures from EF's
`ManyServiceProvidersCreatedWarning`. `PostgresFixture` already suppresses it;
`CustomWebApplicationFactory` does not. CI runs without parallelism caps so
the cascade reproduces there too.
**Sketch**:
- Add `.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))`
  to the `services.AddDbContext<AssetHubDbContext>(...)` and the matching
  `AddDbContextFactory<AssetHubDbContext>(...)` calls inside
  `CustomWebApplicationFactory.cs` (lines ~102 and ~107).
- Verify: full Release suite runs to completion without the cascade.
**Acceptance**: `dotnet test --configuration Release` returns the same
deterministic 0 failures locally and in CI.
