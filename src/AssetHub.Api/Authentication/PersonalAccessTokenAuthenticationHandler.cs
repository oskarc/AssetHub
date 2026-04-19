using System.Security.Claims;
using System.Text.Encodings.Web;
using AssetHub.Application;
using AssetHub.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Api.Authentication;

/// <summary>
/// Authentication scheme that resolves <c>Authorization: Bearer pat_*</c> headers against
/// the <see cref="IPersonalAccessTokenService"/>. On success, builds a ClaimsPrincipal
/// equivalent to an OIDC-authenticated user (sub + display name + realm roles) plus a
/// <c>pat_id</c> and per-scope <c>pat_scope</c> claims so endpoints can enforce scope.
///
/// Realm roles are fetched from Keycloak and cached for 1 minute — short enough that
/// a role change in Keycloak takes effect quickly, long enough to avoid hammering the
/// admin API on every request.
/// </summary>
public sealed class PersonalAccessTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PAT";

    /// <summary>
    /// Claim type carrying the PAT row id. Endpoints / audit can use this to identify
    /// which token was presented without re-querying.
    /// </summary>
    public const string TokenIdClaimType = "pat_id";

    /// <summary>
    /// Claim type carrying a single granted scope. Multiple claims of this type may be
    /// present — one per scope on the PAT. An empty scope set means "owner-impersonation".
    /// </summary>
    public const string ScopeClaimType = "pat_scope";

    private readonly IPersonalAccessTokenService _patService;
    private readonly IKeycloakUserService _keycloak;
    private readonly HybridCache _cache;

    public PersonalAccessTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        IPersonalAccessTokenService patService,
        IKeycloakUserService keycloak,
        HybridCache cache)
        : base(options, loggerFactory, encoder)
    {
        _patService = patService;
        _keycloak = keycloak;
        _cache = cache;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!TryReadBearer(out var bearer))
            return AuthenticateResult.NoResult();

        if (!bearer.StartsWith(IPersonalAccessTokenService.TokenPrefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var token = await _patService.VerifyAndStampAsync(bearer, Context.RequestAborted);
        if (token is null)
        {
            // No row, or row is revoked / expired — same response either way so timing leaks nothing.
            return AuthenticateResult.Fail("Invalid or revoked personal access token");
        }

        var roles = await GetUserRolesAsync(token.OwnerUserId, Context.RequestAborted);

        var claims = new List<Claim>
        {
            new("sub", token.OwnerUserId),
            new(ClaimTypes.NameIdentifier, token.OwnerUserId),
            new("preferred_username", token.OwnerUserId),
            new(TokenIdClaimType, token.Id.ToString())
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        foreach (var scope in token.Scopes)
            claims.Add(new Claim(ScopeClaimType, scope));

        var identity = new ClaimsIdentity(claims, SchemeName, "preferred_username", ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"Bearer realm=\"AssetHub\", scheme=\"{SchemeName}\"";
        return Task.CompletedTask;
    }

    private bool TryReadBearer(out string bearer)
    {
        bearer = string.Empty;
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header)) return false;

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        bearer = header[prefix.Length..].Trim();
        return bearer.Length > 0;
    }

    private async Task<HashSet<string>> GetUserRolesAsync(string userId, CancellationToken ct)
    {
        // HybridCache deserialises HashSet<string> via System.Text.Json — works out of the box.
        var roles = await _cache.GetOrCreateAsync(
            CacheKeys.UserRealmRoles(userId),
            async innerCt => (await _keycloak.GetUserRealmRolesAsync(userId, innerCt)).ToList(),
            new HybridCacheEntryOptions
            {
                Expiration = CacheKeys.UserRealmRolesTtl,
                LocalCacheExpiration = TimeSpan.FromSeconds(30)
            },
            cancellationToken: ct);

        return new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
    }
}
