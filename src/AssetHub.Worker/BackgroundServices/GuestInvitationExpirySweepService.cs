using AssetHub.Application;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssetHub.Worker.BackgroundServices;

/// <summary>
/// Background sweep that revokes ACLs and stamps <c>RevokedAt</c> on
/// accepted-but-now-expired guest invitations (T4-GUEST-01). Runs hourly.
/// Idempotent — already-revoked rows are skipped by the repository
/// query, and a partial failure inside the loop is logged per-item so
/// one stuck invitation can't poison the whole sweep.
/// </summary>
public sealed class GuestInvitationExpirySweepService(
    IServiceScopeFactory scopeFactory,
    ILogger<GuestInvitationExpirySweepService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private const string PrincipalUser = "user";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Guest-invitation expiry sweep started. Interval: {Hours}h",
            Interval.TotalHours);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunSweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Guest-invitation expiry sweep failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunSweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGuestInvitationRepository>();
        var aclRepo = scope.ServiceProvider.GetRequiredService<ICollectionAclRepository>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var expired = await repo.ListExpiredAcceptedAsync(DateTime.UtcNow, ct);
        if (expired.Count == 0)
        {
            logger.LogDebug("Guest-invitation expiry sweep: nothing to revoke");
            return;
        }

        int revoked = 0;
        int failed = 0;

        foreach (var invitation in expired)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!string.IsNullOrEmpty(invitation.AcceptedUserId))
                    await RevokeCollectionAclsAsync(aclRepo, invitation, ct);

                invitation.RevokedAt = DateTime.UtcNow;
                await repo.UpdateAsync(invitation, ct);

                await audit.LogAsync(
                    NotificationConstants.AuditEvents.GuestExpired,
                    Constants.ScopeTypes.GuestInvitation,
                    invitation.Id,
                    actorUserId: null,
                    new Dictionary<string, object>
                    {
                        ["invited_email"] = invitation.Email,
                        ["expired_at"] = invitation.ExpiresAt
                    },
                    ct);

                revoked++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogWarning(ex,
                    "Failed to expire guest invitation {InvitationId}", invitation.Id);
            }
        }

        logger.LogInformation(
            "Guest-invitation expiry sweep done: {Revoked} revoked, {Failed} failed",
            revoked, failed);
    }

    private async Task RevokeCollectionAclsAsync(
        Application.Repositories.ICollectionAclRepository aclRepo,
        Domain.Entities.GuestInvitation invitation,
        CancellationToken ct)
    {
        foreach (var collectionId in invitation.CollectionIds)
        {
            try
            {
                await aclRepo.RevokeAccessAsync(
                    collectionId, PrincipalUser, invitation.AcceptedUserId!, ct);
            }
            catch (Exception inner) when (inner is not OperationCanceledException)
            {
                logger.LogWarning(inner,
                    "Failed to revoke ACL on collection {CollectionId} for expired guest {UserId}",
                    collectionId, invitation.AcceptedUserId);
            }
        }
    }
}
