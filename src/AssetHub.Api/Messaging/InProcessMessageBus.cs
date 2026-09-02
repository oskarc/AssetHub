using System.Threading.Channels;
using AssetHub.Application.Services;

namespace AssetHub.Api.Messaging;

/// <summary>
/// <see cref="IAppMessageBus"/> backed by an unbounded in-process channel.
/// Registered as a singleton so the producer side (this) and the consumer side
/// (<see cref="MessageDispatcherService"/>) share one channel.
/// </summary>
public sealed class InProcessMessageBus : IAppMessageBus
{
    // Unbounded: the media/zip volume is low and bursty, and back-pressure here
    // would block an upload's request thread. Single reader — the one dispatcher.
    private readonly Channel<object> _channel =
        Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Consumed only by the dispatcher hosted service.</summary>
    public ChannelReader<object> Reader => _channel.Reader;

    public ValueTask PublishAsync(object message, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(message, cancellationToken);
}
