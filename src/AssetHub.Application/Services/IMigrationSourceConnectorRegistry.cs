using AssetHub.Domain.Entities;

namespace AssetHub.Application.Services;

/// <summary>
/// Resolves the <see cref="IMigrationSourceConnector"/> that owns a given
/// <see cref="MigrationSourceType"/>. Injected wherever the migration
/// pipeline would otherwise branch on source type.
/// </summary>
public interface IMigrationSourceConnectorRegistry
{
    /// <summary>
    /// Returns the connector for the given source type, or throws
    /// <see cref="InvalidOperationException"/> if no connector is registered.
    /// Startup-level programming error; not something callers should catch.
    /// </summary>
    IMigrationSourceConnector Resolve(MigrationSourceType sourceType);
}
