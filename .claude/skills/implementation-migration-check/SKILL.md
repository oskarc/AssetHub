---
name: implementation-migration-check
description: Audit an EF Core migration (Up, Down, snapshot) for safety before it auto-runs at startup. Use when a new migration was added, an entity config changed, or the user asks to verify a migration.
---

# AssetHub Migration Safety Check

AssetHub auto-migrates on Api + Worker startup (`Database.MigrateAsync()`), so a bad migration ships as a bad deploy. This skill audits migrations against the rules in CLAUDE.md § Database Migrations plus PostgreSQL-specific gotchas, before the commit leaves the branch.

## How to run

1. **Scope** — find the migrations to review:
   - If args provided, use them.
   - Otherwise: migration files under `src/AssetHub.Infrastructure/Migrations/` that are untracked (`git status --porcelain`) or in the last commit (`git show --name-only HEAD`).
   - Include the `.cs`, `.Designer.cs`, and `AssetHubDbContextModelSnapshot.cs` trio.
   - If nothing matches, ask the user which migration to audit.
2. **Read** all files in full, plus the matching `OnModelCreating` block in `AssetHubDbContext.cs` for every entity the migration touches.
3. **Walk the checklist** below, citing `file:line` on every finding.
4. **Cross-check** the snapshot: every new FK, index, and column in the migration must also appear in `AssetHubDbContextModelSnapshot.cs`. A drifted snapshot means someone hand-edited a migration — dangerous.
5. **Report** grouped by severity (Critical / Major / Minor) with a concrete fix.
6. **Offer to apply** safe fixes (rename indexes, add missing `Down()`, add FK constraints, fix column types). Ask before applying.

## Checklist

### Destructive operations
- **`DropColumn` / `DropTable` / `DropForeignKey`** — is the referencing code already removed in an earlier migration or commit? If not, this migration will break the app before or during deploy. Flag CRITICAL.
- **Rename without data migration** — `RenameColumn` or `RenameTable` in isolation is a data-loss risk if an old replica reads. Pair with a backfill step or split across two releases.
- **`AlterColumn` type change** — must include a `using` conversion for Postgres or explicit cast. Otherwise Postgres fails on non-trivial conversions. Flag CRITICAL if missing.

### Non-nullable additions
- **`AddColumn` with `nullable: false` and no `defaultValue` / `defaultValueSql`** — fails on a non-empty table. Either make it nullable, supply a default, or run a backfill in raw SQL before the column becomes required.
- **`AddColumn` with `nullable: false`, a default, but semantic meaning (e.g., `Version` starts at 1)** — verify the default matches the entity's initializer in Domain.

### Indexes and keys
- **Index naming**: `idx_{entity}_{fields}` (snake-separated), `_unique` suffix for `unique: true`. Flag any `IX_*` auto-generated names in new indexes.
- **FKs without matching index** — Postgres does not auto-index FK columns. If the migration adds a FK to `X`, it should also add `idx_{table}_{X}`.
- **`OnDelete` explicit** — no migration should rely on the default; every FK must set `ReferentialAction.Cascade` / `Restrict` / `SetNull` / `NoAction` and the choice must match the entity's intent.
- **`IsUnique()`** indexes — verify the uniqueness is a real invariant (not just test data convenience) and that the column is NOT NULL or the unique constraint intentionally treats NULLs as distinct.

### Raw SQL
- **`migrationBuilder.Sql(...)`** — must be idempotent. Use `CREATE INDEX IF NOT EXISTS`, `CREATE EXTENSION IF NOT EXISTS`, `DO $$ ... $$ IF NOT EXISTS` patterns. Non-idempotent raw SQL breaks the Api-vs-Worker startup race.
- **pg_trgm or other extensions** — create via `CREATE EXTENSION IF NOT EXISTS pg_trgm;` before any `gin_trgm_ops` index.
- **No interpolated user data** — raw SQL in a migration should never touch runtime data; it's DDL only.

### JSONB columns
- Column type set to `type: "jsonb"`.
- Matching `OnModelCreating` has a `HasConversion<string>(...)` or JSON serializer + a `ValueComparer` (critical for change tracking — see `Asset.Tags`, `Asset.Metadata` as the reference).
- Flag if the migration adds a JSONB column but the entity config has no `ValueComparer`.

### `Down()` method
- Must exist and reverse every operation in `Up()` in reverse order.
- If `Down()` cannot safely reverse (e.g., data loss, enum widening), document why in a comment and still provide the closest reversal.

### Snapshot drift
- For every `CreateTable`, `AddColumn`, `CreateIndex`, `AddForeignKey` in the migration, grep `AssetHubDbContextModelSnapshot.cs` for the corresponding `b.Property`, `b.HasIndex`, `b.HasOne` entry.
- Any missing entry indicates a hand-edited migration — force the user to regenerate via `dotnet ef migrations add`.

### Startup-race safety
- Both Api and Worker call `MigrateAsync()` on boot. The first to acquire the advisory lock wins; the other no-ops. The migration must survive being seen by a replica that hasn't started it yet — which means all DDL must be idempotent when combined with EF's `__EFMigrationsHistory` tracking.
- Flag anything that relies on a specific call order across processes.

### Naming and convention
- `PascalCaseName` for the migration class.
- Timestamp prefix is generated by EF — never hand-edit.
- `.Designer.cs` and snapshot are auto-generated — never hand-edit.

## Output

Report grouped by severity. For each finding:
- File and line.
- Rule violated.
- Concrete fix (migration code or regeneration command).

Include a **regeneration command** if the fix requires EF to rewrite the migration:

```
dotnet ef migrations remove --project src/AssetHub.Infrastructure --startup-project src/AssetHub.Api --force
# fix entity config in AssetHubDbContext.cs
dotnet ef migrations add <Name> --project src/AssetHub.Infrastructure --startup-project src/AssetHub.Api
```

## Abort conditions

- Migration already applied to a shared environment — report but do not suggest destructive changes.
- Schema drift larger than the migration scope (e.g., the snapshot references entities the migration doesn't touch) — escalate to the user; do not rewrite the snapshot by hand.
