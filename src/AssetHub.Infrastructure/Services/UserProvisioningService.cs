using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Email.Templates;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Orchestrates user provisioning steps: collection validation, access granting, and welcome emails.
/// </summary>
public sealed class UserProvisioningService(
    ICollectionRepository collectionRepo,
    ICollectionAclRepository aclRepo,
    IEmailService emailService,
    IAuditService audit,
    ILogger<UserProvisioningService> logger) : IUserProvisioningService
{

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> ValidateCollectionsExistAsync(
        List<Guid> collectionIds, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        foreach (var id in collectionIds)
        {
            if (!await collectionRepo.ExistsAsync(id, ct))
                errors[$"collection_{id}"] = $"Collection {id} not found";
        }
        return errors;
    }

    /// <inheritdoc />
    public async Task GrantCollectionAccessAsync(
        List<Guid> collectionIds, string userId, string role, string username,
        CancellationToken ct = default)
    {
        foreach (var collectionId in collectionIds)
        {
            try
            {
                await aclRepo.SetAccessAsync(collectionId, Constants.PrincipalTypes.User, userId, role, ct);
                await audit.LogAsync("user.access_granted", Constants.ScopeTypes.Collection, collectionId, userId,
                    new() { ["role"] = role, ["username"] = username }, ct);
                logger.LogInformation("Granted '{Role}' access on collection {CollectionId} to new user '{Username}'",
                    role, collectionId, username);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to grant access on collection {CollectionId} for new user '{Username}'",
                    collectionId, username);
            }
        }
    }

    /// <inheritdoc />
    public async Task SendWelcomeEmailAsync(
        string email, string username, string password, bool requirePasswordChange,
        string baseUrl, string adminUsername, CancellationToken ct = default)
    {
        try
        {
            var loginUrl = $"{baseUrl}/auth/login";

            var emailTemplate = new WelcomeEmailTemplate(
                username, password, loginUrl, requirePasswordChange, adminUsername);

            await emailService.SendEmailAsync(email, emailTemplate, ct);
            logger.LogInformation("Welcome email sent to '{Email}' for new user '{Username}'", email, username);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send welcome email to '{Email}' for new user '{Username}'", email, username);
        }
    }
}
