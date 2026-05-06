using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWatermarkSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WatermarkOverride",
                table: "Shares",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WatermarkEnabled",
                table: "Collections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AssetWatermarkAppliedAt",
                table: "Assets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssetWatermarkToken",
                table: "Assets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WatermarkOverride",
                table: "Assets",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WatermarkDownloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedRecipientUserId = table.Column<byte[]>(type: "bytea", nullable: true),
                    EncryptedRecipientEmail = table.Column<byte[]>(type: "bytea", nullable: true),
                    RecipientUserIdHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    RecipientEmailHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    ShareId = table.Column<Guid>(type: "uuid", nullable: true),
                    DownloadPath = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DownloadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PayloadHmac = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatermarkDownloads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_watermark_download_asset_downloaded_at",
                table: "WatermarkDownloads",
                columns: new[] { "AssetId", "DownloadedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_watermark_download_recipient_email_hash",
                table: "WatermarkDownloads",
                column: "RecipientEmailHash");

            migrationBuilder.CreateIndex(
                name: "idx_watermark_download_recipient_user_hash",
                table: "WatermarkDownloads",
                column: "RecipientUserIdHash");

            migrationBuilder.CreateIndex(
                name: "idx_watermark_download_share",
                table: "WatermarkDownloads",
                column: "ShareId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WatermarkDownloads");

            migrationBuilder.DropColumn(
                name: "WatermarkOverride",
                table: "Shares");

            migrationBuilder.DropColumn(
                name: "WatermarkEnabled",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "AssetWatermarkAppliedAt",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "AssetWatermarkToken",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "WatermarkOverride",
                table: "Assets");
        }
    }
}
