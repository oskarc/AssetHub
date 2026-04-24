using AssetHub.Domain.Entities;

namespace AssetHub.Application.Configuration;

/// <summary>
/// Publishing-workflow policy (T3-WF-01). Controls the starting state of
/// newly-uploaded assets and which states are allowed to be shared
/// externally. Defaults are backward-compatible:
/// - <see cref="NewAssetState"/> = Published so existing flows that upload
///   and immediately share keep working.
/// - <see cref="AllowedShareStates"/> = { Approved, Published } matches the
///   roadmap's default share policy — only bites when admins flip
///   <see cref="NewAssetState"/> to Draft (or manually put assets through
///   the workflow).
/// </summary>
public class WorkflowSettings
{
    public const string SectionName = "Workflow";

    /// <summary>
    /// State applied to newly-uploaded assets. Flip to
    /// <see cref="AssetWorkflowState.Draft"/> to activate the review
    /// workflow; leave as <see cref="AssetWorkflowState.Published"/> for
    /// the "upload and share immediately" default.
    /// </summary>
    public AssetWorkflowState NewAssetState { get; set; } = AssetWorkflowState.Published;

    /// <summary>
    /// External sharing (<c>ShareService.CreateShareAsync</c>) is rejected
    /// when the asset's current state is not in this list. System admins
    /// bypass the check (same pattern as existing ACL bypasses).
    /// </summary>
    public List<AssetWorkflowState> AllowedShareStates { get; set; } = new()
    {
        AssetWorkflowState.Approved,
        AssetWorkflowState.Published
    };
}
