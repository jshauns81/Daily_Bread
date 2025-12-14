namespace Daily_Bread.Data.Models;

/// <summary>
/// User-specific preferences stored in the database.
/// Each user can have their own settings.
/// </summary>
public class UserPreference
{
    public int Id { get; set; }

    /// <summary>
    /// The user this preference belongs to.
    /// </summary>
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Unique key for the preference.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Value stored as string (deserialize as needed).
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// Timestamp when the preference was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
}

/// <summary>
/// Well-known user preference keys.
/// </summary>
public static class UserPreferenceKeys
{
    /// <summary>
    /// User's preferred theme (light/dark/system).
    /// </summary>
    public const string Theme = "Theme";

    /// <summary>
    /// Whether to show completed chores in the list.
    /// </summary>
    public const string ShowCompletedChores = "ShowCompletedChores";

    /// <summary>
    /// User's timezone for date calculations.
    /// </summary>
    public const string Timezone = "Timezone";

    /// <summary>
    /// Whether to receive notifications.
    /// </summary>
    public const string NotificationsEnabled = "NotificationsEnabled";
}
