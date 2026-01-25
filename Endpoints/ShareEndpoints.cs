using Dam.Application.Repositories;
using Dam.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Security.Cryptography;

namespace AssetHub.Endpoints;

public class CreateShareDto
{
    public required Guid ScopeId { get; set; } // AssetId or CollectionId
    public required string ScopeType { get; set; } // "asset" or "collection"
    public DateTime? ExpiresAt { get; set; }
    public string? Password { get; set; }
    public Dictionary<string, bool>? PermissionsJson { get; set; }
}

public class ShareResponseDto
{
    public required Guid Id { get; set; }
    public required string ScopeType { get; set; }
    public required Guid ScopeId { get; set; }
    public required string Token { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public required Dictionary<string, bool> PermissionsJson { get; set; }
    public required string ShareUrl { get; set; }
}

public static class ShareEndpoints
{
    public static void MapShareEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/shares")
            .WithTags("Shares");

        // Public endpoints (no auth required)
        group.MapGet("{token}", GetSharedAsset).WithName("GetSharedAsset");
        group.MapGet("{token}/download", DownloadSharedAsset).WithName("DownloadSharedAsset");

        // Protected endpoints
        var authGroup = group.RequireAuthorization();
        authGroup.MapPost("", CreateShare).WithName("CreateShare");
        authGroup.MapDelete("{id}", RevokeShare).WithName("RevokeShare");
    }

    private static async Task<IResult> CreateShare(
        CreateShareDto dto,
        [FromServices] IAssetRepository assetRepository,
        HttpContext httpContext)
    {
        // Validate scope
        if (dto.ScopeType != "asset" && dto.ScopeType != "collection")
            return Results.BadRequest("ScopeType must be 'asset' or 'collection'");

        if (dto.ScopeType == "asset")
        {
            var asset = await assetRepository.GetByIdAsync(dto.ScopeId);
            if (asset == null)
                return Results.NotFound("Asset not found");
        }
        // TODO: Add collection check when ICollectionRepository available in endpoint

        // Generate secure token (in production, use cryptographically secure tokens)
        var tokenBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(tokenBytes);
        }
        var token = Convert.ToBase64String(tokenBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // Hash token for storage
        using var sha256 = SHA256.Create();
        var tokenHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token)));

        var share = new Share
        {
            Id = Guid.NewGuid(),
            ScopeId = dto.ScopeId,
            ScopeType = dto.ScopeType,
            TokenHash = tokenHash,
            ExpiresAt = dto.ExpiresAt ?? DateTime.UtcNow.AddDays(7), // Default 7 days
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = httpContext.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?? httpContext.User.FindFirstValue("sub")
                ?? "unknown",
            PermissionsJson = dto.PermissionsJson ?? new Dictionary<string, bool> { { "view", true }, { "download", true } },
            PasswordHash = !string.IsNullOrWhiteSpace(dto.Password)
                ? Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(dto.Password)))
                : null
        };

        // TODO: Save share to database via IShareRepository

        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

        return Results.Created($"/api/shares/{share.Id}", new ShareResponseDto
        {
            Id = share.Id,
            ScopeType = share.ScopeType,
            ScopeId = share.ScopeId,
            Token = token, // Return unhashed token to user for this one-time display
            CreatedAt = share.CreatedAt,
            ExpiresAt = share.ExpiresAt,
            PermissionsJson = share.PermissionsJson,
            ShareUrl = $"{baseUrl}/share/{token}"
        });
    }

    private static async Task<IResult> GetSharedAsset(
        string token,
        [FromServices] IAssetRepository assetRepository)
    {
        // In production, you'd look up the share token in the database
        // For now, this is a placeholder
        return Results.BadRequest("Share endpoint not fully implemented - add ShareRepository");
    }

    private static async Task<IResult> DownloadSharedAsset(
        string token,
        string? password)
    {
        // Check share token validity, expiry, password
        // Download file from MinIO
        // Return file stream
        return Results.BadRequest("Share download endpoint not fully implemented");
    }

    private static async Task<IResult> RevokeShare(
        Guid id,
        HttpContext httpContext)
    {
        // Check authorization (owner can revoke)
        // Delete share record
        return Results.NoContent();
    }
}
