namespace Daily_Bread.Components.Shared;

/// <summary>
/// Semantic color options for stat card text values.
/// </summary>
public enum StatColor
{
    /// <summary>Uses default --nord8 color from CSS.</summary>
    Default,
    /// <summary>Green (--nord14) for positive values, earnings, completed.</summary>
    Success,
    /// <summary>Red (--nord11) for negative values, missed, errors.</summary>
    Danger,
    /// <summary>Yellow/Orange (--nord13) for warnings, pending, streaks.</summary>
    Warning,
    /// <summary>Blue (--nord8) for informational, achievements.</summary>
    Info,
    /// <summary>Primary blue (--nord10) for primary actions.</summary>
    Primary,
    /// <summary>Muted gray (--nord4) for secondary/disabled states.</summary>
    Muted
}

/// <summary>
/// Semantic background color options for stat cards.
/// </summary>
public enum StatBackground
{
    /// <summary>No background color applied.</summary>
    Default,
    /// <summary>Subtle green background for success/positive values.</summary>
    SuccessSubtle,
    /// <summary>Subtle red background for danger/negative values.</summary>
    DangerSubtle,
    /// <summary>Subtle yellow/orange background for warnings.</summary>
    WarningSubtle,
    /// <summary>Subtle blue background for informational.</summary>
    InfoSubtle,
    /// <summary>Subtle primary blue background.</summary>
    PrimarySubtle,
    /// <summary>Subtle gray background for secondary/neutral.</summary>
    SecondarySubtle
}
