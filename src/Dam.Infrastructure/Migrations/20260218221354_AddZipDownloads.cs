using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddZipDownloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ZipDownloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    HangfireJobId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ZipObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZipFileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ShareTokenHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    AssetCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZipDownloads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_assets_original_object_key",
                table: "Assets",
                column: "OriginalObjectKey");

            migrationBuilder.CreateIndex(
                name: "idx_zip_downloads_expires_at",
                table: "ZipDownloads",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "idx_zip_downloads_status",
                table: "ZipDownloads",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_zip_downloads_user_id",
                table: "ZipDownloads",
                column: "RequestedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZipDownloads");

            migrationBuilder.DropIndex(
                name: "idx_assets_original_object_key",
                table: "Assets");
        }
    }
}
