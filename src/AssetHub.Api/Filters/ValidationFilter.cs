using System.ComponentModel.DataAnnotations;
using AssetHub.Application.Dtos;

namespace AssetHub.Api.Filters;

/// <summary>
/// Endpoint filter that enforces DataAnnotation validation on request DTOs.
/// Returns 400 with field-level errors when validation fails.
/// </summary>
public class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var dto = context.Arguments.OfType<T>().FirstOrDefault();
        if (dto is null)
            return await next(context);

        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(dto);

        if (!Validator.TryValidateObject(dto, validationContext, validationResults, validateAllProperties: true))
        {
            var fieldErrors = validationResults
                .Where(r => r.ErrorMessage is not null)
                .ToDictionary(
                    r => r.MemberNames.FirstOrDefault() ?? "general",
                    r => r.ErrorMessage!);

            return Results.BadRequest(ApiError.ValidationError("One or more validation errors occurred.", fieldErrors));
        }

        return await next(context);
    }
}
