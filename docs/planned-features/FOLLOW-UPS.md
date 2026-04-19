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
