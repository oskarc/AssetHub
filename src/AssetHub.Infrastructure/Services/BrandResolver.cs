using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <inheritdoc />
public sealed class BrandResolver(
    IBrandRepository brandRepo,
    ICollectionRepository collectionRepo,
    IAssetCollectionRepository assetCollectionRepo,
    IMinIOAdapter minio,
    IOptions<MinIOSettings> minioSettings,
    ILogger<BrandResolver> logger) : IBrandResolver
{
    private const int LogoUrlExpirySeconds = 60 * 60 * 24;

    public async Task<BrandResponseDto?> ResolveForShareAsync(
        string scopeType, Guid scopeId, CancellationToken ct)
    {
        try
        {
            Brand? brand = scopeType switch
            {
                Constants.ScopeTypes.Collection => await ResolveFromCollectionAsync(scopeId, ct),
                Constants.ScopeTypes.Asset => await ResolveFromAssetAsync(scopeId, ct),
                _ => null
            };

            // Fall back to the global default if the scope didn't carry one.
            brand ??= await brandRepo.GetDefaultAsync(ct);

            return brand is null ? null : await ToDtoAsync(brand, ct);
        }
        catch (Exception ex)
        {
            // Resolver must never crash the share page — bad branding is
            // a soft failure, render unbranded.
            logger.LogWarning(ex,
                "Brand resolution failed for {ScopeType} {ScopeId}; falling back to unbranded",
                scopeType, scopeId);
            return null;
        }
    }

    private async Task<Brand?> ResolveFromCollectionAsync(Guid collectionId, CancellationToken ct)
    {
        var collection = await collectionRepo.GetByIdAsync(collectionId, ct: ct);
        if (collection?.BrandId is not Guid bid) return null;
        return await brandRepo.GetByIdAsync(bid, ct);
    }

    private async Task<Brand?> ResolveFromAssetAsync(Guid assetId, CancellationToken ct)
    {
        // Pick the first collection with a brand. Order is whatever the repo
        // returns (insertion today); we'll revisit if customers care.
        var collections = await assetCollectionRepo.GetCollectionsForAssetAsync(assetId, ct);
        foreach (var c in collections)
        {
            if (c.BrandId is not Guid bid) continue;
            var brand = await brandRepo.GetByIdAsync(bid, ct);
            if (brand is not null) return brand;
        }
        return null;
    }

    private async Task<BrandResponseDto> ToDtoAsync(Brand b, CancellationToken ct)
    {
        string? logoUrl = null;
        if (!string.IsNullOrWhiteSpace(b.LogoObjectKey))
        {
            try
            {
                logoUrl = await minio.GetPresignedDownloadUrlAsync(
                    minioSettings.Value.BucketName, b.LogoObjectKey,
                    LogoUrlExpirySeconds, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to presign logo URL for brand {BrandId}", b.Id);
            }
        }

        return new BrandResponseDto
        {
            Id = b.Id,
            Name = b.Name,
            IsDefault = b.IsDefault,
            LogoObjectKey = b.LogoObjectKey,
            LogoUrl = logoUrl,
            PrimaryColor = b.PrimaryColor,
            SecondaryColor = b.SecondaryColor,
            CreatedAt = b.CreatedAt,
            CreatedByUserId = b.CreatedByUserId,
            UpdatedAt = b.UpdatedAt
        };
    }
}
