using System.Security.Cryptography;
using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace AssetHub.Infrastructure.Services;

public sealed class PersonalAccessTokenService(
    IPersonalAccessTokenRepository repo,
    IAuditService audit,
    CurrentUser currentUser,
    ILogger<PersonalAccessTokenService> logger) : IPersonalAccessTokenService
{
    /// <summary>
    /// Plaintext token format: "pat_" + 32 base64url chars (24 bytes of CSPRNG entropy).
    /// The prefix lets the auth scheme selector route quickly without trying to decode JWT.
    /// </summary>
    public const string TokenPrefix = "pat_";
    private const int TokenEntropyBytes = 24;

    public async Task<ServiceResult<CreatedPersonalAccessTokenDto>> CreateAsync(
        CreatePersonalAccessTokenRequest request,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden();

        if (string.IsNullOrWhiteSpace(request.Name))
            return ServiceError.BadRequest("Name is required");

        if (request.ExpiresAt is { } expires && expires <= DateTime.UtcNow)
            return ServiceError.BadRequest("ExpiresAt must be in the future");

        // Validate scopes against the allow-list. An empty list means "act as the owner with
        // no extra restrictions" — that's allowed (matches the OIDC session's surface area).
        if (request.Scopes.Count > 0)
        {
            var invalid = request.Scopes
                .Where(s => !PersonalAccessTokenDto.AllowedScopes.Contains(s, StringComparer.Ordinal))
                .ToList();
            if (invalid.Count > 0)
                return ServiceError.BadRequest($"Unknown scope(s): {string.Join(", ", invalid)}");
        }

        var plaintext = GeneratePlaintextToken();
        var hash = ComputeHash(plaintext);

        var entity = new PersonalAccessToken
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            OwnerUserId = currentUser.UserId,
            TokenHash = hash,
            Scopes = request.Scopes.Distinct(StringComparer.Ordinal).ToList(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = request.ExpiresAt
        };

        await repo.CreateAsync(entity, ct);

        await audit.LogAsync(
            "pat.created",
            "user",
            null,
            currentUser.UserId,
            new()
            {
                ["tokenId"] = entity.Id,
                ["name"] = entity.Name,
                ["scopes"] = entity.Scopes,
                ["expiresAt"] = entity.ExpiresAt is null ? "never" : entity.ExpiresAt.Value.ToString("O")
            },
            ct);

        logger.LogInformation("User {UserId} created PAT {TokenId} ({Name})",
            currentUser.UserId, entity.Id, entity.Name);

        return new CreatedPersonalAccessTokenDto
        {
            Token = ToDto(entity),
            PlaintextToken = plaintext
        };
    }

    public async Task<ServiceResult<List<PersonalAccessTokenDto>>> ListMineAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden();

        var rows = await repo.ListForOwnerAsync(currentUser.UserId, ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ServiceResult> RevokeAsync(Guid tokenId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden();

        var token = await repo.GetForOwnerAsync(tokenId, currentUser.UserId, ct);
        if (token is null)
            return ServiceError.NotFound("Token not found");

        if (token.RevokedAt is not null)
            return ServiceResult.Success;

        token.MarkRevoked();
        await repo.UpdateAsync(token, ct);

        await audit.LogAsync(
            "pat.revoked",
            "user",
            null,
            currentUser.UserId,
            new() { ["tokenId"] = token.Id, ["name"] = token.Name },
            ct);

        logger.LogInformation("User {UserId} revoked PAT {TokenId}", currentUser.UserId, token.Id);
        return ServiceResult.Success;
    }

    public async Task<PersonalAccessToken?> VerifyAndStampAsync(string plaintextToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(plaintextToken)) return null;
        if (!plaintextToken.StartsWith(TokenPrefix, StringComparison.Ordinal)) return null;

        var hash = ComputeHash(plaintextToken);
        var token = await repo.GetByHashAsync(hash, ct);
        if (token is null) return null;

        if (!token.IsActive(DateTime.UtcNow))
        {
            // Don't distinguish revoked vs expired in the auth handler — both are "this
            // bearer is dead". We log it here so admins can see attempted reuse.
            logger.LogWarning("Inactive PAT {TokenId} presented (revoked={Revoked} expires={Expires})",
                token.Id, token.RevokedAt is not null, token.ExpiresAt);
            return null;
        }

        token.MarkUsed(DateTime.UtcNow);
        await repo.UpdateAsync(token, ct);
        return token;
    }

    public string ComputeHash(string plaintextToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(plaintextToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GeneratePlaintextToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenEntropyBytes);
        // Base64url so the token is URL-safe and prefix-friendly. 24 bytes => 32 chars.
        var encoded = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"{TokenPrefix}{encoded}";
    }

    private static PersonalAccessTokenDto ToDto(PersonalAccessToken t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        OwnerUserId = t.OwnerUserId,
        Scopes = new List<string>(t.Scopes),
        CreatedAt = t.CreatedAt,
        ExpiresAt = t.ExpiresAt,
        RevokedAt = t.RevokedAt,
        LastUsedAt = t.LastUsedAt,
        IsActive = t.IsActive(DateTime.UtcNow)
    };
}
