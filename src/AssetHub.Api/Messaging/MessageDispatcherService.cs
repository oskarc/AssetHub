using AssetHub.Api.Handlers;
using AssetHub.Application.Messages;
using AssetHub.Application.Services;

namespace AssetHub.Api.Messaging;

/// <summary>
/// Consumes the in-process message channel and invokes the matching handler,
/// re-publishing any events a handler returns. Replaces Wolverine's dispatch
/// (contract-026).
/// </summary>
/// <remarks>
/// Dispatch is an explicit switch, not reflection: six message types, and the
/// reader can see at a glance what handles what. Each message runs in its own DI
/// scope. Handler failures retry on the same 1-2-5-10-30s cooldown Wolverine used;
/// after the last attempt the message is dropped with a logged error — for the
/// media pipeline the stuck-Processing reaper re-enqueues it, and a lost ZIP job
/// is re-clickable.
/// </remarks>
public sealed class MessageDispatcherService(
    InProcessMessageBus bus,
    IServiceScopeFactory scopeFactory,
    ILogger<MessageDispatcherService> logger) : BackgroundService
{
    private static readonly TimeSpan[] RetryCooldowns =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in bus.Reader.ReadAllAsync(stoppingToken))
        {
            await DispatchWithRetryAsync(message, stoppingToken);
        }
    }

    private async Task DispatchWithRetryAsync(object message, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await DispatchOnceAsync(message, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= RetryCooldowns.Length)
                {
                    logger.LogError(ex,
                        "Handler for {MessageType} failed after {Attempts} attempts — dropping. "
                        + "Media assets are recovered by the stuck-Processing reaper.",
                        message.GetType().Name, attempt);
                    return;
                }
                logger.LogWarning(ex,
                    "Handler for {MessageType} failed (attempt {Attempt}); retrying in {Cooldown}s",
                    message.GetType().Name, attempt + 1, RetryCooldowns[attempt].TotalSeconds);
                await Task.Delay(RetryCooldowns[attempt], ct);
            }
        }
    }

    private async Task DispatchOnceAsync(object message, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        object[]? cascaded = null;

        switch (message)
        {
            case ProcessImageCommand c:
                cascaded = await sp.GetRequiredService<ProcessImageHandler>().HandleAsync(c, ct);
                break;
            case ProcessVideoCommand c:
                cascaded = await sp.GetRequiredService<ProcessVideoHandler>().HandleAsync(c, ct);
                break;
            case ProcessAudioCommand c:
                cascaded = await sp.GetRequiredService<ProcessAudioHandler>().HandleAsync(c, ct);
                break;
            case BuildZipCommand c:
                await sp.GetRequiredService<BuildZipHandler>().HandleAsync(c, ct);
                break;
            case AssetProcessingCompletedEvent e:
                await sp.GetRequiredService<AssetProcessingCompletedHandler>().HandleAsync(e, ct);
                break;
            case AssetProcessingFailedEvent e:
                await sp.GetRequiredService<AssetProcessingFailedHandler>().HandleAsync(e, ct);
                break;
            default:
                logger.LogError("No handler registered for message type {MessageType}", message.GetType().FullName);
                return;
        }

        // Cascading events (the media handlers return completed/failed events).
        if (cascaded is not null)
            foreach (var evt in cascaded)
                await sp.GetRequiredService<IAppMessageBus>().PublishAsync(evt, ct);
    }
}
