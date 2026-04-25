using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

public sealed class AssetCommentService(
    IAssetCommentRepository repo,
    IAssetRepository assetRepo,
    IAssetCollectionRepository assetCollectionRepo,
    ICollectionAuthorizationService authService,
    IUserLookupService userLookup,
    INotificationService notifications,
    IWebhookEventPublisher webhooks,
    IAuditService audit,
    CurrentUser currentUser,
    ILogger<AssetCommentService> logger) : IAssetCommentService
{
    public async Task<ServiceResult<List<AssetCommentResponseDto>>> ListForAssetAsync(
        Guid assetId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden();

        if (!await CanAccessAssetAsync(assetId, RoleHierarchy.Roles.Viewer, ct))
            return ServiceError.Forbidden();

        var comments = await repo.ListByAssetAsync(assetId, ct);
        return comments.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<AssetCommentResponseDto>> CreateAsync(
        Guid assetId, CreateAssetCommentDto dto, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden();

        var asset = await assetRepo.GetByIdAsync(assetId, ct);
        if (asset is null)
            return ServiceError.NotFound("Asset not found.");

        // Contributor on any collection containing the asset is the bar for
        // posting a comment — aligns with "who can add content".
        if (!await CanAccessAssetAsync(assetId, RoleHierarchy.Roles.Contributor, ct))
            return ServiceError.Forbidden();

        if (dto.ParentCommentId is Guid parentId)
        {
            var parent = await repo.GetByIdAsync(parentId, ct);
            if (parent is null || parent.AssetId != assetId)
                return ServiceError.BadRequest("Parent comment not found on this asset.");
            if (parent.ParentCommentId is not null)
                return ServiceError.BadRequest("Replies can only nest one level deep.");
        }

        var mentionedIds = await ResolveMentionsAsync(dto.Body, ct);

        var entity = new AssetComment
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            AuthorUserId = currentUser.UserId,
            Body = dto.Body,
            MentionedUserIds = mentionedIds,
            ParentCommentId = dto.ParentCommentId,
            CreatedAt = DateTime.UtcNow
        };
        await repo.CreateAsync(entity, ct);

        await audit.LogAsync(
            NotificationConstants.AuditEvents.CommentCreated,
            Constants.ScopeTypes.Comment,
            entity.Id,
            currentUser.UserId,
            new Dictionary<string, object>
            {
                ["asset_id"] = assetId,
                ["parent_comment_id"] = (object?)entity.ParentCommentId ?? string.Empty,
                ["mentioned_user_count"] = mentionedIds.Count
            },
            ct);

        await FanOutMentionsAsync(entity, asset, ct);

        await webhooks.PublishAsync(WebhookEvents.CommentCreated, new
        {
            commentId = entity.Id,
            assetId = entity.AssetId,
            authorUserId = entity.AuthorUserId,
            body = entity.Body,
            parentCommentId = entity.ParentCommentId,
            mentionedUserIds = entity.MentionedUserIds,
            createdAt = entity.CreatedAt
        }, ct);

        logger.LogInformation(
            "Comment {CommentId} created on asset {AssetId} by {UserId} ({MentionCount} mentions)",
            entity.Id, assetId, currentUser.UserId, mentionedIds.Count);

        return ToDto(entity);
    }

    public async Task<ServiceResult<AssetCommentResponseDto>> UpdateAsync(
        Guid commentId, UpdateAssetCommentDto dto, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden();

        var comment = await repo.GetByIdAsync(commentId, ct);
        if (comment is null)
            return ServiceError.NotFound("Comment not found.");

        // Authors-only edit. Admins intentionally can't silently edit a
        // user's words; they can delete if moderation is needed.
        if (comment.AuthorUserId != currentUser.UserId)
            return ServiceError.Forbidden();

        var asset = await assetRepo.GetByIdAsync(comment.AssetId, ct);
        if (asset is null)
            return ServiceError.NotFound("Asset not found.");

        var previouslyMentioned = new HashSet<string>(comment.MentionedUserIds, StringComparer.Ordinal);
        var newMentioned = await ResolveMentionsAsync(dto.Body, ct);

        comment.Body = dto.Body;
        comment.MentionedUserIds = newMentioned;
        comment.EditedAt = DateTime.UtcNow;
        await repo.UpdateAsync(comment, ct);

        await audit.LogAsync(
            NotificationConstants.AuditEvents.CommentUpdated,
            Constants.ScopeTypes.Comment,
            comment.Id,
            currentUser.UserId,
            new Dictionary<string, object>
            {
                ["asset_id"] = comment.AssetId,
                ["mentioned_user_count"] = newMentioned.Count
            },
            ct);

        // Only notify users newly mentioned by this edit — avoid re-notifying
        // people who were already in the original comment.
        var addedMentions = newMentioned.Where(id => !previouslyMentioned.Contains(id)).ToList();
        if (addedMentions.Count > 0)
        {
            await FanOutMentionsAsync(comment, asset, ct, recipients: addedMentions);
        }

        return ToDto(comment);
    }

    public async Task<ServiceResult> DeleteAsync(Guid commentId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden();

        var comment = await repo.GetByIdAsync(commentId, ct);
        if (comment is null)
            return ServiceError.NotFound("Comment not found.");

        // Author OR system admin — aligns with existing moderation semantics
        // (authors own their words; admins can remove abusive content).
        if (comment.AuthorUserId != currentUser.UserId && !currentUser.IsSystemAdmin)
            return ServiceError.Forbidden();

        await repo.DeleteAsync(commentId, ct);

        await audit.LogAsync(
            NotificationConstants.AuditEvents.CommentDeleted,
            Constants.ScopeTypes.Comment,
            comment.Id,
            currentUser.UserId,
            new Dictionary<string, object>
            {
                ["asset_id"] = comment.AssetId,
                ["author_user_id"] = comment.AuthorUserId,
                ["was_admin_delete"] = currentUser.IsSystemAdmin && comment.AuthorUserId != currentUser.UserId
            },
            ct);

        return ServiceResult.Success;
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private async Task<bool> CanAccessAssetAsync(Guid assetId, string requiredRole, CancellationToken ct)
    {
        if (currentUser.IsSystemAdmin)
            return true;
        var collections = await assetCollectionRepo.GetCollectionIdsForAssetAsync(assetId, ct);
        var accessible = await authService.FilterAccessibleAsync(
            currentUser.UserId, collections, requiredRole, ct);
        return accessible.Count > 0;
    }

    private async Task<List<string>> ResolveMentionsAsync(string body, CancellationToken ct)
    {
        var usernames = MentionParser.ExtractUsernames(body);
        if (usernames.Count == 0)
            return new List<string>();

        var resolved = new List<string>(usernames.Count);
        foreach (var name in usernames)
        {
            ct.ThrowIfCancellationRequested();
            var id = await userLookup.GetUserIdByUsernameAsync(name, ct);
            if (id is not null && id != currentUser.UserId && !resolved.Contains(id))
                resolved.Add(id);
        }
        return resolved;
    }

    private async Task FanOutMentionsAsync(
        AssetComment comment, Asset asset, CancellationToken ct,
        List<string>? recipients = null)
    {
        var targets = recipients ?? comment.MentionedUserIds;
        if (targets.Count == 0) return;

        var preview = comment.Body.Length > 180
            ? comment.Body[..180] + "…"
            : comment.Body;

        foreach (var userId in targets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await notifications.CreateAsync(
                    userId: userId,
                    category: NotificationConstants.Categories.Mention,
                    title: $"You were mentioned on '{asset.Title}'",
                    body: preview,
                    url: $"/assets/{asset.Id}",
                    data: new Dictionary<string, object>
                    {
                        ["comment_id"] = comment.Id,
                        ["asset_id"] = asset.Id,
                        ["author_user_id"] = comment.AuthorUserId
                    },
                    ct);

                await audit.LogAsync(
                    NotificationConstants.AuditEvents.CommentMentionDelivered,
                    Constants.ScopeTypes.Comment,
                    comment.Id,
                    currentUser.UserId,
                    new Dictionary<string, object>
                    {
                        ["asset_id"] = asset.Id,
                        ["mentioned_user_id"] = userId
                    },
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One broken notification shouldn't block the others.
                logger.LogWarning(ex,
                    "Failed to deliver mention notification for comment {CommentId} to user {UserId}",
                    comment.Id, userId);
            }
        }
    }

    private static AssetCommentResponseDto ToDto(AssetComment c) => new()
    {
        Id = c.Id,
        AssetId = c.AssetId,
        AuthorUserId = c.AuthorUserId,
        Body = c.Body,
        MentionedUserIds = c.MentionedUserIds.ToList(),
        CreatedAt = c.CreatedAt,
        EditedAt = c.EditedAt,
        ParentCommentId = c.ParentCommentId
    };
}
