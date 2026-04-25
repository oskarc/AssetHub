using System.Collections.Frozen;

namespace AssetHub.Application;

/// <summary>
/// Canonical webhook event-type constants (T3-INT-01). Producers use these
/// strings when calling <c>IWebhookEventPublisher.PublishAsync</c>; the
/// publisher matches them against <c>Webhook.EventTypes</c> to decide which
/// subscribers to fan out to.
///
/// Tokens are stable across releases — adding a new event is a SemVer
/// minor; renaming one is breaking. New event types added here should also
/// be surfaced in the admin webhook-create UI and listed in
/// <see cref="All"/>.
/// </summary>
public static class WebhookEvents
{
    public const string AssetCreated = "asset.created";
    public const string AssetUpdated = "asset.updated";
    public const string AssetDeleted = "asset.deleted";
    public const string AssetRestored = "asset.restored";
    public const string ShareCreated = "share.created";
    public const string CommentCreated = "comment.created";
    public const string WorkflowStateChanged = "workflow.state_changed";

    public static readonly FrozenSet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        AssetCreated,
        AssetUpdated,
        AssetDeleted,
        AssetRestored,
        ShareCreated,
        CommentCreated,
        WorkflowStateChanged
    }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsKnown(string eventType) => All.Contains(eventType);
}
