using System.Globalization;
using System.Resources;

namespace AssetHub.Application.Resources;

/// <summary>
/// Provides localized validation error messages for DataAnnotation attributes.
/// Backed by ValidationResource.resx / ValidationResource.sv.resx.
/// </summary>
public static class ValidationResource
{
    private static readonly ResourceManager _resourceManager =
        new("AssetHub.Application.Resources.ValidationResource",
            typeof(ValidationResource).Assembly);

    // User / Admin
    public static string Username_Length => GetString(nameof(Username_Length));
    public static string Username_Format => GetString(nameof(Username_Format));
    public static string Email_Invalid => GetString(nameof(Email_Invalid));
    public static string FirstName_Required => GetString(nameof(FirstName_Required));
    public static string LastName_Required => GetString(nameof(LastName_Required));
    public static string Role_Invalid => GetString(nameof(Role_Invalid));

    // Share
    public static string ScopeType_Invalid => GetString(nameof(ScopeType_Invalid));
    public static string SharePassword_Length => GetString(nameof(SharePassword_Length));
    public static string ShareEmails_MaxCount => GetString(nameof(ShareEmails_MaxCount));

    // Collection / ACL
    public static string PrincipalType_MustBeUser => GetString(nameof(PrincipalType_MustBeUser));
    public static string CollectionRole_Invalid => GetString(nameof(CollectionRole_Invalid));

    // Asset
    public static string Tags_MaxCount => GetString(nameof(Tags_MaxCount));

    private static string GetString(string name) =>
        _resourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
