using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Identity;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssetHub.Tests.Services;

/// <summary>
/// Covers the local Identity provider path added by contract-013. The wider
/// suite runs behind <c>TestAuthHandler</c> and therefore never exercises real
/// authentication — these are the unit-level checks for the pieces testable
/// without a browser: seeding, role creation, password policy, and the
/// Identity-backed user directory. End-to-end sign-in is an E2E concern.
/// </summary>
[Collection("Database")]
public class IdentityProviderTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private DbContextProvider _provider = null!;
    private ServiceProvider _identityServices = null!;
    private string _connectionString = null!;

    public IdentityProviderTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        var dbName = _db.Database.GetDbConnection().Database!;
        _provider = _fixture.CreateDbContextProvider(dbName);
        // GetDbConnection().ConnectionString redacts the password, so derive the
        // per-test connection from the fixture's own string instead.
        _connectionString = new Npgsql.NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Database = dbName
        }.ConnectionString;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AssetHubDbContext>(o =>
            o.UseNpgsql(_connectionString));
        services.AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 12;
                o.Password.RequireNonAlphanumeric = true;
                o.Password.RequireDigit = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AssetHubDbContext>();
        services.Configure<IdentitySettings>(o => o.SeedAdmin = new SeedAdminSettings
        {
            UserName = "seed-admin",
            Email = "seed-admin@example.test",
            Password = "Correct-Horse-1!"
        });
        services.AddScoped<IdentitySeeder>();
        _identityServices = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _identityServices.DisposeAsync();
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Seed_EmptyStore_CreatesAdminWithAdminRole()
    {
        using var scope = _identityServices.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = await users.FindByNameAsync("seed-admin");

        Assert.NotNull(admin);
        Assert.True(await users.IsInRoleAsync(admin!, RoleHierarchy.Roles.Admin));
    }

    [Fact]
    public async Task Seed_CreatesAllFourApplicationRoles()
    {
        using var scope = _identityServices.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[]
                 {
                     RoleHierarchy.Roles.Viewer, RoleHierarchy.Roles.Contributor,
                     RoleHierarchy.Roles.Manager, RoleHierarchy.Roles.Admin
                 })
        {
            Assert.True(await roles.RoleExistsAsync(role), $"role '{role}' should exist after seeding");
        }
    }

    [Fact]
    public async Task Seed_NonEmptyStore_DoesNotCreateBootstrapAdmin()
    {
        using var scope = _identityServices.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await users.CreateAsync(
            new AppUser { UserName = "someone", Email = "someone@example.test" },
            "Correct-Horse-1!")).Succeeded);

        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();

        // Once the store holds a real user the bootstrap admin must never appear,
        // otherwise a configured seed password is a permanent back door.
        Assert.Null(await users.FindByNameAsync("seed-admin"));
    }

    [Fact]
    public async Task Seed_RunTwice_CreatesOnlyOneAdmin()
    {
        using var scope = _identityServices.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Single(users.Users.Where(u => u.UserName == "seed-admin"));
    }

    [Fact]
    public async Task Seed_MissingPassword_ThrowsRatherThanInventingOne()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AssetHubDbContext>(o =>
            o.UseNpgsql(_connectionString));
        services.AddIdentityCore<AppUser>().AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AssetHubDbContext>();
        services.Configure<IdentitySettings>(o => o.SeedAdmin = new SeedAdminSettings
        {
            UserName = "admin", Email = "", Password = ""
        });
        services.AddScoped<IdentitySeeder>();
        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        // Any throw is the contract here — the point is that seeding refuses to
        // proceed without a configured password rather than inventing one.
        var ex = await Record.ExceptionAsync(
            () => scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync());
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task PasswordPolicy_RejectsTooShortPassword()
    {
        using var scope = _identityServices.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var result = await users.CreateAsync(
            new AppUser { UserName = "shorty", Email = "shorty@example.test" }, "Short-1!");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UserLookup_ResolvesDisplayNameAndEmailFromLocalStore()
    {
        using var scope = _identityServices.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser
        {
            UserName = "lookup-target",
            Email = "lookup@example.test",
            DisplayName = "Lookup Target"
        };
        Assert.True((await users.CreateAsync(user, "Correct-Horse-1!")).Succeeded);

        var lookup = new IdentityUserLookupService(_provider, NullLogger<IdentityUserLookupService>.Instance);

        Assert.Equal("Lookup Target", (await lookup.GetUserNamesAsync([user.Id]))[user.Id]);
        Assert.Equal("lookup@example.test", (await lookup.GetUserEmailsAsync([user.Id]))[user.Id]);
        Assert.Equal(user.Id, await lookup.GetUserIdByUsernameAsync("lookup-target"));
        Assert.True(await lookup.UserExistsAsync("lookup-target"));
    }

    [Fact]
    public async Task UserLookup_UnknownIds_AreAbsentRatherThanThrowing()
    {
        var lookup = new IdentityUserLookupService(_provider, NullLogger<IdentityUserLookupService>.Instance);

        var names = await lookup.GetUserNamesAsync(["does-not-exist"]);
        var existing = await lookup.GetExistingUserIdsAsync(["does-not-exist"]);

        Assert.Empty(names);
        Assert.Empty(existing);
    }
}
