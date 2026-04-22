namespace AssetHub.Application;

/// <summary>
/// Constants specific to the migration (bulk import) feature.
/// </summary>
public static class MigrationConstants
{
    /// <summary>
    /// Error codes set on <c>MigrationItem.ErrorCode</c> when processing fails or is skipped.
    /// </summary>
    public static class ErrorCodes
    {
        public const string ProcessingError = "PROCESSING_ERROR";
        public const string FileNotFound = "FILE_NOT_FOUND";
        public const string FileStatFailed = "FILE_STAT_FAILED";
        public const string MissingFilename = "MISSING_FILENAME";
        public const string Duplicate = "DUPLICATE";
        public const string MigrationCancelled = "MIGRATION_CANCELLED";
    }

    /// <summary>
    /// Audit event types logged during migration lifecycle.
    /// </summary>
    public static class AuditEvents
    {
        public const string Created = "migration.created";
        public const string Started = "migration.started";
        public const string Completed = "migration.completed";
        public const string Cancelled = "migration.cancelled";
        public const string Deleted = "migration.deleted";
        public const string BulkDeleted = "migration.bulk_deleted";
        public const string Retried = "migration.retried";
        public const string S3ScanStarted = "migration.s3_scan_started";
        public const string S3ScanCompleted = "migration.s3_scan_completed";
        public const string S3ScanFailed = "migration.s3_scan_failed";
    }

    /// <summary>
    /// CSV column header names used in the migration manifest.
    /// </summary>
    public static class CsvHeaders
    {
        public const string Filename = "filename";
        public const string ExternalId = "external_id";
        public const string Title = "title";
        public const string Description = "description";
        public const string Copyright = "copyright";
        public const string Tags = "tags";
        public const string CollectionNames = "collection_names";
        public const string Sha256 = "sha256";
        public const string MetadataPrefix = "metadata.";
    }

    /// <summary>
    /// Limits applied during migration processing.
    /// </summary>
    public static class Limits
    {
        /// <summary>Maximum length of an error message stored on a migration item.</summary>
        public const int MaxErrorMessageLength = 2000;

        /// <summary>Maximum rows returned in the outcome CSV download.</summary>
        public const int MaxOutcomeCsvRows = 100_000;
    }

    /// <summary>
    /// Builds MinIO object key paths for migration staging files.
    /// </summary>
    public static string StagingKey(Guid migrationId, string fileName)
        => $"migrations/{migrationId}/staging/{fileName}";
}
