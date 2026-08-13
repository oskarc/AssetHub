using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropPersonalAccessTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonalAccessTokens");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonalAccessTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Scopes = table.Column<List<string>>(type: "text[]", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalAccessTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_pat_owner_created",
                table: "PersonalAccessTokens",
                columns: new[] { "OwnerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_pat_token_hash_unique",
                table: "PersonalAccessTokens",
                column: "TokenHash",
                unique: true);
        }
    }
}
