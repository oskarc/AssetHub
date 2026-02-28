using System.Text.RegularExpressions;

namespace AssetHub.Application;

/// <summary>
/// Shared input validation helpers used by both API endpoints and UI components.
/// Each method returns null on success, or an error message string on failure.
/// </summary>
public static partial class InputValidation
{
    // Precompiled regexes for performance
    private static readonly Regex UsernameRegex = UsernamePattern();
    private static readonly Regex EmailRegex = EmailPattern();

    [GeneratedRegex(@"^[a-zA-Z0-9_\-\.]+$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    /// <summary>
    /// Validates a username: 3-50 chars, alphanumeric + underscore/hyphen/dot.
    /// </summary>
    public static string? ValidateUsername(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Username is required";
        var trimmed = value.Trim();
        if (trimmed.Length < 3)
            return "Username must be at least 3 characters";
        if (trimmed.Length > 50)
            return "Username must be at most 50 characters";
        if (!UsernameRegex.IsMatch(trimmed))
            return "Username can only contain letters, numbers, underscores, hyphens, and dots";
        return null;
    }

    /// <summary>
    /// Validates an email address format.
    /// </summary>
    public static string? ValidateEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Email is required";
        if (!EmailRegex.IsMatch(value.Trim()))
            return "Invalid email address format";
        return null;
    }

    /// <summary>
    /// Validates a required non-empty string.
    /// </summary>
    public static string? ValidateRequired(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return $"{fieldName} is required";
        return null;
    }

    /// <summary>
    /// Validates password strength: ≥8 chars, uppercase, lowercase, digit, special.
    /// </summary>
    public static string? ValidatePassword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Password is required";
        if (value.Length < 8)
            return "Password must be at least 8 characters";
        if (!value.Any(char.IsUpper))
            return "Password must contain at least one uppercase letter";
        if (!value.Any(char.IsLower))
            return "Password must contain at least one lowercase letter";
        if (!value.Any(char.IsDigit))
            return "Password must contain at least one number";
        if (!value.Any(c => !char.IsLetterOrDigit(c)))
            return "Password must contain at least one special character";
        return null;
    }

    /// <summary>
    /// Validates a share password: must not be blank and must meet the minimum length requirement.
    /// Returns null on success, or an error message string on failure.
    /// </summary>
    public static string? ValidateSharePassword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Password cannot be empty";
        if (value.Length < Constants.Limits.MinSharePasswordLength)
            return $"Password must be at least {Constants.Limits.MinSharePasswordLength} characters";
        return null;
    }

    /// <summary>
    /// Runs multiple field validations and collects errors into a dictionary.
    /// Returns true if all validations pass (no errors).
    /// </summary>
    public static bool TryValidate(out Dictionary<string, string> errors, params (string field, string? error)[] checks)
    {
        errors = [];
        foreach (var (field, error) in checks)
        {
            if (error != null)
                errors[field] = error;
        }
        return errors.Count == 0;
    }
}
