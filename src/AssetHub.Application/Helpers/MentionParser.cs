using System.Text.RegularExpressions;

namespace AssetHub.Application.Helpers;

/// <summary>
/// Extracts <c>@username</c> tokens from comment bodies. Kept deliberately
/// narrow — matches ASCII letters, digits, <c>.</c>, <c>-</c>, <c>_</c>,
/// 1–32 chars, after a word boundary. Unknown usernames are the caller's
/// problem (they get dropped by <c>IUserLookupService.GetUserIdByUsernameAsync</c>).
///
/// The source-generated regex is compiled once at startup, no per-call cost.
/// </summary>
public static partial class MentionParser
{
    [GeneratedRegex(@"(?<![A-Za-z0-9])@([A-Za-z0-9._-]{1,32})", RegexOptions.CultureInvariant)]
    private static partial Regex MentionRegex();

    /// <summary>
    /// Returns distinct usernames found in the body, in first-seen order.
    /// Case is preserved as written; caller lower-cases for lookup if the
    /// identity provider is case-insensitive.
    /// </summary>
    public static IReadOnlyList<string> ExtractUsernames(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return Array.Empty<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (Match m in MentionRegex().Matches(body))
        {
            var name = m.Groups[1].Value;
            if (seen.Add(name))
                result.Add(name);
        }
        return result;
    }
}
