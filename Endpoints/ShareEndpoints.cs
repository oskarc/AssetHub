using System.IO.Compression;
using Dam.Application;
using Dam.Application.Dtos;
using Dam.Application.Helpers;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Application.Services.Email.Templates;
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
    /// <summary>
    /// Optional list of email addresses to notify about this share.
    /// </summary>
    public List<string>? NotifyEmails { get; set; }
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
    /// <summary>
    /// The plaintext password (only returned once at creation time).
    /// </summary>
    public string? Password { get; set; }
}

public class SharedAssetDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AssetType { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? MediumUrl { get; set; }
    public Dictionary<string, object> MetadataJson { get; set; } = new();
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
        group.MapGet("{token}/download-all", DownloadAllSharedAssets)
            .WithName("DownloadAllSharedAssets")
            .AllowAnonymous();
        group.MapGet("{token}/preview", PreviewSharedAsset)
            .WithName("PreviewSharedAsset")
            .AllowAnonymous();

        // Protected endpoints
        var authGroup = group.RequireAuthorization();
        authGroup.MapPost("", CreateShare).WithName("CreateShare");
        authGroup.MapDelete("{id}", RevokeShare).WithName("RevokeShare");
        authGroup.MapPut("{id}/password", UpdateSharePassword).WithName("UpdateSharePassword");
    }

    private static async Task<IResult> CreateShare(
        CreateShareDto dto,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] ICollectionRepository collectionRepository,
        [FromServices] ICollectionAuthorizationService authService,
        [FromServices] IShareRepository shareRepository,
        [FromServices] IEmailService emailService,
        [FromServices] IUserLookupService userLookupService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = httpContext.User.GetRequiredUserId();

        // Validate scope
        if (dto.ScopeType != "asset" && dto.ScopeType != "collection")
            return Results.BadRequest(ApiError.BadRequest("ScopeType must be 'asset' or 'collection'"));

        Guid collectionIdToCheck;
        string contentName;

        if (dto.ScopeType == "asset")
        {
            var asset = await assetRepository.GetByIdAsync(dto.ScopeId, ct);
            if (asset == null)
                return Results.NotFound(ApiError.NotFound("Asset not found"));
            
            // Get collections the asset belongs to
            var assetCollections = await assetCollectionRepo.GetCollectionsForAssetAsync(dto.ScopeId, ct);
            if (assetCollections.Count == 0)
                return Results.BadRequest(ApiError.BadRequest("Cannot create share for orphan asset. Add asset to a collection first."));
            
            // Use the first collection for authorization (user must have access to at least one collection)
            collectionIdToCheck = assetCollections[0].Id;
            contentName = asset.Title;
        }
        else // collection
        {
            var collection = await collectionRepository.GetByIdAsync(dto.ScopeId, ct: ct);
            if (collection == null)
                return Results.NotFound(ApiError.NotFound("Collection not found"));
            collectionIdToCheck = collection.Id;
            contentName = collection.Name;
        }

        // Authorization: User must have contributor+ role to share
        if (!await authService.CheckAccessAsync(userId, collectionIdToCheck, RoleHierarchy.Roles.Contributor, ct))
            return Results.Json(ApiError.Forbidden("You don't have permission to share this resource"), statusCode: 403);

        // Generate secure token
        var token = ShareHelpers.GenerateToken();
        var tokenHash = ShareHelpers.ComputeTokenHash(token);

        // Generate password if not provided (always require password)
        var plainPassword = dto.Password;
        if (string.IsNullOrWhiteSpace(plainPassword))
        {
            plainPassword = PasswordGenerator.Generate(12);
        }

        var share = new Share
        {
            Id = Guid.NewGuid(),
            ScopeId = dto.ScopeId,
            ScopeType = dto.ScopeType,
            TokenHash = tokenHash,
            ExpiresAt = dto.ExpiresAt?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(7), // Default 7 days
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = httpContext.User.GetRequiredUserId(),
            PermissionsJson = dto.PermissionsJson ?? new Dictionary<string, bool> { { "view", true }, { "download", true } },
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(plainPassword)
        };

        // Save share to database
        await shareRepository.CreateAsync(share, ct);

        var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var shareUrl = $"{baseUrl}/share/{token}";

        // Send notification emails if recipients are provided
        if (dto.NotifyEmails?.Any() == true)
        {
            try
            {
                // Get sender's display name
                var userNames = await userLookupService.GetUserNamesAsync(new[] { userId }, default);
                var senderName = userNames.TryGetValue(userId, out var name) ? name : null;

                var emailTemplate = new ShareCreatedEmailTemplate(
                    shareUrl: shareUrl,
                    password: plainPassword,
                    contentName: contentName,
                    contentType: dto.ScopeType,
                    senderName: senderName,
                    expiresAt: share.ExpiresAt
                );

                await emailService.SendEmailAsync(dto.NotifyEmails, emailTemplate);
            }
            catch (Exception ex)
            {
                // Log the error but don't fail the share creation
                // The share is already created successfully
                var loggerFactory = httpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("AssetHub.ShareEndpoints");
                logger.LogError(ex, $"Failed to send share notification emails for share {share.Id} to {string.Join(", ", dto.NotifyEmails)}");
            }
        }

        return Results.Created($"/api/shares/{share.Id}", new ShareResponseDto
        {
            Id = share.Id,
            ScopeType = share.ScopeType,
            ScopeId = share.ScopeId,
            Token = token, // Return unhashed token to user for this one-time display
            CreatedAt = share.CreatedAt,
            ExpiresAt = share.ExpiresAt,
            PermissionsJson = share.PermissionsJson,
            ShareUrl = shareUrl,
            Password = plainPassword // Return password for one-time display
        });
    }

    private static async Task<IResult> GetSharedAsset(
        string token,
        string? password,
        [FromServices] IShareRepository shareRepository,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] ICollectionRepository collectionRepository,
        [FromServices] IMinIOAdapter minioAdapter,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var tokenHash = ShareHelpers.ComputeTokenHash(token);

        var share = await shareRepository.GetByTokenHashAsync(tokenHash, ct);
        if (share == null)
            return Results.NotFound("Share link not found or invalid");

        var accessError = ShareHelpers.ValidateShareAccess(share.RevokedAt, share.ExpiresAt);
        if (accessError != null)
            return Results.BadRequest(accessError);

        // Check password if required
        if (!string.IsNullOrEmpty(share.PasswordHash))
        {
            if (string.IsNullOrEmpty(password))
                return Results.Json(new { requiresPassword = true }, statusCode: 401);

            if (!BCrypt.Net.BCrypt.Verify(password, share.PasswordHash))
                return Results.Unauthorized();
        }

        // Update access tracking
        share.LastAccessedAt = DateTime.UtcNow;
        share.AccessCount++;
        await shareRepository.UpdateAsync(share, ct);

        var bucketName = StorageConfig.GetBucketName(configuration);

        if (share.ScopeType == "asset")
        {
            var asset = await assetRepository.GetByIdAsync(share.ScopeId, ct);
            if (asset == null)
                return Results.NotFound("Asset not found");

            // Use API URLs for thumbnails to hide MinIO backend
            string? thumbnailUrl = null;
            string? mediumUrl = null;

            if (!string.IsNullOrEmpty(asset.ThumbObjectKey))
                thumbnailUrl = $"/api/shares/{token}/preview?size=thumb";
            if (!string.IsNullOrEmpty(asset.MediumObjectKey))
                mediumUrl = $"/api/shares/{token}/preview?size=medium";

            return Results.Ok(new SharedAssetDto
            {
                Id = asset.Id,
                Title = asset.Title,
                Description = asset.Description,
                AssetType = asset.AssetType,
                ContentType = asset.ContentType,
                SizeBytes = asset.SizeBytes,
                ThumbnailUrl = thumbnailUrl,
                MediumUrl = mediumUrl,
                MetadataJson = asset.MetadataJson,
                Permissions = share.PermissionsJson
            });
        }
        else if (share.ScopeType == "collection")
        {
            var collection = await collectionRepository.GetByIdAsync(share.ScopeId, ct: ct);
            if (collection == null)
                return Results.NotFound("Collection not found");

            var assets = await assetRepository.GetByCollectionAsync(share.ScopeId, 0, 100, ct);
            var assetDtos = new List<SharedAssetDto>();

            foreach (var asset in assets)
            {
                string? thumbnailUrl = null;
                string? mediumUrl = null;

                if (!string.IsNullOrEmpty(asset.ThumbObjectKey))
                    thumbnailUrl = $"/api/shares/{token}/preview?size=thumb&assetId={asset.Id}";
                if (!string.IsNullOrEmpty(asset.MediumObjectKey))
                    mediumUrl = $"/api/shares/{token}/preview?size=medium&assetId={asset.Id}";

                assetDtos.Add(new SharedAssetDto
                {
                    Id = asset.Id,
                    Title = asset.Title,
                    Description = asset.Description,
                    AssetType = asset.AssetType,
                    ContentType = asset.ContentType,
                    SizeBytes = asset.SizeBytes,
                    ThumbnailUrl = thumbnailUrl,
                    MediumUrl = mediumUrl,
                    MetadataJson = asset.MetadataJson,
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
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] IMinIOAdapter minioAdapter,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var tokenHash = ShareHelpers.ComputeTokenHash(token);

        var share = await shareRepository.GetByTokenHashAsync(tokenHash, ct);
        if (share == null)
            return Results.NotFound("Share link not found or invalid");

        var accessError = ShareHelpers.ValidateShareAccess(share.RevokedAt, share.ExpiresAt);
        if (accessError != null)
            return Results.BadRequest(accessError);

        // Check if download permission is granted
        if (!share.PermissionsJson.TryGetValue("download", out var canDownload) || !canDownload)
            return Results.Forbid();

        // Check password if required
        if (!string.IsNullOrEmpty(share.PasswordHash))
        {
            if (string.IsNullOrEmpty(password))
                return Results.Json(new { requiresPassword = true }, statusCode: 401);

            if (!BCrypt.Net.BCrypt.Verify(password, share.PasswordHash))
                return Results.Unauthorized();
        }

        var bucketName = StorageConfig.GetBucketName(configuration);
        Asset? targetAsset = null;

        if (share.ScopeType == "asset")
        {
            targetAsset = await assetRepository.GetByIdAsync(share.ScopeId, ct);
        }
        else if (share.ScopeType == "collection")
        {
            // For collection shares, assetId must be provided to specify which asset to download
            if (!assetId.HasValue)
                return Results.BadRequest("assetId query parameter is required for collection share downloads");

            targetAsset = await assetRepository.GetByIdAsync(assetId.Value, ct);

            // Verify the asset belongs to the shared collection using join table
            if (targetAsset == null)
                return Results.NotFound("Asset not found in this shared collection");
            
            var belongsToCollection = await assetCollectionRepo.BelongsToCollectionAsync(assetId.Value, share.ScopeId, ct);
            if (!belongsToCollection)
                return Results.NotFound("Asset not found in this shared collection");
        }

        if (targetAsset == null)
            return Results.NotFound("Asset not found");

        if (string.IsNullOrEmpty(targetAsset.OriginalObjectKey))
            return Results.BadRequest("Asset file not available");

        // Update access tracking
        share.LastAccessedAt = DateTime.UtcNow;
        share.AccessCount++;
        await shareRepository.UpdateAsync(share, ct);

        // Redirect to presigned URL — file goes directly from MinIO to browser
        var presignedUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, targetAsset.OriginalObjectKey, expirySeconds: 300, ct);
        return Results.Redirect(presignedUrl);
    }

    private static async Task<IResult> DownloadAllSharedAssets(
        string token,
        string? password,
        [FromServices] IShareRepository shareRepository,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] ICollectionRepository collectionRepository,
        [FromServices] IMinIOAdapter minioAdapter,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tokenHash = ShareHelpers.ComputeTokenHash(token);

        var share = await shareRepository.GetByTokenHashAsync(tokenHash, ct);
        if (share == null)
            return Results.NotFound("Share link not found or invalid");

        var accessError = ShareHelpers.ValidateShareAccess(share.RevokedAt, share.ExpiresAt);
        if (accessError != null)
            return Results.BadRequest(accessError);

        // Check if download permission is granted
        if (!share.PermissionsJson.TryGetValue("download", out var canDownload) || !canDownload)
            return Results.Forbid();

        // Check password if required
        if (!string.IsNullOrEmpty(share.PasswordHash))
        {
            if (string.IsNullOrEmpty(password))
                return Results.Json(new { requiresPassword = true }, statusCode: 401);

            if (!BCrypt.Net.BCrypt.Verify(password, share.PasswordHash))
                return Results.Unauthorized();
        }

        // Only works for collection shares
        if (share.ScopeType != "collection")
            return Results.BadRequest("Download all is only available for collection shares");

        var collection = await collectionRepository.GetByIdAsync(share.ScopeId, ct: ct);
        if (collection == null)
            return Results.NotFound("Collection not found");

        var assets = await assetRepository.GetByCollectionAsync(share.ScopeId, 0, 1000, ct);
        if (!assets.Any())
            return Results.BadRequest("No assets in collection");

        var bucketName = StorageConfig.GetBucketName(configuration);

        // Update access tracking
        share.LastAccessedAt = DateTime.UtcNow;
        share.AccessCount++;
        await shareRepository.UpdateAsync(share, ct);

        // Stream ZIP directly to the HTTP response — never buffer entire collection in memory.
        // Each asset is fetched from MinIO and written to the ZIP entry one at a time.
        var zipFileName = $"{collection.Name.Replace(" ", "_")}_assets.zip";
        httpContext.Response.ContentType = "application/zip";
        httpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{zipFileName}\"";

        await using var responseStream = httpContext.Response.BodyWriter.AsStream();
        using var archive = new ZipArchive(responseStream, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var asset in assets.Where(a => !string.IsNullOrEmpty(a.OriginalObjectKey)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var assetStream = await minioAdapter.DownloadAsync(bucketName, asset.OriginalObjectKey!, ct);
                var fileName = FileHelpers.GetSafeFileName(asset.Title, asset.OriginalObjectKey!, asset.ContentType);

                var entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await assetStream.CopyToAsync(entryStream, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Skip assets that fail to download
            }
        }

        return Results.Empty;
    }

    private static async Task<IResult> PreviewSharedAsset(
        string token,
        string? password,
        string? size,
        Guid? assetId,
        [FromServices] IShareRepository shareRepository,
        [FromServices] IAssetRepository assetRepository,
        [FromServices] IAssetCollectionRepository assetCollectionRepo,
        [FromServices] IMinIOAdapter minioAdapter,
        IConfiguration configuration,
        CancellationToken ct)
    {
        // Compute token hash to lookup in database
        var tokenHash = ShareHelpers.ComputeTokenHash(token);

        var share = await shareRepository.GetByTokenHashAsync(tokenHash, ct);
        if (share == null)
            return Results.NotFound("Share link not found or invalid");

        var accessError = ShareHelpers.ValidateShareAccess(share.RevokedAt, share.ExpiresAt);
        if (accessError != null)
            return Results.BadRequest(accessError);

        // Check password if required
        if (!string.IsNullOrEmpty(share.PasswordHash))
        {
            if (string.IsNullOrEmpty(password))
                return Results.Json(new { requiresPassword = true }, statusCode: 401);

            if (!BCrypt.Net.BCrypt.Verify(password, share.PasswordHash))
                return Results.Unauthorized();
        }

        var bucketName = StorageConfig.GetBucketName(configuration);
        Asset? targetAsset = null;

        if (share.ScopeType == "asset")
        {
            targetAsset = await assetRepository.GetByIdAsync(share.ScopeId, ct);
        }
        else if (share.ScopeType == "collection")
        {
            // For collection shares, assetId must be provided
            if (!assetId.HasValue)
                return Results.BadRequest("assetId query parameter is required for collection share previews");

            targetAsset = await assetRepository.GetByIdAsync(assetId.Value, ct);

            // Verify the asset belongs to the shared collection using join table
            if (targetAsset == null)
                return Results.NotFound("Asset not found in this shared collection");
            
            var belongsToCollection = await assetCollectionRepo.BelongsToCollectionAsync(assetId.Value, share.ScopeId, ct);
            if (!belongsToCollection)
                return Results.NotFound("Asset not found in this shared collection");
        }

        if (targetAsset == null)
            return Results.NotFound("Asset not found");

        // For PDF files, redirect to presigned URL for inline preview
        if (string.Equals(targetAsset.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdfUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, targetAsset.OriginalObjectKey, expirySeconds: 300, ct);
            return Results.Redirect(pdfUrl);
        }

        // For video/audio files without a specific rendition size, serve the original
        // so <video>/<audio> elements can play the actual media file.
        if (string.IsNullOrEmpty(size)
            && targetAsset.ContentType != null
            && (targetAsset.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                || targetAsset.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)))
        {
            var mediaUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, targetAsset.OriginalObjectKey, expirySeconds: 300, ct);
            return Results.Redirect(mediaUrl);
        }

        // Determine which object key to use based on size
        string? objectKey = size?.ToLower() switch
        {
            "thumb" => targetAsset.ThumbObjectKey,
            "medium" => targetAsset.MediumObjectKey,
            _ => targetAsset.MediumObjectKey ?? targetAsset.ThumbObjectKey
        };

        if (string.IsNullOrEmpty(objectKey))
            return Results.NotFound("Preview not available");

        // Redirect to presigned URL — file goes directly from MinIO to browser
        var presignedUrl = await minioAdapter.GetPresignedDownloadUrlAsync(bucketName, objectKey, expirySeconds: 300, ct);
        return Results.Redirect(presignedUrl);
    }

    private static async Task<IResult> RevokeShare(
        Guid id,
        [FromServices] IShareRepository shareRepository,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var share = await shareRepository.GetByIdAsync(id, ct);
        if (share == null)
            return Results.NotFound(ApiError.NotFound("Share not found"));

        // Check authorization (owner can revoke)
        var userId = httpContext.User.GetRequiredUserId();
        if (share.CreatedByUserId != userId)
            return Results.Json(ApiError.Forbidden("You don't have permission to revoke this share"), statusCode: 403);

        // Mark as revoked instead of deleting (for audit trail)
        share.RevokedAt = DateTime.UtcNow;
        await shareRepository.UpdateAsync(share, ct);

        return Results.NoContent();
    }

    /// <summary>
    /// Updates the password for an existing share.
    /// </summary>
    private static async Task<IResult> UpdateSharePassword(
        Guid id,
        [FromBody] UpdateSharePasswordDto dto,
        [FromServices] IShareRepository shareRepository,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var share = await shareRepository.GetByIdAsync(id, ct);
        if (share == null)
            return Results.NotFound(ApiError.NotFound("Share not found"));

        // Check authorization (owner or admin can update)
        var userId = httpContext.User.GetRequiredUserId();
        var isAdmin = httpContext.User.IsInRole(RoleHierarchy.Roles.Admin);
        if (share.CreatedByUserId != userId && !isAdmin)
            return Results.Json(ApiError.Forbidden("You don't have permission to update this share"), statusCode: 403);

        if (string.IsNullOrWhiteSpace(dto.Password))
            return Results.BadRequest(ApiError.BadRequest("Password cannot be empty"));

        share.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
        await shareRepository.UpdateAsync(share, ct);

        return Results.Ok(new { message = "Password updated successfully" });
    }
}

/// <summary>
/// DTO for updating a share's password.
/// </summary>
public class UpdateSharePasswordDto
{
    public required string Password { get; set; }
}
