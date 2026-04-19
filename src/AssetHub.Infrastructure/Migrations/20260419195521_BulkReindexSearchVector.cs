using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BulkReindexSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Follow-up to AddAssetSearchAndSavedSearch. The original migration's backfill iterated
            // one row at a time, calling assets_refresh_search_vector() per asset — roughly
            // O(n) round trips to build vectors Postgres could just build in a single pass. This
            // rewrites it as one set-based UPDATE, keeping it idempotent (running again produces
            // identical output) so deploys already past the previous migration are unaffected and
            // future fresh deploys go faster.
            migrationBuilder.Sql(@"
UPDATE ""Assets"" a
SET search_vector =
    setweight(to_tsvector('simple', coalesce(a.""Title"", '')), 'A') ||
    setweight(to_tsvector('simple', coalesce(a.""Description"", '')), 'B') ||
    setweight(to_tsvector('simple', coalesce(array_to_string(a.""Tags"", ' ', ''), '')), 'C') ||
    setweight(to_tsvector('simple', coalesce(meta.joined_text, '')), 'D')
FROM (
    SELECT v.""AssetId"" AS asset_id,
           string_agg(v.""ValueText"", ' ') AS joined_text
    FROM ""AssetMetadataValues"" v
    JOIN ""MetadataFields"" f ON f.""Id"" = v.""MetadataFieldId""
    WHERE f.""Searchable"" = true
      AND v.""ValueText"" IS NOT NULL
    GROUP BY v.""AssetId""
) meta
WHERE a.""Id"" = meta.asset_id;

-- Cover assets with no searchable metadata so their vectors still reflect the latest Asset fields.
UPDATE ""Assets"" a
SET search_vector =
    setweight(to_tsvector('simple', coalesce(a.""Title"", '')), 'A') ||
    setweight(to_tsvector('simple', coalesce(a.""Description"", '')), 'B') ||
    setweight(to_tsvector('simple', coalesce(array_to_string(a.""Tags"", ' ', ''), '')), 'C')
WHERE NOT EXISTS (
    SELECT 1 FROM ""AssetMetadataValues"" v
    JOIN ""MetadataFields"" f ON f.""Id"" = v.""MetadataFieldId""
    WHERE v.""AssetId"" = a.""Id""
      AND f.""Searchable"" = true
      AND v.""ValueText"" IS NOT NULL
);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: Up() is a pure re-index of an existing column. Nothing to undo — the
            // search_vector column itself was created by the previous migration.
        }
    }
}
