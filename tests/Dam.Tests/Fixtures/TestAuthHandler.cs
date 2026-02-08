using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dam.Tests.Fixtures;

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
        var provider = ClaimsOverride ?? TestClaimsProvider.Default();

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
        provider.Claims.Add(new Claim(ClaimTypes.Role, "viewer"));
        return provider;
    }

    public static TestClaimsProvider Admin()
    {
        var provider = new TestClaimsProvider();
        provider.Claims.Add(new Claim(ClaimTypes.NameIdentifier, TestAuthHandler.AdminUserId));
        provider.Claims.Add(new Claim("sub", TestAuthHandler.AdminUserId));
        provider.Claims.Add(new Claim("preferred_username", TestAuthHandler.AdminUsername));
        provider.Claims.Add(new Claim(ClaimTypes.Role, "admin"));
        return provider;
    }

    public static TestClaimsProvider WithUser(string userId, string username, string role = "viewer")
    {
        var provider = new TestClaimsProvider();
        provider.Claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        provider.Claims.Add(new Claim("sub", userId));
        provider.Claims.Add(new Claim("preferred_username", username));
        provider.Claims.Add(new Claim(ClaimTypes.Role, role));
        return provider;
    }
}
