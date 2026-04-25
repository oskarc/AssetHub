using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Unit tests for <see cref="PersonalAccessTokenService"/>. Uses the real repository
/// against a Testcontainers Postgres DB, a fake <see cref="CurrentUser"/>, and a mock
/// <see cref="IAuditService"/> — no HTTP pipeline.
/// </summary>
[Collection("Database")]
public class PersonalAccessTokenServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private PersonalAccessTokenRepository _repo = null!;
    private Mock<IAuditService> _auditMock = null!;

    private const string OwnerUserId = "user-pat-001";

    public PersonalAccessTokenServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        _repo = new PersonalAccessTokenRepository(_db, NullLogger<PersonalAccessTokenRepository>.Instance);
        _auditMock = new Mock<IAuditService>();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
    }

    private PersonalAccessTokenService NewServiceFor(string userId) =>
        new(_repo, _auditMock.Object, new UnitOfWork(_db),
            new CurrentUser(userId, isSystemAdmin: false),
            NullLogger<PersonalAccessTokenService>.Instance);

    private PersonalAccessTokenService AnonymousService() =>
        new(_repo, _auditMock.Object, new UnitOfWork(_db), CurrentUser.Anonymous,
            NullLogger<PersonalAccessTokenService>.Instance);

    // ── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Anonymous_ReturnsForbidden()
    {
        var svc = AnonymousService();
        var req = new CreatePersonalAccessTokenRequest { Name = "x", Scopes = [] };

        var result = await svc.CreateAsync(req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("FORBIDDEN", result.Error!.Code);
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ReturnsBadRequest()
    {
        var svc = NewServiceFor(OwnerUserId);
        var req = new CreatePersonalAccessTokenRequest { Name = "   ", Scopes = [] };

        var result = await svc.CreateAsync(req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BAD_REQUEST", result.Error!.Code);
    }

    [Fact]
    public async Task CreateAsync_PastExpiry_ReturnsBadRequest()
    {
        var svc = NewServiceFor(OwnerUserId);
        var req = new CreatePersonalAccessTokenRequest
        {
            Name = "pat",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            Scopes = []
        };

        var result = await svc.CreateAsync(req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BAD_REQUEST", result.Error!.Code);
    }

    [Fact]
    public async Task CreateAsync_UnknownScope_ReturnsBadRequest()
    {
        var svc = NewServiceFor(OwnerUserId);
        var req = new CreatePersonalAccessTokenRequest
        {
            Name = "pat",
            Scopes = ["assets:read", "not-a-real-scope"]
        };

        var result = await svc.CreateAsync(req, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BAD_REQUEST", result.Error!.Code);
        Assert.Contains("not-a-real-scope", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_Valid_ReturnsPlaintextOnceAndPersistsHashOnly()
    {
        var svc = NewServiceFor(OwnerUserId);
        var req = new CreatePersonalAccessTokenRequest
        {
            Name = "ci-read",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            Scopes = ["assets:read", "assets:read"] // duplicates must be de-duped
        };

        var result = await svc.CreateAsync(req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var created = result.Value!;
        Assert.StartsWith(IPersonalAccessTokenService.TokenPrefix, created.PlaintextToken, StringComparison.Ordinal);
        Assert.Equal("ci-read", created.Token.Name);
        Assert.Equal(OwnerUserId, created.Token.OwnerUserId);
        Assert.Equal(new[] { "assets:read" }, created.Token.Scopes); // de-duped
        Assert.True(created.Token.IsActive);

        // Server persists hash, never plaintext.
        var row = await _db.PersonalAccessTokens.AsNoTracking().SingleAsync(t => t.Id == created.Token.Id);
        Assert.NotEqual(created.PlaintextToken, row.TokenHash);
        Assert.Equal(svc.ComputeHash(created.PlaintextToken), row.TokenHash);

        _auditMock.Verify(a => a.LogAsync(
            "pat.created",
            "user",
            null,
            OwnerUserId,
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_TrimsName()
    {
        var svc = NewServiceFor(OwnerUserId);
        var req = new CreatePersonalAccessTokenRequest { Name = "  whitespace-padded  ", Scopes = [] };

        var result = await svc.CreateAsync(req, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("whitespace-padded", result.Value!.Token.Name);
    }

    [Fact]
    public async Task CreateAsync_GeneratesUniquePlaintextEachCall()
    {
        var svc = NewServiceFor(OwnerUserId);

        var a = await svc.CreateAsync(new CreatePersonalAccessTokenRequest { Name = "a", Scopes = [] }, CancellationToken.None);
        var b = await svc.CreateAsync(new CreatePersonalAccessTokenRequest { Name = "b", Scopes = [] }, CancellationToken.None);

        Assert.True(a.IsSuccess);
        Assert.True(b.IsSuccess);
        Assert.NotEqual(a.Value!.PlaintextToken, b.Value!.PlaintextToken);
    }

    // ── ListMineAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListMineAsync_ReturnsOnlyCallersTokensNewestFirst()
    {
        var mine = NewServiceFor(OwnerUserId);
        var theirs = NewServiceFor("other-user-999");

        await mine.CreateAsync(new() { Name = "mine-1", Scopes = [] }, CancellationToken.None);
        await theirs.CreateAsync(new() { Name = "theirs-1", Scopes = [] }, CancellationToken.None);
        await mine.CreateAsync(new() { Name = "mine-2", Scopes = [] }, CancellationToken.None);

        var result = await mine.ListMineAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var names = result.Value!.Select(t => t.Name).ToList();
        Assert.Equal(new[] { "mine-2", "mine-1" }, names); // newest first, theirs-1 absent
    }

    [Fact]
    public async Task ListMineAsync_Anonymous_ReturnsForbidden()
    {
        var svc = AnonymousService();
        var result = await svc.ListMineAsync(CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal("FORBIDDEN", result.Error!.Code);
    }

    // ── RevokeAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeAsync_OwnedToken_SucceedsAndMarksInactive()
    {
        var svc = NewServiceFor(OwnerUserId);
        var created = (await svc.CreateAsync(new() { Name = "to-revoke", Scopes = [] }, CancellationToken.None)).Value!;

        var result = await svc.RevokeAsync(created.Token.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = await _db.PersonalAccessTokens.AsNoTracking().SingleAsync(t => t.Id == created.Token.Id);
        Assert.NotNull(row.RevokedAt);
        Assert.False(row.IsActive(DateTime.UtcNow));

        _auditMock.Verify(a => a.LogAsync(
            "pat.revoked", "user", null, OwnerUserId,
            It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_Idempotent_SecondRevokePreservesFirstTimestamp()
    {
        var svc = NewServiceFor(OwnerUserId);
        var created = (await svc.CreateAsync(new() { Name = "twice-revoked", Scopes = [] }, CancellationToken.None)).Value!;

        Assert.True((await svc.RevokeAsync(created.Token.Id, CancellationToken.None)).IsSuccess);
        var firstRow = await _db.PersonalAccessTokens.AsNoTracking().SingleAsync(t => t.Id == created.Token.Id);
        var firstRevokedAt = firstRow.RevokedAt;

        // Small delay so a naive implementation overwriting RevokedAt would produce a different stamp.
        await Task.Delay(50);

        Assert.True((await svc.RevokeAsync(created.Token.Id, CancellationToken.None)).IsSuccess);
        var secondRow = await _db.PersonalAccessTokens.AsNoTracking().SingleAsync(t => t.Id == created.Token.Id);
        Assert.Equal(firstRevokedAt, secondRow.RevokedAt);

        // Audit fires only once (the initial revoke).
        _auditMock.Verify(a => a.LogAsync(
            "pat.revoked", "user", null, OwnerUserId,
            It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_OtherUsersToken_ReturnsNotFound_NoIdLeak()
    {
        var alice = NewServiceFor(OwnerUserId);
        var bob = NewServiceFor("user-bob");

        var bobsToken = (await bob.CreateAsync(new() { Name = "bobs", Scopes = [] }, CancellationToken.None)).Value!;

        // Alice tries to revoke Bob's token — must be NOT_FOUND (not FORBIDDEN), so we don't
        // leak whether a given GUID corresponds to any token at all.
        var result = await alice.RevokeAsync(bobsToken.Token.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("NOT_FOUND", result.Error!.Code);

        // Bob's token is still active.
        var row = await _db.PersonalAccessTokens.AsNoTracking().SingleAsync(t => t.Id == bobsToken.Token.Id);
        Assert.Null(row.RevokedAt);
    }

    // ── VerifyAndStampAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAndStampAsync_ValidToken_ReturnsRowAndStampsLastUsedAt()
    {
        var svc = NewServiceFor(OwnerUserId);
        var created = (await svc.CreateAsync(new() { Name = "pulse", Scopes = [] }, CancellationToken.None)).Value!;

        var verified = await svc.VerifyAndStampAsync(created.PlaintextToken, CancellationToken.None);

        Assert.NotNull(verified);
        Assert.Equal(created.Token.Id, verified!.Id);
        var row = await _db.PersonalAccessTokens.AsNoTracking().SingleAsync(t => t.Id == created.Token.Id);
        Assert.NotNull(row.LastUsedAt);
    }

    [Fact]
    public async Task VerifyAndStampAsync_RevokedToken_ReturnsNullWithoutDistinguishing()
    {
        var svc = NewServiceFor(OwnerUserId);
        var created = (await svc.CreateAsync(new() { Name = "doomed", Scopes = [] }, CancellationToken.None)).Value!;
        await svc.RevokeAsync(created.Token.Id, CancellationToken.None);

        var verified = await svc.VerifyAndStampAsync(created.PlaintextToken, CancellationToken.None);

        Assert.Null(verified); // Revoked ⇒ inactive ⇒ auth failure, no distinguishing leak.
    }

    [Fact]
    public async Task VerifyAndStampAsync_ExpiredToken_ReturnsNull()
    {
        var svc = NewServiceFor(OwnerUserId);
        // Create with far-future expiry, then back-date the row to simulate expiry — this avoids
        // the CreateAsync pre-check that rejects past expiries.
        var created = (await svc.CreateAsync(new() { Name = "stale", ExpiresAt = DateTime.UtcNow.AddDays(1), Scopes = [] },
            CancellationToken.None)).Value!;

        var row = await _db.PersonalAccessTokens.SingleAsync(t => t.Id == created.Token.Id);
        row.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
        await _db.SaveChangesAsync();

        var verified = await svc.VerifyAndStampAsync(created.PlaintextToken, CancellationToken.None);
        Assert.Null(verified);
    }

    [Fact]
    public async Task VerifyAndStampAsync_UnknownPlaintext_ReturnsNull()
    {
        var svc = NewServiceFor(OwnerUserId);
        var verified = await svc.VerifyAndStampAsync("pat_this-is-not-a-real-token", CancellationToken.None);
        Assert.Null(verified);
    }

    [Fact]
    public async Task VerifyAndStampAsync_WrongPrefix_ReturnsNull()
    {
        var svc = NewServiceFor(OwnerUserId);

        // A well-formed OIDC JWT shouldn't reach this method, but defence in depth: reject anything
        // that doesn't carry the PAT prefix without even computing the hash.
        var verified = await svc.VerifyAndStampAsync("Bearer eyJhbGciOiJSUzI1NiJ9.aaa.bbb", CancellationToken.None);
        Assert.Null(verified);
    }

    [Fact]
    public async Task VerifyAndStampAsync_EmptyOrWhitespace_ReturnsNull()
    {
        var svc = NewServiceFor(OwnerUserId);
        Assert.Null(await svc.VerifyAndStampAsync("", CancellationToken.None));
        Assert.Null(await svc.VerifyAndStampAsync("   ", CancellationToken.None));
    }

    // ── ComputeHash ──────────────────────────────────────────────────────────

    [Fact]
    public void ComputeHash_IsDeterministicAndHex()
    {
        var svc = NewServiceFor(OwnerUserId);
        var a = svc.ComputeHash("pat_fixed-input");
        var b = svc.ComputeHash("pat_fixed-input");

        Assert.Equal(a, b);
        Assert.Equal(64, a.Length); // SHA-256 as hex
        Assert.Matches("^[0-9a-f]+$", a);
    }
}
