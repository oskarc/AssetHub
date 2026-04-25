namespace AssetHub.Application.Helpers;

/// <summary>
/// Shared file and path utilities for safe filenames, content-type mapping, and size formatting.
/// </summary>
public static class FileHelpers
{
    /// <summary>
    /// Escapes <c>\</c>, <c>%</c>, <c>_</c> in user-supplied LIKE input so the
    /// caller can wrap with their own wildcards without letting the user
    /// inject pattern metacharacters. Postgres ILIKE uses <c>\</c> as the
    /// default escape, so this is the right output for <c>EF.Functions.ILike</c>.
    /// Stops a search for <c>"%"</c> from matching every row (forced full
    /// scan / accidental DoS).
    /// </summary>
    public static string EscapeLikePattern(string input)
        => input.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

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

        var safeName = SanitizeNamePart(string.Join("_", title.Split(Path.GetInvalidFileNameChars())));
        if (string.IsNullOrEmpty(safeName))
            safeName = SanitizeNamePart(Path.GetFileNameWithoutExtension(objectKey));
        if (string.IsNullOrEmpty(safeName))
            safeName = "untitled";

        return safeName + extension;
    }

    /// <summary>
    /// Removes leading dots and surrounding whitespace from a filename component
    /// to prevent hidden-file creation and ZIP path traversal on extraction.
    /// </summary>
    private static string SanitizeNamePart(string name)
    {
        // Replace any internal ".." sequences, then strip leading dots/spaces
        return name.Replace("..", "_").TrimStart('.', ' ').TrimEnd(' ');
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

    private static readonly System.Text.RegularExpressions.Regex CamelCaseBoundary =
        new(@"([a-z])([A-Z])", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
    private static readonly System.Text.RegularExpressions.Regex AcronymBoundary =
        new(@"([A-Z]+)([A-Z][a-z])", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Converts a camelCase or PascalCase key into a readable "Title Case" string.
    /// e.g. "cameraModel" → "Camera Model", "GPSLatitude" → "GPS Latitude".
    /// </summary>
    public static string FormatMetadataKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;

        var result = CamelCaseBoundary.Replace(key, "$1 $2");
        result = AcronymBoundary.Replace(result, "$1 $2");
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
