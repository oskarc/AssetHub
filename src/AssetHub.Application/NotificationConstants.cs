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
    }

    /// <summary>Limits.</summary>
    public static class Limits
    {
        public const int MaxTitleLength = 255;
        public const int MaxBodyLength = 2000;
        public const int MaxUrlLength = 500;
        public const int DefaultListTake = 50;
        public const int MaxListTake = 200;
    }
}
