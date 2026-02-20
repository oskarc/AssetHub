namespace AssetHub.Domain.Entities;

public class AuditEvent
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public string? IP { get; set; }
    public string? UserAgent { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, object> DetailsJson { get; set; } = new();
}
