using System.Text.Json;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public sealed class OutboxPublisher(DbContextProvider provider) : IOutboxPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task EnqueueAsync<T>(T message, CancellationToken ct = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);

        // The row joins the ambient UoW transaction (if any) via the
        // DbContextProvider lease — same pattern as OrphanedObjectRepository.
        // Outside a UoW the SaveChanges below commits the row standalone,
        // which is still correct for the drainer.
        await using var lease = await provider.AcquireAsync(ct);
        var db = lease.Db;

        // AssemblyQualifiedName is required so the drainer can call Type.GetType
        // across project boundaries — Application + Infrastructure messages live
        // in different assemblies. Strip the version/culture/key suffix we don't
        // need so a message persisted by today's build is still resolvable after
        // a routine NuGet bump.
        var type = message.GetType();
        var typeName = $"{type.FullName}, {type.Assembly.GetName().Name}";

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = typeName,
            PayloadJson = JsonSerializer.Serialize(message, type, JsonOptions),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
