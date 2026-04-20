using System.Security.Claims;
using AssetHub.Api.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AssetHub.Tests.Authentication;

/// <summary>
/// Pure unit tests for <see cref="RequireScopeFilter"/>. No WebApplicationFactory —
/// the filter is a small pipeline element and worth its own contract test because it
/// is load-bearing for PAT authorization.
/// </summary>
public class RequireScopeFilterTests
{
    private const string ScopeClaim = PersonalAccessTokenAuthenticationHandler.ScopeClaimType;
    private const string PatIdClaim = "pat_id";

    [Fact]
    public async Task Cookie_Or_Jwt_Principal_Bypasses_The_Filter()
    {
        // No pat_scope claims ⇒ caller is not PAT-authenticated ⇒ filter must pass through.
        var filter = new RequireScopeFilter("assets:write");
        var ctx = NewContextFor(new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sub", "alice") }, "Cookies")));

        var result = await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>("NEXT_CALLED"));

        Assert.Equal("NEXT_CALLED", result);
    }

    [Fact]
    public async Task Pat_Without_Required_Scope_Is_Rejected_With_403()
    {
        var filter = new RequireScopeFilter("assets:write");
        var ctx = NewContextFor(NewPatPrincipal(scopes: new[] { "assets:read" }));

        var result = await filter.InvokeAsync(ctx, _ =>
            throw new InvalidOperationException("next() should not run when scope is missing"));

        AssertIsForbidden(result);
    }

    [Fact]
    public async Task Pat_With_Required_Scope_Is_Allowed()
    {
        var filter = new RequireScopeFilter("assets:write");
        var ctx = NewContextFor(NewPatPrincipal(scopes: new[] { "assets:read", "assets:write" }));

        var result = await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>("NEXT_CALLED"));

        Assert.Equal("NEXT_CALLED", result);
    }

    [Fact]
    public async Task Pat_With_Admin_Scope_Passes_Any_Check()
    {
        // The admin scope is documented as a wildcard — a PAT minted with "admin" must
        // pass every RequireScopeFilter regardless of the specific scope being checked.
        var filter = new RequireScopeFilter("collections:write");
        var ctx = NewContextFor(NewPatPrincipal(scopes: new[] { "admin" }));

        var result = await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>("NEXT_CALLED"));

        Assert.Equal("NEXT_CALLED", result);
    }

    [Fact]
    public async Task Pat_With_Zero_Scopes_Behaves_As_Owner_Impersonation()
    {
        // Entity design: an empty Scopes list means "act as the owner with no restrictions".
        // Filter detects this via absence of pat_scope claims and passes through. The UI
        // already warns users on create — the filter simply honours that contract.
        var filter = new RequireScopeFilter("assets:write");
        var ctx = NewContextFor(NewPatPrincipal(scopes: Array.Empty<string>()));

        var result = await filter.InvokeAsync(ctx, _ => ValueTask.FromResult<object?>("NEXT_CALLED"));

        Assert.Equal("NEXT_CALLED", result);
    }

    [Fact]
    public async Task Scope_Comparison_Is_Case_Sensitive()
    {
        // Ordinal comparison keeps the PAT model tight — "Assets:Read" is not "assets:read".
        var filter = new RequireScopeFilter("assets:read");
        var ctx = NewContextFor(NewPatPrincipal(scopes: new[] { "Assets:Read" }));

        var result = await filter.InvokeAsync(ctx, _ =>
            throw new InvalidOperationException("next() should not run for case-mismatched scope"));

        AssertIsForbidden(result);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ClaimsPrincipal NewPatPrincipal(IEnumerable<string> scopes)
    {
        var claims = new List<Claim>
        {
            new("sub", "alice"),
            new(PatIdClaim, Guid.NewGuid().ToString())
        };
        claims.AddRange(scopes.Select(s => new Claim(ScopeClaim, s)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "PAT"));
    }

    private static TestFilterContext NewContextFor(ClaimsPrincipal user)
    {
        var http = new DefaultHttpContext { User = user };
        return new TestFilterContext(http);
    }

    private static void AssertIsForbidden(object? result)
    {
        Assert.NotNull(result);
        // JsonHttpResult carries IStatusCodeHttpResult — read the property without
        // executing, so the test doesn't need a full IServiceProvider for JsonOptions.
        var statusCodeResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, statusCodeResult.StatusCode);
    }

    private sealed class TestFilterContext(HttpContext httpContext) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext { get; } = httpContext;
        public override IList<object?> Arguments { get; } = new List<object?>();
        public override T GetArgument<T>(int index) => throw new NotSupportedException();
    }
}
