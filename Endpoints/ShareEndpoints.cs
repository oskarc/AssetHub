using Dam.Application.Repositories;
using Dam.Application.Services;
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

public class SharedAssetDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? MediumUrl { get; set; }
    public Dictionary<string, bool> Permissions { get; set; } = new();
}

public class SharedCollectionDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<SharedAssetDto> Assets { get; set; } = new();
    public Dictionary<string, bool> Permissions { get; set; } = new();
}

public static class ShareEndpoints
{
    public static void MapShareEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/shares")
            .DisableAntiforgery() // API uses JWT Bearer auth, not cookies with CSRF tokens
            .WithTags("Shares");

        // Public endpoints (no auth required)
        group.MapGet("{token}", GetSharedAsset)
            .WithName("GetSharedAsset")
            .AllowAnonymous();
        group.MapGet("{token}/download", DownloadSharedAsset)
            .WithName("DownloadSharedAsset")
            .AllowAnonymous();

        // Protected endpoints
        var authGroup = group.RequireAuthorization();
        authGroup.MapPost("", CreateShare).WithName("CreateShare");
        authGroup.MapDelete("{id}", RevokeShare).WithName("RevokeShare");
    }

    private static async Task<IResult> CreateShare(
        CreateShareDto dto,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] ICollectionRepository collectionRepository,
        [FromServices] IShareRepository shareRepository,
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
        else if (dto.ScopeType == "collection")
        {
            var collection = await collectionRepository.GetByIdAsync(dto.ScopeId);
            if (collection == null)
                return Results.NotFound("Collection not found");
        }

        // Generate secure token
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
            CreatedByUserId = httpContext.User.GetUserIdOrDefault(),
            PermissionsJson = dto.PermissionsJson ?? new Dictionary<string, bool> { { "view", true }, { "download", true } },
            PasswordHash = !string.IsNullOrWhiteSpace(dto.Password)
                ? Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(dto.Password)))
                : null
        };

        // Save share to database
        await shareRepository.CreateAsync(share);

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
        string? password,
        [FromServices] IShareRepository shareRepository,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] ICollectionRepository collectionRepository,
        [FromServices] IMinIOAdapter minioAdapter,
        IConfiguration configuration)
    {
        // Compute token hash to lookup in database
        using var sha256 = SHA256.Create();
        var tokenHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token)));

        var share = await shareRepository.GetByTokenHashAsync(tokenHash);
        if (share == null)
            return Results.NotFound("Share link not found or invalid");

        // Check if share is revoked
        if (share.RevokedAt.HasValue)
            return Results.BadRequest("This share link has been revoked");

        // Check if share is expired
        if (share.ExpiresAt < DateTime.UtcNow)
            return Results.BadRequest("This share link has expired");

        // Check password if required
        if (!string.IsNullOrEmpty(share.PasswordHash))
        {
            if (string.IsNullOrEmpty(password))
                return Results.Json(new { requiresPassword = true }, statusCode: 401);

            var inputPasswordHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password)));
            if (inputPasswordHash != share.PasswordHash)
                return Results.Unauthorized();
        }

        // Update access tracking
        share.LastAccessedAt = DateTime.UtcNow;
        share.AccessCount++;
        await shareRepository.UpdateAsync(share);

        var bucketName = configuration["MinIO:BucketName"] ?? "assethub-dev";

        if (share.ScopeType == "asset")
        {
            var asset = await assetRepository.GetByIdAsync(share.ScopeId);
            if (asset == null)
                return Results.NotFound("Asset not found");

            // Generate presigned URLs for thumbnails
            string? thumbnailUrl = null;
            string? mediumUrl = null;

            if (!string.IsNullOrEmpty(asset.ThumbObjectKey))
                thumbnailUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, asset.ThumbObjectKey, 3600);
            if (!string.IsNullOrEmpty(asset.MediumObjectKey))
                mediumUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, asset.MediumObjectKey, 3600);

            return Results.Ok(new SharedAssetDto
            {
                Id = asset.Id,
                Title = asset.Title,
                AssetType = asset.AssetType,
                ContentType = asset.ContentType,
                SizeBytes = asset.SizeBytes,
                ThumbnailUrl = thumbnailUrl,
                MediumUrl = mediumUrl,
                Permissions = share.PermissionsJson
            });
        }
        else if (share.ScopeType == "collection")
        {
            var collection = await collectionRepository.GetByIdAsync(share.ScopeId);
            if (collection == null)
                return Results.NotFound("Collection not found");

            var assets = await assetRepository.GetByCollectionAsync(share.ScopeId, 0, 100);
            var assetDtos = new List<SharedAssetDto>();

            foreach (var asset in assets)
            {
                string? thumbnailUrl = null;
                string? mediumUrl = null;

                if (!string.IsNullOrEmpty(asset.ThumbObjectKey))
                    thumbnailUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, asset.ThumbObjectKey, 3600);
                if (!string.IsNullOrEmpty(asset.MediumObjectKey))
                    mediumUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, asset.MediumObjectKey, 3600);

                assetDtos.Add(new SharedAssetDto
                {
                    Id = asset.Id,
                    Title = asset.Title,
                    AssetType = asset.AssetType,
                    ContentType = asset.ContentType,
                    SizeBytes = asset.SizeBytes,
                    ThumbnailUrl = thumbnailUrl,
                    MediumUrl = mediumUrl,
                    Permissions = share.PermissionsJson
                });
            }

            return Results.Ok(new SharedCollectionDto
            {
                Id = collection.Id,
                Name = collection.Name,
                Description = collection.Description,
                Assets = assetDtos,
                Permissions = share.PermissionsJson
            });
        }

        return Results.BadRequest("Invalid share scope type");
    }

    private static async Task<IResult> DownloadSharedAsset(
        string token,
        string? password,
        Guid? assetId,
        [FromServices] IShareRepository shareRepository,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IMinIOAdapter minioAdapter,
        IConfiguration configuration)
    {
        // Compute token hash to lookup in database
        using var sha256 = SHA256.Create();
        var tokenHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token)));

        var share = await shareRepository.GetByTokenHashAsync(tokenHash);
        if (share == null)
            return Results.NotFound("Share link not found or invalid");

        // Check if share is revoked
        if (share.RevokedAt.HasValue)
            return Results.BadRequest("This share link has been revoked");

        // Check if share is expired
        if (share.ExpiresAt < DateTime.UtcNow)
            return Results.BadRequest("This share link has expired");

        // Check if download permission is granted
        if (!share.PermissionsJson.TryGetValue("download", out var canDownload) || !canDownload)
            return Results.Forbid();

        // Check password if required
        if (!string.IsNullOrEmpty(share.PasswordHash))
        {
            if (string.IsNullOrEmpty(password))
                return Results.Json(new { requiresPassword = true }, statusCode: 401);

            var inputPasswordHash = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password)));
            if (inputPasswordHash != share.PasswordHash)
                return Results.Unauthorized();
        }

        var bucketName = configuration["MinIO:BucketName"] ?? "assethub-dev";
        Asset? targetAsset = null;

        if (share.ScopeType == "asset")
        {
            targetAsset = await assetRepository.GetByIdAsync(share.ScopeId);
        }
        else if (share.ScopeType == "collection")
        {
            // For collection shares, assetId must be provided to specify which asset to download
            if (!assetId.HasValue)
                return Results.BadRequest("assetId query parameter is required for collection share downloads");

            targetAsset = await assetRepository.GetByIdAsync(assetId.Value);

            // Verify the asset belongs to the shared collection
            if (targetAsset == null || targetAsset.CollectionId != share.ScopeId)
                return Results.NotFound("Asset not found in this shared collection");
        }

        if (targetAsset == null)
            return Results.NotFound("Asset not found");

        if (string.IsNullOrEmpty(targetAsset.OriginalObjectKey))
            return Results.BadRequest("Asset file not available");

        // Update access tracking
        share.LastAccessedAt = DateTime.UtcNow;
        share.AccessCount++;
        await shareRepository.UpdateAsync(share);

        // Generate presigned download URL and redirect
        var downloadUrl = await minioAdapter.GetPresignedDownloadUrlAsync(
            bucketName, 
            targetAsset.OriginalObjectKey, 
            3600);

        return Results.Redirect(downloadUrl);
    }

    private static async Task<IResult> RevokeShare(
        Guid id,
        [FromServices] IShareRepository shareRepository,
        HttpContext httpContext)
    {
        var share = await shareRepository.GetByIdAsync(id);
        if (share == null)
            return Results.NotFound("Share not found");

        // Check authorization (owner can revoke)
        var userId = httpContext.User.GetUserIdOrDefault();
        if (share.CreatedByUserId != userId)
            return Results.Forbid();

        // Mark as revoked instead of deleting (for audit trail)
        share.RevokedAt = DateTime.UtcNow;
        await shareRepository.UpdateAsync(share);

        return Results.NoContent();
    }
}
