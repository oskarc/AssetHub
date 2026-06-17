using System.Net;

namespace AssetHub.Ui.Services;

/// <summary>
/// Exception thrown when a facade call fails. Carries the same shape callers
/// expected from the legacy HTTP-based client (status code, error code, details)
/// so error-handling code in pages does not need to change.
/// </summary>
public sealed class ApiException : Exception
{
    /// <summary>The HTTP-equivalent status code (mapped from the service error status code).</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The structured error code (e.g. "DUPLICATE_ASSET", "NOT_FOUND", "PASSWORD_REQUIRED").</summary>
    public string? ErrorCode { get; }

    /// <summary>Additional structured details (e.g. validation field errors).</summary>
    public Dictionary<string, string>? Details { get; }

    public ApiException(string message, HttpStatusCode statusCode, string? errorCode = null, Dictionary<string, string>? details = null) : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Details = details;
    }

    public override string ToString()
    {
        return $"ApiException: {Message} (HTTP {(int)StatusCode}, Code={ErrorCode})";
    }
}
