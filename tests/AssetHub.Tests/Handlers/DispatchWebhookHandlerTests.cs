using System.Net;
using System.Security.Cryptography;
using System.Text;
using AssetHub.Application;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Domain.Entities;
using AssetHub.Worker.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace AssetHub.Tests.Handlers;

public class DispatchWebhookHandlerTests
{
    private readonly Mock<IWebhookDeliveryRepository> _deliveries = new();
    private readonly Mock<IWebhookRepository> _webhooks = new();
    private readonly Mock<IWebhookSecretProtector> _protector = new();
    private readonly Mock<IAuditService> _audit = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly Mock<IHttpClientFactory> _httpFactory = new();

    private const string SecretPlaintext = "test-secret-12345";
    private const string PayloadJson = "{\"id\":\"00000000-0000-0000-0000-000000000000\",\"type\":\"webhook.test\"}";

    public DispatchWebhookHandlerTests()
    {
        _protector.Setup(p => p.Unprotect(It.IsAny<string>())).Returns(SecretPlaintext);
        _httpFactory.Setup(f => f.CreateClient("webhook-dispatch"))
            .Returns(() => new HttpClient(_httpHandler.Object) { BaseAddress = null });
    }

    private DispatchWebhookHandler Create()
        => new(_deliveries.Object, _webhooks.Object, _protector.Object,
               _audit.Object, _httpFactory.Object,
               NullLogger<DispatchWebhookHandler>.Instance);

    private static (Webhook hook, WebhookDelivery delivery) MakePair()
    {
        var hook = new Webhook
        {
            Id = Guid.NewGuid(),
            Name = "x",
            Url = "https://example.com/webhook-in",
            SecretEncrypted = "enc",
            EventTypes = [WebhookEvents.AssetCreated],
            IsActive = true,
            CreatedByUserId = "admin",
            CreatedAt = DateTime.UtcNow
        };
        var delivery = new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            WebhookId = hook.Id,
            EventType = WebhookEvents.AssetCreated,
            PayloadJson = PayloadJson,
            Status = WebhookDeliveryStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        return (hook, delivery);
    }

    private void SetupHttp(HttpStatusCode status, string body = "", Action<HttpRequestMessage>? capture = null)
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capture?.Invoke(req))
            .ReturnsAsync(() => new HttpResponseMessage(status)
            {
                Content = new StringContent(body)
            });
    }

    [Fact]
    public async Task DeliveryMissing_NoOp()
    {
        _deliveries.Setup(d => d.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebhookDelivery?)null);

        var handler = Create();
        await handler.HandleAsync(new DispatchWebhookCommand { DeliveryId = Guid.NewGuid() }, CancellationToken.None);

        // Did not attempt the HTTP call.
        _httpHandler.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task AlreadyDelivered_DoesNotResend()
    {
        var (_, delivery) = MakePair();
        delivery.Status = WebhookDeliveryStatus.Delivered;
        _deliveries.Setup(d => d.GetByIdAsync(delivery.Id, It.IsAny<CancellationToken>())).ReturnsAsync(delivery);

        var handler = Create();
        await handler.HandleAsync(new DispatchWebhookCommand { DeliveryId = delivery.Id }, CancellationToken.None);

        _httpHandler.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task HappyPath_SignsWithHmacSha256_AndMarksDelivered()
    {
        var (hook, delivery) = MakePair();
        _deliveries.Setup(d => d.GetByIdAsync(delivery.Id, It.IsAny<CancellationToken>())).ReturnsAsync(delivery);
        _webhooks.Setup(w => w.GetByIdAsync(hook.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hook);

        HttpRequestMessage? sent = null;
        SetupHttp(HttpStatusCode.OK, capture: req => sent = req);

        var handler = Create();
        await handler.HandleAsync(new DispatchWebhookCommand { DeliveryId = delivery.Id }, CancellationToken.None);

        Assert.NotNull(sent);
        // Headers wired correctly.
        Assert.Equal(delivery.EventType, sent!.Headers.GetValues("X-AssetHub-Event").Single());
        Assert.Equal(delivery.Id.ToString(), sent.Headers.GetValues("X-AssetHub-Delivery").Single());

        // Signature is `sha256=<hex>` over the body bytes with the unprotected secret.
        var sigHeader = sent.Headers.GetValues("X-AssetHub-Signature").Single();
        Assert.StartsWith("sha256=", sigHeader);
        var expected = "sha256=" + Convert.ToHexStringLower(
            new HMACSHA256(Encoding.UTF8.GetBytes(SecretPlaintext))
                .ComputeHash(Encoding.UTF8.GetBytes(PayloadJson)));
        Assert.Equal(expected, sigHeader);

        // Row state.
        Assert.Equal(WebhookDeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(200, delivery.ResponseStatus);
        Assert.NotNull(delivery.DeliveredAt);
        Assert.Null(delivery.LastError);
    }

    [Fact]
    public async Task ClientError_4xx_MarksFailedAndAuditsWithoutRetry()
    {
        var (hook, delivery) = MakePair();
        _deliveries.Setup(d => d.GetByIdAsync(delivery.Id, It.IsAny<CancellationToken>())).ReturnsAsync(delivery);
        _webhooks.Setup(w => w.GetByIdAsync(hook.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hook);
        SetupHttp(HttpStatusCode.BadRequest, body: "no thanks");

        var handler = Create();
        // Must NOT throw — 4xx is permanent, not retried.
        await handler.HandleAsync(new DispatchWebhookCommand { DeliveryId = delivery.Id }, CancellationToken.None);

        Assert.Equal(WebhookDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(400, delivery.ResponseStatus);
        Assert.NotNull(delivery.LastError);
        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.WebhookDeliveryFailedPermanently,
                Constants.ScopeTypes.WebhookDelivery, delivery.Id, null,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ServerError_5xx_ThrowsToTriggerWolverineRetry()
    {
        var (hook, delivery) = MakePair();
        _deliveries.Setup(d => d.GetByIdAsync(delivery.Id, It.IsAny<CancellationToken>())).ReturnsAsync(delivery);
        _webhooks.Setup(w => w.GetByIdAsync(hook.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hook);
        SetupHttp(HttpStatusCode.InternalServerError);

        var handler = Create();
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            handler.HandleAsync(new DispatchWebhookCommand { DeliveryId = delivery.Id }, CancellationToken.None));

        // Status remains Pending so the next retry sees it; attempt count bumped.
        Assert.Equal(WebhookDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Equal(500, delivery.ResponseStatus);
    }

    [Fact]
    public async Task ServerError_AfterMaxAttempts_MarksFailedInsteadOfThrowing()
    {
        var (hook, delivery) = MakePair();
        delivery.AttemptCount = DispatchWebhookHandler.MaxAttempts - 1;
        _deliveries.Setup(d => d.GetByIdAsync(delivery.Id, It.IsAny<CancellationToken>())).ReturnsAsync(delivery);
        _webhooks.Setup(w => w.GetByIdAsync(hook.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hook);
        SetupHttp(HttpStatusCode.InternalServerError);

        var handler = Create();
        // After this attempt, AttemptCount == MaxAttempts → no throw, just mark failed.
        await handler.HandleAsync(new DispatchWebhookCommand { DeliveryId = delivery.Id }, CancellationToken.None);

        Assert.Equal(WebhookDeliveryStatus.Failed, delivery.Status);
        _audit.Verify(a => a.LogAsync(
                NotificationConstants.AuditEvents.WebhookDeliveryFailedPermanently,
                Constants.ScopeTypes.WebhookDelivery, delivery.Id, null,
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NetworkException_ThrowsToTriggerRetryAndUpdatesError()
    {
        var (hook, delivery) = MakePair();
        _deliveries.Setup(d => d.GetByIdAsync(delivery.Id, It.IsAny<CancellationToken>())).ReturnsAsync(delivery);
        _webhooks.Setup(w => w.GetByIdAsync(hook.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hook);

        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var handler = Create();
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            handler.HandleAsync(new DispatchWebhookCommand { DeliveryId = delivery.Id }, CancellationToken.None));

        Assert.NotNull(delivery.LastError);
        Assert.Contains("connection refused", delivery.LastError);
    }
}
