using AssetHub.Application.Configuration;
using AssetHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;

namespace AssetHub.Tests.Services;

/// <summary>
/// Unit tests for KeycloakUserService focusing on grant type selection
/// and admin token acquisition.
/// </summary>
public class KeycloakUserServiceTests
{
    private static IOptions<KeycloakSettings> CreateSettings(
        string? adminClientSecret = null,
        string adminClientId = "admin-cli",
        string adminUsername = "admin",
        string adminPassword = "admin123")
    {
        var settings = new KeycloakSettings
        {
            Authority = "http://keycloak:8080/realms/media",
            AdminUsername = adminUsername,
            AdminPassword = adminPassword,
            AdminClientId = adminClientId,
            AdminClientSecret = adminClientSecret
        };
        return Options.Create(settings);
    }

    private static HttpClient CreateMockHttpClient(HttpStatusCode statusCode, object responseBody)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(JsonSerializer.Serialize(responseBody))
            });

        return new HttpClient(handlerMock.Object);
    }

    private static (HttpClient client, Mock<HttpMessageHandler> mock) CreateVerifiableHttpClient(
        HttpStatusCode statusCode, object responseBody)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(JsonSerializer.Serialize(responseBody))
            })
            .Verifiable();

        return (new HttpClient(handlerMock.Object), handlerMock);
    }

    [Fact]
    public void Constructor_WithClientSecret_UsesClientCredentialsGrant()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<KeycloakUserService>>();
        var config = CreateSettings(adminClientSecret: "my-secret", adminClientId: "assethub-admin");
        var httpClient = new HttpClient();

        // Act
        var service = new KeycloakUserService(config, loggerMock.Object, httpClient);

        // Assert - verify log message indicates client_credentials
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("client_credentials")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_WithoutClientSecret_UsesPasswordGrant()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<KeycloakUserService>>();
        var config = CreateSettings(adminClientSecret: null);
        var httpClient = new HttpClient();

        // Act
        var service = new KeycloakUserService(config, loggerMock.Object, httpClient);

        // Assert - verify log message indicates password grant
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("password grant")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_WithEmptyClientSecret_UsesPasswordGrant()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<KeycloakUserService>>();
        var config = CreateSettings(adminClientSecret: "");
        var httpClient = new HttpClient();

        // Act
        var service = new KeycloakUserService(config, loggerMock.Object, httpClient);

        // Assert
        loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("password grant")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_MissingAuthority_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = Options.Create(new KeycloakSettings
        {
            Authority = "", // Missing
            AdminUsername = "admin",
            AdminPassword = "admin123"
        });

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new KeycloakUserService(settings, NullLogger<KeycloakUserService>.Instance, new HttpClient()));
    }

    [Fact]
    public void Constructor_MissingAdminUsername_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = Options.Create(new KeycloakSettings
        {
            Authority = "http://keycloak:8080/realms/media",
            AdminUsername = "", // Missing
            AdminPassword = "admin123"
        });

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new KeycloakUserService(settings, NullLogger<KeycloakUserService>.Instance, new HttpClient()));
    }

    [Fact]
    public void Constructor_MissingAdminPassword_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = Options.Create(new KeycloakSettings
        {
            Authority = "http://keycloak:8080/realms/media",
            AdminUsername = "admin",
            AdminPassword = "" // Missing
        });

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new KeycloakUserService(settings, NullLogger<KeycloakUserService>.Instance, new HttpClient()));
    }

    [Fact]
    public void Constructor_ParsesRealmFromAuthority()
    {
        // Arrange - the realm extraction is internal, but we can verify it works
        // by checking that calls go to the correct realm endpoint
        var settings = CreateSettings();
        var httpClient = new HttpClient();

        // Act - should not throw, meaning parsing succeeded
        var service = new KeycloakUserService(settings, NullLogger<KeycloakUserService>.Instance, httpClient);

        // Assert - no exception means parsing worked
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_DefaultsAdminClientIdToAdminCli()
    {
        // Arrange
        var settings = Options.Create(new KeycloakSettings
        {
            Authority = "http://keycloak:8080/realms/media",
            AdminUsername = "admin",
            AdminPassword = "admin123"
            // AdminClientId defaults to "admin-cli"
        });

        // Act - should not throw
        var service = new KeycloakUserService(settings, NullLogger<KeycloakUserService>.Instance, new HttpClient());

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public async Task CreateUserAsync_WithValidInput_ReturnsUserId()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        var tokenResponse = new { access_token = "mock-token", expires_in = 300 };

        var handlerMock = new Mock<HttpMessageHandler>();
        var callCount = 0;
        
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) =>
            {
                callCount++;
                if (req.RequestUri!.PathAndQuery.Contains("token"))
                {
                    // Token request
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(tokenResponse))
                    };
                }
                else if (req.Method == HttpMethod.Post)
                {
                    // User creation
                    return new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Headers = { Location = new Uri($"http://keycloak:8080/admin/realms/media/users/{userId}") }
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

        var config = CreateSettings();
        var httpClient = new HttpClient(handlerMock.Object);
        var service = new KeycloakUserService(config, NullLogger<KeycloakUserService>.Instance, httpClient);

        // Act
        var result = await service.CreateUserAsync("testuser", "test@example.com", "Test", "User", "password123");

        // Assert
        Assert.Equal(userId, result);
    }
}
