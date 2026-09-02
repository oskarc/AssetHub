using AssetHub.Api.Extensions;
using AssetHub.Application.Configuration;
using AssetHub.Application.Messages;
using AssetHub.Application.Services;
using AssetHub.Api.BackgroundServices;
using AssetHub.Api.Handlers;
using AssetHub.Api.Messaging;
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

    // -- In-process messaging (contract-026: replaced RabbitMQ/Wolverine) ------
    // Producers publish to InProcessMessageBus (a channel); MessageDispatcherService
    // consumes it and invokes the handler in-process. Media durability is the outbox
    // upstream of the bus; StuckProcessingReaperService recovers anything lost to a
    // mid-flight restart. Handlers are registered explicitly (Wolverine used to
    // discover them).
    builder.Services.AddSingleton<InProcessMessageBus>();
    builder.Services.AddSingleton<IAppMessageBus>(sp => sp.GetRequiredService<InProcessMessageBus>());
    builder.Services.AddHostedService<MessageDispatcherService>();
    builder.Services.AddHostedService<StuckProcessingReaperService>();

    builder.Services.AddScoped<ProcessImageHandler>();
    builder.Services.AddScoped<ProcessVideoHandler>();
    builder.Services.AddScoped<ProcessAudioHandler>();
    builder.Services.AddScoped<BuildZipHandler>();
    builder.Services.AddScoped<AssetProcessingCompletedHandler>();
    builder.Services.AddScoped<AssetProcessingFailedHandler>();

    // -- Build & run startup tasks -------------------------------------------
    var app = builder.Build();
    await app.RunStartupTasksAsync();

    // -- Middleware pipeline --------------------------------------------------
    app.UseAssetHubMiddleware();

    // -- Endpoints -----------------------------------------------------------
    app.MapAssetHubEndpoints();

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Make the auto-generated Program class visible for WebApplicationFactory<Program> in integration tests
public partial class Program { private Program() { } }
