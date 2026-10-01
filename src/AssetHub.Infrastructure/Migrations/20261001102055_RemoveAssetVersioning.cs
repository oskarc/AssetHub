using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    // DESTRUCTIVE (contract-034, versioning cut). Up drops the AssetVersions table and the
    // Assets.CurrentVersionNumber column; their rows cannot be recovered. Down restores the
    // schema only: the table comes back empty and every asset's CurrentVersionNumber is 1
    // (the default InitialCreate set, hand-corrected here from EF's scaffolded 0).
    //
    // Operator note: before applying, list any original files that only a version row
    // points to. They stay in MinIO afterwards; deleting them is optional. Query (psql):
    //   SELECT DISTINCT v."OriginalObjectKey" FROM "AssetVersions" v WHERE NOT EXISTS
    //   (SELECT 1 FROM "Assets" a WHERE a."OriginalObjectKey" = v."OriginalObjectKey")
    // Thumbnail, medium and poster keys are fixed per asset and never version-only: never
    // delete them on this basis.
    /// <inheritdoc />
    public partial class RemoveAssetVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetVersions");

            migrationBuilder.DropColumn(
                name: "CurrentVersionNumber",
                table: "Assets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentVersionNumber",
                table: "Assets",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "AssetVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EditDocument = table.Column<string>(type: "jsonb", nullable: true),
                    MediumObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    MetadataSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    OriginalObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PosterObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ThumbObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetVersions_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_asset_version_asset_version_unique",
                table: "AssetVersions",
                columns: new[] { "AssetId", "VersionNumber" },
                unique: true);
        }
    }
}
