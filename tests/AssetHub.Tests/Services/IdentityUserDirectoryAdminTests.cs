using AssetHub.Application;
using AssetHub.Application.Configuration;
using AssetHub.Application.Services;
using AssetHub.Application.Services.Email;
using AssetHub.Infrastructure.Data;
using AssetHub.Infrastructure.Identity;
using AssetHub.Infrastructure.Services;
using AssetHub.Tests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AssetHub.Tests.Services;

/// <summary>
/// Covers the Identity-backed admin directory and the password-reset flow added
/// by contract-014. End-to-end sign-in and the emailed link remain E2E concerns;
/// these check the behaviour reachable without a browser — including the
/// security properties the contract requires by construction.
/// </summary>
[Collection("Database")]
public class IdentityUserDirectoryAdminTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AssetHubDbContext _db = null!;
    private ServiceProvider _services = null!;
    private Mock<IEmailService> _email = null!;

    public IdentityUserDirectoryAdminTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _db = await _fixture.CreateDbContextAsync();
        var dbName = _db.Database.GetDbConnection().Database!;
        var conn = new Npgsql.NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = dbName }
            .ConnectionString;

        _email = new Mock<IEmailService>();

        var services = new ServiceCollection();
        services.AddLogging();
        // AddDefaultTokenProviders() builds on data protection — without this the
        // UserManager cannot be constructed.
        services.AddDataProtection();
        services.AddDbContext<AssetHubDbContext>(o => o.UseNpgsql(conn));
        services.AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 12;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AssetHubDbContext>()
            .AddDefaultTokenProviders();
        services.AddSingleton(_email.Object);
        services.Configure<AppSettings>(o => o.BaseUrl = "https://assethub.test");
        services.AddScoped<PasswordResetLinkSender>();
        services.AddScoped<IPasswordResetLinkSender<AppUser>>(sp => sp.GetRequiredService<PasswordResetLinkSender>());
        services.AddScoped<IUserDirectoryAdmin, IdentityUserDirectoryAdmin>();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var r in new[] { RoleHierarchy.Roles.Viewer, RoleHierarchy.Roles.Admin })
            await roles.CreateAsync(new IdentityRole(r));
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private IServiceScope Scope() => _services.CreateScope();

    [Fact]
    public async Task CreateUser_ReturnsIdAndPersistsDisplayName()
    {
        using var scope = Scope();
        var admin = scope.ServiceProvider.GetRequiredService<IUserDirectoryAdmin>();

        var id = await admin.CreateUserAsync("newbie", "newbie@example.test", "New", "Bie", "Correct-Horse-1!");

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var created = await users.FindByIdAsync(id);
        Assert.NotNull(created);
        Assert.Equal("New Bie", created!.DisplayName);
    }

    [Fact]
    public async Task AssignAndRemoveRole_RoundTrips()
    {
        using var scope = Scope();
        var admin = scope.ServiceProvider.GetRequiredService<IUserDirectoryAdmin>();

        var id = await admin.CreateUserAsync("roled", "roled@example.test", "R", "D", "Correct-Horse-1!");

        await admin.AssignRealmRoleAsync(id, RoleHierarchy.Roles.Admin);
        Assert.Contains(id, await admin.GetRealmRoleMemberIdsAsync(RoleHierarchy.Roles.Admin));

        await admin.RemoveRealmRoleAsync(id, RoleHierarchy.Roles.Admin);
        Assert.DoesNotContain(id, await admin.GetRealmRoleMemberIdsAsync(RoleHierarchy.Roles.Admin));
    }

    [Fact]
    public async Task AssignRole_Twice_IsIdempotent()
    {
        using var scope = Scope();
        var admin = scope.ServiceProvider.GetRequiredService<IUserDirectoryAdmin>();
        var id = await admin.CreateUserAsync("twice", "twice@example.test", "T", "W", "Correct-Horse-1!");

        await admin.AssignRealmRoleAsync(id, RoleHierarchy.Roles.Viewer);
        await admin.AssignRealmRoleAsync(id, RoleHierarchy.Roles.Viewer);

        Assert.Single(await admin.GetRealmRoleMemberIdsAsync(RoleHierarchy.Roles.Viewer));
    }

    [Fact]
    public async Task DeleteUser_UnknownId_DoesNotThrow()
    {
        using var scope = Scope();
        var admin = scope.ServiceProvider.GetRequiredService<IUserDirectoryAdmin>();

        // Matches the identity-store implementation's idempotent behaviour: deleting an
        // absent user is a no-op, not an error.
        var ex = await Record.ExceptionAsync(() => admin.DeleteUserAsync(Guid.NewGuid().ToString()));
        Assert.Null(ex);
    }

    [Fact]
    public async Task SendExecuteActionsEmail_UnsupportedAction_Throws()
    {
        using var scope = Scope();
        var admin = scope.ServiceProvider.GetRequiredService<IUserDirectoryAdmin>();
        var id = await admin.CreateUserAsync("acty", "acty@example.test", "A", "C", "Correct-Horse-1!");

        await Assert.ThrowsAsync<NotSupportedException>(
            () => admin.SendExecuteActionsEmailAsync(id, ["VERIFY_EMAIL"]));
    }

    [Fact]
    public async Task SendExecuteActionsEmail_SendsResetMail()
    {
        using var scope = Scope();
        var admin = scope.ServiceProvider.GetRequiredService<IUserDirectoryAdmin>();
        var id = await admin.CreateUserAsync("mailme", "mailme@example.test", "M", "M", "Correct-Horse-1!");

        await admin.SendExecuteActionsEmailAsync(id, ["UPDATE_PASSWORD"]);

        _email.Verify(e => e.SendEmailAsync(
            "mailme@example.test", It.IsAny<IEmailTemplate>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PasswordReset_ValidToken_ChangesPasswordAndIsSingleUse()
    {
        using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var sender = scope.ServiceProvider.GetRequiredService<PasswordResetLinkSender>();

        var user = new AppUser { UserName = "resetme", Email = "resetme@example.test" };
        Assert.True((await users.CreateAsync(user, "Correct-Horse-1!")).Succeeded);

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var encoded = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            System.Text.Encoding.UTF8.GetBytes(token));

        var (ok, _) = await sender.ResetAsync(user.Id, encoded, "Brand-New-Pass-9!");
        Assert.True(ok);

        // The security stamp rotated, so replaying the same link must fail.
        var (replay, _) = await sender.ResetAsync(user.Id, encoded, "Another-Pass-9!");
        Assert.False(replay);
    }

    [Fact]
    public async Task PasswordReset_UnknownUser_FailsWithoutRevealingExistence()
    {
        using var scope = Scope();
        var sender = scope.ServiceProvider.GetRequiredService<PasswordResetLinkSender>();

        var (ok, error) = await sender.ResetAsync(Guid.NewGuid().ToString(), "not-a-token", "Brand-New-Pass-9!");

        Assert.False(ok);
        // A null error is what the endpoint turns into a generic message — an
        // account-specific reason here would be an enumeration oracle.
        Assert.Null(error);
    }

    [Fact]
    public async Task PasswordReset_MalformedToken_FailsQuietly()
    {
        using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var sender = scope.ServiceProvider.GetRequiredService<PasswordResetLinkSender>();

        var user = new AppUser { UserName = "badtoken", Email = "badtoken@example.test" };
        Assert.True((await users.CreateAsync(user, "Correct-Horse-1!")).Succeeded);

        var (ok, error) = await sender.ResetAsync(user.Id, "!!!not-base64url!!!", "Brand-New-Pass-9!");

        Assert.False(ok);
        Assert.Null(error);
    }

    [Fact]
    public async Task PasswordReset_WeakPassword_SurfacesPolicyErrorOnly()
    {
        using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var sender = scope.ServiceProvider.GetRequiredService<PasswordResetLinkSender>();

        var user = new AppUser { UserName = "weak", Email = "weak@example.test" };
        Assert.True((await users.CreateAsync(user, "Correct-Horse-1!")).Succeeded);

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var encoded = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            System.Text.Encoding.UTF8.GetBytes(token));

        var (ok, error) = await sender.ResetAsync(user.Id, encoded, "short");

        Assert.False(ok);
        // Policy failures ARE actionable by the user, so they are surfaced.
        Assert.NotNull(error);
    }
}
