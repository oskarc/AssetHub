using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Email.Templates;
using AssetHub.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

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
                CollectionIdsToCheck = assetCollections.Select(c => c.Id).ToList(),
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
            CollectionIdsToCheck = new List<Guid> { collection.Id },
            ContentName = collection.Name
        };
    }

    public async Task<ShareCreationResult> CreateShareAsync(
        CreateShareDto dto, string userId, string baseUrl, CancellationToken ct = default)
    {
        // Re-validate scope to get the content name for email notifications
        var validation = await ValidateScopeAsync(dto, ct);
        if (!validation.IsValid)
            return ShareCreationResult.Error(validation.ErrorMessage!);

        // Validate expiry: must be in the future and at most 90 days out
        if (dto.ExpiresAt.HasValue)
        {
            var expiryUtc = dto.ExpiresAt.Value.ToUniversalTime();
            if (expiryUtc <= DateTime.UtcNow)
                return ShareCreationResult.Error("Expiry date must be in the future");
            if (expiryUtc > DateTime.UtcNow.AddDays(Constants.Limits.MaxShareExpiryDays))
                return ShareCreationResult.Error($"Expiry date cannot be more than {Constants.Limits.MaxShareExpiryDays} days in the future");
        }

        // Validate notification email addresses up-front so the caller receives
        // a 400 rather than a silent email failure (CWE-20)
        if (dto.NotifyEmails?.Count > 0)
        {
            var invalidEmails = dto.NotifyEmails
                .Where(e => InputValidation.ValidateEmail(e) != null)
                .ToList();
            if (invalidEmails.Count > 0)
                return ShareCreationResult.Error("One or more notification email addresses are invalid");
        }

        // Generate secure token
        var token = ShareHelpers.GenerateToken();
        var tokenHash = ShareHelpers.ComputeTokenHash(token);

        // Generate password if not provided; validate length if explicitly provided
        var plainPassword = dto.Password;
        if (string.IsNullOrWhiteSpace(plainPassword))
            plainPassword = PasswordGenerator.Generate(12);
        else if (plainPassword.Length < Constants.Limits.MinSharePasswordLength)
            return ShareCreationResult.Error($"Password must be at least {Constants.Limits.MinSharePasswordLength} characters");

        var protector = dataProtection.CreateProtector(Constants.DataProtection.ShareTokenProtector);
        var protectedBytes = protector.Protect(System.Text.Encoding.UTF8.GetBytes(token));
        var protectedToken = Convert.ToBase64String(protectedBytes);

        var share = new Share
        {
            Id = Guid.NewGuid(),
            ScopeId = dto.ScopeId,
            ScopeType = dto.ScopeType.ToShareScopeType(),
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
            new() { ["scopeType"] = dto.ScopeType, ["scopeId"] = dto.ScopeId, ["expiresAt"] = share.ExpiresAt }, ct);

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
                ScopeType = share.ScopeType.ToDbString(),
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
