using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class NotificationPreferencesConfiguration : IEntityTypeConfiguration<NotificationPreferences>
{
    public void Configure(EntityTypeBuilder<NotificationPreferences> entity)
    {
        entity.HasKey(e => e.Id);
        // One row per user. The service upserts into this index.
        entity.HasIndex(e => e.UserId).IsUnique()
            .HasDatabaseName("idx_notif_prefs_user_unique");
        // Anonymous unsubscribe endpoint looks up by token hash; must be unique + indexed.
        entity.HasIndex(e => e.UnsubscribeTokenHash).IsUnique()
            .HasDatabaseName("idx_notif_prefs_unsubscribe_hash_unique");

        entity.Property(e => e.UserId).HasMaxLength(255).IsRequired();
        entity.Property(e => e.UnsubscribeTokenHash).HasMaxLength(64).IsRequired();

        entity.Property(e => e.Categories)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, NotificationCategoryPrefs>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, NotificationCategoryPrefs>())
            .HasColumnType(ModelConventions.Jsonb)
            .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, NotificationCategoryPrefs>>(
                (c1, c2) => JsonSerializer.Serialize(c1, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c2, (JsonSerializerOptions?)null),
                c => JsonSerializer.Serialize(c, (JsonSerializerOptions?)null).GetHashCode(),
                c => JsonSerializer.Deserialize<Dictionary<string, NotificationCategoryPrefs>>(JsonSerializer.Serialize(c, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
    }
}
