namespace AssetHub.Application.Dtos;

/// <summary>
/// Returned by the deletion-context endpoint to inform the UI
/// whether a multi-collection prompt is needed.
/// </summary>
public class AssetDeletionContextDto
{
    /// <summary>Total number of collections this asset belongs to.</summary>
    public int CollectionCount { get; set; }

    /// <summary>True when the current user has manager+ role on every collection the asset belongs to.</summary>
    public bool CanDeletePermanently { get; set; }
}
