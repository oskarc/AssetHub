using AssetHub.Api.Extensions;
using Serilog;
using Serilog.Events;

// -- Bootstrap Serilog (captures startup errors before DI is ready) ----------
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // -- Serilog structured logging ------------------------------------------
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .Enrich.WithProperty("Application", "AssetHub"));

    // Allow personal overrides via appsettings.Local.json (gitignored)
    builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

    // -- Services ------------------------------------------------------------
    builder.Services.AddAssetHubServices(
        builder.Configuration, builder.Environment, builder.WebHost);
    builder.Services.AddAssetHubAuthentication(
        builder.Configuration, builder.Environment);
    builder.Services.AddAssetHubOpenTelemetry(builder.Configuration);

    // -- Build & run startup tasks -------------------------------------------
    var app = builder.Build();
    await app.RunStartupTasksAsync();

    // -- Middleware pipeline --------------------------------------------------
    app.UseAssetHubMiddleware();

    // -- Endpoints -----------------------------------------------------------
    app.MapAssetHubEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Make the auto-generated Program class visible for WebApplicationFactory<Program> in integration tests
public partial class Program { }
