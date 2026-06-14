using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class AssetCommentConfiguration : IEntityTypeConfiguration<AssetComment>
{
    public void Configure(EntityTypeBuilder<AssetComment> entity)
    {
        entity.HasKey(e => e.Id);

        // Comments panel loads N latest top-level + replies for one asset.
        entity.HasIndex(e => new { e.AssetId, e.CreatedAt })
            .HasDatabaseName("idx_asset_comment_asset_created");

        // Threading fetch: find all replies under a parent.
        entity.HasIndex(e => e.ParentCommentId)
            .HasDatabaseName("idx_asset_comment_parent");

        entity.Property(e => e.AuthorUserId).HasMaxLength(255).IsRequired();
        entity.Property(e => e.Body).HasMaxLength(4000).IsRequired();

        // Postgres text[] mirrors PersonalAccessToken.Scopes. Cheap to
        // store the resolved ids so the notification fan-out doesn't
        // re-parse the body on read, and we can group-by mentioned user
        // for future "mentions of me" views.
        entity.Property(e => e.MentionedUserIds)
            .HasColumnType(ModelConventions.TextArray)
            .Metadata.SetValueComparer(ModelConventions.StringListComparer);

        // Cascade with the Asset row on hard-delete / purge. Soft delete
        // is hidden via the Asset global query filter (we only read
        // comments through the asset's scope).
        entity.HasOne(e => e.Asset)
            .WithMany()
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        // Threaded comments: a deleted parent cascades to its replies
        // (Postgres default; no orphan replies pointing at nothing).
        entity.HasOne(e => e.ParentComment)
            .WithMany()
            .HasForeignKey(e => e.ParentCommentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
