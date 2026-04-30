namespace AssetHub.Domain.Entities;

/// <summary>
/// Durable record of a Wolverine command/event that needs to reach RabbitMQ.
/// Inserted in the same transaction as the source business mutation so a
/// crash or Rabbit blip between SQL commit and broker publish can't lose the
/// message — the OutboxDrainService picks up undispatched rows and publishes
/// them out-of-band (D-2).
///
/// PayloadJson stores the serialized message. MessageType is the
/// assembly-qualified CLR type name; the drainer reflects it back to a
/// concrete type before calling <c>IMessageBus.PublishAsync</c>.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>Assembly-qualified CLR type (e.g., "AssetHub.Application.Messages.ProcessImageCommand, AssetHub.Application").</summary>
    public string MessageType { get; set; } = string.Empty;

    /// <summary>System.Text.Json-serialized message body.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>UTC timestamp when the drainer successfully published to Rabbit. Null until drained.</summary>
    public DateTime? DispatchedAt { get; set; }

    /// <summary>Drainer attempts so far. Caps via MaxAttempts so a poison message can't block the queue.</summary>
    public int AttemptCount { get; set; }

    /// <summary>UTC timestamp of the most recent drain attempt, null until first try.</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>Truncated error message from the last failed publish attempt.</summary>
    public string? LastError { get; set; }
}
