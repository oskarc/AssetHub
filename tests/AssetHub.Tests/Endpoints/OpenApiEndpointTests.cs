using System.Net;
using System.Text.Json;
using AssetHub.Tests.Fixtures;

namespace AssetHub.Tests.Endpoints;

/// <summary>
/// Smoke tests for the public OpenAPI document at <c>/swagger/v1/swagger.json</c>.
/// The test environment reports itself as "Testing" (not "Development"), so the Swagger
/// JSON endpoint carries <c>RequireAuthorization("RequireAdmin")</c> — exactly like
/// Staging / Production. This gives us the same gate an external attacker would face.
/// </summary>
[Collection("Api")]
public class OpenApiEndpointTests
{
    private readonly CustomWebApplicationFactory _factory;
    private const string SwaggerJson = "/swagger/v1/swagger.json";

    public OpenApiEndpointTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SwaggerJson_AsAdmin_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
        var response = await client.GetAsync(SwaggerJson);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SwaggerJson_AsViewer_Returns403()
    {
        // The JSON endpoint's RequireAdmin policy must reject non-admin principals in
        // every non-Development environment. Viewer is authenticated but not admin.
        var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());
        var response = await client.GetAsync(SwaggerJson);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SwaggerJson_Anonymous_Returns401()
    {
        var client = _factory.CreateAuthenticatedClient(claims: null);
        var response = await client.GetAsync(SwaggerJson);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SwaggerJson_ExposesPublicEndpointsOnly()
    {
        var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
        var response = await client.GetAsync(SwaggerJson);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");

        // Sampled public contract — these MUST appear.
        var pathNames = paths.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Contains("/api/v1/assets/{id}", pathNames);
        Assert.Contains("/api/v1/assets/search", pathNames);
        Assert.Contains("/api/v1/collections", pathNames);
        Assert.Contains("/api/v1/me/personal-access-tokens", pathNames);

        // Internal / admin / UI-only endpoints MUST NOT appear in the public schema.
        Assert.DoesNotContain("/api/v1/admin/users", pathNames);
        Assert.DoesNotContain("/api/v1/admin/migrations", pathNames);
        Assert.DoesNotContain("/api/v1/admin/audit", pathNames);
        Assert.DoesNotContain("/api/v1/admin/trash", pathNames);
        Assert.DoesNotContain("/api/v1/admin/export-presets", pathNames);
        Assert.DoesNotContain("/api/v1/shares", pathNames);

        // Collection ACL management is a manager/admin UX surface — not part of the integration contract.
        Assert.DoesNotContain("/api/v1/collections/{collectionId}/acl", pathNames);
    }

    [Fact]
    public async Task SwaggerJson_DeclaresBearerSecurityScheme()
    {
        var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
        var response = await client.GetAsync(SwaggerJson);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Shared "Bearer" scheme advertised to callers so Swagger UI can render the auth form.
        var scheme = doc.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task SwaggerUi_AsViewer_Returns403_InNonDevelopment()
    {
        // The UI middleware gate rejects non-admin principals independently of the
        // RequireAuthorization policy on the JSON endpoint.
        var client = _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());
        var response = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
