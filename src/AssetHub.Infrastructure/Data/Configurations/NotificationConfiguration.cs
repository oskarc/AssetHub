using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> entity)
    {
        entity.HasKey(e => e.Id);
        // Bell list + unread count both filter by UserId and order by CreatedAt DESC.
        entity.HasIndex(e => new { e.UserId, e.CreatedAt })
            .HasDatabaseName("idx_notifications_user_created");
        // Unread scan is the hot-path for the badge; partial index via a composite works fine on Postgres.
        entity.HasIndex(e => new { e.UserId, e.ReadAt })
            .HasDatabaseName("idx_notifications_user_read");

        entity.Property(e => e.UserId).HasMaxLength(255).IsRequired();
        entity.Property(e => e.Category).HasMaxLength(64).IsRequired();
        entity.Property(e => e.Title).HasMaxLength(255).IsRequired();
        entity.Property(e => e.Body).HasMaxLength(2000);
        entity.Property(e => e.Url).HasMaxLength(500);

        entity.Property(e => e.Data)
            .HasConversion(ModelConventions.JsonbDictionaryConverter)
            .HasColumnType(ModelConventions.Jsonb)
            .Metadata.SetValueComparer(ModelConventions.JsonbDictionaryComparer);
    }
}
