using System;
using Dam.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Dam.Infrastructure.Migrations
{
    [DbContext(typeof(AssetHubDbContext))]
    partial class AssetHubDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Dam.Domain.Entities.Asset", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("AssetType")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<Guid>("CollectionId")
                        .HasColumnType("uuid");

                    b.Property<string>("ContentType")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("CreatedByUserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Description")
                        .HasMaxLength(2000)
                        .HasColumnType("character varying(2000)");

                    b.Property<string>("MediumObjectKey")
                        .HasMaxLength(512)
                        .HasColumnType("character varying(512)");

                    b.Property<string>("OriginalObjectKey")
                        .IsRequired()
                        .HasMaxLength(512)
                        .HasColumnType("character varying(512)");

                    b.Property<string>("PosterObjectKey")
                        .HasMaxLength(512)
                        .HasColumnType("character varying(512)");

                    b.Property<string>("Sha256")
                        .HasColumnType("text");

                    b.Property<long>("SizeBytes")
                        .HasColumnType("bigint");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("Tags")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("ThumbObjectKey")
                        .HasMaxLength(512)
                        .HasColumnType("character varying(512)");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(500)
                        .HasColumnType("character varying(500)");

                    b.Property<DateTime>("UpdatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("MetadataJson")
                        .IsRequired()
                        .HasColumnType("jsonb");

                    b.HasKey("Id");

                    b.HasIndex("CollectionId")
                        .HasDatabaseName("idx_assets_collection_id");

                    b.HasIndex("CreatedAt")
                        .HasDatabaseName("idx_assets_created_at");

                    b.HasIndex("Status")
                        .HasDatabaseName("idx_assets_status");

                    b.HasIndex("AssetType")
                        .HasDatabaseName("idx_assets_type");

                    b.ToTable("Assets");
                });

            modelBuilder.Entity("Dam.Domain.Entities.AuditEvent", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("ActorUserId")
                        .HasColumnType("text");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("EventType")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)");

                    b.Property<string>("IP")
                        .HasColumnType("text");

                    b.Property<Guid?>("TargetId")
                        .HasColumnType("uuid");

                    b.Property<string>("TargetType")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)");

                    b.Property<string>("UserAgent")
                        .HasColumnType("text");

                    b.Property<string>("DetailsJson")
                        .IsRequired()
                        .HasColumnType("jsonb");

                    b.HasKey("Id");

                    b.HasIndex("CreatedAt")
                        .HasDatabaseName("idx_audit_created_at");

                    b.HasIndex("EventType", "CreatedAt")
                        .HasDatabaseName("idx_audit_event_type_created");

                    b.ToTable("AuditEvents");
                });

            modelBuilder.Entity("Dam.Domain.Entities.Collection", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("CreatedByUserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Description")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)");

                    b.Property<Guid?>("ParentId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .HasDatabaseName("idx_collections_name");

                    b.HasIndex("ParentId")
                        .HasDatabaseName("idx_collections_parent_id");

                    b.ToTable("Collections");
                });

            modelBuilder.Entity("Dam.Domain.Entities.CollectionAcl", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("CollectionId")
                        .HasColumnType("uuid");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("PrincipalId")
                        .IsRequired()
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)");

                    b.Property<string>("PrincipalType")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("Role")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.HasKey("Id");

                    b.HasIndex("CollectionId")
                        .HasDatabaseName("idx_collection_acl_collection_id");

                    b.HasIndex("PrincipalType", "PrincipalId")
                        .HasDatabaseName("idx_collection_acl_principal");

                    b.ToTable("CollectionAcls");
                });

            modelBuilder.Entity("Dam.Domain.Entities.Share", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<int>("AccessCount")
                        .HasColumnType("integer");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("CreatedByUserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime>("ExpiresAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime?>("LastAccessedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("PasswordHash")
                        .HasColumnType("text");

                    b.Property<string>("PermissionsJson")
                        .IsRequired()
                        .HasColumnType("jsonb");

                    b.Property<DateTime?>("RevokedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<Guid>("ScopeId")
                        .HasColumnType("uuid");

                    b.Property<string>("ScopeType")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("TokenHash")
                        .IsRequired()
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)");

                    b.HasKey("Id");

                    b.HasIndex("ScopeType", "ScopeId")
                        .HasDatabaseName("idx_shares_scope");

                    b.HasIndex("TokenHash")
                        .IsUnique()
                        .HasDatabaseName("idx_shares_token_hash_unique");

                    b.ToTable("Shares");
                });

            modelBuilder.Entity("Dam.Domain.Entities.Asset", b =>
                {
                    b.HasOne("Dam.Domain.Entities.Collection", "Collection")
                        .WithMany("Assets")
                        .HasForeignKey("CollectionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Collection");
                });

            modelBuilder.Entity("Dam.Domain.Entities.CollectionAcl", b =>
                {
                    b.HasOne("Dam.Domain.Entities.Collection", "Collection")
                        .WithMany("Acls")
                        .HasForeignKey("CollectionId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Collection");
                });

            modelBuilder.Entity("Dam.Domain.Entities.Collection", b =>
                {
                    b.HasOne("Dam.Domain.Entities.Collection", "Parent")
                        .WithMany("Children")
                        .HasForeignKey("ParentId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.Navigation("Parent");
                });

            modelBuilder.Entity("Dam.Domain.Entities.Collection", b =>
                {
                    b.Navigation("Acls");
                    b.Navigation("Assets");
                    b.Navigation("Children");
                });
#pragma warning restore 612, 618
        }
    }
}
