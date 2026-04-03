---
applyTo: "src/AssetHub.Infrastructure/Services/**, src/AssetHub.Api/Endpoints/**"
description: "Use when implementing error handling, service results, or HTTP response mapping in AssetHub services or endpoints."
---
# Error Handling Conventions

## ServiceResult — The Only Way Services Report Errors

Services **never throw** for business errors. All service methods return `ServiceResult` (void) or `ServiceResult<T>`:

```csharp
public async Task<ServiceResult<AssetDto>> GetByIdAsync(Guid id, CancellationToken ct)
{
    var asset = await _repo.GetByIdAsync(id, ct);
    if (asset is null) return ServiceError.NotFound("Asset not found");
    if (!HasAccess(asset)) return ServiceError.Forbidden("Access denied");
    return new AssetDto(asset);  // implicit conversion to ServiceResult<T>
}
```

### Error Factories — Use These, Don't Create Raw ServiceError

| Factory | HTTP | Code | When |
|---------|------|------|------|
| `ServiceError.NotFound(msg)` | 404 | `NOT_FOUND` | Entity not found by ID |
| `ServiceError.Forbidden(msg)` | 403 | `FORBIDDEN` | User lacks permission |
| `ServiceError.BadRequest(msg)` | 400 | `BAD_REQUEST` | Invalid input |
| `ServiceError.Conflict(msg)` | 409 | `CONFLICT` | Duplicate or state conflict |
| `ServiceError.Validation(msg, details)` | 400 | `VALIDATION_ERROR` | Field-level errors |
| `ServiceError.Server(msg)` | 500 | `SERVER_ERROR` | Unexpected infrastructure failure |

### What NOT to Do

```csharp
// BAD: Throwing exceptions for business logic
throw new NotFoundException("Asset not found");

// BAD: Returning HTTP types from services
return Results.NotFound();

// BAD: Creating raw ServiceError without factory
return new ServiceError(404, "CUSTOM_CODE", "msg");

// BAD: Returning null instead of ServiceError
return default;
```

## Endpoint Layer — ToHttpResult()

Endpoints call `.ToHttpResult()` and nothing else:

```csharp
// Standard: 200 with value, or 204 for void
return (await svc.DeleteAsync(id, ct)).ToHttpResult();

// Custom success: 201 Created
return (await svc.CreateAsync(dto, ct))
    .ToHttpResult(value => Results.Created($"/api/v1/items/{value.Id}", value));
```

Never manually inspect `IsSuccess` or `Error` in endpoints. The extension handles all mapping including `403 → Results.Forbid()` and error body as `ApiError { Code, Message, Details }`.

## Exception Handling (Infrastructure Layer Only)

Exceptions are acceptable only for truly unexpected infrastructure failures:
- Unhandled exceptions are caught by global middleware → `500 + ApiError`.
- `StorageException` → `503`, `BadHttpRequestException` → `400`, `UnauthorizedAccessException` → `401`.
- When catching infrastructure exceptions in a service, wrap them in `ServiceError.Server()`.
