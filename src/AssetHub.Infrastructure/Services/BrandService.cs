using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Standard DI shape for an admin CRUD + IO + audit service: brand repo + collection repo (for assign/unassign) + MinIO adapter + audit + CurrentUser + IOptions + logger. Bundling them into a holder would obscure intent.")]
public sealed class BrandService(
    IBrandRepository repo,
    ICollectionRepository collectionRepo,
    IMinIOAdapter minio,
    IAuditService audit,
    CurrentUser currentUser,
    IOptions<MinIOSettings> minioSettings,
    ILogger<BrandService> logger) : IBrandService
{
    private const int MaxLogoBytes = 1 * 1024 * 1024; // 1 MB cap; admin-uploaded logos shouldn't be huge
    private const int LogoUrlExpirySeconds = 60 * 60 * 24; // 24 h — share pages are public

    private const string BrandNotFound = "Brand not found.";

    // Audit detail keys / values — pulled out to satisfy S1192 and to keep
    // the audit table parseable (any analytics on brand events should
    // grep for these constants, not the magic strings sprinkled inline).
    private const string ScopeKey = "scope";
    private const string ScopeGlobal = "global";
    private const string ScopeCollection = "collection";

    // SVG is intentionally excluded — even when served via <img src> in the
    // share-page UI, a directly-navigated presigned URL renders the SVG as a
    // top-level document and runs any embedded <script>. Raster formats only.
    // (P-2 in the security review.)
    private static readonly HashSet<string> AllowedLogoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp"
    };

    private string Bucket => minioSettings.Value.BucketName;

    public async Task<ServiceResult<List<BrandResponseDto>>> ListAsync(CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();
        var rows = await repo.ListAllAsync(ct);
        var dtos = new List<BrandResponseDto>(rows.Count);
        foreach (var row in rows) dtos.Add(await ToDtoAsync(row, ct));
        return dtos;
    }

    public async Task<ServiceResult<BrandResponseDto>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();
        var row = await repo.GetByIdAsync(id, ct);
        if (row is null) return ServiceError.NotFound(BrandNotFound);
        return await ToDtoAsync(row, ct);
    }

    public async Task<ServiceResult<BrandResponseDto>> CreateAsync(CreateBrandDto dto, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            Name = dto.Name.Trim(),
            PrimaryColor = dto.PrimaryColor,
            SecondaryColor = dto.SecondaryColor,
            IsDefault = dto.IsDefault,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedByUserId = currentUser.UserId
        };

        // Demote any other default before persisting this one — partial
        // unique index would otherwise reject the insert.
        if (dto.IsDefault) await repo.ClearDefaultExceptAsync(brand.Id, ct);
        await repo.CreateAsync(brand, ct);

        await audit.LogAsync(
            NotificationConstants.AuditEvents.BrandCreated,
            Constants.ScopeTypes.Brand, brand.Id, currentUser.UserId,
            new Dictionary<string, object>
            {
                [ScopeKey] = brand.IsDefault ? ScopeGlobal : ScopeCollection,
                ["name"] = brand.Name
            },
            ct);

        logger.LogInformation(
            "Brand {BrandId} '{Name}' created by {UserId} (default={Default})",
            brand.Id, brand.Name, currentUser.UserId, brand.IsDefault);

        return await ToDtoAsync(brand, ct);
    }

    public async Task<ServiceResult<BrandResponseDto>> UpdateAsync(
        Guid id, UpdateBrandDto dto, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var row = await repo.GetByIdAsync(id, ct);
        if (row is null) return ServiceError.NotFound(BrandNotFound);

        var changed = await ApplyBrandPatchAsync(row, dto, ct);
        if (changed.Count == 0) return await ToDtoAsync(row, ct);

        await repo.UpdateAsync(row, ct);
        await audit.LogAsync(
            NotificationConstants.AuditEvents.BrandUpdated,
            Constants.ScopeTypes.Brand, id, currentUser.UserId,
            new Dictionary<string, object>
            {
                [ScopeKey] = row.IsDefault ? ScopeGlobal : ScopeCollection,
                ["changed_fields"] = changed
            },
            ct);

        return await ToDtoAsync(row, ct);
    }

    private async Task<List<string>> ApplyBrandPatchAsync(Brand row, UpdateBrandDto dto, CancellationToken ct)
    {
        var changed = new List<string>();
        if (dto.Name is not null && dto.Name != row.Name)
        {
            row.Name = dto.Name.Trim();
            changed.Add(nameof(row.Name));
        }
        if (dto.PrimaryColor is not null && dto.PrimaryColor != row.PrimaryColor)
        {
            row.PrimaryColor = dto.PrimaryColor;
            changed.Add(nameof(row.PrimaryColor));
        }
        if (dto.SecondaryColor is not null && dto.SecondaryColor != row.SecondaryColor)
        {
            row.SecondaryColor = dto.SecondaryColor;
            changed.Add(nameof(row.SecondaryColor));
        }
        if (dto.IsDefault is bool flag && flag != row.IsDefault)
        {
            row.IsDefault = flag;
            changed.Add(nameof(row.IsDefault));
            if (flag) await repo.ClearDefaultExceptAsync(row.Id, ct);
        }
        return changed;
    }

    public async Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var existing = await repo.GetByIdAsync(id, ct);
        if (existing is null) return ServiceError.NotFound(BrandNotFound);

        // Best-effort delete the logo object; the row delete is the
        // contract, MinIO cleanup is hygiene.
        if (!string.IsNullOrWhiteSpace(existing.LogoObjectKey))
            await TryDeleteLogoAsync(existing.LogoObjectKey, ct);

        await repo.DeleteAsync(id, ct);
        await audit.LogAsync(
            NotificationConstants.AuditEvents.BrandDeleted,
            Constants.ScopeTypes.Brand, id, currentUser.UserId,
            new Dictionary<string, object>
            {
                [ScopeKey] = existing.IsDefault ? ScopeGlobal : ScopeCollection,
                ["name"] = existing.Name
            },
            ct);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult<BrandResponseDto>> UploadLogoAsync(
        Guid id, Stream content, string fileName, string contentType, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        if (!AllowedLogoContentTypes.Contains(contentType))
            return ServiceError.BadRequest(
                "Logo must be PNG, JPEG, SVG, or WebP.");

        var row = await repo.GetByIdAsync(id, ct);
        if (row is null) return ServiceError.NotFound(BrandNotFound);

        var buffered = await BufferWithLimitAsync(content, ct);
        if (buffered.Error is not null) return buffered.Error;
        var ms = buffered.Stream!;

        // Use only the extension from the supplied filename — the path
        // segment is fixed (brands/{id}/logo.{ext}) so traversal and
        // sanitization concerns reduce to "is the extension known".
        var ext = Path.GetExtension(fileName ?? string.Empty);
        if (string.IsNullOrEmpty(ext) || !IsAllowedLogoExtension(ext))
            ext = ContentTypeToExt(contentType);
        var objectKey = $"{Constants.StoragePrefixes.Brands}/{row.Id}/logo{ext.ToLowerInvariant()}";

        await minio.UploadAsync(Bucket, objectKey, ms, contentType, ct);

        // If a previous logo existed and the key changed, delete the old one.
        if (!string.IsNullOrWhiteSpace(row.LogoObjectKey) && row.LogoObjectKey != objectKey)
            await TryDeleteLogoAsync(row.LogoObjectKey, ct);

        row.LogoObjectKey = objectKey;
        await repo.UpdateAsync(row, ct);

        await audit.LogAsync(
            NotificationConstants.AuditEvents.BrandUpdated,
            Constants.ScopeTypes.Brand, row.Id, currentUser.UserId,
            new Dictionary<string, object>
            {
                [ScopeKey] = row.IsDefault ? ScopeGlobal : ScopeCollection,
                ["changed_fields"] = new[] { "LogoObjectKey" }
            },
            ct);

        return await ToDtoAsync(row, ct);
    }

    public async Task<ServiceResult<BrandResponseDto>> RemoveLogoAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var row = await repo.GetByIdAsync(id, ct);
        if (row is null) return ServiceError.NotFound(BrandNotFound);
        if (string.IsNullOrWhiteSpace(row.LogoObjectKey)) return await ToDtoAsync(row, ct);

        await TryDeleteLogoAsync(row.LogoObjectKey, ct);
        row.LogoObjectKey = null;
        await repo.UpdateAsync(row, ct);
        return await ToDtoAsync(row, ct);
    }

    public async Task<ServiceResult> AssignToCollectionAsync(
        Guid brandId, Guid collectionId, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var brand = await repo.GetByIdAsync(brandId, ct);
        if (brand is null) return ServiceError.NotFound(BrandNotFound);

        var collection = await collectionRepo.GetByIdAsync(collectionId, ct: ct);
        if (collection is null) return ServiceError.NotFound("Collection not found.");

        if (collection.BrandId == brandId) return ServiceResult.Success;
        collection.BrandId = brandId;
        await collectionRepo.UpdateAsync(collection, ct);

        await audit.LogAsync(
            NotificationConstants.AuditEvents.BrandUpdated,
            Constants.ScopeTypes.Brand, brandId, currentUser.UserId,
            new Dictionary<string, object>
            {
                [ScopeKey] = ScopeCollection,
                ["assigned_collection_id"] = collectionId
            },
            ct);

        return ServiceResult.Success;
    }

    public async Task<ServiceResult> UnassignFromCollectionAsync(Guid collectionId, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var collection = await collectionRepo.GetByIdAsync(collectionId, ct: ct);
        if (collection is null) return ServiceError.NotFound("Collection not found.");

        if (collection.BrandId is null) return ServiceResult.Success;
        var previous = collection.BrandId.Value;
        collection.BrandId = null;
        await collectionRepo.UpdateAsync(collection, ct);

        await audit.LogAsync(
            NotificationConstants.AuditEvents.BrandUpdated,
            Constants.ScopeTypes.Brand, previous, currentUser.UserId,
            new Dictionary<string, object>
            {
                [ScopeKey] = ScopeCollection,
                ["unassigned_collection_id"] = collectionId
            },
            ct);

        return ServiceResult.Success;
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private record struct BufferedContent(MemoryStream? Stream, ServiceError? Error);

    private static async Task<BufferedContent> BufferWithLimitAsync(Stream content, CancellationToken ct)
    {
        // Buffer + size-cap up front so we don't stream past the limit.
        var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > MaxLogoBytes)
                return new BufferedContent(null, ServiceError.BadRequest($"Logo exceeds {MaxLogoBytes} bytes."));
            await ms.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        ms.Position = 0;
        return new BufferedContent(ms, null);
    }

    private async Task TryDeleteLogoAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await minio.DeleteAsync(Bucket, objectKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete brand logo at {ObjectKey}", objectKey);
        }
    }

    private async Task<BrandResponseDto> ToDtoAsync(Brand b, CancellationToken ct)
    {
        string? logoUrl = null;
        if (!string.IsNullOrWhiteSpace(b.LogoObjectKey))
        {
            try
            {
                logoUrl = await minio.GetPresignedDownloadUrlAsync(
                    Bucket, b.LogoObjectKey, LogoUrlExpirySeconds, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to presign logo URL for brand {BrandId} (key {ObjectKey})",
                    b.Id, b.LogoObjectKey);
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

    private static string ContentTypeToExt(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        _ => ""
    };

    private static bool IsAllowedLogoExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".webp" => true,
        _ => false
    };
}
