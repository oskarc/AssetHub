using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Dtos;

/// <summary>
/// Body of a new comment. <see cref="ParentCommentId"/> nests a reply under
/// an existing comment; <c>null</c> creates a top-level comment.
/// </summary>
public class CreateAssetCommentDto
{
    [Required, StringLength(4000, MinimumLength = 1)]
    public string Body { get; set; } = string.Empty;

    public Guid? ParentCommentId { get; set; }
}

/// <summary>Partial update — only <see cref="Body"/> is mutable.</summary>
public class UpdateAssetCommentDto
{
    [Required, StringLength(4000, MinimumLength = 1)]
    public string Body { get; set; } = string.Empty;
}

public class AssetCommentResponseDto
{
    public required Guid Id { get; set; }
    public required Guid AssetId { get; set; }
    public required string AuthorUserId { get; set; }
    public required string Body { get; set; }
    public required List<string> MentionedUserIds { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public Guid? ParentCommentId { get; set; }
}
