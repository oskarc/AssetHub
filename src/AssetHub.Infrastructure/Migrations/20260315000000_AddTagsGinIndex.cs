using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations;

/// <summary>
/// Adds a GIN index on the Tags JSONB column for efficient tag search queries.
/// </summary>
public partial class AddTagsGinIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Assets_Tags"
            ON "Assets" USING GIN ("Tags");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_Assets_Tags";
            """);
    }
}
