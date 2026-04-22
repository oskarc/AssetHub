using AssetHub.Application.Services;
using AssetHub.Domain.Entities;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public sealed class MigrationSourceConnectorRegistry : IMigrationSourceConnectorRegistry
{
    private readonly Dictionary<MigrationSourceType, IMigrationSourceConnector> _byType;

    public MigrationSourceConnectorRegistry(IEnumerable<IMigrationSourceConnector> connectors)
    {
        _byType = new Dictionary<MigrationSourceType, IMigrationSourceConnector>();
        foreach (var connector in connectors)
        {
            if (_byType.ContainsKey(connector.SourceType))
                throw new InvalidOperationException(
                    $"Multiple IMigrationSourceConnector implementations registered for {connector.SourceType}.");
            _byType[connector.SourceType] = connector;
        }
    }

    public IMigrationSourceConnector Resolve(MigrationSourceType sourceType)
        => _byType.TryGetValue(sourceType, out var connector)
            ? connector
            : throw new InvalidOperationException(
                $"No IMigrationSourceConnector registered for source type '{sourceType.ToDbString()}'.");
}
