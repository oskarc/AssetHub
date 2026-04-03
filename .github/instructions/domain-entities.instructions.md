---
applyTo: "src/AssetHub.Domain/**"
description: "Use when creating or editing domain entities, enums, or value objects in the AssetHub.Domain project."
---
# Domain Entity Conventions (AssetHub.Domain)

AssetHub.Domain has **zero project references** — only entities, enums, and extension methods. Never add NuGet packages or project references here.

## Entity Structure

No base entity class — each entity is standalone:

```csharp
public class Example
{
    public Guid Id { get; set; }

    // Business properties
    public string Title { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, object> MetadataJson { get; set; } = new();

    // Audit fields
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }  // on mutable entities
}
```

### Rules
- **No base classes or interfaces** on entities.
- **No parameterless constructors** — use property initializers for defaults.
- **Audit fields**: `CreatedAt` (UTC) is present on all entities. A creator user field is present but the naming varies by context:
  - `CreatedByUserId` — standard for most entities (Asset, Collection, Share)
  - `AddedByUserId` — on join tables (AssetCollection)
  - `ActorUserId` — on audit records (AuditEvent, nullable for system events)
  - `RequestedByUserId` — on request entities (ZipDownload, nullable for anonymous)
- **`UpdatedAt`** exists on `Asset` — other mutable entities (Collection, Share, ZipDownload) currently lack it. New entities with mutable state should include `UpdatedAt`.
- **No soft delete** — use status-based lifecycle (`AssetStatus.Failed`, `.Uploading`) or hard delete.
- **JSONB fields**: `List<string>` for tags, `Dictionary<string, object>` for metadata. Initialize with `new()`.
- **Navigation properties**: `ICollection<T>` with `new List<T>()` default.

## State Transition Methods

The `Asset` entity owns its state transitions — never set `Status` directly from services:

```csharp
public void MarkReady(string? thumbKey = null)
{
    Status = ExampleStatus.Ready;
    UpdatedAt = DateTime.UtcNow;
    if (thumbKey != null) ThumbObjectKey = thumbKey;
}

public void MarkFailed(string errorMessage)
{
    Status = ExampleStatus.Failed;
    UpdatedAt = DateTime.UtcNow;
    MetadataJson["error"] = errorMessage;
}
```

### Rules
- State transitions update `UpdatedAt` internally.
- Error context goes into `MetadataJson` — not a separate error column.
- Validation methods return `bool` (e.g., `IsValidContentType()`).
- Not all entities have state methods — only `Asset` currently does. Other entities (Share, ZipDownload) have status fields set directly by services.

## Enums

Stored as strings via extension methods defined alongside the enum:

```csharp
public enum ExampleStatus { Processing, Ready, Failed, Uploading }

public static class ExampleStatusExtensions
{
    public static string ToDbString(this ExampleStatus s) => s switch
    {
        ExampleStatus.Processing => "processing",
        // ...
    };

    public static ExampleStatus ToExampleStatus(this string s) => s switch
    {
        "processing" => ExampleStatus.Processing,
        // ...
    };
}
```

Never use `int` or `ToString()` for database storage — always explicit string mapping.
