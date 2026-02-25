using System.Security.Claims;
using System.Text.Encodings.Web;
using AssetHub.Application;
using Microsoft.AspNetCore.Authentication;
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
