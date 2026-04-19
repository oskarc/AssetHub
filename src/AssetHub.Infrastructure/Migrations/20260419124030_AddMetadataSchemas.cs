using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetadataSchemas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Scope = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AssetType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
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
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
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
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    LabelSv = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Required = table.Column<bool>(type: "boolean", nullable: false),
                    Searchable = table.Column<bool>(type: "boolean", nullable: false),
                    Facetable = table.Column<bool>(type: "boolean", nullable: false),
                    PatternRegex = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MaxLength = table.Column<int>(type: "integer", nullable: true),
                    NumericMin = table.Column<decimal>(type: "numeric", nullable: true),
                    NumericMax = table.Column<decimal>(type: "numeric", nullable: true),
                    SelectOptions = table.Column<List<string>>(type: "text[]", nullable: false),
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
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
                    TaxonomyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentTermId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    ValueText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ValueNumeric = table.Column<decimal>(type: "numeric", nullable: true),
                    ValueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValueTaxonomyTermId = table.Column<Guid>(type: "uuid", nullable: true)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}
