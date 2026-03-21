using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Configuration;

/// <summary>
/// Redis connection settings.
/// Bound to the "Redis" section in appsettings.
/// </summary>
public class RedisSettings
{
    public const string SectionName = "Redis";

    /// <summary>
    /// Redis connection string (e.g. "localhost:6379" or "redis:6379,abortConnect=false").
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Instance name prefix for cache keys. Prevents key collisions when
    /// multiple applications share the same Redis instance.
    /// </summary>
    public string InstanceName { get; set; } = "AssetHub:";
}
