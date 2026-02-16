using Dam.Application;
using Dam.Application.Dtos;

namespace AssetHub.Extensions;

/// <summary>
/// Maps <see cref="ServiceResult"/> / <see cref="ServiceResult{T}"/>
/// to <see cref="IResult"/> for Minimal API endpoints.
/// </summary>
public static class ServiceResultExtensions
{
    /// <summary>
    /// Converts a void ServiceResult to an HTTP response (default: 204 No Content on success).
    /// </summary>
    public static IResult ToHttpResult(this ServiceResult result)
    {
        return result.IsSuccess
            ? Results.NoContent()
            : ToErrorResult(result.Error!);
    }

    /// <summary>
    /// Converts a typed ServiceResult to an HTTP response (default: 200 OK on success).
    /// </summary>
    public static IResult ToHttpResult<T>(this ServiceResult<T> result)
    {
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : ToErrorResult(result.Error!);
    }

    /// <summary>
    /// Converts a typed ServiceResult with a custom success mapper.
    /// Use this for 201 Created, 202 Accepted, or other non-200 success codes.
    /// </summary>
    public static IResult ToHttpResult<T>(this ServiceResult<T> result, Func<T, IResult> onSuccess)
    {
        return result.IsSuccess
            ? onSuccess(result.Value!)
            : ToErrorResult(result.Error!);
    }

    private static IResult ToErrorResult(ServiceError error)
    {
        // Use Forbid() for 403 so ASP.NET's auth pipeline handles it consistently
        if (error.StatusCode == 403)
            return Results.Forbid();

        var body = new ApiError
        {
            Code = error.Code,
            Message = error.Message,
            Details = error.Details
        };

        return Results.Json(body, statusCode: error.StatusCode);
    }
}
