using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddUniqueCollectionName : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Replace non-unique index with a unique index on lower(name) to enforce
        // case-insensitive uniqueness at the database level (prevents race conditions).
        migrationBuilder.DropIndex(
            name: "idx_collections_name",
            table: "Collections");

        migrationBuilder.Sql(
            """CREATE UNIQUE INDEX idx_collections_name_unique ON "Collections" (lower("Name"));""");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """DROP INDEX IF EXISTS idx_collections_name_unique;""");

        migrationBuilder.CreateIndex(
            name: "idx_collections_name",
            table: "Collections",
            column: "Name");
    }
}
