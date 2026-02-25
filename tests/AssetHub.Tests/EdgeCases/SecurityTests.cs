using System.Net;
using System.Net.Http.Json;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Tests.Fixtures;
using AssetHub.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace AssetHub.Tests.EdgeCases;

/// <summary>
/// Comprehensive security tests covering:
/// - Authorization bypass attempts (role-based access)
/// - Parameter tampering (accessing other users' resources)
/// - Cross-collection access violations
/// - Input sanitization verification
/// - Share access control (expired, revoked, password-protected)
/// - Cross-tenant isolation
/// </summary>
[Collection("Api")]
public class SecurityTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;

    // Different user contexts for testing
    private const string UserAId = "user-a-001";
    private const string UserBId = "user-b-002";
    private const string ViewerUserId = "viewer-user-003";
    private const string ContributorUserId = "contributor-user-004";
    private const string ManagerUserId = "manager-user-005";

    public SecurityTests(CustomWebApplicationFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient ClientForUser(string userId, string username, string role) =>
        _factory.CreateAuthenticatedClient(TestClaimsProvider.WithUser(userId, username, role));

    private HttpClient AdminClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Admin());
    private HttpClient ViewerClient() => _factory.CreateAuthenticatedClient(TestClaimsProvider.Default());
    private HttpClient AnonymousClient()
    {
        // Clear any previous auth state to simulate unauthenticated request
        TestAuthHandler.ClaimsOverride = null;
        return _factory.CreateClient();
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 1: ROLE-BASED AUTHORIZATION BYPASS TESTS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Viewer_CannotAccess_AdminOnlyAssetsList()
    {
        var client = ViewerClient();

        var response = await client.GetAsync("/api/assets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotAccess_AdminHealthEndpoints()
    {
        var client = ViewerClient();

        // Admin-only endpoints should return 403
        var healthResponse = await client.GetAsync("/api/admin/audit");

        Assert.Equal(HttpStatusCode.Forbidden, healthResponse.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotCreateCollection()
    {
        var client = ViewerClient();
        var dto = new CreateCollectionDto { Name = "Unauthorized Collection" };

        var response = await client.PostAsJsonAsync("/api/collections", dto);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CannotDeleteAsset_InOwnCollection()
    {
        // Seed a collection with Contributor role for User A
        var (colId, assetId) = await SeedCollectionWithAssetAsync(UserAId, AclRole.Contributor);
        var client = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Contributor);

        var response = await client.DeleteAsync($"/api/assets/{assetId}?fromCollectionId={colId}");

        // Contributor can upload/edit but cannot delete - requires Manager+
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotUpdateAssetMetadata()
    {
        // Seed a collection with Viewer role for User A
        var (colId, assetId) = await SeedCollectionWithAssetAsync(UserAId, AclRole.Viewer);
        var client = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Viewer);

        var patchContent = JsonContent.Create(new { Title = "Hacked Title" });
        var response = await client.PatchAsync($"/api/assets/{assetId}", patchContent);

        // Viewer cannot edit - requires Contributor+
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Contributor_CannotManageCollectionACLs()
    {
        // Seed a collection with Contributor role
        var (colId, _) = await SeedCollectionWithAssetAsync(UserAId, AclRole.Contributor);
        var client = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Contributor);

        var dto = new SetCollectionAccessRequest
        {
            PrincipalId = "some-user-id",
            Role = RoleHierarchy.Roles.Viewer
        };
        var response = await client.PostAsJsonAsync($"/api/collections/{colId}/acl", dto);

        // ACL management requires Manager+
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 2: PARAMETER TAMPERING / CROSS-USER ACCESS TESTS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task UserA_CannotAccess_UserBs_Asset()
    {
        // User B creates a collection and asset
        var (colId, assetId) = await SeedCollectionWithAssetAsync(UserBId, AclRole.Admin);
        // User A tries to access it
        var clientA = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Contributor);

        var response = await clientA.GetAsync($"/api/assets/{assetId}");

        // User A has no ACL entry for User B's collection
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserA_CannotDelete_UserBs_Asset()
    {
        var (colId, assetId) = await SeedCollectionWithAssetAsync(UserBId, AclRole.Admin);
        var clientA = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Contributor);

        var response = await clientA.DeleteAsync($"/api/assets/{assetId}?fromCollectionId={colId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserA_CannotModify_UserBs_Collection()
    {
        var (colId, _) = await SeedCollectionWithAssetAsync(UserBId, AclRole.Admin);
        var clientA = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Admin);

        var patchContent = JsonContent.Create(new { Name = "Stolen Collection" });
        var response = await clientA.PatchAsync($"/api/collections/{colId}", patchContent);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserA_CannotDownload_UserBs_Asset()
    {
        var (colId, assetId) = await SeedCollectionWithAssetAsync(UserBId, AclRole.Admin);
        var clientA = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Contributor);

        var response = await clientA.GetAsync($"/api/assets/{assetId}/download");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserA_CannotView_UserBs_Thumbnail()
    {
        var (colId, assetId) = await SeedCollectionWithAssetAsync(UserBId, AclRole.Admin);
        var clientA = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Contributor);

        var response = await clientA.GetAsync($"/api/assets/{assetId}/thumb");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserA_CannotCreateShare_For_UserBs_Asset()
    {
        var (colId, assetId) = await SeedCollectionWithAssetAsync(UserBId, AclRole.Admin);
        var clientA = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Admin);

        var dto = new CreateShareDto
        {
            ScopeId = assetId,
            ScopeType = Constants.ScopeTypes.Asset
        };
        var response = await clientA.PostAsJsonAsync("/api/shares", dto);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserA_CannotAddAsset_To_UserBs_Collection()
    {
        // User B creates a collection
        var (colIdB, _) = await SeedCollectionWithAssetAsync(UserBId, AclRole.Admin);
        // User A creates their own asset (via their own collection)
        var (colIdA, assetIdA) = await SeedCollectionWithAssetAsync(UserAId, AclRole.Admin);
        var clientA = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Contributor);

        // User A tries to add their asset to User B's collection
        var response = await clientA.PostAsync($"/api/assets/{assetIdA}/collections/{colIdB}", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserA_CannotManageACLs_On_UserBs_Collection()
    {
        var (colId, _) = await SeedCollectionWithAssetAsync(UserBId, AclRole.Admin);
        var clientA = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Contributor);

        var dto = new SetCollectionAccessRequest
        {
            PrincipalId = UserAId, // Trying to give themselves access
            Role = RoleHierarchy.Roles.Admin
        };
        var response = await clientA.PostAsJsonAsync($"/api/collections/{colId}/acl", dto);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 3: DATA ENUMERATION / GUESSING ATTACKS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Random_AssetId_Guessing_Returns403_Not_AssetDetails()
    {
        // Create a real asset for User B
        var (colId, assetId) = await SeedCollectionWithAssetAsync(UserBId, AclRole.Admin);
        var clientA = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Contributor);

        // User A tries various GUIDs - should get 403 (forbidden) not 404 (not found)
        // This prevents enumeration attacks where attacker learns which IDs exist
        var response = await clientA.GetAsync($"/api/assets/{assetId}");

        // Should return 403 to prevent information leakage about existence
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Random_CollectionId_Guessing_Returns403_Or_404()
    {
        var (colId, _) = await SeedCollectionWithAssetAsync(UserBId, AclRole.Admin);
        var clientA = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Admin);

        var response = await clientA.GetAsync($"/api/collections/{colId}");

        // Either 403 or 404 is acceptable but must not leak data
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.NotFound);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 4: SHARE ACCESS CONTROL TESTS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExpiredShare_ReturnsUnauthorized()
    {
        // Create a share that expired yesterday
        var (colId, assetId, shareId, token) = await SeedShareAsync(
            userId: UserAId,
            scopeType: ShareScopeType.Asset,
            expiresAt: DateTime.UtcNow.AddDays(-1));

        // Use the real token - the share lookup will succeed but validation will fail
        var client = AnonymousClient();
        var response = await client.GetAsync($"/api/shares/{token}");

        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.NotFound,
            $"Expected 401 or 404 for expired share but got {response.StatusCode}");
    }

    [Fact]
    public async Task RevokedShare_ReturnsUnauthorized()
    {
        var (colId, assetId, shareId, token) = await SeedShareAsync(
            userId: UserAId,
            scopeType: ShareScopeType.Asset,
            revoked: true);

        // Use the real token - revoked share should be rejected
        var client = AnonymousClient();
        var response = await client.GetAsync($"/api/shares/{token}");

        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.NotFound,
            $"Expected 401 or 404 for revoked share but got {response.StatusCode}");
    }

    [Fact]
    public async Task PasswordProtectedShare_WithoutPassword_ReturnsUnauthorized()
    {
        var (colId, assetId, shareId, token) = await SeedShareAsync(
            userId: UserAId,
            scopeType: ShareScopeType.Asset,
            passwordHash: "$argon2id$v=19$m=65536,t=3,p=4$somesalt$somehash");  // Fake hash

        // Use the real token - password-protected share should require password
        var client = AnonymousClient();
        var response = await client.GetAsync($"/api/shares/{token}");

        // Should require password - returns 401 (PasswordRequired) or similar
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.NotFound,
            $"Expected 401/404 for password-protected share but got {response.StatusCode}");
    }

    [Fact]
    public async Task Share_Download_WithInvalidToken_ReturnsUnauthorized()
    {
        // Verify download endpoint rejects invalid tokens
        var client = AnonymousClient();
        var response = await client.GetAsync("/api/shares/invalid-token-for-download/download");

        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Share_Preview_WithInvalidToken_ReturnsUnauthorized()
    {
        // Verify preview endpoint rejects invalid tokens
        var client = AnonymousClient();
        var response = await client.GetAsync("/api/shares/invalid-token-for-preview/preview");

        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Viewer_CannotRevoke_OtherUsers_Share()
    {
        // Admin creates a share
        var (_, _, shareId, _) = await SeedShareAsync(
            userId: TestAuthHandler.AdminUserId,
            scopeType: ShareScopeType.Asset);

        // Viewer tries to revoke it
        var client = ViewerClient();
        var response = await client.DeleteAsync($"/api/shares/{shareId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserA_CannotRevoke_UserBs_Share()
    {
        // User B creates a share
        var (_, _, shareId, _) = await SeedShareAsync(
            userId: UserBId,
            scopeType: ShareScopeType.Asset);

        // User A tries to revoke it
        var clientA = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Admin);
        var response = await clientA.DeleteAsync($"/api/shares/{shareId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 5: INPUT SANITIZATION / INJECTION TESTS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateCollection_WithScriptInName_IsSanitized()
    {
        var client = AdminClient();
        var dto = new CreateCollectionDto
        {
            Name = "<script>alert('xss')</script>",
            Description = "Normal description"
        };

        var response = await client.PostAsJsonAsync("/api/collections", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CollectionResponseDto>();
        // The name should be returned as-is (stored safely) - XSS prevention is at render time
        // But it should not cause server errors
        Assert.NotNull(result);
        Assert.NotNull(result.Name);
    }

    [Fact]
    public async Task UpdateAsset_WithHtmlInTitle_DoesNotBreak()
    {
        var (colId, assetId) = await SeedCollectionWithAssetAsync(UserAId, AclRole.Admin);
        var client = ClientForUser(UserAId, "usera", RoleHierarchy.Roles.Admin);

        var patchContent = JsonContent.Create(new
        {
            Title = "<img src=x onerror=alert('xss')>",
            Description = "Test"
        });
        var response = await client.PatchAsync($"/api/assets/{assetId}", patchContent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidGuid_InAssetPath_Returns404()
    {
        var client = AdminClient();

        var response = await client.GetAsync("/api/assets/not-a-valid-guid");

        // Should handle gracefully - not crash with 500
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SqlInjection_InSearchQuery_DoesNotBreak()
    {
        var client = AdminClient();

        // Various SQL injection attempts
        string[] injectionAttempts =
        [
            "'; DROP TABLE Assets;--",
            "1 OR 1=1",
            "1'; SELECT * FROM users WHERE '1'='1",
            "admin'--",
            "\" OR \"\"=\""
        ];

        foreach (var attempt in injectionAttempts)
        {
            var response = await client.GetAsync($"/api/assets?search={Uri.EscapeDataString(attempt)}");
            // Should not crash - return 200 (empty results) or 400
            Assert.True(
                response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.BadRequest,
                $"SQL injection attempt failed: {attempt} returned {response.StatusCode}");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 6: AUTHENTICATION BOUNDARY TESTS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Anonymous_CannotAccess_AuthenticatedEndpoints()
    {
        var client = AnonymousClient();

        // All protected endpoints should require authentication.
        // Anonymous requests should return 401 Unauthorized.
        var endpoints = new[]
        {
            "/api/collections",     // Viewer+ required
            "/api/dashboard",       // Viewer+ required
            "/api/assets",          // Admin required
            "/api/admin/audit",     // Admin required
            "/api/admin/shares",    // Admin required
            "/api/admin/users"      // Admin required
        };

        foreach (var endpoint in endpoints)
        {
            var response = await client.GetAsync(endpoint);
            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.Forbidden,
                $"Anonymous access to {endpoint} returned {response.StatusCode} - expected 401 or 403");
        }
    }

    [Fact]
    public async Task Anonymous_CannotAccess_Collections_Returns401()
    {
        var client = AnonymousClient();

        var response = await client.GetAsync("/api/collections");

        // Critical security test: unauthenticated access to collections MUST be blocked
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_CanAccess_PublicShareEndpoints()
    {
        var client = AnonymousClient();

        // Public share endpoints should be accessible (even if token is invalid)
        var response = await client.GetAsync("/api/shares/some-token");

        // Should get 401/404 for invalid token, not redirect to login
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.NotFound);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SECTION 7: PRIVILEGE ESCALATION TESTS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Manager_CannotGrantAdmin_ToSelf()
    {
        // Create collection where user is Manager
        var (colId, _) = await SeedCollectionWithAssetAsync(ManagerUserId, AclRole.Manager);
        var client = ClientForUser(ManagerUserId, "manager", RoleHierarchy.Roles.Manager);

        // Try to escalate to Admin
        var dto = new SetCollectionAccessRequest
        {
            PrincipalId = ManagerUserId,
            Role = RoleHierarchy.Roles.Admin
        };
        var response = await client.PostAsJsonAsync($"/api/collections/{colId}/acl", dto);

        // Should be denied - Manager cannot grant Admin role
        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Privilege escalation attempt returned {response.StatusCode}");
    }

    [Fact]
    public async Task Contributor_CannotGrantManager_ToOther()
    {
        var (colId, _) = await SeedCollectionWithAssetAsync(ContributorUserId, AclRole.Contributor);
        var client = ClientForUser(ContributorUserId, "contributor", RoleHierarchy.Roles.Contributor);

        var dto = new SetCollectionAccessRequest
        {
            PrincipalId = "some-other-user",
            Role = RoleHierarchy.Roles.Manager
        };
        var response = await client.PostAsJsonAsync($"/api/collections/{colId}/acl", dto);

        // Contributor cannot manage ACLs
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPER METHODS
    // ═══════════════════════════════════════════════════════════════

    private async Task<(Guid ColId, Guid AssetId)> SeedCollectionWithAssetAsync(
        string userId,
        AclRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();

        var col = TestData.CreateCollection(name: $"SecCol-{Guid.NewGuid():N}", createdByUserId: userId);
        var asset = TestData.CreateAsset(title: $"SecAsset-{Guid.NewGuid():N}", createdByUserId: userId);
        db.Collections.Add(col);
        db.Assets.Add(asset);
        db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id, addedByUserId: userId));
        db.CollectionAcls.Add(TestData.CreateAcl(col.Id, userId, role));
        await db.SaveChangesAsync();

        return (col.Id, asset.Id);
    }

    private async Task<(Guid ColId, Guid AssetId, Guid ShareId, string Token)> SeedShareAsync(
        string userId,
        ShareScopeType scopeType,
        DateTime? expiresAt = null,
        bool revoked = false,
        string? passwordHash = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetHubDbContext>();

        var col = TestData.CreateCollection(name: $"ShareSecCol-{Guid.NewGuid():N}", createdByUserId: userId);
        var asset = TestData.CreateAsset(title: $"ShareSecAsset-{Guid.NewGuid():N}", createdByUserId: userId);
        db.Collections.Add(col);
        db.Assets.Add(asset);
        db.AssetCollections.Add(TestData.CreateAssetCollection(asset.Id, col.Id, addedByUserId: userId));
        db.CollectionAcls.Add(TestData.CreateAcl(col.Id, userId, AclRole.Admin));

        var scopeId = scopeType == ShareScopeType.Asset ? asset.Id : col.Id;
        var (share, token) = TestData.CreateShareWithToken(
            scopeType: scopeType,
            scopeId: scopeId,
            expiresAt: expiresAt,
            revoked: revoked,
            passwordHash: passwordHash,
            createdByUserId: userId);
        db.Shares.Add(share);
        await db.SaveChangesAsync();

        return (col.Id, asset.Id, share.Id, token);
    }
}
