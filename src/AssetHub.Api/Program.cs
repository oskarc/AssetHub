using AssetHub.Api.Extensions;
using AssetHub.Application.Configuration;
using AssetHub.Application.Messages;
using Serilog;
using Serilog.Events;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;

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

    // -- Wolverine (message bus via RabbitMQ) ---------------------------------
    var rabbitSettings = builder.Configuration
        .GetSection(RabbitMQSettings.SectionName)
        .Get<RabbitMQSettings>() ?? new RabbitMQSettings();

    builder.Host.UseWolverine(opts =>
    {
        opts.ApplicationAssembly = typeof(Program).Assembly;

        opts.UseRabbitMq(rabbit =>
        {
            rabbit.HostName = rabbitSettings.Host;
            rabbit.VirtualHost = rabbitSettings.VirtualHost;
            rabbit.UserName = rabbitSettings.Username;
            rabbit.Password = rabbitSettings.Password;
        }).AutoProvision();

        // Route commands to Worker queues
        opts.PublishMessage<ProcessImageCommand>()
            .ToRabbitQueue("process-image");
        opts.PublishMessage<ProcessVideoCommand>()
            .ToRabbitQueue("process-video");
        opts.PublishMessage<BuildZipCommand>()
            .ToRabbitQueue("build-zip");
        opts.PublishMessage<ApplyExportPresetsCommand>()
            .ToRabbitQueue("apply-export-presets");
        opts.PublishMessage<StartMigrationCommand>()
            .ToRabbitQueue("start-migration");
        opts.PublishMessage<ProcessMigrationItemCommand>()
            .ToRabbitQueue("process-migration-item");
        opts.PublishMessage<S3MigrationScanCommand>()
            .ToRabbitQueue("s3-migration-scan");
        opts.PublishMessage<SendNotificationEmailCommand>()
            .ToRabbitQueue("send-notification-email");

        // Listen for events from Worker
        opts.ListenToRabbitQueue("asset-processing-completed");
        opts.ListenToRabbitQueue("asset-processing-failed");

        opts.Policies.AutoApplyTransactions();

        opts.OnException<Exception>().RetryWithCooldown(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30));
    });

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
