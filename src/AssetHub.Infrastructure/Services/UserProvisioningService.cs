using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Email.Templates;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

/// <summary>
/// Orchestrates user provisioning steps: collection validation, access granting, and welcome emails.
/// </summary>
public class UserProvisioningService : IUserProvisioningService
{
    private readonly ICollectionRepository _collectionRepo;
    private readonly ICollectionAclRepository _aclRepo;
    private readonly IEmailService _emailService;
    private readonly IAuditService _audit;
    private readonly ILogger<UserProvisioningService> _logger;

    public UserProvisioningService(
        ICollectionRepository collectionRepo,
        ICollectionAclRepository aclRepo,
        IEmailService emailService,
        IAuditService audit,
        ILogger<UserProvisioningService> logger)
    {
        _collectionRepo = collectionRepo;
        _aclRepo = aclRepo;
        _emailService = emailService;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, string>> ValidateCollectionsExistAsync(
        List<Guid> collectionIds, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        foreach (var id in collectionIds)
        {
            if (!await _collectionRepo.ExistsAsync(id, ct))
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
                await _aclRepo.SetAccessAsync(collectionId, "user", userId, role, ct);
                await _audit.LogAsync("user.access_granted", "collection", collectionId, userId,
                    new() { ["role"] = role, ["username"] = username }, ct);
                _logger.LogInformation("Granted '{Role}' access on collection {CollectionId} to new user '{Username}'",
                    role, collectionId, username);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to grant access on collection {CollectionId} for new user '{Username}'",
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

            await _emailService.SendEmailAsync(email, emailTemplate, ct);
            _logger.LogInformation("Welcome email sent to '{Email}' for new user '{Username}'", email, username);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send welcome email to '{Email}' for new user '{Username}'", email, username);
        }
    }
}
