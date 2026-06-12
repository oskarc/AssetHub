namespace AssetHub.Domain.Entities;

/// <summary>
/// Notification cadence for saved searches.
/// </summary>
public enum SavedSearchNotifyCadence
{
    None,
    OnNewMatch,
    Daily,
    Weekly
}

/// <summary>
/// Enum ↔ lowercase-db-string conversion for the saved search enums.
/// </summary>
public static class SavedSearchEnumExtensions
{
    public static string ToDbString(this SavedSearchNotifyCadence cadence) => cadence switch
    {
        SavedSearchNotifyCadence.None => "none",
        SavedSearchNotifyCadence.OnNewMatch => "on_new_match",
        SavedSearchNotifyCadence.Daily => "daily",
        SavedSearchNotifyCadence.Weekly => "weekly",
        _ => throw new ArgumentOutOfRangeException(nameof(cadence))
    };

    public static SavedSearchNotifyCadence ToSavedSearchNotifyCadence(this string value) => value switch
    {
        "none" => SavedSearchNotifyCadence.None,
        "on_new_match" => SavedSearchNotifyCadence.OnNewMatch,
        "daily" => SavedSearchNotifyCadence.Daily,
        "weekly" => SavedSearchNotifyCadence.Weekly,
        _ => throw new ArgumentOutOfRangeException(nameof(value), $"Unknown saved search cadence: {value}")
    };

    public static bool IsValidSavedSearchNotifyCadence(string value)
        => value is "none" or "on_new_match" or "daily" or "weekly";
}
