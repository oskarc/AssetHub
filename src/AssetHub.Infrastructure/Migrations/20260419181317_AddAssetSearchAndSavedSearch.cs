using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetSearchAndSavedSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "Assets",
                type: "tsvector",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SavedSearches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: false),
                    Notify = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastHighestSeenAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedSearches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_asset_search_vector",
                table: "Assets",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "idx_saved_searches_owner",
                table: "SavedSearches",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "idx_saved_searches_owner_name_unique",
                table: "SavedSearches",
                columns: new[] { "OwnerUserId", "Name" },
                unique: true);

            // Function that rebuilds the composite search_vector for a single asset.
            // Combines the asset's own searchable fields with the concatenated text values of
            // every metadata field marked Searchable. Weights: Title=A, Description=B, Tags=C,
            // metadata=D — see ts_rank ordering for why this matters to result relevance.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION assets_refresh_search_vector(p_asset_id uuid) RETURNS void AS $$
BEGIN
    UPDATE ""Assets"" a
    SET search_vector =
        setweight(to_tsvector('simple', coalesce(a.""Title"", '')), 'A') ||
        setweight(to_tsvector('simple', coalesce(a.""Description"", '')), 'B') ||
        setweight(to_tsvector('simple', coalesce(array_to_string(a.""Tags"", ' ', ''), '')), 'C') ||
        setweight(to_tsvector('simple', coalesce((
            SELECT string_agg(v.""ValueText"", ' ')
            FROM ""AssetMetadataValues"" v
            JOIN ""MetadataFields"" f ON f.""Id"" = v.""MetadataFieldId""
            WHERE v.""AssetId"" = a.""Id""
              AND f.""Searchable"" = true
              AND v.""ValueText"" IS NOT NULL
        ), '')), 'D')
    WHERE a.""Id"" = p_asset_id;
END;
$$ LANGUAGE plpgsql;
");

            // Trigger on Assets: recompute search_vector whenever title/description/tags change or a new row is inserted.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION tg_assets_refresh_search_vector() RETURNS trigger AS $$
BEGIN
    PERFORM assets_refresh_search_vector(NEW.""Id"");
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_assets_search_vector_ins ON ""Assets"";
CREATE TRIGGER tg_assets_search_vector_ins
    AFTER INSERT ON ""Assets""
    FOR EACH ROW
    EXECUTE FUNCTION tg_assets_refresh_search_vector();

DROP TRIGGER IF EXISTS tg_assets_search_vector_upd ON ""Assets"";
CREATE TRIGGER tg_assets_search_vector_upd
    AFTER UPDATE OF ""Title"", ""Description"", ""Tags"" ON ""Assets""
    FOR EACH ROW
    EXECUTE FUNCTION tg_assets_refresh_search_vector();
");

            // Trigger on AssetMetadataValues: recompute the affected asset's search_vector on any change.
            // Handles AssetId moves (rare but possible) by refreshing both old and new.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION tg_asset_metadata_values_refresh_search() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        PERFORM assets_refresh_search_vector(OLD.""AssetId"");
        RETURN OLD;
    ELSE
        PERFORM assets_refresh_search_vector(NEW.""AssetId"");
        IF TG_OP = 'UPDATE' AND NEW.""AssetId"" <> OLD.""AssetId"" THEN
            PERFORM assets_refresh_search_vector(OLD.""AssetId"");
        END IF;
        RETURN NEW;
    END IF;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_asset_metadata_values_search ON ""AssetMetadataValues"";
CREATE TRIGGER tg_asset_metadata_values_search
    AFTER INSERT OR UPDATE OR DELETE ON ""AssetMetadataValues""
    FOR EACH ROW
    EXECUTE FUNCTION tg_asset_metadata_values_refresh_search();
");

            // Trigger on MetadataFields.Searchable: flip includes/excludes the field's values for every asset that has them.
            // Schema-admin operation — worth taking the hit to recompute affected assets.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION tg_metadata_fields_refresh_search() RETURNS trigger AS $$
DECLARE
    affected_asset_id uuid;
BEGIN
    IF NEW.""Searchable"" IS DISTINCT FROM OLD.""Searchable"" THEN
        FOR affected_asset_id IN
            SELECT DISTINCT v.""AssetId"" FROM ""AssetMetadataValues"" v
            WHERE v.""MetadataFieldId"" = NEW.""Id""
        LOOP
            PERFORM assets_refresh_search_vector(affected_asset_id);
        END LOOP;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_metadata_fields_searchable ON ""MetadataFields"";
CREATE TRIGGER tg_metadata_fields_searchable
    AFTER UPDATE OF ""Searchable"" ON ""MetadataFields""
    FOR EACH ROW
    EXECUTE FUNCTION tg_metadata_fields_refresh_search();
");

            // Backfill: compute search_vector for every existing asset. Cheap even at 100k rows
            // because we're iterating in Postgres, not shipping data to the app.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    asset_id uuid;
BEGIN
    FOR asset_id IN SELECT ""Id"" FROM ""Assets"" LOOP
        PERFORM assets_refresh_search_vector(asset_id);
    END LOOP;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS tg_metadata_fields_searchable ON ""MetadataFields"";
DROP TRIGGER IF EXISTS tg_asset_metadata_values_search ON ""AssetMetadataValues"";
DROP TRIGGER IF EXISTS tg_assets_search_vector_upd ON ""Assets"";
DROP TRIGGER IF EXISTS tg_assets_search_vector_ins ON ""Assets"";

DROP FUNCTION IF EXISTS tg_metadata_fields_refresh_search();
DROP FUNCTION IF EXISTS tg_asset_metadata_values_refresh_search();
DROP FUNCTION IF EXISTS tg_assets_refresh_search_vector();
DROP FUNCTION IF EXISTS assets_refresh_search_vector(uuid);
");

            migrationBuilder.DropTable(
                name: "SavedSearches");

            migrationBuilder.DropIndex(
                name: "idx_asset_search_vector",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "Assets");
        }
    }
}
