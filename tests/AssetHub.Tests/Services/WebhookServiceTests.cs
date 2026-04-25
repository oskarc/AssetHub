using AssetHub.Application;
using AssetHub.Application.Dtos;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AssetHub.Tests.Services;

public class WebhookServiceTests
{
    private readonly Mock<IWebhookRepository> _repo = new();
    private readonly Mock<IWebhookDeliveryRepository> _deliveries = new();
    private readonly Mock<IWebhookSecretProtector> _protector = new();
    private readonly Mock<IWebhookEventPublisher> _publisher = new();
    private readonly Mock<IAuditService> _audit = new();

    private const string AdminId = "admin-1";

    public WebhookServiceTests()
    {
        _protector.Setup(p => p.GeneratePlaintext()).Returns("plaintext-secret");
        _protector.Setup(p => p.Protect(It.IsAny<string>())).Returns<string>(s => $"enc({s})");
    }

    private WebhookService Create(string userId = AdminId, bool isAdmin = true)
        => new(_repo.Object, _deliveries.Object, _protector.Object, _publisher.Object,
               _audit.Object, new CurrentUser(userId, isAdmin),
               NullLogger<WebhookService>.Instance);

    [Fact]
    public async Task Create_NonAdmin_Forbidden()
    {
        var svc = Create(isAdmin: false);

        var result = await svc.CreateAsync(
            new CreateWebhookDto { Name = "x", Url = "https://example.com", EventTypes = [WebhookEvents.AssetCreated] },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Create_HappyPath_PersistsAndReturnsPlaintextSecret()
    {
        Webhook? captured = null;
        _repo.Setup(r => r.CreateAsync(It.IsAny<Webhook>(), It.IsAny<CancellationToken>()))
            .Callback<Webhook, CancellationToken>((w, _) => captured = w)
            .ReturnsAsync((Webhook w, CancellationToken _) => w);

        var svc = Create();
        var result = await svc.CreateAsync(new CreateWebhookDto
        {
            Name = "Slack",
            Url = "https://hooks.slack.com/x",
            EventTypes = [WebhookEvents.CommentCreated, WebhookEvents.WorkflowStateChanged]
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("plaintext-secret", result.Value!.PlaintextSecret);
        Assert.Equal("Slack", result.Value.Webhook.Name);

        Assert.NotNull(captured);
        Assert.Equal("enc(plaintext-secret)", captured!.SecretEncrypted);
        Assert.Contains(WebhookEvents.CommentCreated, captured.EventTypes);

        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.WebhookCreated,
                Constants.ScopeTypes.Webhook,
                It.IsAny<Guid?>(), AdminId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Create_UnknownEventType_ReturnsValidation()
    {
        var svc = Create();
        var result = await svc.CreateAsync(
            new CreateWebhookDto { Name = "x", Url = "https://example.com", EventTypes = ["bogus.event"] },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not-a-url")]
    public async Task Create_NonHttpUrl_BadRequest(string badUrl)
    {
        var svc = Create();
        var result = await svc.CreateAsync(
            new CreateWebhookDto { Name = "x", Url = badUrl, EventTypes = [WebhookEvents.AssetCreated] },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.Error!.StatusCode);
    }

    [Fact]
    public async Task RotateSecret_ReplacesEncryptedAndAudits()
    {
        var existing = new Webhook
        {
            Id = Guid.NewGuid(),
            Name = "x",
            Url = "https://example.com",
            SecretEncrypted = "enc(old)",
            EventTypes = [WebhookEvents.AssetCreated],
            IsActive = true,
            CreatedByUserId = AdminId,
            CreatedAt = DateTime.UtcNow
        };
        _repo.Setup(r => r.GetByIdAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var svc = Create();
        var result = await svc.RotateSecretAsync(existing.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("plaintext-secret", result.Value!.PlaintextSecret);
        Assert.Equal("enc(plaintext-secret)", existing.SecretEncrypted);

        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.WebhookSecretRotated,
                Constants.ScopeTypes.Webhook, existing.Id, AdminId,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendTest_PublishesAndReturnsPendingDelivery()
    {
        var hook = new Webhook
        {
            Id = Guid.NewGuid(),
            Name = "x",
            Url = "https://example.com",
            SecretEncrypted = "enc(s)",
            EventTypes = [WebhookEvents.AssetCreated],
            IsActive = true,
            CreatedByUserId = AdminId
        };
        _repo.Setup(r => r.GetByIdAsync(hook.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hook);
        _deliveries.Setup(d => d.CreateAsync(It.IsAny<WebhookDelivery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookDelivery d, CancellationToken _) => d);

        var svc = Create();
        var result = await svc.SendTestAsync(hook.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("webhook.test", result.Value!.EventType);
        Assert.Equal("pending", result.Value.Status);

        _publisher.Verify(p => p.PublishAsync("webhook.test", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Webhook?)null);

        var svc = Create();
        var result = await svc.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.Error!.StatusCode);
    }
}
