using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

/// <summary>Optional note / reason sent with a workflow action.</summary>
public class WorkflowActionDto
{
    [StringLength(1000)]
    public string? Reason { get; set; }
}

/// <summary>Required reason — used for the reject path.</summary>
public class WorkflowRejectDto
{
    [Required, StringLength(1000, MinimumLength = 1)]
    public string Reason { get; set; } = string.Empty;
}

public class AssetWorkflowTransitionResponseDto
{
    public required Guid Id { get; set; }
    public required Guid AssetId { get; set; }
    public required string FromState { get; set; }
    public required string ToState { get; set; }
    public required string ActorUserId { get; set; }
    public string? Reason { get; set; }
    public required DateTime CreatedAt { get; set; }
}

/// <summary>
/// Current state + history + the transitions the caller is allowed to
/// perform right now. The UI uses <see cref="AvailableActions"/> to render
/// only the buttons the current user can actually invoke.
/// </summary>
public class AssetWorkflowResponseDto
{
    public required Guid AssetId { get; set; }
    public required string CurrentState { get; set; }
    public DateTime? StateUpdatedAt { get; set; }
    public required List<string> AvailableActions { get; set; }
    public required List<AssetWorkflowTransitionResponseDto> History { get; set; }
}
