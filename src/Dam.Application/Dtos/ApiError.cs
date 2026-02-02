namespace Dam.Application.Dtos;

/// <summary>
/// Standardized error response format for all API endpoints.
/// </summary>
public class ApiError
{
    /// <summary>
    /// A short error code for programmatic handling.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Optional additional details (field errors, etc).
    /// </summary>
    public Dictionary<string, string>? Details { get; init; }

    /// <summary>
    /// Creates a NotFound error.
    /// </summary>
    public static ApiError NotFound(string message = "Resource not found") => new()
    {
        Code = "NOT_FOUND",
        Message = message
    };

    /// <summary>
    /// Creates a Forbidden error.
    /// </summary>
    public static ApiError Forbidden(string message = "Access denied") => new()
    {
        Code = "FORBIDDEN",
        Message = message
    };

    /// <summary>
    /// Creates a BadRequest error.
    /// </summary>
    public static ApiError BadRequest(string message) => new()
    {
        Code = "BAD_REQUEST",
        Message = message
    };

    /// <summary>
    /// Creates a BadRequest error with field validation details.
    /// </summary>
    public static ApiError ValidationError(string message, Dictionary<string, string> fieldErrors) => new()
    {
        Code = "VALIDATION_ERROR",
        Message = message,
        Details = fieldErrors
    };

    /// <summary>
    /// Creates a server error without exposing internal details.
    /// </summary>
    public static ApiError ServerError(string message = "An unexpected error occurred") => new()
    {
        Code = "SERVER_ERROR",
        Message = message
    };
}
