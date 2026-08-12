using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropImageEditorAndExportPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_Assets_SourceAssetId",
                table: "Assets");

            migrationBuilder.DropTable(
                name: "ExportPresets");

            migrationBuilder.DropIndex(
                name: "idx_assets_source_asset_id",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "EditDocument",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "SourceAssetId",
                table: "Assets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EditDocument",
                table: "Assets",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceAssetId",
                table: "Assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExportPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FitMode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Format = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Quality = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportPresets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_assets_source_asset_id",
                table: "Assets",
                column: "SourceAssetId");

            migrationBuilder.CreateIndex(
                name: "idx_export_presets_name_unique",
                table: "ExportPresets",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_Assets_SourceAssetId",
                table: "Assets",
                column: "SourceAssetId",
                principalTable: "Assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
