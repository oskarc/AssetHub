using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Email.Templates;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Services;

public class GuestInvitationServiceTests
{
    private const string AdminId = "admin-1";
    private const string Email = "guest@example.com";
    private const string BaseUrl = "https://assethub.test";

    private readonly Mock<IGuestInvitationRepository> _repo = new();
    private readonly Mock<ICollectionRepository> _collectionRepo = new();
    private readonly Mock<ICollectionAclRepository> _aclRepo = new();
    private readonly Mock<IGuestInvitationTokenService> _tokens = new();
    private readonly Mock<IKeycloakUserService> _keycloak = new();
    private readonly Mock<IUserLookupService> _userLookup = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IAuditService> _audit = new();

    public GuestInvitationServiceTests()
    {
        _tokens.Setup(t => t.Generate(It.IsAny<Guid>()))
            .Returns<Guid>(id => new GuestInvitationToken($"plain-{id}", $"hash-{id}"));
        _tokens.Setup(t => t.HashToken(It.IsAny<string>()))
            .Returns<string>(p => p.StartsWith("plain-")
                ? $"hash-{p["plain-".Length..]}"
                : $"hash-{p}");
        _tokens.Setup(t => t.TryParse(It.IsAny<string>()))
            .Returns<string>(p => p.StartsWith("plain-") && Guid.TryParse(p["plain-".Length..], out var g)
                ? g
                : null);
    }

    private GuestInvitationService Create(string userId = AdminId, bool isAdmin = true)
        => new(_repo.Object, _collectionRepo.Object, _aclRepo.Object,
               _tokens.Object, _keycloak.Object, _userLookup.Object,
               _email.Object, _audit.Object,
               new CurrentUser(userId, isAdmin),
               NullLogger<GuestInvitationService>.Instance);

    private static Collection FakeCollection(Guid id) => new()
    {
        Id = id,
        Name = $"col-{id}",
        CreatedByUserId = AdminId,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task List_NonAdmin_Forbidden()
    {
        var svc = Create(isAdmin: false);
        var result = await svc.ListAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Create_NonAdmin_Forbidden()
    {
        var svc = Create(isAdmin: false);
        var result = await svc.CreateAsync(
            new CreateGuestInvitationDto { Email = Email, CollectionIds = [Guid.NewGuid()] },
            BaseUrl, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownCollection_BadRequest()
    {
        var ghostId = Guid.NewGuid();
        _collectionRepo.Setup(r => r.GetByIdAsync(ghostId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Collection?)null);

        var svc = Create();
        var result = await svc.CreateAsync(
            new CreateGuestInvitationDto { Email = Email, CollectionIds = [ghostId] },
            BaseUrl, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
        _repo.Verify(r => r.CreateAsync(It.IsAny<GuestInvitation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_HappyPath_PersistsAuditsAndEmails()
    {
        var collectionId = Guid.NewGuid();
        _collectionRepo.Setup(r => r.GetByIdAsync(collectionId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FakeCollection(collectionId));

        GuestInvitation? captured = null;
        _repo.Setup(r => r.CreateAsync(It.IsAny<GuestInvitation>(), It.IsAny<CancellationToken>()))
            .Callback<GuestInvitation, CancellationToken>((g, _) => captured = g)
            .ReturnsAsync((GuestInvitation g, CancellationToken _) => g);

        var svc = Create();
        var result = await svc.CreateAsync(
            new CreateGuestInvitationDto
            {
                Email = "  GUEST@example.com  ",
                CollectionIds = [collectionId, collectionId], // dup → distinct
                ExpiresInDays = 7
            },
            BaseUrl, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal(Email, captured!.Email); // lowercased + trimmed
        Assert.Single(captured.CollectionIds);
        Assert.StartsWith("hash-", captured.TokenHash);

        Assert.StartsWith($"{BaseUrl}/guest-accept?token=", result.Value!.MagicLinkUrl);

        _email.Verify(e => e.SendEmailAsync(
                Email, It.IsAny<GuestInvitationEmailTemplate>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.GuestInvited,
                Constants.ScopeTypes.GuestInvitation,
                It.IsAny<Guid?>(), AdminId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_EmailFailure_DoesNotVoidInvitation()
    {
        var collectionId = Guid.NewGuid();
        _collectionRepo.Setup(r => r.GetByIdAsync(collectionId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FakeCollection(collectionId));
        _email.Setup(e => e.SendEmailAsync(
                It.IsAny<string>(), It.IsAny<GuestInvitationEmailTemplate>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var svc = Create();
        var result = await svc.CreateAsync(
            new CreateGuestInvitationDto { Email = Email, CollectionIds = [collectionId] },
            BaseUrl, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repo.Verify(r => r.CreateAsync(It.IsAny<GuestInvitation>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Accept_BadToken_NotFound()
    {
        _tokens.Setup(t => t.TryParse("garbage")).Returns((Guid?)null);

        var svc = Create();
        var result = await svc.AcceptAsync("garbage", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Accept_TokenWithMismatchedHash_NotFound()
    {
        // tokens.TryParse decodes to id A, but no row with that hash exists
        var id = Guid.NewGuid();
        var token = $"plain-{id}";
        _repo.Setup(r => r.GetByTokenHashAsync($"hash-{id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GuestInvitation?)null);

        var svc = Create();
        var result = await svc.AcceptAsync(token, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Theory]
    [InlineData(true, false, false, 409)]   // revoked
    [InlineData(false, true, false, 409)]   // already accepted
    [InlineData(false, false, true, 409)]   // expired
    public async Task Accept_InvalidState_ReturnsConflict(
        bool revoked, bool accepted, bool expired, int expectedStatus)
    {
        var id = Guid.NewGuid();
        var inv = new GuestInvitation
        {
            Id = id,
            Email = Email,
            TokenHash = $"hash-{id}",
            CollectionIds = [Guid.NewGuid()],
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddDays(7),
            RevokedAt = revoked ? DateTime.UtcNow : null,
            AcceptedAt = accepted ? DateTime.UtcNow : null,
            AcceptedUserId = accepted ? "kc-user" : null,
            CreatedByUserId = AdminId
        };
        _repo.Setup(r => r.GetByTokenHashAsync(inv.TokenHash, It.IsAny<CancellationToken>())).ReturnsAsync(inv);

        var svc = Create();
        var result = await svc.AcceptAsync($"plain-{id}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedStatus, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Accept_HappyPath_NewKeycloakUser_GrantsAclsAndAudits()
    {
        var id = Guid.NewGuid();
        var collection1 = Guid.NewGuid();
        var collection2 = Guid.NewGuid();
        var inv = new GuestInvitation
        {
            Id = id,
            Email = Email,
            TokenHash = $"hash-{id}",
            CollectionIds = [collection1, collection2],
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = AdminId
        };
        _repo.Setup(r => r.GetByTokenHashAsync(inv.TokenHash, It.IsAny<CancellationToken>())).ReturnsAsync(inv);
        _repo.Setup(r => r.TryMarkAcceptedAsync(id, "kc-new", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _userLookup.Setup(u => u.GetUserIdByUsernameAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _keycloak.Setup(k => k.CreateUserAsync(
                Email, Email, "Guest", Email,
                It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync("kc-new");

        var svc = Create();
        var result = await svc.AcceptAsync($"plain-{id}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(id, result.Value!.InvitationId);

        _keycloak.Verify(k => k.AssignRealmRoleAsync("kc-new",
            RoleHierarchy.Roles.Viewer, It.IsAny<CancellationToken>()), Times.Once);
        _aclRepo.Verify(r => r.SetAccessAsync(collection1, "user", "kc-new",
            RoleHierarchy.Roles.Viewer, It.IsAny<CancellationToken>()), Times.Once);
        _aclRepo.Verify(r => r.SetAccessAsync(collection2, "user", "kc-new",
            RoleHierarchy.Roles.Viewer, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(inv.AcceptedAt);
        Assert.Equal("kc-new", inv.AcceptedUserId);

        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.GuestAccepted,
                Constants.ScopeTypes.GuestInvitation, id, "kc-new",
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Accept_HappyPath_ReusesExistingKeycloakUser()
    {
        var id = Guid.NewGuid();
        var inv = new GuestInvitation
        {
            Id = id,
            Email = Email,
            TokenHash = $"hash-{id}",
            CollectionIds = [Guid.NewGuid()],
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = AdminId
        };
        _repo.Setup(r => r.GetByTokenHashAsync(inv.TokenHash, It.IsAny<CancellationToken>())).ReturnsAsync(inv);
        _repo.Setup(r => r.TryMarkAcceptedAsync(id, "kc-existing", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _userLookup.Setup(u => u.GetUserIdByUsernameAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync("kc-existing");

        var svc = Create();
        var result = await svc.AcceptAsync($"plain-{id}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("kc-existing", inv.AcceptedUserId);
        _keycloak.Verify(k => k.CreateUserAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _keycloak.Verify(k => k.AssignRealmRoleAsync("kc-existing",
            RoleHierarchy.Roles.Viewer, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Accept_KeycloakFailure_ReturnsServerError()
    {
        var id = Guid.NewGuid();
        var inv = new GuestInvitation
        {
            Id = id,
            Email = Email,
            TokenHash = $"hash-{id}",
            CollectionIds = [Guid.NewGuid()],
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = AdminId
        };
        _repo.Setup(r => r.GetByTokenHashAsync(inv.TokenHash, It.IsAny<CancellationToken>())).ReturnsAsync(inv);
        _userLookup.Setup(u => u.GetUserIdByUsernameAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _keycloak.Setup(k => k.CreateUserAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("kc unreachable"));

        var svc = Create();
        var result = await svc.AcceptAsync($"plain-{id}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(500, result.Error!.StatusCode);
        Assert.Null(inv.AcceptedAt);
    }

    [Fact]
    public async Task Revoke_NonAdmin_Forbidden()
    {
        var svc = Create(isAdmin: false);
        var result = await svc.RevokeAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Revoke_NotFound_Returns404()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GuestInvitation?)null);

        var svc = Create();
        var result = await svc.RevokeAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Revoke_AlreadyRevoked_Idempotent()
    {
        var inv = new GuestInvitation
        {
            Id = Guid.NewGuid(),
            Email = Email,
            TokenHash = "x",
            CollectionIds = [Guid.NewGuid()],
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            RevokedAt = DateTime.UtcNow.AddMinutes(-5),
            CreatedByUserId = AdminId
        };
        _repo.Setup(r => r.GetByIdAsync(inv.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inv);

        var svc = Create();
        var result = await svc.RevokeAsync(inv.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<GuestInvitation>(), It.IsAny<CancellationToken>()), Times.Never);
        _aclRepo.Verify(r => r.RevokeAccessAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Revoke_Accepted_StripsAclsAndStampsRevokedAt()
    {
        var collection = Guid.NewGuid();
        var inv = new GuestInvitation
        {
            Id = Guid.NewGuid(),
            Email = Email,
            TokenHash = "x",
            CollectionIds = [collection],
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            AcceptedAt = DateTime.UtcNow.AddMinutes(-30),
            AcceptedUserId = "kc-guest",
            CreatedByUserId = AdminId
        };
        _repo.Setup(r => r.GetByIdAsync(inv.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inv);

        var svc = Create();
        var result = await svc.RevokeAsync(inv.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(inv.RevokedAt);
        _aclRepo.Verify(r => r.RevokeAccessAsync(
            collection, "user", "kc-guest", It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.GuestAccessRevoked,
                Constants.ScopeTypes.GuestInvitation, inv.Id, AdminId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
