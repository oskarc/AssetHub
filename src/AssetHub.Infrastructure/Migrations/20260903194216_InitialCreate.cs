using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Copyright = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: true),
                    OriginalObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ThumbObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    MediumObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PosterObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    AudioBitrateKbps = table.Column<int>(type: "integer", nullable: true),
                    AudioSampleRateHz = table.Column<int>(type: "integer", nullable: true),
                    AudioChannels = table.Column<int>(type: "integer", nullable: true),
                    WaveformPeaksPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "text", nullable: true),
                    CurrentVersionNumber = table.Column<int>(type: "integer", nullable: false),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorUserId = table.Column<string>(type: "text", nullable: true),
                    IP = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    TargetType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrphanedObjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BucketName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrphanedObjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageType = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TokenEncrypted = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ScopeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    PasswordEncrypted = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PasswordVersion = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    LastAccessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AccessCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shares", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ZipDownloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    HangfireJobId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ZipObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ZipFileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ShareTokenHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    AssetCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZipDownloads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    OriginalObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ThumbObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    MediumObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PosterObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EditDocument = table.Column<string>(type: "jsonb", nullable: true),
                    MetadataSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangeNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetVersions_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCollections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetCollections_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetCollections_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectionAcls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PrincipalId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionAcls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectionAcls_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_asset_collection_collection_id",
                table: "AssetCollections",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "idx_asset_collection_unique",
                table: "AssetCollections",
                columns: new[] { "AssetId", "CollectionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_asset_search_vector",
                table: "Assets",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "idx_assets_created_at",
                table: "Assets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "idx_assets_created_by_user_id",
                table: "Assets",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "idx_assets_deleted_at",
                table: "Assets",
                column: "DeletedAt",
                filter: "\"DeletedAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_assets_original_object_key",
                table: "Assets",
                column: "OriginalObjectKey");

            migrationBuilder.CreateIndex(
                name: "idx_assets_status",
                table: "Assets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_assets_type",
                table: "Assets",
                column: "AssetType");

            migrationBuilder.CreateIndex(
                name: "idx_asset_version_asset_version_unique",
                table: "AssetVersions",
                columns: new[] { "AssetId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_audit_created_at",
                table: "AuditEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "idx_audit_event_type_created",
                table: "AuditEvents",
                columns: new[] { "EventType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_audit_target_id",
                table: "AuditEvents",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "idx_collection_acl_collection_id",
                table: "CollectionAcls",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "idx_collection_acl_principal",
                table: "CollectionAcls",
                columns: new[] { "PrincipalType", "PrincipalId" });

            migrationBuilder.CreateIndex(
                name: "idx_collection_acl_unique",
                table: "CollectionAcls",
                columns: new[] { "CollectionId", "PrincipalType", "PrincipalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_collections_name_unique",
                table: "Collections",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_orphaned_object_attempts_created",
                table: "OrphanedObjects",
                columns: new[] { "AttemptCount", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_outbox_pending",
                table: "OutboxMessages",
                columns: new[] { "DispatchedAt", "AttemptCount", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_shares_created_by_user_id",
                table: "Shares",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "idx_shares_expires_at",
                table: "Shares",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "idx_shares_scope",
                table: "Shares",
                columns: new[] { "ScopeType", "ScopeId" });

            migrationBuilder.CreateIndex(
                name: "idx_shares_token_hash_unique",
                table: "Shares",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_zip_downloads_expires_at",
                table: "ZipDownloads",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "idx_zip_downloads_status",
                table: "ZipDownloads",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "idx_zip_downloads_user_id",
                table: "ZipDownloads",
                column: "RequestedByUserId");

            // ── Raw SQL preserved from the pre-squash migrations (contract-029) ──
            // EF's model-based generation does not produce these; the schema-diff
            // gate (pg_dump of the 51-migration DB vs this one) is what proves they
            // must be here for full-text/fuzzy search and the historical column
            // defaults to match exactly.

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql(@"
                CREATE FUNCTION public.assets_refresh_search_vector(p_asset_id uuid) RETURNS void
                    LANGUAGE plpgsql AS $$
                BEGIN
                    UPDATE ""Assets"" a
                    SET search_vector =
                        setweight(to_tsvector('simple', coalesce(a.""Title"", '')), 'A') ||
                        setweight(to_tsvector('simple', coalesce(a.""Description"", '')), 'B') ||
                        setweight(to_tsvector('simple', coalesce(array_to_string(a.""Tags"", ' ', ''), '')), 'C')
                    WHERE a.""Id"" = p_asset_id;
                END;
                $$;");

            migrationBuilder.Sql(@"
                CREATE FUNCTION public.tg_assets_refresh_search_vector() RETURNS trigger
                    LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM assets_refresh_search_vector(NEW.""Id"");
                    RETURN NEW;
                END;
                $$;");

            migrationBuilder.Sql(@"CREATE TRIGGER tg_assets_search_vector_ins AFTER INSERT ON ""Assets""
                FOR EACH ROW EXECUTE FUNCTION tg_assets_refresh_search_vector();");
            migrationBuilder.Sql(@"CREATE TRIGGER tg_assets_search_vector_upd AFTER UPDATE OF ""Title"", ""Description"", ""Tags"" ON ""Assets""
                FOR EACH ROW EXECUTE FUNCTION tg_assets_refresh_search_vector();");

            // Column defaults that lived in raw SQL, not the model, pre-squash.
            migrationBuilder.Sql(@"ALTER TABLE ""Assets"" ALTER COLUMN ""Tags"" SET DEFAULT '{}'::text[];");
            migrationBuilder.Sql(@"ALTER TABLE ""Assets"" ALTER COLUMN ""CurrentVersionNumber"" SET DEFAULT 1;");
            migrationBuilder.Sql(@"ALTER TABLE ""Shares"" ALTER COLUMN ""PasswordVersion"" SET DEFAULT 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Tear down the raw-SQL objects first (contract-029), reverse order.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tg_assets_search_vector_upd ON \"Assets\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS tg_assets_search_vector_ins ON \"Assets\";");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tg_assets_refresh_search_vector();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS assets_refresh_search_vector(uuid);");
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AssetCollections");

            migrationBuilder.DropTable(
                name: "AssetVersions");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "CollectionAcls");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "OrphanedObjects");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "Shares");

            migrationBuilder.DropTable(
                name: "ZipDownloads");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Assets");

            migrationBuilder.DropTable(
                name: "Collections");
        }
    }
}
