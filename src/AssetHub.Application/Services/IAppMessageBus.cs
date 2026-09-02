namespace AssetHub.Application.Services;

/// <summary>
/// In-process message bus. Producers publish commands/events; a single hosted
/// dispatcher consumes them and invokes the matching handler in the same process.
/// </summary>
/// <remarks>
/// Replaces Wolverine's <c>IMessageBus</c> (contract-026). Publisher and consumer
/// share one process, so messages travel over a <c>System.Threading.Channels</c>
/// channel rather than RabbitMQ. Durability for the media pipeline is provided by
/// the outbox upstream of this bus, not by the transport; a stuck-Processing
/// reaper recovers any message lost to a mid-flight restart.
/// </remarks>
public interface IAppMessageBus
{
    /// <summary>Hand a message to the dispatcher. Returns once it is queued, not handled.</summary>
    ValueTask PublishAsync(object message, CancellationToken cancellationToken = default);
}
