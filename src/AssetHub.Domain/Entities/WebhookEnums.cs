namespace AssetHub.Domain.Entities;

/// <summary>
/// Enum ↔ lowercase-db-string conversion for <see cref="WebhookDeliveryStatus"/>
/// (the enum itself is declared alongside the webhook entities).
/// </summary>
public static class WebhookEnumExtensions
{
    public static string ToDbString(this WebhookDeliveryStatus status) => status switch
    {
        WebhookDeliveryStatus.Pending => "pending",
        WebhookDeliveryStatus.Delivered => "delivered",
        WebhookDeliveryStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static WebhookDeliveryStatus ToWebhookDeliveryStatus(this string value) => value switch
    {
        "pending" => WebhookDeliveryStatus.Pending,
        "delivered" => WebhookDeliveryStatus.Delivered,
        "failed" => WebhookDeliveryStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown delivery status: {value}")
    };
}
