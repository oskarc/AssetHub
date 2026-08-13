using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropMigrationSourceConnectors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remote-pull (S3) migrations go with their connector. Their rows must be
            // removed before the columns are: SourceConfig holds the encrypted access
            // credentials for the remote bucket, so deleting them is also secret
            // hygiene, and without SourceType a leftover s3 row would be
            // indistinguishable from a CSV one whose staged files never existed.
            // Items first (FK), then the migrations. Idempotent: the predicate matches
            // nothing on a database that never had an s3 migration.
            // NOT reversible — Down restores the columns, not the deleted rows.
            migrationBuilder.Sql("""
                DELETE FROM "MigrationItems"
                WHERE "MigrationId" IN (SELECT "Id" FROM "Migrations" WHERE "SourceType" = 's3');
                """);
            migrationBuilder.Sql("""
                DELETE FROM "Migrations" WHERE "SourceType" = 's3';
                """);

            migrationBuilder.DropColumn(
                name: "SourceConfig",
                table: "Migrations");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "Migrations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceConfig",
                table: "Migrations",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "Migrations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }
    }
}
