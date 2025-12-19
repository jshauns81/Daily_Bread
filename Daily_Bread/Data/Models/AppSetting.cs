namespace Daily_Bread.Data.Models;

/// <summary>
/// Application-wide settings stored in the database.
/// Key-value pairs for configurable behaviors.
/// </summary>
public class AppSetting
{
    public int Id { get; set; }

    /// <summary>
    /// Unique key for the setting.
    /// </summary>
    public required string Key { get; set; }

    /// <summary>
    /// Value stored as string (deserialize as needed).
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// Description of what this setting controls.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Data type hint for UI and deserialization.
    /// </summary>
    public SettingDataType DataType { get; set; } = SettingDataType.String;

    /// <summary>
    /// Timestamp when the setting was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
}

/// <summary>
/// Data type hints for app settings.
/// </summary>
public enum SettingDataType
{
    String = 0,
    Integer = 1,
    Decimal = 2,
    Boolean = 3,
    DateTime = 4,
    Json = 5
}

/// <summary>
/// Well-known setting keys used throughout the application.
/// </summary>
public static class AppSettingKeys
{
    /// <summary>
    /// Whether children can mark chores as completed (vs parent-only).
    /// </summary>
    public const string AllowChildSelfReport = "AllowChildSelfReport";

    /// <summary>
    /// Whether to auto-mark chores as missed at end of day.
    /// </summary>
    public const string AutoMarkMissedChores = "AutoMarkMissedChores";

    /// <summary>
    /// Time of day to run the auto-mark missed process (HH:mm format).
    /// </summary>
    public const string MissedChoresCutoffTime = "MissedChoresCutoffTime";

    /// <summary>
    /// Default currency symbol for display.
    /// </summary>
    public const string CurrencySymbol = "CurrencySymbol";

    /// <summary>
    /// Minimum balance required before cash out is allowed.
    /// </summary>
    public const string CashOutThreshold = "CashOutThreshold";

    /// <summary>
    /// Default cash out threshold value.
    /// </summary>
    public const decimal DefaultCashOutThreshold = 10.00m;

    /// <summary>
    /// The family's timezone (IANA timezone ID, e.g., "America/New_York").
    /// Used for determining "today" and scheduling.
    /// </summary>
    public const string TimeZone = "TimeZone";

    /// <summary>
    /// Default timezone (US Eastern).
    /// </summary>
    public const string DefaultTimeZone = "America/New_York";
}
