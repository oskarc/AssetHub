---
description: "Use when creating EF Core database migrations, altering schema, adding indexes, or changing column types in AssetHub."
---
# Database Migration Conventions

## Generating Migrations

```powershell
$env:EF_CONNECTION = "Host=localhost;Database=assethub;Username=assethub;Password=assethub123"
dotnet ef migrations add <Name> --project src/AssetHub.Infrastructure --startup-project src/AssetHub.Api
```

Migration name: PascalCase describing the change (e.g., `AddAssetCopyrightField`, `CreateShareTokenIndex`).

## Safety Rules

### Never in the same migration
- **Drop a column** and **remove code** that references it — do these in separate releases.
- **Rename a column** without a data migration step.
- **Change a column type** without explicit data conversion SQL.

### Always include
- **`Down()` method** that reverses the `Up()` — verify it compiles and makes semantic sense.
- **Index names** following the convention `idx_{entity}_{fields}` (with `_unique` suffix for unique):
  ```csharp
  migrationBuilder.CreateIndex(
      name: "idx_asset_status_created",
      table: "Assets",
      columns: new[] { "Status", "CreatedAt" });
  ```

## JSONB Columns

When adding a new JSONB column, the migration must set the column type explicitly:

```csharp
migrationBuilder.AddColumn<string>(
    name: "MetadataJson",
    table: "Assets",
    type: "jsonb",
    nullable: false,
    defaultValue: "{}");
```

The corresponding `OnModelCreating` config must include a `ValueComparer` — see `infrastructure-services.instructions.md` for the full pattern.

## pg_trgm Indexes

For text search columns, create trigram GIN indexes via raw SQL:

```csharp
migrationBuilder.Sql(
    "CREATE INDEX IF NOT EXISTS idx_asset_title_trgm ON \"Assets\" USING gin (\"Title\" gin_trgm_ops);");
```

Ensure the `pg_trgm` extension is created (already present in the initial migration).

## Auto-Migration

AssetHub auto-migrates on startup (`Database.MigrateAsync()`). This means:
- Migrations must be **idempotent** — use `IF NOT EXISTS` in raw SQL.
- Destructive operations (drop table/column) require careful coordination.
- Both API and Worker hosts run migrations — only the first one to acquire the lock applies them.
