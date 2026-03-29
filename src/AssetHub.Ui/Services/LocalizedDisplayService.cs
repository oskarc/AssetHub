using AssetHub.Ui.Resources;
using Microsoft.Extensions.Localization;

namespace AssetHub.Ui.Services;

/// <summary>
/// Provides localized display strings for roles, asset types, content types, and scope types.
/// Inject this instead of passing IStringLocalizer to static AssetDisplayHelpers methods.
/// </summary>
public sealed class LocalizedDisplayService(IStringLocalizer<CommonResource> loc)
{
    public string Role(string? role) => AssetDisplayHelpers.GetLocalizedRole(role, loc);

    public string AssetType(string? assetType) => AssetDisplayHelpers.GetLocalizedAssetType(assetType, loc);

    public string ContentType(string? contentType) => AssetDisplayHelpers.GetLocalizedContentType(contentType, loc);

    public string ScopeType(string? scopeType) => AssetDisplayHelpers.GetLocalizedScopeType(scopeType, loc);
}
