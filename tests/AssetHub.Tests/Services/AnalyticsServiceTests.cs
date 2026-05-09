using System.Security.Cryptography;
using System.Text;
using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Watermarking;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using AssetHub.Infrastructure.Services.Watermarking;
using AssetHub.Tests.Fixtures;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Read-side analytics tests (T5-ANL-01). Focused on the reveal-PII flow
/// because that's the security-critical path: G-12b (carries forward from
/// T5-WMK-01) requires that <c>analytics.exposure_revealed</c> audit rows
/// record the recipient hash + outcome only, never the decrypted plaintext.
/// </summary>
[Collection("Database")]
public class AnalyticsServiceTests
{
    private readonly PostgresFixture _fixture;

    public AnalyticsServiceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RevealRecipientAsync_AuditRow_ContainsHashAndOutcome_NeverPlaintext()
    {
        // Arrange — write a WatermarkDownload with a real encrypted PII column,
        // then invoke RevealRecipientAsync and capture the audit row.
        var dbName = $"test_anlsvc_{Guid.NewGuid():N}";
        await using var db = await _fixture.CreateDbContextAsync(dbName);

        var crypto = CreateCryptoService();
        var plaintextEmail = "alice@example.com";
        var encrypted = crypto.Encrypt(plaintextEmail);
        var emailHash = crypto.Hash(plaintextEmail);

        db.WatermarkDownloads.Add(new WatermarkDownload
        {
            Id = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            DownloadedAt = DateTime.UtcNow,
            DownloadPath = "share",
            AlgorithmVersion = "v1",
            PayloadHmac = RandomBytes(32),
            EncryptedRecipientEmail = encrypted,
            RecipientEmailHash = emailHash
        });
        await db.SaveChangesAsync();

        var auditCapture = new CapturingAuditService();
        var service = new AnalyticsService(
            Mock.Of<IAnalyticsRollupRepository>(),
            _fixture.CreateDbContextProvider(dbName),
            crypto,
            auditCapture,
            NullLogger<AnalyticsService>.Instance);

        // Act
        var request = new RevealRecipientRequestDto
        {
            RecipientHash = Convert.ToBase64String(emailHash),
            RecipientKind = AnalyticsConstants.RecipientKinds.Email
        };
        var result = await service.RevealRecipientAsync(request, CancellationToken.None);

        // Assert — reveal succeeded
        Assert.True(result.IsSuccess);
        Assert.Equal(plaintextEmail, result.Value!.RecipientEmail);
        Assert.False(result.Value.DecryptionFailed);

        // Assert — audit row has hash + kind + outcome, NO plaintext
        Assert.Single(auditCapture.Logs);
        var entry = auditCapture.Logs[0];
        Assert.Equal(AnalyticsConstants.AuditEvents.ExposureRevealed, entry.EventType);
        Assert.Equal(AnalyticsConstants.ScopeTypes.Analytics, entry.TargetType);
        Assert.NotNull(entry.Details);
        Assert.Equal(Convert.ToBase64String(emailHash), entry.Details!["recipient_hash"]);
        Assert.Equal("email", entry.Details["recipient_kind"]);
        Assert.Equal(false, entry.Details["decryption_failed"]);

        // The most important check: the plaintext must NOT appear anywhere in the audit row.
        var serialized = string.Join("|", entry.Details.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        Assert.DoesNotContain(plaintextEmail, serialized);
        Assert.DoesNotContain("alice", serialized);
    }

    [Fact]
    public async Task RevealRecipientAsync_DecryptionFailure_ReturnsFailedFlag_StillAudits()
    {
        var dbName = $"test_anlsvc_decryptfail_{Guid.NewGuid():N}";
        await using var db = await _fixture.CreateDbContextAsync(dbName);

        // Use one crypto service to encrypt, a different (different-keyed) one
        // to attempt decryption — simulates key rotation. Hash stays the same
        // because it derives from the deployment-scoped HmacKey, but the
        // protected blob is unreadable.
        var keyA = new EphemeralDataProtectionProvider();
        var hmacKey = Convert.ToBase64String(RandomBytes(32));
        var settings = Options.Create(new WatermarkSettings { HmacKeyBase64 = hmacKey });
        var cryptoA = new RecipientCryptoService(keyA, settings, NullLogger<RecipientCryptoService>.Instance);

        var plaintextUserId = "550e8400-e29b-41d4-a716-446655440000";
        var encryptedByA = cryptoA.Encrypt(plaintextUserId);
        var hash = cryptoA.Hash(plaintextUserId);

        db.WatermarkDownloads.Add(new WatermarkDownload
        {
            Id = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            DownloadedAt = DateTime.UtcNow,
            DownloadPath = "raw",
            AlgorithmVersion = "v1",
            PayloadHmac = RandomBytes(32),
            EncryptedRecipientUserId = encryptedByA,
            RecipientUserIdHash = hash
        });
        await db.SaveChangesAsync();

        // Different key provider — can't decrypt the row written above.
        var keyB = new EphemeralDataProtectionProvider();
        var cryptoB = new RecipientCryptoService(keyB, settings, NullLogger<RecipientCryptoService>.Instance);

        var auditCapture = new CapturingAuditService();
        var service = new AnalyticsService(
            Mock.Of<IAnalyticsRollupRepository>(),
            _fixture.CreateDbContextProvider(dbName),
            cryptoB,
            auditCapture,
            NullLogger<AnalyticsService>.Instance);

        var result = await service.RevealRecipientAsync(new RevealRecipientRequestDto
        {
            RecipientHash = Convert.ToBase64String(hash),
            RecipientKind = AnalyticsConstants.RecipientKinds.User
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.DecryptionFailed);
        Assert.Null(result.Value.RecipientUserId);

        // Audit row still recorded — auditors need to see "admin attempted reveal,
        // got nothing" as much as "admin attempted reveal, got X."
        Assert.Single(auditCapture.Logs);
        Assert.Equal(true, auditCapture.Logs[0].Details!["decryption_failed"]);
    }

    [Fact]
    public async Task RevealRecipientAsync_UnknownHash_ReturnsNotFound()
    {
        var dbName = $"test_anlsvc_404_{Guid.NewGuid():N}";
        await using var db = await _fixture.CreateDbContextAsync(dbName);

        var crypto = CreateCryptoService();
        var auditCapture = new CapturingAuditService();
        var service = new AnalyticsService(
            Mock.Of<IAnalyticsRollupRepository>(),
            _fixture.CreateDbContextProvider(dbName),
            crypto,
            auditCapture,
            NullLogger<AnalyticsService>.Instance);

        var unknownHash = Convert.ToBase64String(RandomBytes(32));
        var result = await service.RevealRecipientAsync(new RevealRecipientRequestDto
        {
            RecipientHash = unknownHash,
            RecipientKind = AnalyticsConstants.RecipientKinds.User
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
        // Audit must NOT fire when there's no matching row — otherwise the
        // audit log becomes a probe-detection signal for someone fishing for
        // valid hashes.
        Assert.Empty(auditCapture.Logs);
    }

    [Fact]
    public async Task RevealRecipientAsync_InvalidBase64_ReturnsBadRequest()
    {
        var dbName = $"test_anlsvc_400_{Guid.NewGuid():N}";
        await using var db = await _fixture.CreateDbContextAsync(dbName);

        var service = new AnalyticsService(
            Mock.Of<IAnalyticsRollupRepository>(),
            _fixture.CreateDbContextProvider(dbName),
            CreateCryptoService(),
            new CapturingAuditService(),
            NullLogger<AnalyticsService>.Instance);

        var result = await service.RevealRecipientAsync(new RevealRecipientRequestDto
        {
            RecipientHash = "not-base64-!!!",
            RecipientKind = AnalyticsConstants.RecipientKinds.User
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    private static IRecipientCryptoService CreateCryptoService()
    {
        var hmacKey = Convert.ToBase64String(RandomBytes(32));
        var settings = Options.Create(new WatermarkSettings { HmacKeyBase64 = hmacKey });
        return new RecipientCryptoService(
            new EphemeralDataProtectionProvider(),
            settings,
            NullLogger<RecipientCryptoService>.Instance);
    }

    private static byte[] RandomBytes(int length)
    {
        var b = new byte[length];
        RandomNumberGenerator.Fill(b);
        return b;
    }

    private sealed record AuditEntry(
        string EventType,
        string TargetType,
        Guid? TargetId,
        string? ActorUserId,
        Dictionary<string, object>? Details);

    private sealed class CapturingAuditService : IAuditService
    {
        public List<AuditEntry> Logs { get; } = new();

        public Task LogAsync(
            string eventType, string targetType, Guid? targetId, string? actorUserId,
            Dictionary<string, object>? details = null,
            CancellationToken ct = default)
        {
            Logs.Add(new AuditEntry(eventType, targetType, targetId, actorUserId, details));
            return Task.CompletedTask;
        }
    }
}
