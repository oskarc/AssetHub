---
applyTo: "src/AssetHub.Api/**"
description: "Use when creating or editing Minimal API endpoints, middleware, filters, or DI wiring in the AssetHub.Api project."
---
# API Layer Conventions (AssetHub.Api)

AssetHub.Api is the composition root — it wires DI, hosts Blazor Server, and exposes Minimal API endpoints.

## Endpoint Structure
- One static class per domain: `AssetEndpoints`, `CollectionEndpoints`, `ShareEndpoints`, `AdminEndpoints`, etc.
- Extension method: `Map*Endpoints(this WebApplication app)`.
- All endpoints registered in `WebApplicationExtensions.MapAssetHubEndpoints()`.

```csharp
public static class ExampleEndpoints
{
    public static WebApplication MapExampleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/examples")
            .RequireAuthorization("RequireViewer")
            .WithTags("Examples");

        group.MapGet("/", GetAll).WithName("GetAllExamples");
        group.MapPost("/", Create)
            .AddEndpointFilter<ValidationFilter<CreateExampleDto>>()
            .DisableAntiforgery();

        return app;
    }
}
```

## ServiceResult → HTTP Response
Always use `.ToHttpResult()` from `ServiceResultExtensions`:

```csharp
// Default: 200 OK (value) or 204 No Content (void)
return (await svc.GetByIdAsync(id, ct)).ToHttpResult();

// Custom success status (e.g., 201 Created)
return (await svc.CreateAsync(dto, ct))
    .ToHttpResult(value => Results.Created($"/api/v1/examples/{value.Id}", value));
```

Never manually map ServiceResult errors — `ToHttpResult()` handles all error codes automatically via `ApiError`.

## Request Binding
- **Route**: `Guid id` with `{id:guid}` constraint.
- **Query strings**: `[AsParameters] SearchQueryDto dto` — DTO with public properties.
- **JSON body**: automatic by parameter name (no attribute needed).
- **Form data**: `[FromForm]` for file uploads.
- **Services**: `[FromServices] IAssetService svc`.

## Validation
- Apply `ValidationFilter<T>` per-endpoint via `.AddEndpointFilter<ValidationFilter<CreateDto>>()`.
- DTOs use DataAnnotations (`[Required]`, `[StringLength]`, `[Range]`).
- Returns `400 BadRequest` with field-level `ApiError.Details` dictionary.

## Authorization
- Route groups: `.RequireAuthorization("RequireViewer")` (or `RequireContributor`, `RequireManager`, `RequireAdmin`).
- Per-endpoint override is possible but prefer group-level policies.
- Collection-level RBAC: inject `ICollectionAuthorizationService` and check permissions in the endpoint handler.

## Anti-forgery
- `.DisableAntiforgery()` on all API POST/PATCH/DELETE endpoints (JWT clients are CSRF-immune).
- Blazor UI antiforgery is handled separately by middleware.

## Error Responses
All API errors use `ApiError` format:
```json
{ "code": "NOT_FOUND", "message": "Asset not found", "details": {} }
```

## Middleware
- **Global exception handler** in `WebApplicationExtensions` — catches unhandled exceptions for `/api/*` paths and returns `ApiError`.
- **Security headers** via `UseSecurityHeaders()` — CSP, X-Frame-Options, etc.
- **MetricsIpRestrictionMiddleware** — blocks `/metrics` from non-private IPs.

## DI Wiring
- Infrastructure: `AddSharedInfrastructure()` (shared with Worker).
- API services: `AddAssetHubServices()` in `ServiceCollectionExtensions`.
- Options with `.ValidateOnStart()` for critical settings (`KeycloakSettings`, `AppSettings`).
