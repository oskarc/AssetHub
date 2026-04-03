using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Configuration;

public sealed class RabbitMQSettings
{
    public const string SectionName = "RabbitMQ";

    [Required]
    public string Host { get; set; } = "localhost";

    public string Username { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";
}
