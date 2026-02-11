using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConvertTagsToJsonb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Convert existing comma-separated tags to JSON arrays while column is still text
            migrationBuilder.Sql("""
                UPDATE "Assets"
                SET "Tags" = CASE
                    WHEN "Tags" IS NULL OR "Tags" = '' THEN '[]'
                    ELSE (
                        SELECT jsonb_agg(trim(elem))::text
                        FROM unnest(string_to_array("Tags", ',')) AS elem
                        WHERE trim(elem) <> ''
                    )
                END;
                
                -- Handle any rows where jsonb_agg returned null (no valid tags)
                UPDATE "Assets" SET "Tags" = '[]' WHERE "Tags" IS NULL;
                """);

            // Step 2: Alter column type to jsonb (USING clause required for text→jsonb cast)
            migrationBuilder.Sql("""
                ALTER TABLE "Assets" ALTER COLUMN "Tags" TYPE jsonb USING "Tags"::jsonb;
                ALTER TABLE "Assets" ALTER COLUMN "Tags" SET DEFAULT '[]'::jsonb;
                ALTER TABLE "Assets" ALTER COLUMN "Tags" SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Tags",
                table: "Assets",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");
        }
    }
}
