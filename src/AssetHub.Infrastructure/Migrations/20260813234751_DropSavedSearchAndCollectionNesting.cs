using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropSavedSearchAndCollectionNesting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Collections_Collections_ParentCollectionId",
                table: "Collections");

            migrationBuilder.DropTable(
                name: "SavedSearches");

            migrationBuilder.DropIndex(
                name: "idx_collections_parent_id",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "InheritParentAcl",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "ParentCollectionId",
                table: "Collections");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InheritParentAcl",
                table: "Collections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentCollectionId",
                table: "Collections",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SavedSearches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastHighestSeenAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Notify = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedSearches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_collections_parent_id",
                table: "Collections",
                column: "ParentCollectionId");

            migrationBuilder.CreateIndex(
                name: "idx_saved_searches_owner",
                table: "SavedSearches",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "idx_saved_searches_owner_name_unique",
                table: "SavedSearches",
                columns: new[] { "OwnerUserId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Collections_Collections_ParentCollectionId",
                table: "Collections",
                column: "ParentCollectionId",
                principalTable: "Collections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
