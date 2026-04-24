namespace AssetHub.Domain.Entities;

/// <summary>
/// Append-only log of workflow state changes on an asset (T3-WF-01). Each
/// <see cref="Asset.WorkflowState"/> mutation writes one row here so the
/// UI can show the history and audit has something to reference. Cascades
/// on hard-delete of the asset.
/// </summary>
public class AssetWorkflowTransition
{
    public Guid Id { get; set; }

    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }

    public AssetWorkflowState FromState { get; set; }
    public AssetWorkflowState ToState { get; set; }

    /// <summary>Keycloak sub of the user who made the transition.</summary>
    public string ActorUserId { get; set; } = string.Empty;

    /// <summary>
    /// Submit note, approval note, rejection reason. Required on rejection;
    /// optional on all other transitions.
    /// </summary>
    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }
}
