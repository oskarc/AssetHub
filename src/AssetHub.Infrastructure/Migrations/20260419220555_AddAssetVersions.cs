using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing assets are conceptually v1 — they were never replaced. The entity
            // initializer is also 1, so this default keeps fresh inserts and back-filled rows
            // semantically aligned. EF's default of 0 here would be wrong: v0 doesn't exist.
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
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    OriginalObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ThumbObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    MediumObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PosterObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EditDocument = table.Column<string>(type: "jsonb", nullable: true),
                    MetadataSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangeNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetVersions");

            migrationBuilder.DropColumn(
                name: "CurrentVersionNumber",
                table: "Assets");
        }
    }
}
