using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Email.Templates;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell", "S107:Methods should not have too many parameters",
    Justification = "Composition root for the guest-invite flow: invitation repo + collection repo (existence check) + ACL repo (grant/revoke) + token service + Keycloak provisioning + user lookup + email service + audit + scoped CurrentUser + logger. Bundling them obscures intent.")]
public sealed class GuestInvitationService(
    IGuestInvitationRepository repo,
    ICollectionRepository collectionRepo,
    ICollectionAclRepository aclRepo,
    IGuestInvitationTokenService tokens,
    IKeycloakUserService keycloak,
    IUserLookupService userLookup,
    IEmailService email,
    IAuditService audit,
    IUnitOfWork uow,
    CurrentUser currentUser,
    ILogger<GuestInvitationService> logger) : IGuestInvitationService
{
    /// <summary>
    /// Keycloak realm role applied to provisioned guests. They'll need
    /// at least the global "viewer" floor so the existing route policies
    /// let them in; their actual reach is bounded by the per-collection
    /// ACLs we grant on accept.
    /// </summary>
    private const string GuestRealmRole = RoleHierarchy.Roles.Viewer;

    private const string InvitationNotFound = "Invitation not found.";
    private const string PrincipalUser = "user";

    public async Task<ServiceResult<List<GuestInvitationResponseDto>>> ListAsync(CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();
        var rows = await repo.ListAllAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<CreatedGuestInvitationDto>> CreateAsync(
        CreateGuestInvitationDto dto, string baseUrl, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        // Verify every collection exists before persisting. We don't grant
        // ACLs until accept, but admins shouldn't be able to invite to
        // ghost collections — better to fail fast.
        foreach (var cid in dto.CollectionIds.Distinct())
        {
            var collection = await collectionRepo.GetByIdAsync(cid, ct: ct);
            if (collection is null)
                return ServiceError.BadRequest($"Collection {cid} not found.");
        }

        var invitationId = Guid.NewGuid();
        var token = tokens.Generate(invitationId);
        var entity = new GuestInvitation
        {
            Id = invitationId,
            Email = dto.Email.Trim().ToLowerInvariant(),
            TokenHash = token.Hash,
            CollectionIds = dto.CollectionIds.Distinct().ToList(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(dto.ExpiresInDays),
            CreatedByUserId = currentUser.UserId
        };
        // Invitation insert + audit are atomic — a torn write would either
        // leave a redeemable invitation with no audit row or, worse, an
        // audit-ack with no actual invitation (A-4).
        await uow.ExecuteAsync(async tct =>
        {
            await repo.CreateAsync(entity, tct);
            await audit.LogAsync(
                NotificationConstants.AuditEvents.GuestInvited,
                Constants.ScopeTypes.GuestInvitation,
                entity.Id,
                currentUser.UserId,
                new Dictionary<string, object>
                {
                    ["invited_email"] = entity.Email,
                    ["collection_ids"] = entity.CollectionIds,
                    ["expires_at"] = entity.ExpiresAt
                },
                tct);
        }, ct);

        var magicLinkUrl = BuildMagicLinkUrl(baseUrl, token.Plaintext);

        // Resolve the inviter's display name so the email reads "Alice has
        // invited you…" instead of the bare "You've been invited…". Lookup
        // failures are tolerated — the template falls back to the anonymous
        // greeting when inviterName is null.
        var inviterName = await ResolveInviterNameAsync(ct);

        try
        {
            await email.SendEmailAsync(entity.Email, new GuestInvitationEmailTemplate(
                magicLinkUrl: magicLinkUrl,
                inviterName: inviterName,
                expiresAt: entity.ExpiresAt,
                collectionCount: entity.CollectionIds.Count), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Email failure shouldn't void the invitation — admin can
            // re-send via a future "resend" action (FOLLOW-UP) or copy
            // the magic link from the response.
            logger.LogWarning(ex,
                "Guest invitation {InvitationId} created but email to {Email} failed",
                entity.Id, entity.Email);
        }

        return new CreatedGuestInvitationDto
        {
            Invitation = ToDto(entity),
            MagicLinkUrl = magicLinkUrl
        };
    }

    public async Task<ServiceResult<AcceptGuestInvitationResponseDto>> AcceptAsync(
        string token, CancellationToken ct)
    {
        // Anonymous endpoint — no CurrentUser check.
        if (string.IsNullOrWhiteSpace(token))
            return ServiceError.NotFound(InvitationNotFound);

        // Don't trust the parsed id alone — also verify the hash matches a
        // persisted row. The token-id check is a fast-fail; the hash check
        // is the authoritative one.
        var invitationId = tokens.TryParse(token);
        if (invitationId is null)
            return ServiceError.NotFound(InvitationNotFound);

        var hash = tokens.HashToken(token);
        var invitation = await repo.GetByTokenHashAsync(hash, ct);
        if (invitation is null || invitation.Id != invitationId)
            return ServiceError.NotFound(InvitationNotFound);

        // Single generic 409 across revoked / accepted / expired — exposing
        // which state the invitation is in lets a token-holder fingerprint
        // its lifecycle (P-8 in the security review). The actual concurrency-
        // safe check happens via TryMarkAcceptedAsync below; this fast-path
        // just avoids the Keycloak round-trip when the row is already dead.
        if (invitation.RevokedAt is not null
            || invitation.AcceptedAt is not null
            || invitation.ExpiresAt <= DateTime.UtcNow)
        {
            return ServiceError.Conflict("This invitation can no longer be redeemed.");
        }

        // Re-use a Keycloak user if one already exists for this email
        // (admin might have already provisioned them). Otherwise create
        // a fresh one with a random password — guest signs in via the
        // execute-actions email flow instead of receiving a password.
        var existingUserId = await userLookup.GetUserIdByUsernameAsync(invitation.Email, ct);
        string keycloakUserId;
        var newKeycloakUser = existingUserId is null;
        try
        {
            keycloakUserId = existingUserId
                ?? await keycloak.CreateUserAsync(
                    username: invitation.Email,
                    email: invitation.Email,
                    firstName: "Guest",
                    lastName: invitation.Email,
                    password: GenerateRandomPassword(),
                    temporaryPassword: true,
                    ct: ct);

            await keycloak.AssignRealmRoleAsync(keycloakUserId, GuestRealmRole, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Failed to provision Keycloak guest user for invitation {InvitationId} ({Email})",
                invitation.Id, invitation.Email);
            return ServiceError.Server("Failed to provision guest user.");
        }

        // Newly-provisioned guests get a password-set + email-verify link by
        // email — the post-accept page promises this and without the call
        // the user has no way to log in (P-4 in the security review).
        // Existing KC users skip this; they already have credentials.
        if (newKeycloakUser)
        {
            try
            {
                await keycloak.SendExecuteActionsEmailAsync(
                    keycloakUserId,
                    actions: new[] { "UPDATE_PASSWORD", "VERIFY_EMAIL" },
                    lifespan: (int)TimeSpan.FromHours(24).TotalSeconds,
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Email failure shouldn't void the accept — admin can
                // re-trigger via Keycloak's reset-password flow. Log so SREs
                // see the gap.
                logger.LogWarning(ex,
                    "Guest {UserId} provisioned but execute-actions email failed",
                    keycloakUserId);
            }
        }

        // Atomic accept — single-row conditional UPDATE in Postgres. Closes
        // the race where two concurrent magic-link clicks both pass the load
        // check and both run the Keycloak provisioning + ACL grants (P-5).
        // The losing call returns the same 409 as a stale invitation.
        var acceptedAt = DateTime.UtcNow;
        var won = await repo.TryMarkAcceptedAsync(invitation.Id, keycloakUserId, acceptedAt, ct);
        if (!won)
            return ServiceError.Conflict("This invitation can no longer be redeemed.");

        // Now that we hold the accept "lock", grant ACLs. Per-collection
        // failures are logged but don't abort — admin can fix from the
        // audit trail. Done after the accept-mark so a duplicate accept
        // attempt never grants a second round of ACLs.
        foreach (var collectionId in invitation.CollectionIds)
        {
            try
            {
                await aclRepo.SetAccessAsync(
                    collectionId, PrincipalUser, keycloakUserId,
                    RoleHierarchy.Roles.Viewer, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Failed to grant viewer ACL on collection {CollectionId} for guest {UserId}",
                    collectionId, keycloakUserId);
            }
        }

        invitation.AcceptedAt = acceptedAt;
        invitation.AcceptedUserId = keycloakUserId;

        await audit.LogAsync(
            NotificationConstants.AuditEvents.GuestAccepted,
            Constants.ScopeTypes.GuestInvitation,
            invitation.Id,
            actorUserId: keycloakUserId,
            new Dictionary<string, object>
            {
                ["invited_email"] = invitation.Email,
                ["collection_ids"] = invitation.CollectionIds,
                ["expires_at"] = invitation.ExpiresAt
            },
            ct);

        return new AcceptGuestInvitationResponseDto
        {
            InvitationId = invitation.Id,
            Email = invitation.Email,
            CollectionIds = invitation.CollectionIds,
            ExpiresAt = invitation.ExpiresAt
        };
    }

    public async Task<ServiceResult> RevokeAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsSystemAdmin) return ServiceError.Forbidden();

        var invitation = await repo.GetByIdAsync(id, ct);
        if (invitation is null) return ServiceError.NotFound(InvitationNotFound);
        if (invitation.RevokedAt is not null) return ServiceResult.Success;

        // If the guest accepted, strip their ACL on every granted collection.
        if (invitation.AcceptedAt is not null && !string.IsNullOrEmpty(invitation.AcceptedUserId))
        {
            foreach (var collectionId in invitation.CollectionIds)
            {
                try
                {
                    await aclRepo.RevokeAccessAsync(
                        collectionId, PrincipalUser, invitation.AcceptedUserId, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex,
                        "Failed to revoke ACL on collection {CollectionId} for guest {UserId}",
                        collectionId, invitation.AcceptedUserId);
                }
            }
        }

        invitation.RevokedAt = DateTime.UtcNow;

        // Revocation + audit are atomic. ACL strips above are best-effort
        // (Postgres can't roll back across services anyway) — the audit
        // entry is the load-bearing forensic record.
        await uow.ExecuteAsync(async tct =>
        {
            await repo.UpdateAsync(invitation, tct);
            await audit.LogAsync(
                NotificationConstants.AuditEvents.GuestAccessRevoked,
                Constants.ScopeTypes.GuestInvitation,
                invitation.Id,
                currentUser.UserId,
                new Dictionary<string, object>
                {
                    ["invited_email"] = invitation.Email,
                    ["was_accepted"] = invitation.AcceptedAt is not null
                },
                tct);
        }, ct);

        return ServiceResult.Success;
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static string BuildMagicLinkUrl(string baseUrl, string token)
    {
        var trimmed = baseUrl.TrimEnd('/');
        return $"{trimmed}/guest-accept?token={Uri.EscapeDataString(token)}";
    }

    /// <summary>
    /// Best-effort lookup of the current user's display name for the email
    /// greeting. Returns null on lookup failure or when the user has no
    /// resolvable name — the email template handles null by falling back
    /// to the anonymous greeting.
    /// </summary>
    private async Task<string?> ResolveInviterNameAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(currentUser.UserId)) return null;
        try
        {
            return await userLookup.GetUserNameAsync(currentUser.UserId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Inviter name lookup failed for {UserId}; email will use anonymous greeting", currentUser.UserId);
            return null;
        }
    }

    private static string GenerateRandomPassword()
    {
        // 24 bytes of entropy → 32-char base64 — far over the Keycloak
        // policy minimum. Guest sets their own via the password-reset
        // flow on first login (temporaryPassword: true).
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes);
    }

    private static GuestInvitationResponseDto ToDto(GuestInvitation g) => new()
    {
        Id = g.Id,
        Email = g.Email,
        CollectionIds = g.CollectionIds.ToList(),
        CreatedAt = g.CreatedAt,
        ExpiresAt = g.ExpiresAt,
        AcceptedAt = g.AcceptedAt,
        AcceptedUserId = g.AcceptedUserId,
        CreatedByUserId = g.CreatedByUserId,
        RevokedAt = g.RevokedAt,
        Status = ComputeStatus(g)
    };

    private static string ComputeStatus(GuestInvitation g)
    {
        if (g.RevokedAt is not null) return "revoked";
        if (g.ExpiresAt <= DateTime.UtcNow) return "expired";
        if (g.AcceptedAt is not null) return "accepted";
        return "pending";
    }
}
