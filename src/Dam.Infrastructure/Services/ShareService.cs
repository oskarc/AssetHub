using Dam.Application;
using Dam.Application.Dtos;
using Dam.Application.Helpers;
using Dam.Application.Repositories;
using Dam.Application.Services;
using Dam.Application.Services.Email.Templates;
using Dam.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Dam.Infrastructure.Services;

/// <inheritdoc />
public class ShareService(
    IAssetRepository assetRepository,
    IAssetCollectionRepository assetCollectionRepo,
    ICollectionRepository collectionRepository,
    IShareRepository shareRepository,
    IEmailService emailService,
    IUserLookupService userLookupService,
    IAuditService audit,
    IDataProtectionProvider dataProtection,
    ILogger<ShareService> logger) : IShareService
{
    public async Task<ShareScopeValidation> ValidateScopeAsync(
        CreateShareDto dto, CancellationToken ct = default)
    {
        if (dto.ScopeType != Constants.ScopeTypes.Asset && dto.ScopeType != Constants.ScopeTypes.Collection)
            return new ShareScopeValidation { ErrorMessage = "ScopeType must be 'asset' or 'collection'", ErrorStatusCode = 400 };

        if (dto.ScopeType == Constants.ScopeTypes.Asset)
        {
            var asset = await assetRepository.GetByIdAsync(dto.ScopeId, ct);
            if (asset == null)
                return new ShareScopeValidation { ErrorMessage = "Asset not found", ErrorStatusCode = 404 };

            var assetCollections = await assetCollectionRepo.GetCollectionsForAssetAsync(dto.ScopeId, ct);
            if (assetCollections.Count == 0)
                return new ShareScopeValidation { ErrorMessage = "Cannot create share for orphan asset. Add asset to a collection first.", ErrorStatusCode = 400 };

            return new ShareScopeValidation
            {
                IsValid = true,
                CollectionIdToCheck = assetCollections[0].Id,
                ContentName = asset.Title
            };
        }

        // Collection scope
        var collection = await collectionRepository.GetByIdAsync(dto.ScopeId, ct: ct);
        if (collection == null)
            return new ShareScopeValidation { ErrorMessage = "Collection not found", ErrorStatusCode = 404 };

        return new ShareScopeValidation
        {
            IsValid = true,
            CollectionIdToCheck = collection.Id,
            ContentName = collection.Name
        };
    }

    public async Task<ShareCreationResult> CreateShareAsync(
        CreateShareDto dto, string userId, string baseUrl, HttpContext httpContext, CancellationToken ct = default)
    {
        var validation = await ValidateScopeAsync(dto, ct);

        // Generate secure token
        var token = ShareHelpers.GenerateToken();
        var tokenHash = ShareHelpers.ComputeTokenHash(token);

        // Generate password if not provided
        var plainPassword = dto.Password;
        if (string.IsNullOrWhiteSpace(plainPassword))
            plainPassword = PasswordGenerator.Generate(12);

        var protector = dataProtection.CreateProtector(Constants.DataProtection.ShareTokenProtector);
        var protectedBytes = protector.Protect(System.Text.Encoding.UTF8.GetBytes(token));
        var protectedToken = Convert.ToBase64String(protectedBytes);

        var share = new Share
        {
            Id = Guid.NewGuid(),
            ScopeId = dto.ScopeId,
            ScopeType = dto.ScopeType,
            TokenHash = tokenHash,
            TokenEncrypted = protectedToken,
            ExpiresAt = dto.ExpiresAt?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
            PermissionsJson = dto.PermissionsJson ?? new Dictionary<string, bool> { { "view", true }, { "download", true } },
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(plainPassword)
        };

        await shareRepository.CreateAsync(share, ct);

        await audit.LogAsync("share.created", "share", share.Id, userId,
            new() { ["scopeType"] = dto.ScopeType, ["scopeId"] = dto.ScopeId, ["expiresAt"] = share.ExpiresAt }, httpContext, ct);

        var shareUrl = $"{baseUrl}/share/{token}";
        var emailFailed = false;

        // Send notification emails if recipients are provided
        if (dto.NotifyEmails?.Any() == true)
        {
            try
            {
                var userNames = await userLookupService.GetUserNamesAsync(new[] { userId }, default);
                var senderName = userNames.TryGetValue(userId, out var name) ? name : null;

                var emailTemplate = new ShareCreatedEmailTemplate(
                    shareUrl: shareUrl,
                    password: plainPassword,
                    contentName: validation.ContentName!,
                    contentType: dto.ScopeType,
                    senderName: senderName,
                    expiresAt: share.ExpiresAt);

                await emailService.SendEmailAsync(dto.NotifyEmails, emailTemplate);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send share notification emails for share {ShareId}", share.Id);
                emailFailed = true;
            }
        }

        return new ShareCreationResult
        {
            Response = new ShareResponseDto
            {
                Id = share.Id,
                ScopeType = share.ScopeType,
                ScopeId = share.ScopeId,
                Token = token,
                CreatedAt = share.CreatedAt,
                ExpiresAt = share.ExpiresAt,
                PermissionsJson = share.PermissionsJson,
                ShareUrl = shareUrl,
                Password = plainPassword
            },
            EmailFailed = emailFailed
        };
    }
}
