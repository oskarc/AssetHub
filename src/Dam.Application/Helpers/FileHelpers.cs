namespace Dam.Application.Helpers;

/// <summary>
/// Shared file and path utilities for safe filenames, content-type mapping, and size formatting.
/// </summary>
public static class FileHelpers
{
    /// <summary>
    /// Sanitizes an asset title into a safe filename with the correct extension
    /// derived from the object key or content type.
    /// </summary>
    public static string GetSafeFileName(string title, string objectKey, string contentType)
    {
        var extension = Path.GetExtension(objectKey);
        if (string.IsNullOrEmpty(extension))
        {
            extension = contentType switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "video/mp4" => ".mp4",
                "video/webm" => ".webm",
                "application/pdf" => ".pdf",
                _ => ""
            };
        }

        var safeName = string.Join("_", title.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrEmpty(safeName))
            safeName = Path.GetFileNameWithoutExtension(objectKey);

        return safeName + extension;
    }

    /// <summary>
    /// Formats a byte count into a human-readable file size string (e.g. "12.5 MB").
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Converts a camelCase or PascalCase key into a readable "Title Case" string.
    /// e.g. "cameraModel" → "Camera Model", "GPSLatitude" → "GPS Latitude".
    /// </summary>
    public static string FormatMetadataKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;

        var result = System.Text.RegularExpressions.Regex.Replace(key, "([a-z])([A-Z])", "$1 $2");
        result = System.Text.RegularExpressions.Regex.Replace(result, "([A-Z]+)([A-Z][a-z])", "$1 $2");
        return char.ToUpper(result[0]) + result[1..];
    }

    /// <summary>
    /// Checks whether the content type represents a PDF document.
    /// </summary>
    public static bool IsPdfContentType(string? contentType)
    {
        return string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
    }
}
