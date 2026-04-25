namespace AssetHub.Application;

/// <summary>
/// Constants for the notifications feature (T3-NTF-01). Categories identify the
/// producer of a notification; preferences store per-category channel settings
/// keyed by these values.
/// </summary>
public static class NotificationConstants
{
    /// <summary>
    /// Canonical category keys. New producers (T3-COL-01 mentions, T3-WF-01
    /// workflow) add a new constant here and use it when calling
    /// <c>INotificationService.CreateAsync</c>. Unknown keys still work — they
    /// get default preferences — but adding the constant ensures consistent
    /// cross-referencing.
    /// </summary>
    public static class Categories
    {
        /// <summary>New matches for a saved search with Daily/Weekly cadence (T1-SRCH-01).</summary>
        public const string SavedSearchDigest = "saved_search_digest";

        /// <summary>Bulk migration finished (any terminal status). Admin-visible.</summary>
        public const string MigrationCompleted = "migration_completed";

        /// <summary>User mentioned in an asset comment (T3-COL-01 — future).</summary>
        public const string Mention = "mention";

        /// <summary>Workflow state transition on an asset the user owns or reviews (T3-WF-01 — future).</summary>
        public const string WorkflowTransition = "workflow_transition";

        /// <summary>Webhook delivery failed permanently (admin-only, T3-INT-01 — future).</summary>
        public const string WebhookFailure = "webhook_failure";

        /// <summary>All category keys known at compile time. Used to populate defaults.</summary>
        public static readonly IReadOnlyList<string> All =
        [
            SavedSearchDigest,
            MigrationCompleted,
            Mention,
            WorkflowTransition,
            WebhookFailure
        ];
    }

    /// <summary>Email delivery cadence values stored on <c>NotificationCategoryPrefs.EmailCadence</c>.</summary>
    public static class EmailCadences
    {
        public const string Instant = "instant";
        public const string Daily = "daily";
        public const string Weekly = "weekly";

        public static bool IsValid(string? value)
            => value is Instant or Daily or Weekly;
    }

    /// <summary>Audit event types.</summary>
    public static class AuditEvents
    {
        public const string PreferencesUpdated = "notification.preferences_updated";
        public const string UnsubscribedViaEmail = "notification.unsubscribed_via_email";
        public const string SavedSearchDigestSent = "saved_search.digest_sent";

        // T3-COL-01 — comments on assets
        public const string CommentCreated = "comment.created";
        public const string CommentUpdated = "comment.updated";
        public const string CommentDeleted = "comment.deleted";
        public const string CommentMentionDelivered = "comment.mention_delivered";

        // T3-WF-01 — publishing workflow
        public const string WorkflowSubmitted = "asset.workflow_submitted";
        public const string WorkflowApproved = "asset.workflow_approved";
        public const string WorkflowRejected = "asset.workflow_rejected";
        public const string WorkflowPublished = "asset.workflow_published";
        public const string WorkflowUnpublished = "asset.workflow_unpublished";

        // T3-INT-01 — webhooks
        public const string WebhookCreated = "webhook.created";
        public const string WebhookUpdated = "webhook.updated";
        public const string WebhookDeleted = "webhook.deleted";
        public const string WebhookSecretRotated = "webhook.secret_rotated";
        public const string WebhookDeliveryFailedPermanently = "webhook.delivery_failed_permanently";

        // T4-BP-01 — branded share portals
        public const string BrandCreated = "brand.created";
        public const string BrandUpdated = "brand.updated";
        public const string BrandDeleted = "brand.deleted";

        // T4-GUEST-01 — guest invitations
        public const string GuestInvited = "guest.invited";
        public const string GuestAccepted = "guest.accepted";
        public const string GuestAccessRevoked = "guest.access_revoked";
        public const string GuestExpired = "guest.expired";
    }

    /// <summary>Limits.</summary>
    public static class Limits
    {
        public const int MaxTitleLength = 255;
        public const int MaxBodyLength = 2000;
        public const int MaxUrlLength = 500;
        public const int DefaultListTake = 50;
        public const int MaxListTake = 200;

        /// <summary>
        /// How many new saved-search matches we include in a single digest
        /// notification body. Extras beyond this are summarised as a
        /// "+N more" line so the in-app bell stays readable.
        /// </summary>
        public const int SavedSearchDigestMaxMatches = 10;
    }

    /// <summary>
    /// Saved-search digest worker cadence. The worker polls frequently and
    /// decides per-SavedSearch whether it's due, so the top-level interval is
    /// the floor for OnNewMatch cadence.
    /// </summary>
    public static class DigestSchedule
    {
        public const int PollIntervalMinutes = 30;
        public const int DailyCooldownHours = 20;
        public const int WeeklyCooldownDays = 6;
    }
}
