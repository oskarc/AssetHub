using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Configuration;

/// <summary>
/// General application settings.
/// Bound to the "App" section in appsettings.
/// </summary>
public class AppSettings
{
    public const string SectionName = "App";

    /// <summary>
    /// The base URL of the application (e.g. "https://assethub.example.com").
    /// Used for generating share links and email URLs.
    /// </summary>
    [Required]
    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// Default page size for paginated endpoints.
    /// </summary>
    public int DefaultPageSize { get; set; } = 50;

    /// <summary>
    /// Maximum upload size in megabytes.
    /// </summary>
    public int MaxUploadSizeMb { get; set; } = 500;
}
