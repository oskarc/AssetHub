using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill value for existing rows — they've been shareable up
            // to now, so "published" preserves the pre-migration behaviour.
            // The EF model doesn't declare HasDefaultValue (see DbContext
            // comment for why), so this only affects this one-time backfill.
            migrationBuilder.AddColumn<string>(
                name: "WorkflowState",
                table: "Assets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "published");

            migrationBuilder.AddColumn<DateTime>(
                name: "WorkflowStateUpdatedAt",
                table: "Assets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetWorkflowTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromState = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ToState = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetWorkflowTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetWorkflowTransitions_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_assets_workflow_state",
                table: "Assets",
                column: "WorkflowState");

            migrationBuilder.CreateIndex(
                name: "idx_asset_workflow_transition_asset_created",
                table: "AssetWorkflowTransitions",
                columns: new[] { "AssetId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetWorkflowTransitions");

            migrationBuilder.DropIndex(
                name: "idx_assets_workflow_state",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "WorkflowState",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "WorkflowStateUpdatedAt",
                table: "Assets");
        }
    }
}
