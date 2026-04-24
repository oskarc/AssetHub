namespace AssetHub.Domain.Entities;

/// <summary>
/// A single comment on an Asset. Supports a single level of threading via
/// <see cref="ParentCommentId"/> (null = top-level). Body is plain text with
/// <c>@username</c> mention tokens that the service resolves to user ids at
/// create / update time — resolved ids are persisted in
/// <see cref="MentionedUserIds"/> so the notification fan-out doesn't have
/// to re-parse on read.
///
/// Cascades with the Asset on hard-delete / purge; soft-deleted assets hide
/// comments via the Asset global query filter (comments are only ever read
/// through the Asset).
/// </summary>
public class AssetComment
{
    public Guid Id { get; set; }

    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }

    /// <summary>Keycloak sub of the comment author.</summary>
    public string AuthorUserId { get; set; } = string.Empty;

    /// <summary>
    /// Plain-text body with <c>@username</c> mention tokens. Newlines are
    /// preserved but no markup is rendered — HTML is escaped on display.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Keycloak sub for each resolved mention in the body. Populated by the
    /// service at create / update time from a compile-time regex against
    /// <see cref="Body"/>; unknown usernames are dropped silently.
    /// </summary>
    public List<string> MentionedUserIds { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    /// <summary>Null until the author edits; stamped to UtcNow on update.</summary>
    public DateTime? EditedAt { get; set; }

    /// <summary>
    /// Null for top-level comments. When set, this comment is a reply in the
    /// parent's thread. Only one level of nesting — replies-of-replies aren't
    /// modelled; the UI flattens them alongside direct replies.
    /// </summary>
    public Guid? ParentCommentId { get; set; }
    public AssetComment? ParentComment { get; set; }
}
