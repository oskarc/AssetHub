using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropAnalyticsRollups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data loss is accepted by design: rollup rows are pre-aggregated derivatives of
            // audit/download events, re-derivable while those sources are within retention.
            // Down() restores the schema only (contract-002, reshape C1 analytics cut).
            //
            // IF EXISTS is load-bearing, not defensive: the paired 20260508210000_AddAnalyticsRollups
            // migration was hand-written without a Designer.cs and therefore carries no [Migration]
            // attribute — EF never discovered it, so migration-managed databases never created these
            // tables. Only EnsureCreated-provisioned databases (test fixtures) have them. This drop
            // must succeed against both states.
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "AnalyticsDailyRollups";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "AnalyticsPdfJobs";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "AnalyticsStorageRollups";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyticsDailyRollups",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Metric = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analytics_daily_rollups", x => new { x.Date, x.Metric, x.EntityId });
                });

            migrationBuilder.CreateTable(
                name: "AnalyticsPdfJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    RequestedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    WindowDays = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsPdfJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnalyticsStorageRollups",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Metric = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    AssetCount = table.Column<int>(type: "integer", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analytics_storage_rollups", x => new { x.Date, x.Metric, x.EntityId });
                });

            migrationBuilder.CreateIndex(
                name: "idx_analytics_daily_date_metric",
                table: "AnalyticsDailyRollups",
                columns: new[] { "Date", "Metric" });

            migrationBuilder.CreateIndex(
                name: "idx_analytics_pdf_jobs_expires_at",
                table: "AnalyticsPdfJobs",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "idx_analytics_pdf_jobs_status",
                table: "AnalyticsPdfJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_analytics_storage_date_metric",
                table: "AnalyticsStorageRollups",
                columns: new[] { "Date", "Metric" });
        }
    }
}
