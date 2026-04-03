---
applyTo: "src/AssetHub.Application/Configuration/**, src/AssetHub.Api/Extensions/**"
description: "Use when creating or editing settings classes, configuration binding, options validation, or secrets management in AssetHub."
---
# Configuration & Secrets Conventions

## Settings Class Pattern

All settings classes live in `Application/Configuration/` with a `const string SectionName`:

```csharp
public class ExampleSettings
{
    public const string SectionName = "Example";

    [Required]
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 5672;

    public string? OptionalField { get; set; }
}
```

### Rules
- **`const string SectionName`** matches the `appsettings.json` key exactly.
- **`[Required]`** on fields that must be set for the service to function.
- **Sensible defaults** for non-critical fields (ports, timeouts, feature flags).
- **No secrets in the class** — passwords come from env vars or Docker secrets at runtime.

## Registration & Validation

In `ServiceCollectionExtensions.cs` or `InfrastructureServiceExtensions.cs`:

```csharp
// Critical settings — fail fast on startup if misconfigured
services.AddOptions<KeycloakSettings>()
    .BindConfiguration(KeycloakSettings.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Optional settings — don't fail startup
services.AddOptions<EmailSettings>()
    .BindConfiguration(EmailSettings.SectionName)
    .ValidateDataAnnotations();
```

### When to use `ValidateOnStart()`
- **Required infrastructure**: Keycloak, MinIO, PostgreSQL, RabbitMQ, Redis — these must be valid or the app can't run.
- **Optional features**: Email (SMTP), ImageProcessing — okay to start without them.

## Secrets Management

### Never hardcode secrets
```csharp
// BAD
var password = "admin123";

// GOOD — from environment or appsettings override
var password = configuration["MinIO:SecretKey"];
```

### Environment variable overrides
ASP.NET Core's config system maps `__` to `:` in section paths:
```
MinIO__SecretKey=my-secret        → MinIO:SecretKey
RabbitMQ__Password=guest          → RabbitMQ:Password
Redis__ConnectionString=redis:6379 → Redis:ConnectionString
```

### Docker Secrets (production)
Production compose uses file-based secrets — never environment variables for sensitive data.

## Existing Settings Classes

| Class | Section | ValidateOnStart | Purpose |
|-------|---------|:-:|---------|
| `AppSettings` | `App` | ✅ | Base URL, upload limits |
| `KeycloakSettings` | `Keycloak` | ✅ | OIDC authority, client ID/secret |
| `MinIOSettings` | `MinIO` | ✅ | Endpoint, bucket, credentials |
| `RabbitMQSettings` | `RabbitMQ` | ✅ | Host, port, credentials |
| `RedisSettings` | `Redis` | ❌ | Connection string (optional — falls back to in-memory) |
| `EmailSettings` | `Email` | ❌ | SMTP config (optional) |
| `ImageProcessingSettings` | `ImageProcessing` | ❌ | Thumbnail/medium dimensions |
| `OpenTelemetrySettings` | `OpenTelemetry` | ❌ | OTLP endpoint, service name |
