using AssetHub.Application.Services;
using AssetHub.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Testcontainers.PostgreSql;
using Wolverine;

namespace AssetHub.Tests.Fixtures;

/// <summary>
/// Alternate factory that preserves the real authentication chain (Smart selector +
/// PAT handler + JWT + Cookie) instead of swapping it for <see cref="TestAuthHandler"/>.
/// Used exclusively by the PAT end-to-end test so requests with
/// <c>Authorization: Bearer pat_*</c> traverse the full production pipeline.
///
/// External services (MinIO, Keycloak, Email, Media) are still mocked — the mock
/// <see cref="IKeycloakUserService"/> is exposed so tests can stub
/// <c>GetUserRealmRolesAsync</c> when the PAT handler needs role hydration.
/// </summary>
public class PatAuthWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
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

        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _connectionString);

        MockMinIO.Setup(m => m.EnsureBucketExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        MockMinIO.Setup(m => m.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        MockKeycloak.Setup(m => m.GetRealmRoleMemberIdsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());
        MockKeycloak.Setup(m => m.GetUserRealmRolesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        MockUserLookup.Setup(m => m.GetUserNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => $"user-{id[..8]}");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _connectionString,
                ["Keycloak:RequireHttpsMetadata"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AssetHubDbContext>>();
            services.RemoveAll<AssetHubDbContext>();
            services.RemoveAll<Npgsql.NpgsqlDataSource>();

            var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_connectionString);
            dataSourceBuilder.EnableDynamicJson();
            var dataSource = dataSourceBuilder.Build();
            services.AddDbContext<AssetHubDbContext>(options => options
                .UseNpgsql(dataSource)
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)));

            services.RemoveAll<IDbContextFactory<AssetHubDbContext>>();
            services.AddDbContextFactory<AssetHubDbContext>(options => options
                .UseNpgsql(dataSource)
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)),
                ServiceLifetime.Scoped);

            services.DisableAllExternalWolverineTransports();
            services.RunWolverineInSoloMode();

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

            services.RemoveAll<Minio.IMinioClient>();
            var mockMinioClient = new Mock<Minio.IMinioClient>();
            mockMinioClient
                .Setup(m => m.BucketExistsAsync(It.IsAny<Minio.BucketExistsArgs>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            services.AddSingleton(mockMinioClient.Object);

            services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(options =>
            {
                options.GlobalLimiter = null;
                options.OnRejected = null;
            });

            // Keep the real Smart selector for authentication, but pin the challenge scheme
            // to PAT so unauthenticated / failed-auth requests return 401 + WWW-Authenticate
            // instead of trying to OIDC-redirect to an unreachable Keycloak in tests.
            services.Configure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(options =>
            {
                options.DefaultChallengeScheme =
                    AssetHub.Api.Authentication.PersonalAccessTokenAuthenticationHandler.SchemeName;
            });
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>
/// Shared collection fixture for the PAT E2E test — separate from "Api" because the
/// auth configuration is fundamentally different.
/// </summary>
[CollectionDefinition("PatAuth")]
public class PatAuthCollection : ICollectionFixture<PatAuthWebApplicationFactory> { }
