using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Helpers;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Email.Templates;
using AssetHub.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Groups repository dependencies for <see cref="ShareService"/>
/// to keep the constructor parameter count manageable.
/// </summary>
public sealed record ShareServiceRepositories(
    IAssetRepository AssetRepo,
    IAssetCollectionRepository AssetCollectionRepo,
    ICollectionRepository CollectionRepo,
    IShareRepository ShareRepo);

/// <inheritdoc />
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Composition root for share creation: pre-grouped repos + email + user lookup + audit + Data Protection + workflow settings + webhook publisher + scoped CurrentUser + logger. Already grouped via ShareServiceRepositories; further bundling would obscure intent.")]
public sealed class ShareService(
    ShareServiceRepositories repos,
    IEmailService emailService,
    IUserLookupService userLookupService,
    IAuditService audit,
    IUnitOfWork uow,
    IDataProtectionProvider dataProtection,
    IOptions<WorkflowSettings> workflowSettings,
    IWebhookEventPublisher webhooks,
    CurrentUser currentUser,
    ILogger<ShareService> logger) : IShareService
{
    public async Task<ShareScopeValidation> ValidateScopeAsync(
        CreateShareDto dto, CancellationToken ct = default)
    {
        if (dto.ScopeType != Constants.ScopeTypes.Asset && dto.ScopeType != Constants.ScopeTypes.Collection)
            return new ShareScopeValidation { ErrorMessage = "ScopeType must be 'asset' or 'collection'", ErrorStatusCode = 400 };

        if (dto.ScopeType == Constants.ScopeTypes.Asset)
        {
            var asset = await repos.AssetRepo.GetByIdAsync(dto.ScopeId, ct);
            if (asset is null)
                return new ShareScopeValidation { ErrorMessage = "Asset not found", ErrorStatusCode = 404 };

            var assetCollections = await repos.AssetCollectionRepo.GetCollectionsForAssetAsync(dto.ScopeId, ct);

            // Allow sharing orphan assets - authorization will check system admin
            return new ShareScopeValidation
            {
                IsValid = true,
                CollectionIdsToCheck = assetCollections.Select(c => c.Id).ToList(),
                IsOrphanAsset = assetCollections.Count == 0,
                ContentName = asset.Title
            };
        }

        // Collection scope
        var collection = await repos.CollectionRepo.GetByIdAsync(dto.ScopeId, ct: ct);
        if (collection is null)
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
        var validation = await ValidateScopeAsync(dto, ct);
        if (!validation.IsValid)
            return ShareCreationResult.Error(validation.ErrorMessage!);

        var inputError = ValidateShareInputs(dto);
        if (inputError is not null)
            return ShareCreationResult.Error(inputError);

        // Workflow share-policy gate (T3-WF-01). Asset-scoped shares only —
        // collection shares aren't bulk-checked here to keep behaviour
        // simple for brand-portal use cases. System admins bypass, matching
        // the existing ACL-bypass pattern.
        if (dto.ScopeType == Constants.ScopeTypes.Asset && !currentUser.IsSystemAdmin)
        {
            var gateError = await CheckWorkflowShareGateAsync(dto.ScopeId, ct);
            if (gateError is not null) return ShareCreationResult.Error(gateError);
        }

        var passwordResult = ResolvePassword(dto.Password);
        if (passwordResult.Error is not null)
            return ShareCreationResult.Error(passwordResult.Error);

        var token = ShareHelpers.GenerateToken();
        var share = BuildShareEntity(dto, userId, token, passwordResult.PlainPassword!);

        // Share row + audit are atomic — without this, a torn write could
        // leave a usable share token unaudited or audited-without-share (A-4).
        await uow.ExecuteAsync(async tct =>
        {
            await repos.ShareRepo.CreateAsync(share, tct);
            await audit.LogAsync("share.created", Constants.ScopeTypes.Share, share.Id, userId,
                new() { ["scopeType"] = dto.ScopeType, ["scopeId"] = dto.ScopeId, ["expiresAt"] = share.ExpiresAt }, tct);
        }, ct);

        await PublishShareCreatedEventAsync(share, dto, userId, ct);

        var shareUrl = $"{baseUrl}/{Constants.Routes.Share}/{token}";
        var emailFailed = await TrySendShareEmailsAsync(dto, share, shareUrl, passwordResult.PlainPassword!, validation.ContentName!, userId);

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
                Password = passwordResult.PlainPassword!
            },
            EmailFailed = emailFailed
        };
    }

    private record struct PasswordResolution(string? PlainPassword, string? Error);

    private static PasswordResolution ResolvePassword(string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied))
            return new PasswordResolution(PasswordGenerator.Generate(12), null);
        var pwError = InputValidation.ValidateSharePassword(supplied);
        return pwError is null
            ? new PasswordResolution(supplied, null)
            : new PasswordResolution(null, pwError);
    }

    private Share BuildShareEntity(CreateShareDto dto, string userId, string token, string plainPassword)
    {
        var tokenHash = ShareHelpers.ComputeTokenHash(token);

        var protector = dataProtection.CreateProtector(Constants.DataProtection.ShareTokenProtector);
        var protectedToken = Convert.ToBase64String(protector.Protect(System.Text.Encoding.UTF8.GetBytes(token)));

        var passwordProtector = dataProtection.CreateProtector(Constants.DataProtection.SharePasswordProtector);
        var protectedPassword = Convert.ToBase64String(passwordProtector.Protect(System.Text.Encoding.UTF8.GetBytes(plainPassword)));

        return new Share
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
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(plainPassword),
            PasswordEncrypted = protectedPassword
        };
    }

    private Task PublishShareCreatedEventAsync(Share share, CreateShareDto dto, string userId, CancellationToken ct)
        // Webhook event — only the safe descriptors. Plaintext token /
        // password are NEVER included in the payload; subscribers don't
        // need them and shouldn't be able to access shared content.
        => webhooks.PublishAsync(WebhookEvents.ShareCreated, new
        {
            shareId = share.Id,
            scopeType = dto.ScopeType,
            scopeId = dto.ScopeId,
            createdByUserId = userId,
            createdAt = share.CreatedAt,
            expiresAt = share.ExpiresAt
        }, ct);

    private async Task<bool> TrySendShareEmailsAsync(
        CreateShareDto dto, Share share, string shareUrl, string plainPassword, string contentName, string userId)
    {
        if (dto.NotifyEmails?.Any() != true) return false;

        try
        {
            var userNames = await userLookupService.GetUserNamesAsync(new[] { userId }, default);
            var senderName = userNames.TryGetValue(userId, out var name) ? name : null;

            var emailTemplate = new ShareCreatedEmailTemplate(
                shareUrl: shareUrl,
                password: plainPassword,
                contentName: contentName,
                contentType: dto.ScopeType,
                senderName: senderName,
                expiresAt: share.ExpiresAt);

            await emailService.SendEmailAsync(dto.NotifyEmails, emailTemplate);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send share notification emails for share {ShareId}", share.Id);
            return true;
        }
    }

    /// <summary>
    /// Returns a human-readable error when the asset's workflow state is not
    /// in <see cref="WorkflowSettings.AllowedShareStates"/>, else null.
    /// Scope is intentionally limited to asset shares; collection shares
    /// bypass the gate to keep the common brand-portal flow simple.
    /// </summary>
    private async Task<string?> CheckWorkflowShareGateAsync(Guid assetId, CancellationToken ct)
    {
        var asset = await repos.AssetRepo.GetByIdAsync(assetId, ct);
        if (asset is null) return null; // 404 surfaced later in the flow.

        var allowed = workflowSettings.Value.AllowedShareStates;
        if (allowed is null || allowed.Count == 0) return null;

        if (allowed.Contains(asset.WorkflowState)) return null;

        var allowedStates = string.Join(", ", allowed.Select(s => s.ToDbString()));
        logger.LogInformation(
            "Share blocked by workflow gate: asset {AssetId} is in state {State}, allowed {Allowed}",
            assetId, asset.WorkflowState, allowedStates);
        return $"This asset cannot be shared yet — it must be in state {allowedStates}, " +
               $"currently '{asset.WorkflowState.ToDbString()}'.";
    }

    private static string? ValidateShareInputs(CreateShareDto dto)
    {
        if (dto.ExpiresAt.HasValue)
        {
            var expiryUtc = dto.ExpiresAt.Value.ToUniversalTime();
            if (expiryUtc <= DateTime.UtcNow)
                return "Expiry date must be in the future";
            if (expiryUtc > DateTime.UtcNow.AddDays(Constants.Limits.MaxShareExpiryDays))
                return $"Expiry date cannot be more than {Constants.Limits.MaxShareExpiryDays} days in the future";
        }

        if (dto.NotifyEmails?.Count > 0)
        {
            var invalidEmails = dto.NotifyEmails
                .Where(e => InputValidation.ValidateEmail(e) is not null)
                .ToList();
            if (invalidEmails.Count > 0)
                return "One or more notification email addresses are invalid";
        }

        return null;
    }
}
