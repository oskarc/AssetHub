using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionParentIdAndInheritFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.CreateIndex(
                name: "idx_collections_parent_id",
                table: "Collections",
                column: "ParentCollectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Collections_Collections_ParentCollectionId",
                table: "Collections",
                column: "ParentCollectionId",
                principalTable: "Collections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Collections_Collections_ParentCollectionId",
                table: "Collections");

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
    }
}
