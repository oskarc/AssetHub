using AssetHub.Application.Dtos;

namespace AssetHub.Application.Services;

/// <summary>
/// Resolves the <see cref="BrandResponseDto"/> to apply to a given share
/// context. Resolution order:
/// <list type="number">
/// <item>Collection-scope share → that collection's <c>BrandId</c>.</item>
/// <item>Asset-scope share → first containing collection with a <c>BrandId</c>.</item>
/// <item>Fall back to the default brand (<c>IsDefault = true</c>).</item>
/// <item>Return null — share page falls back to the unbranded theme.</item>
/// </list>
/// </summary>
public interface IBrandResolver
{
    /// <summary><paramref name="scopeType"/> is one of <see cref="Constants.ScopeTypes"/> ("asset" / "collection").</summary>
    Task<BrandResponseDto?> ResolveForShareAsync(
        string scopeType, Guid scopeId, CancellationToken ct);
}
