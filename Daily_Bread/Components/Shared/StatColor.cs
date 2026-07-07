namespace Daily_Bread.Components.Shared;

/// <summary>
/// Semantic color options for stat card text values.
/// </summary>
public enum StatColor
{
    /// <summary>Uses default --db-accent color from CSS.</summary>
    Default,
    /// <summary>Success (--ds-semantic-success) for positive values, earnings, completed.</summary>
    Success,
    /// <summary>Risk (--db-risk) for negative values, missed, errors.</summary>
    Danger,
    /// <summary>Highlight (--ds-semantic-highlight) for warnings, pending, streaks.</summary>
    Warning,
    /// <summary>Accent (--db-accent) for informational, achievements.</summary>
    Info,
    /// <summary>Primary accent (--ds-accent-primary) for primary actions.</summary>
    Primary,
    /// <summary>Muted (--ds-text-muted) for secondary/disabled states.</summary>
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
