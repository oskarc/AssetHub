using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <summary>
    /// T5-ANL-01 — pre-aggregated analytics rollup tables + queued PDF export jobs.
    /// Hand-written because the local <c>dotnet ef</c> tool binding mismatched the
    /// runtime Abstractions assembly; the schema is small and the change is
    /// additive-only so manual authoring is straightforward.
    /// </summary>
    public partial class AddAnalyticsRollups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.CreateIndex(
                name: "idx_analytics_daily_date_metric",
                table: "AnalyticsDailyRollups",
                columns: new[] { "Date", "Metric" });

            migrationBuilder.CreateTable(
                name: "AnalyticsStorageRollups",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Metric = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    AssetCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analytics_storage_rollups", x => new { x.Date, x.Metric, x.EntityId });
                });

            migrationBuilder.CreateIndex(
                name: "idx_analytics_storage_date_metric",
                table: "AnalyticsStorageRollups",
                columns: new[] { "Date", "Metric" });

            migrationBuilder.CreateTable(
                name: "AnalyticsPdfJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    WindowDays = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsPdfJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_analytics_pdf_jobs_status",
                table: "AnalyticsPdfJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_analytics_pdf_jobs_expires_at",
                table: "AnalyticsPdfJobs",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AnalyticsPdfJobs");
            migrationBuilder.DropTable(name: "AnalyticsStorageRollups");
            migrationBuilder.DropTable(name: "AnalyticsDailyRollups");
        }
    }
}
