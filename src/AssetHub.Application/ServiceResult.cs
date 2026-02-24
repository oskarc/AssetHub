using AssetHub.Application.Dtos;

namespace AssetHub.Application;

/// <summary>
/// Represents the outcome of a service operation that returns no value.
/// Services return this instead of throwing or returning HTTP-specific types.
/// </summary>
public record ServiceResult
{
    public ServiceError? Error { get; init; }
    public bool IsSuccess => Error is null;

    /// <summary>Successful result with no value.</summary>
    public static readonly ServiceResult Success = new();

    public static implicit operator ServiceResult(ServiceError error) => new() { Error = error };
}

/// <summary>
/// Represents the outcome of a service operation that returns a value of type <typeparamref name="T"/>.
/// </summary>
public record ServiceResult<T>
{
    public T? Value { get; init; }
    public ServiceError? Error { get; init; }
    public bool IsSuccess => Error is null;

    public static implicit operator ServiceResult<T>(T value) => new() { Value = value };
    public static implicit operator ServiceResult<T>(ServiceError error) => new() { Error = error };
}

/// <summary>
/// Describes a service-layer error with enough context for the endpoint layer
/// to produce the correct HTTP response.
/// </summary>
public record ServiceError(int StatusCode, string Code, string Message, Dictionary<string, string>? Details = null)
{
    public static ServiceError NotFound(string message = "Resource not found")
        => new(404, "NOT_FOUND", message);

    public static ServiceError Forbidden(string message = "Access denied")
        => new(403, "FORBIDDEN", message);

    public static ServiceError BadRequest(string message)
        => new(400, "BAD_REQUEST", message);

    public static ServiceError Conflict(string message)
        => new(409, "CONFLICT", message);

    public static ServiceError Validation(string message, Dictionary<string, string> details)
        => new(400, "VALIDATION_ERROR", message, details);

    public static ServiceError Server(string message = "An unexpected error occurred")
        => new(500, "SERVER_ERROR", message);

    public static ServiceError ShareExpired(string message = "This share link has expired")
        => new(401, "SHARE_EXPIRED", message);

    public static ServiceError ShareRevoked(string message = "This share link has been revoked")
        => new(401, "SHARE_REVOKED", message);
}
