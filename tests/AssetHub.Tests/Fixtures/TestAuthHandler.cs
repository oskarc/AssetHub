using System.Security.Claims;
using System.Text.Encodings.Web;
using AssetHub.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Tests.Fixtures;

/// <summary>
/// Authentication handler for integration tests.
/// Creates claims from a configurable identity without requiring a real OIDC provider.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";
    public const string DefaultUserId = "test-user-001";
    public const string DefaultUsername = "testuser";
    public const string AdminUserId = "test-admin-001";
    public const string AdminUsername = "testadmin";

    /// <summary>
    /// Set from test code to override the identity for a specific request.
    /// Use via <see cref="TestClaimsProvider"/>.
    /// </summary>
    public static TestClaimsProvider? ClaimsOverride { get; set; }

    /// <summary>
    /// Opt-in, per request: send this header to hand challenge and forbid to the
    /// real Identity cookie handler, so a test sees the production redirects
    /// (LoginPath, AccessDeniedPath) with the real fallback policy and page
    /// metadata. Without it this scheme answers a bare 401/403, which other tests
    /// depend on. A header rather than a static flag, so it cannot leak between
    /// tests.
    /// </summary>
    public const string IdentityChallengeHeader = "X-Test-Identity-Challenge";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // If no override is set, simulate unauthenticated request
        if (ClaimsOverride == null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var provider = ClaimsOverride;

        var identity = new ClaimsIdentity(provider.Claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Request.Headers.ContainsKey(IdentityChallengeHeader)
            ? Context.ChallengeAsync(IdentityConstants.ApplicationScheme, properties)
            : base.HandleChallengeAsync(properties);

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Request.Headers.ContainsKey(IdentityChallengeHeader)
            ? Context.ForbidAsync(IdentityConstants.ApplicationScheme, properties)
            : base.HandleForbiddenAsync(properties);
}

/// <summary>
/// Configures claims for a test request.
/// </summary>
public class TestClaimsProvider
{
    public List<Claim> Claims { get; } = new();

    public static TestClaimsProvider Default()
    {
        var provider = new TestClaimsProvider();
        provider.Claims.Add(new Claim(ClaimTypes.NameIdentifier, TestAuthHandler.DefaultUserId));
        provider.Claims.Add(new Claim("sub", TestAuthHandler.DefaultUserId));
        provider.Claims.Add(new Claim("preferred_username", TestAuthHandler.DefaultUsername));
        provider.Claims.Add(new Claim(ClaimTypes.Role, RoleHierarchy.Roles.Viewer));
        return provider;
    }

    public static TestClaimsProvider Admin()
    {
        var provider = new TestClaimsProvider();
        provider.Claims.Add(new Claim(ClaimTypes.NameIdentifier, TestAuthHandler.AdminUserId));
        provider.Claims.Add(new Claim("sub", TestAuthHandler.AdminUserId));
        provider.Claims.Add(new Claim("preferred_username", TestAuthHandler.AdminUsername));
        provider.Claims.Add(new Claim(ClaimTypes.Role, RoleHierarchy.Roles.Admin));
        return provider;
    }

    public static TestClaimsProvider WithUser(string userId, string username, string role = RoleHierarchy.Roles.Viewer)
    {
        var provider = new TestClaimsProvider();
        provider.Claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        provider.Claims.Add(new Claim("sub", userId));
        provider.Claims.Add(new Claim("preferred_username", username));
        provider.Claims.Add(new Claim(ClaimTypes.Role, role));
        return provider;
    }
}
