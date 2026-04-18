---
applyTo: "src/AssetHub.Infrastructure/**, src/AssetHub.Api/**"
description: "Use when implementing authorization checks, role-based access control, user identity access, or permission logic in AssetHub."
---
# Security & Authorization Conventions

## Reference files — read before editing
- `src/AssetHub.Application/CurrentUser.cs` — scoped identity accessor
- `src/AssetHub.Application/RoleHierarchy.cs` — role level predicates (`CanUpload`, `CanDelete`, `HasSufficientLevel`)
- `src/AssetHub.Infrastructure/Services/CollectionAuthorizationService.cs` — per-collection RBAC checks
- `src/AssetHub.Api/Extensions/AuthenticationExtensions.cs` — auth scheme and policy configuration

## CurrentUser — The Only Way to Access Identity

Inject `CurrentUser` (scoped service) — never access `HttpContext.User` or `ClaimsPrincipal` directly in services:

```csharp
public sealed class ExampleService(
    CurrentUser currentUser,
    // other deps
) : IExampleService
{
    public async Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return ServiceError.Forbidden("Not authenticated");

        if (!currentUser.IsSystemAdmin)
            return ServiceError.Forbidden("Admin required");
        // ...
    }
}
```

- `CurrentUser.Anonymous` is used in background jobs where no HTTP context exists.
- Always check `IsAuthenticated` before trusting `UserId`.

## Role Hierarchy — Use RoleHierarchy, Don't Hardcode Levels

Roles are hierarchical: `viewer (1) < contributor (2) < manager (3) < admin (4)`.

```csharp
// GOOD: Use predicate methods
if (!RoleHierarchy.CanUpload(userRole)) return ServiceError.Forbidden("...");
if (!RoleHierarchy.CanDelete(userRole)) return ServiceError.Forbidden("...");

// GOOD: Prevent privilege escalation
if (!RoleHierarchy.HasSufficientLevel(callerRole, targetRole))
    return ServiceError.Forbidden("Cannot assign role higher than your own");

// BAD: Hardcoded role checks
if (role != "admin") return ServiceError.Forbidden("...");
if (roleLevel < 3) return ServiceError.Forbidden("...");
```

## Collection-Scoped RBAC

Per-collection permissions use `CollectionAcl` entities. Authorization checks go through `CollectionAuthorizationService`:

```csharp
var role = await _authService.GetUserRoleAsync(collectionId, currentUser.UserId, ct);
if (!RoleHierarchy.CanEdit(role))
    return ServiceError.Forbidden("Insufficient collection permissions");
```

### Rules
- **System admins bypass** collection ACL checks (`currentUser.IsSystemAdmin`).
- **Check collection access before entity access** — verify the user can reach the collection, then the asset within it.
- **Batch preloading**: Use `PreloadUserRolesAsync()` when checking multiple collections (avoids N+1).

## Endpoint Authorization

Use policy-based authorization on endpoint groups:

```csharp
var group = app.MapGroup("/api/v1/admin")
    .RequireAuthorization("RequireAdmin");
```

Available policies: `RequireViewer`, `RequireContributor`, `RequireManager`, `RequireAdmin`.

## What NOT to Do

- **Never cache ACL/roles globally** — authorization uses request-scoped dictionaries to avoid stale permissions.
- **Never skip role level checks** on mutations that assign roles (prevents privilege escalation).
- **Never trust client-supplied role values** without `HasSufficientLevel()` validation.
- **Never expose user IDs** from other users without authorization checks.
