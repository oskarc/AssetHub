using System.Text.Json;
using AssetHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace AssetHub.Infrastructure.Data.Configurations;

public sealed class AssetWorkflowTransitionConfiguration : IEntityTypeConfiguration<AssetWorkflowTransition>
{
    public void Configure(EntityTypeBuilder<AssetWorkflowTransition> entity)
    {
        entity.HasKey(e => e.Id);

        // Panel loads the history for one asset in timestamp order.
        entity.HasIndex(e => new { e.AssetId, e.CreatedAt })
            .HasDatabaseName("idx_asset_workflow_transition_asset_created");

        entity.Property(e => e.FromState)
            .HasConversion(v => v.ToDbString(), v => v.ToAssetWorkflowState())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.ToState)
            .HasConversion(v => v.ToDbString(), v => v.ToAssetWorkflowState())
            .HasMaxLength(50).IsRequired();
        entity.Property(e => e.ActorUserId).HasMaxLength(255).IsRequired();
        entity.Property(e => e.Reason).HasMaxLength(1000);

        entity.HasOne(e => e.Asset)
            .WithMany()
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
