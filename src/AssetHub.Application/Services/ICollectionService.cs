using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Collection commands: create, update, delete, and download.
/// Authorization and auditing belong here — not in endpoints.
/// </summary>
public interface ICollectionService
{
    /// <summary>Create a new collection.</summary>
    Task<ServiceResult<CollectionResponseDto>> CreateAsync(CreateCollectionDto dto, CancellationToken ct);

    /// <summary>Update collection name/description.</summary>
    Task<ServiceResult<MessageResponse>> UpdateAsync(Guid id, UpdateCollectionDto dto, CancellationToken ct);

    /// <summary>Delete a collection and handle its assets (orphan cleanup).</summary>
    Task<ServiceResult> DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>Enqueue a background ZIP build for all assets in a collection.</summary>
    Task<ServiceResult<ZipDownloadEnqueuedResponse>> DownloadAllAssetsAsync(Guid id, CancellationToken ct);

    // ── Nested collections (T5-NEST-01) ──────────────────────────────────

    /// <summary>
    /// Reparent a collection. Pass <paramref name="parentId"/> = <c>null</c> to
    /// move the collection to root level. Validates cycle prevention and depth
    /// limit; admin-only at the service layer; emits <c>collection.reparented</c>.
    /// </summary>
    Task<ServiceResult> SetParentAsync(Guid id, Guid? parentId, CancellationToken ct);

    /// <summary>
    /// Toggle whether this collection inherits its parent's ACL at runtime.
    /// Requires the collection to have a parent when enabling. Bust descendant
    /// cache tags so any inheriting subtree refreshes its effective-role lookups.
    /// Admin-only; emits <c>collection.inheritance_enabled</c> or
    /// <c>collection.inheritance_disabled</c>.
    /// </summary>
    Task<ServiceResult> SetInheritParentAclAsync(Guid id, bool inherit, CancellationToken ct);

    /// <summary>
    /// One-shot copy: add the parent's <c>CollectionAcl</c> rows that aren't
    /// already on this collection. Existing entries are kept untouched. Does
    /// NOT enable inheritance — copying is a snapshot, inheritance is a live
    /// link, the two are deliberate alternatives. Admin-only; emits
    /// <c>collection.acl_copied_from_parent</c>.
    /// </summary>
    Task<ServiceResult<int>> CopyParentAclAsync(Guid id, CancellationToken ct);
}
