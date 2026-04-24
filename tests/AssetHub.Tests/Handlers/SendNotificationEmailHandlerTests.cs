using AssetHub.Application.Configuration;
using AssetHub.Application.Messages;
using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Email;
using AssetHub.Application.Services.Email.Templates;
using AssetHub.Domain.Entities;
using AssetHub.Worker.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.Handlers;

public class SendNotificationEmailHandlerTests
{
    private readonly Mock<INotificationRepository> _notifRepo = new();
    private readonly Mock<INotificationPreferencesService> _prefs = new();
    private readonly Mock<INotificationUnsubscribeTokenService> _tokens = new();
    private readonly Mock<IUserLookupService> _users = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly IOptions<AppSettings> _appOptions =
        Options.Create(new AppSettings { BaseUrl = "https://assethub.test" });

    private SendNotificationEmailHandler CreateHandler()
        => new(_notifRepo.Object, _prefs.Object, _tokens.Object,
            _users.Object, _email.Object, _appOptions,
            NullLogger<SendNotificationEmailHandler>.Instance);

    private static Notification MakeNotification(
        Guid? id = null, string userId = "user-1", string category = "mention",
        string title = "hello", string? body = "world", string? url = "/assets/abc")
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            UserId = userId,
            Category = category,
            Title = title,
            Body = body,
            Url = url,
            CreatedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task HandleAsync_NotificationMissing_DoesNothing()
    {
        _notifRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Notification?)null);
        var handler = CreateHandler();

        await handler.HandleAsync(new SendNotificationEmailCommand { NotificationId = Guid.NewGuid() }, CancellationToken.None);

        _email.Verify(e => e.SendEmailAsync(
                It.IsAny<string>(), It.IsAny<IEmailTemplate>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NoEmailOnFile_Skips()
    {
        var notification = MakeNotification();
        _notifRepo.Setup(r => r.GetByIdAsync(notification.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);
        _users.Setup(u => u.GetUserEmailsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        var handler = CreateHandler();
        await handler.HandleAsync(new SendNotificationEmailCommand { NotificationId = notification.Id }, CancellationToken.None);

        _email.Verify(e => e.SendEmailAsync(
                It.IsAny<string>(), It.IsAny<IEmailTemplate>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_BuildsUnsubscribeUrlAndSendsEmail()
    {
        var notification = MakeNotification();
        _notifRepo.Setup(r => r.GetByIdAsync(notification.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);
        _users.Setup(u => u.GetUserEmailsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { [notification.UserId] = "jane@example.com" });
        _prefs.Setup(p => p.GetByUserIdAsync(notification.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationPreferences
            {
                Id = Guid.NewGuid(),
                UserId = notification.UserId,
                UnsubscribeTokenHash = "stamp-xyz"
            });
        _tokens.Setup(t => t.CreateToken(notification.UserId, notification.Category, "stamp-xyz"))
            .Returns("signed-token");

        IEmailTemplate? captured = null;
        string? capturedRecipient = null;
        _email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<IEmailTemplate>(), It.IsAny<CancellationToken>()))
            .Callback<string, IEmailTemplate, CancellationToken>((r, t, _) => { capturedRecipient = r; captured = t; })
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        await handler.HandleAsync(new SendNotificationEmailCommand { NotificationId = notification.Id }, CancellationToken.None);

        Assert.Equal("jane@example.com", capturedRecipient);
        Assert.IsType<NotificationEmailTemplate>(captured);
        Assert.Equal(notification.Title, captured!.Subject);
        var html = captured.GetHtmlBody();
        Assert.Contains("https://assethub.test/api/v1/notifications/unsubscribe?token=signed-token", html);
        Assert.Contains("https://assethub.test/assets/abc", html);
    }

    [Fact]
    public async Task HandleAsync_PrefsMissing_SkipsToAvoidBrokenUnsubscribeLink()
    {
        var notification = MakeNotification();
        _notifRepo.Setup(r => r.GetByIdAsync(notification.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(notification);
        _users.Setup(u => u.GetUserEmailsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { [notification.UserId] = "jane@example.com" });
        _prefs.Setup(p => p.GetByUserIdAsync(notification.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationPreferences?)null);

        var handler = CreateHandler();
        await handler.HandleAsync(new SendNotificationEmailCommand { NotificationId = notification.Id }, CancellationToken.None);

        _email.Verify(e => e.SendEmailAsync(
                It.IsAny<string>(), It.IsAny<IEmailTemplate>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
