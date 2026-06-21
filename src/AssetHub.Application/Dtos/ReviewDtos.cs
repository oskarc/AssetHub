using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

// ── Pending review queue ─────────────────────────────────────────────────

/// <summary>
/// Filter for the pending review queue. A manager sees assets in collections
/// where they hold manager+; an admin sees everything and may narrow with these
/// filters. Empty / null filters mean "no filter on this dimension".
/// </summary>
public class ReviewQueueRequest
{
    /// <summary>Restrict to a single collection / area. Null = all areas in scope.</summary>
    public Guid? CollectionId { get; set; }

    /// <summary>Admin triage: show only items whose area has no manager assigned.</summary>
    public bool UnassignedOnly { get; set; }

    /// <summary>Filter by asset-type token — "image", "video", "document".</summary>
    [StringLength(20)]
    public string? AssetType { get; set; }

    [Range(0, int.MaxValue)]
    public int Skip { get; set; }

    [Range(1, 200)]
    public int Take { get; set; } = 50;
}

/// <summary>One collection ("area") an in-review asset belongs to.</summary>
public class ReviewAreaDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
}

/// <summary>
/// One asset awaiting review, annotated with its area ownership so a reviewer
/// can see who is responsible — or that no one is.
/// </summary>
public class ReviewQueueItemDto
{
    public required Guid AssetId { get; set; }
    public required string Title { get; set; }
    public required string AssetType { get; set; }
    public string? ThumbObjectKey { get; set; }
    public string? PosterObjectKey { get; set; }

    /// <summary>The collections this asset belongs to (its "areas").</summary>
    public required List<ReviewAreaDto> Areas { get; set; }

    /// <summary>Display names of the managers across this asset's areas (de-duplicated).</summary>
    public required List<string> Managers { get; set; }

    /// <summary>True when none of the asset's areas has a manager assigned — an admin must own it.</summary>
    public required bool IsUnassigned { get; set; }

    /// <summary>When the asset entered the in-review state (<c>WorkflowStateUpdatedAt</c>).</summary>
    public required DateTime InReviewSince { get; set; }

    /// <summary>Display name of the asset author (the submitter proxy in v1).</summary>
    public required string SubmittedBy { get; set; }

    /// <summary>True when the current caller may approve / reject this item.</summary>
    public required bool CanAct { get; set; }
}

public class ReviewQueueResponse
{
    public required List<ReviewQueueItemDto> Items { get; set; }
    public required int TotalCount { get; set; }

    /// <summary>How many in-scope in-review items sit in areas with no manager (drives the admin banner).</summary>
    public required int UnassignedCount { get; set; }
}

// ── Review decisions (history) ───────────────────────────────────────────

/// <summary>
/// Filter for the decisions / history view — completed review outcomes, sourced
/// from <c>AssetWorkflowTransition</c> (independent of the audit log).
/// </summary>
public class ReviewDecisionsRequest
{
    /// <summary>Target-state tokens to include — "approved", "rejected", "published", "unpublished". Null = all decisions.</summary>
    [MaxLength(10)]
    public List<string>? Decisions { get; set; }

    public Guid? CollectionId { get; set; }
    public DateTime? After { get; set; }
    public DateTime? Before { get; set; }

    [Range(0, int.MaxValue)]
    public int Skip { get; set; }

    [Range(1, 200)]
    public int Take { get; set; } = 50;
}

/// <summary>One completed review decision (a single workflow transition).</summary>
public class ReviewDecisionDto
{
    public required Guid TransitionId { get; set; }
    public required Guid AssetId { get; set; }
    public required string AssetTitle { get; set; }

    /// <summary>Resulting state — "approved", "rejected", "published", "unpublished".</summary>
    public required string Decision { get; set; }

    /// <summary>Display name of the reviewer who made the decision.</summary>
    public required string ReviewerName { get; set; }

    public string? Reason { get; set; }
    public required DateTime DecidedAt { get; set; }
}

public class ReviewDecisionsResponse
{
    public required List<ReviewDecisionDto> Items { get; set; }
    public required int TotalCount { get; set; }
}
