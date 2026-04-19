using Microsoft.AspNetCore.Http;

namespace AssetHub.Api.Authentication;

/// <summary>
/// Endpoint filter for PAT scope enforcement. Apply with
/// <c>.AddEndpointFilter(new RequireScopeFilter("assets:read"))</c> on a Map* group or
/// individual endpoint. Has no effect on cookie/JWT principals — the assumption is that
/// OIDC sessions already operate at the user's full surface area, so scopes only narrow
/// PATs.
///
/// A PAT principal with NO <c>pat_scope</c> claims is treated as full owner-impersonation
/// and passes every scope check.
/// </summary>
public sealed class RequireScopeFilter : IEndpointFilter
{
    private readonly string _requiredScope;

    public RequireScopeFilter(string requiredScope)
    {
        _requiredScope = requiredScope;
    }

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;

        // Only PAT principals carry scope claims; cookie / JWT pass straight through.
        var scopes = user.FindAll(PersonalAccessTokenAuthenticationHandler.ScopeClaimType).ToList();
        if (scopes.Count == 0)
            return next(context);

        var hasScope = scopes.Any(c => string.Equals(c.Value, _requiredScope, StringComparison.Ordinal))
            || scopes.Any(c => string.Equals(c.Value, "admin", StringComparison.Ordinal));

        if (!hasScope)
        {
            return ValueTask.FromResult<object?>(Results.Json(
                new { code = "FORBIDDEN", message = $"PAT does not grant scope '{_requiredScope}'" },
                statusCode: StatusCodes.Status403Forbidden));
        }

        return next(context);
    }
}
