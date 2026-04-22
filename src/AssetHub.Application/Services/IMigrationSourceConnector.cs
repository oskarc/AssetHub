using AssetHub.Application.Dtos;
using AssetHub.Domain.Entities;

namespace AssetHub.Application.Services;

/// <summary>
/// Abstraction over a migration source type. Each implementation handles config
/// encoding (for <c>Migration.SourceConfig</c>), optional bucket-style scanning,
/// and per-item metadata / byte fetch. Handlers resolve the right connector via
/// <see cref="IMigrationSourceConnectorRegistry"/>; adding a new source
/// (Bynder / Canto / SharePoint) is one new implementation plus one DI
/// registration — no changes to the handlers.
/// </summary>
public interface IMigrationSourceConnector
{
    /// <summary>The source type this connector handles. One connector per type.</summary>
    MigrationSourceType SourceType { get; }

    /// <summary>
    /// When true, items require bytes uploaded to the internal staging bucket
    /// before they can be dispatched (CSV flow). When false, bytes live remotely
    /// and every pending item is dispatchable as soon as it exists (S3-style pull).
    /// Controls <see cref="MigrationStatus.PartiallyCompleted"/> interpretation
    /// and <c>StartMigrationHandler</c> fan-out.
    /// </summary>
    bool RequiresLocalStaging { get; }

    /// <summary>
    /// When true, this source type supports bucket-style scanning — the admin
    /// can trigger <c>/{id}/s3/scan</c> (or the per-source equivalent) to seed
    /// items. When false, items come from a manifest upload (CSV flow).
    /// </summary>
    bool SupportsScan { get; }

    /// <summary>
    /// Validates the source-specific config on a <see cref="CreateMigrationDto"/>
    /// and returns the JSONB-encodable dictionary to persist on
    /// <c>Migration.SourceConfig</c>. Returns a success with <c>null</c> value
    /// for sources with no persisted config (CSV). Secrets are encrypted before
    /// returning. Each connector is also responsible for rejecting config that
    /// doesn't belong to its source type (e.g., S3 config on a CSV migration).
    /// </summary>
    ServiceResult<Dictionary<string, object>?> EncodeConfig(CreateMigrationDto dto);

    /// <summary>
    /// Enumerates objects in the remote source and returns one
    /// <see cref="MigrationObjectInfo"/> per discoverable object. Callers must
    /// check <see cref="SupportsScan"/> first; implementations that don't
    /// support scanning throw <see cref="NotSupportedException"/>.
    /// </summary>
    Task<IReadOnlyList<MigrationObjectInfo>> ScanAsync(Migration migration, CancellationToken ct);

    /// <summary>
    /// Resolves the "source key" — the opaque identifier this connector passes
    /// to <see cref="StatAsync"/> and <see cref="DownloadAsync"/> for a given
    /// item. For CSV, this is the staging-bucket key; for S3, the remote
    /// object key.
    /// </summary>
    string ResolveSourceKey(Migration migration, MigrationItem item);

    /// <summary>
    /// Fetches size + content-type for an item without downloading bytes.
    /// Returns <c>null</c> if the object no longer exists (moved/deleted
    /// between scan or manifest upload and ingest).
    /// </summary>
    Task<MigrationObjectStat?> StatAsync(Migration migration, string sourceKey, CancellationToken ct);

    /// <summary>
    /// Downloads an item's bytes into a seekable stream the caller can hand
    /// to the internal MinIO adapter. The caller disposes the stream.
    /// </summary>
    Task<Stream> DownloadAsync(Migration migration, string sourceKey, CancellationToken ct);
}

/// <summary>
/// Minimal metadata about a single object in a remote source. Used when the
/// scan handler seeds <c>MigrationItem</c> rows.
/// </summary>
public record MigrationObjectInfo(string Key, long Size, string ETag);

/// <summary>
/// Per-object metadata returned by <see cref="IMigrationSourceConnector.StatAsync"/>.
/// Independent of <see cref="ObjectStatInfo"/> so the connector abstraction
/// isn't tied to the internal MinIO adapter's DTO.
/// </summary>
public record MigrationObjectStat(long Size, string ContentType, string ETag);
