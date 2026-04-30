namespace AssetHub.Application.Services;

/// <summary>
/// Transactional outbox for Wolverine commands/events. Replaces direct
/// <c>IMessageBus.PublishAsync</c> calls in producers that have already
/// committed business state — so a Rabbit blip between SQL commit and
/// broker publish can't lose the message (D-2).
///
/// Enqueue inside a <see cref="IUnitOfWork"/> scope: the row joins the
/// surrounding transaction, the outbox drainer (Worker background service)
/// publishes the message to Rabbit out-of-band. If the surrounding tx rolls
/// back, the message is rolled back too — no orphan publishes.
///
/// Outside a UoW the row commits on its own (own SaveChanges); the drainer
/// still picks it up. That's a correct fallback but loses the atomicity
/// guarantee, so prefer wrapping the source mutation + enqueue in a UoW.
/// </summary>
public interface IOutboxPublisher
{
    /// <summary>
    /// Stage <paramref name="message"/> for asynchronous delivery via the
    /// configured message bus. Serializes the payload to JSON and persists
    /// the assembly-qualified CLR type so the drainer can reconstruct the
    /// concrete instance.
    /// </summary>
    Task EnqueueAsync<T>(T message, CancellationToken ct = default) where T : class;
}
