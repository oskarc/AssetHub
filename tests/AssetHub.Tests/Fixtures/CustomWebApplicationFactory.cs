using AssetHub.Application.Repositories;
using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Repositories;
using Wolverine;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Testcontainers.PostgreSql;

namespace AssetHub.Tests.Fixtures;

/// <summary>
/// WebApplicationFactory configured for integration tests:
/// - Real PostgreSQL via Testcontainers (full fidelity)
/// - Mocked external services (MinIO, Keycloak, Email, Media)
/// - Test authentication handler (no real OIDC required)
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private string _connectionString = string.Empty;

    public Mock<IMinIOAdapter> MockMinIO { get; } = new();
    public Mock<IKeycloakUserService> MockKeycloak { get; } = new();
    public Mock<IEmailService> MockEmail { get; } = new();
    public Mock<IMediaProcessingService> MockMedia { get; } = new();
    public Mock<IUserLookupService> MockUserLookup { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        // Expose connection string via environment variable so that
        // AddSharedInfrastructure (which reads config eagerly during
        // WebApplicationBuilder.Services setup) picks it up from the
        // default config providers before our ConfigureAppConfiguration runs.
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _connectionString);

        // Set up default mock behaviors
        MockMinIO.Setup(m => m.EnsureBucketExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        MockMinIO.Setup(m => m.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        MockMinIO.Setup(m => m.GetPresignedDownloadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://test-presigned-url.example.com/file");
        MockMinIO.Setup(m => m.GetPresignedUploadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://test-presigned-url.example.com/upload");

        MockMedia.Setup(m => m.ScheduleProcessingAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-job-id");

        MockKeycloak.Setup(m => m.GetRealmRoleMemberIdsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        MockUserLookup.Setup(m => m.GetUserNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => $"user-{id[..8]}");
        MockUserLookup.Setup(m => m.GetUserNamesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, id => $"user-{id[..8]}"));
        MockUserLookup.Setup(m => m.GetUserEmailsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, id => $"user-{id[..8]}@test.com"));
        MockUserLookup.Setup(m => m.GetAllUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string Id, string Username, string? Email, string? FirstName, string? LastName, DateTime? CreatedAt)>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Inject test connection string so AddSharedInfrastructure doesn't fail
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _connectionString,
                ["Keycloak:RequireHttpsMetadata"] = "true"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove real DbContext registrations added by AddSharedInfrastructure
            services.RemoveAll<DbContextOptions<AssetHubDbContext>>();
            services.RemoveAll<AssetHubDbContext>();
            services.RemoveAll<Npgsql.NpgsqlDataSource>();

            // Add test DbContext pointing at the Testcontainer PostgreSQL
            var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_connectionString);
            dataSourceBuilder.EnableDynamicJson();
            var dataSource = dataSourceBuilder.Build();
            services.AddDbContext<AssetHubDbContext>(options =>
                options.UseNpgsql(dataSource));

            // Re-register DbContextFactory (removed when clearing DbContext registrations)
            services.RemoveAll<IDbContextFactory<AssetHubDbContext>>();
            services.AddDbContextFactory<AssetHubDbContext>(options =>
                options.UseNpgsql(dataSource), ServiceLifetime.Scoped);

            // Disable external Wolverine transports to prevent real RabbitMQ connections in tests
            services.DisableAllExternalWolverineTransports();
            services.RunWolverineInSoloMode();

            // Replace external services with mocks
            services.RemoveAll<IMinIOAdapter>();
            services.AddScoped(_ => MockMinIO.Object);

            services.RemoveAll<IKeycloakUserService>();
            services.AddScoped(_ => MockKeycloak.Object);

            services.RemoveAll<IEmailService>();
            services.AddScoped(_ => MockEmail.Object);

            services.RemoveAll<IMediaProcessingService>();
            services.AddScoped(_ => MockMedia.Object);

            services.RemoveAll<IUserLookupService>();
            services.AddScoped(_ => MockUserLookup.Object);

            // Replace authentication with test handler
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });

            // Replace Minio client with mock (used directly in startup)
            services.RemoveAll<Minio.IMinioClient>();
            var mockMinioClient = new Mock<Minio.IMinioClient>();
            mockMinioClient
                .Setup(m => m.BucketExistsAsync(It.IsAny<Minio.BucketExistsArgs>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            services.AddSingleton(mockMinioClient.Object);

            // Disable rate limiting in tests to avoid TooManyRequests interference
            services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(options =>
            {
                options.GlobalLimiter = null;
                options.OnRejected = null;
            });
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Creates an HttpClient authenticated as the specified claims provider.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(TestClaimsProvider? claims = null)
    {
        TestAuthHandler.ClaimsOverride = claims;
        return CreateClient();
    }
}

/// <summary>
/// Shared collection fixture so all endpoint test classes reuse a single
/// CustomWebApplicationFactory (and therefore a single test host +
/// PostgreSQL container). Avoids the Serilog "logger is already frozen" error.
/// </summary>
[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<CustomWebApplicationFactory> { }
