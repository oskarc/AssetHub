namespace AssetHub.Domain.Entities;

/// <summary>
/// Publishing workflow states for an asset (T3-WF-01).
/// Transitions: Draft → InReview → Approved → Published. InReview can move to
/// Rejected, which can be resubmitted back to InReview. Published can be
/// moved back to Approved (unpublish). Share policy (configurable) restricts
/// external sharing to Approved / Published unless explicitly overridden.
/// </summary>
public enum AssetWorkflowState
{
    /// <summary>Author's working state — fully editable, cannot be shared externally under default policy.</summary>
    Draft,
    /// <summary>Submitted by author; waiting for reviewer approval.</summary>
    InReview,
    /// <summary>Reviewer approved — eligible for sharing under default policy; publish to go live.</summary>
    Approved,
    /// <summary>Reviewer rejected with a reason. Author can resubmit after addressing feedback.</summary>
    Rejected,
    /// <summary>Live — shareable externally, visible in public-facing views.</summary>
    Published
}

/// <summary>
/// Enum ↔ lowercase-db-string conversion for the workflow enums.
/// </summary>
public static class WorkflowEnumExtensions
{
    public static string ToDbString(this AssetWorkflowState state) => state switch
    {
        AssetWorkflowState.Draft => "draft",
        AssetWorkflowState.InReview => "in_review",
        AssetWorkflowState.Approved => "approved",
        AssetWorkflowState.Rejected => "rejected",
        AssetWorkflowState.Published => "published",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    public static AssetWorkflowState ToAssetWorkflowState(this string value) => value switch
    {
        "draft" => AssetWorkflowState.Draft,
        "in_review" => AssetWorkflowState.InReview,
        "approved" => AssetWorkflowState.Approved,
        "rejected" => AssetWorkflowState.Rejected,
        "published" => AssetWorkflowState.Published,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown workflow state: {value}")
    };

    private static readonly HashSet<string> ValidAssetWorkflowStates =
        new(StringComparer.Ordinal) { "draft", "in_review", "approved", "rejected", "published" };

    public static bool IsValidAssetWorkflowState(string value) => ValidAssetWorkflowStates.Contains(value);
}
