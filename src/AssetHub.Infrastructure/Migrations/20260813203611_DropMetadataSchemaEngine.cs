using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropMetadataSchemaEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The full-text search vector was computed by joining the metadata tables, and two
            // triggers on those tables kept it fresh. Both must go BEFORE the tables do, or the
            // drops fail on the dependent triggers and assets_refresh_search_vector is left
            // referencing relations that no longer exist — which would break every asset insert.
            // Search itself is unaffected: Title/Description/Tags keep weights A/B/C exactly as
            // before; only the D-weighted metadata segment is removed.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS tg_metadata_fields_searchable ON "MetadataFields";
                DROP TRIGGER IF EXISTS tg_asset_metadata_values_search ON "AssetMetadataValues";
                DROP FUNCTION IF EXISTS tg_metadata_fields_refresh_search();
                DROP FUNCTION IF EXISTS tg_asset_metadata_values_refresh_search();
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION assets_refresh_search_vector(p_asset_id uuid) RETURNS void AS $$
                BEGIN
                    UPDATE "Assets" a
                    SET search_vector =
                        setweight(to_tsvector('simple', coalesce(a."Title", '')), 'A') ||
                        setweight(to_tsvector('simple', coalesce(a."Description", '')), 'B') ||
                        setweight(to_tsvector('simple', coalesce(array_to_string(a."Tags", ' ', ''), '')), 'C')
                    WHERE a."Id" = p_asset_id;
                END;
                $$ LANGUAGE plpgsql;
                """);

            // Existing rows keep a stale D segment until touched; reindex once so search results
            // stop matching on metadata values that are no longer reachable or editable.
            migrationBuilder.Sql("""
                DO $$
                DECLARE r record;
                BEGIN
                    FOR r IN SELECT "Id" FROM "Assets" LOOP
                        PERFORM assets_refresh_search_vector(r."Id");
                    END LOOP;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "AssetMetadataValues");

            migrationBuilder.DropTable(
                name: "MetadataFields");

            migrationBuilder.DropTable(
                name: "TaxonomyTerms");

            migrationBuilder.DropTable(
                name: "MetadataSchemas");

            migrationBuilder.DropTable(
                name: "Taxonomies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetadataSchemas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Scope = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataSchemas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetadataSchemas_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Taxonomies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Taxonomies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetadataFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataSchemaId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Facetable = table.Column<bool>(type: "boolean", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    LabelSv = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    MaxLength = table.Column<int>(type: "integer", nullable: true),
                    NumericMax = table.Column<decimal>(type: "numeric", nullable: true),
                    NumericMin = table.Column<decimal>(type: "numeric", nullable: true),
                    PatternRegex = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    Searchable = table.Column<bool>(type: "boolean", nullable: false),
                    SelectOptions = table.Column<List<string>>(type: "text[]", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetadataFields_MetadataSchemas_MetadataSchemaId",
                        column: x => x.MetadataSchemaId,
                        principalTable: "MetadataSchemas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MetadataFields_Taxonomies_TaxonomyId",
                        column: x => x.TaxonomyId,
                        principalTable: "Taxonomies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TaxonomyTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentTermId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    LabelSv = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Slug = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxonomyTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxonomyTerms_Taxonomies_TaxonomyId",
                        column: x => x.TaxonomyId,
                        principalTable: "Taxonomies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaxonomyTerms_TaxonomyTerms_ParentTermId",
                        column: x => x.ParentTermId,
                        principalTable: "TaxonomyTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetMetadataValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataFieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValueTaxonomyTermId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValueNumeric = table.Column<decimal>(type: "numeric", nullable: true),
                    ValueText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetMetadataValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetMetadataValues_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetMetadataValues_MetadataFields_MetadataFieldId",
                        column: x => x.MetadataFieldId,
                        principalTable: "MetadataFields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetMetadataValues_TaxonomyTerms_ValueTaxonomyTermId",
                        column: x => x.ValueTaxonomyTermId,
                        principalTable: "TaxonomyTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "idx_asset_metadata_values_asset",
                table: "AssetMetadataValues",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "idx_asset_metadata_values_field_asset",
                table: "AssetMetadataValues",
                columns: new[] { "MetadataFieldId", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "idx_asset_metadata_values_taxonomy_term",
                table: "AssetMetadataValues",
                column: "ValueTaxonomyTermId",
                filter: "\"ValueTaxonomyTermId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_metadata_fields_schema_key_unique",
                table: "MetadataFields",
                columns: new[] { "MetadataSchemaId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_metadata_fields_schema_sort",
                table: "MetadataFields",
                columns: new[] { "MetadataSchemaId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MetadataFields_TaxonomyId",
                table: "MetadataFields",
                column: "TaxonomyId");

            migrationBuilder.CreateIndex(
                name: "idx_metadata_schemas_collection_id",
                table: "MetadataSchemas",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "idx_metadata_schemas_name_unique",
                table: "MetadataSchemas",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_metadata_schemas_scope",
                table: "MetadataSchemas",
                column: "Scope");

            migrationBuilder.CreateIndex(
                name: "idx_taxonomies_name_unique",
                table: "Taxonomies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_taxonomy_terms_parent",
                table: "TaxonomyTerms",
                column: "ParentTermId");

            migrationBuilder.CreateIndex(
                name: "idx_taxonomy_terms_taxonomy_slug_unique",
                table: "TaxonomyTerms",
                columns: new[] { "TaxonomyId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_taxonomy_terms_taxonomy_sort",
                table: "TaxonomyTerms",
                columns: new[] { "TaxonomyId", "SortOrder" });

            // Restore the metadata-aware search vector and its two triggers, so a rollback
            // returns search to its prior behaviour rather than to silently-degraded ranking.
            // Byte-identical to AddAssetSearchAndSavedSearch, which is the definition this
            // migration replaced.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION assets_refresh_search_vector(p_asset_id uuid) RETURNS void AS $$
                BEGIN
                    UPDATE "Assets" a
                    SET search_vector =
                        setweight(to_tsvector('simple', coalesce(a."Title", '')), 'A') ||
                        setweight(to_tsvector('simple', coalesce(a."Description", '')), 'B') ||
                        setweight(to_tsvector('simple', coalesce(array_to_string(a."Tags", ' ', ''), '')), 'C') ||
                        setweight(to_tsvector('simple', coalesce((
                            SELECT string_agg(v."ValueText", ' ')
                            FROM "AssetMetadataValues" v
                            JOIN "MetadataFields" f ON f."Id" = v."MetadataFieldId"
                            WHERE v."AssetId" = a."Id"
                              AND f."Searchable" = true
                              AND v."ValueText" IS NOT NULL
                        ), '')), 'D')
                    WHERE a."Id" = p_asset_id;
                END;
                $$ LANGUAGE plpgsql;

                CREATE OR REPLACE FUNCTION tg_asset_metadata_values_refresh_search() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        PERFORM assets_refresh_search_vector(OLD."AssetId");
                        RETURN OLD;
                    ELSE
                        PERFORM assets_refresh_search_vector(NEW."AssetId");
                        IF TG_OP = 'UPDATE' AND NEW."AssetId" <> OLD."AssetId" THEN
                            PERFORM assets_refresh_search_vector(OLD."AssetId");
                        END IF;
                        RETURN NEW;
                    END IF;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS tg_asset_metadata_values_search ON "AssetMetadataValues";
                CREATE TRIGGER tg_asset_metadata_values_search
                    AFTER INSERT OR UPDATE OR DELETE ON "AssetMetadataValues"
                    FOR EACH ROW
                    EXECUTE FUNCTION tg_asset_metadata_values_refresh_search();

                CREATE OR REPLACE FUNCTION tg_metadata_fields_refresh_search() RETURNS trigger AS $$
                DECLARE
                    affected_asset_id uuid;
                BEGIN
                    IF NEW."Searchable" IS DISTINCT FROM OLD."Searchable" THEN
                        FOR affected_asset_id IN
                            SELECT DISTINCT v."AssetId" FROM "AssetMetadataValues" v
                            WHERE v."MetadataFieldId" = NEW."Id"
                        LOOP
                            PERFORM assets_refresh_search_vector(affected_asset_id);
                        END LOOP;
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS tg_metadata_fields_searchable ON "MetadataFields";
                CREATE TRIGGER tg_metadata_fields_searchable
                    AFTER UPDATE OF "Searchable" ON "MetadataFields"
                    FOR EACH ROW
                    EXECUTE FUNCTION tg_metadata_fields_refresh_search();
                """);
        }
    }
}
