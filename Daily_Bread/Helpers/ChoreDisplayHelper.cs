using Daily_Bread.Data.Models;

namespace Daily_Bread.Helpers;

/// <summary>
/// Helper methods for displaying chore status in UI components.
/// </summary>
public static class ChoreDisplayHelper
{
    /// <summary>
    /// Gets the CSS class for a table row based on chore status.
    /// </summary>
    public static string GetRowClass(ChoreStatus status) => status switch
    {
        ChoreStatus.Approved => "table-success",
        ChoreStatus.Completed => "table-info",
        ChoreStatus.Missed => "table-danger",
        ChoreStatus.Skipped => "table-secondary",
        ChoreStatus.Help => "table-warning",
        _ => ""
    };

    /// <summary>
    /// Gets the CSS class for a mobile card based on chore status.
    /// Aligned with SwipeableChoreCard state classes for unified design system.
    /// </summary>
    public static string GetCardClass(ChoreStatus status) => status switch
    {
        ChoreStatus.Approved => "completed",
        ChoreStatus.Completed => "pending-approval",
        ChoreStatus.Missed => "missed",
        ChoreStatus.Skipped => "skipped",
        ChoreStatus.Help => "help-requested",
        _ => ""
    };

    /// <summary>
    /// Gets the CSS class for a checkbox/indicator based on chore status.
    /// Aligned with unified design system indicator states.
    /// </summary>
    public static string GetCheckboxClass(ChoreStatus status, bool isCompleted)
    {
        return status switch
        {
            ChoreStatus.Approved => "indicator-done",
            ChoreStatus.Completed => "indicator-awaiting",
            ChoreStatus.Missed => "indicator-missed",
            ChoreStatus.Skipped => "indicator-skipped",
            ChoreStatus.Help => "indicator-help",
            _ when isCompleted => "indicator-awaiting",
            _ => "indicator-pending"
        };
    }

    /// <summary>
    /// Gets the Bootstrap badge class for a status.
    /// </summary>
    public static string GetStatusBadgeClass(ChoreStatus status) => status switch
    {
        ChoreStatus.Pending => "badge-pending",
        ChoreStatus.Completed => "badge-awaiting",
        ChoreStatus.Approved => "badge-approved",
        ChoreStatus.Missed => "badge-missed",
        ChoreStatus.Skipped => "badge-excused",
        ChoreStatus.Help => "badge-help",
        _ => "badge-default"
    };

    /// <summary>
    /// Gets the display text for a chore status.
    /// Clear, action-oriented language that tells parents exactly what's happening.
    /// </summary>
    public static string GetStatusDisplay(ChoreStatus status) => status switch
    {
        ChoreStatus.Pending => "Not Started",
        ChoreStatus.Completed => "Awaiting Review",
        ChoreStatus.Approved => "Approved",
        ChoreStatus.Missed => "Not Completed",
        ChoreStatus.Skipped => "Excused",
        ChoreStatus.Help => "Needs Help",
        _ => status.ToString()
    };

    /// <summary>
    /// Gets a short badge label for status (used in compact views).
    /// </summary>
    public static string GetStatusBadgeText(ChoreStatus status) => status switch
    {
        ChoreStatus.Pending => "Pending",
        ChoreStatus.Completed => "Review",
        ChoreStatus.Approved => "Done",
        ChoreStatus.Missed => "Missed",
        ChoreStatus.Skipped => "Excused",
        ChoreStatus.Help => "Help",
        _ => status.ToString()
    };

    /// <summary>
    /// Gets the icon for a status indicator.
    /// Uses clear, meaningful icons instead of confusing circles.
    /// </summary>
    public static string GetStatusIcon(ChoreStatus status) => status switch
    {
        ChoreStatus.Pending => "bi-circle",
        ChoreStatus.Completed => "bi-clock-history",  // Clock = awaiting review
        ChoreStatus.Approved => "bi-check2-all",      // Double check = fully approved
        ChoreStatus.Missed => "bi-x-circle",
        ChoreStatus.Skipped => "bi-dash-circle",
        ChoreStatus.Help => "bi-question-circle-fill",
        _ => "bi-circle"
    };

    /// <summary>
    /// Formats a timestamp as a relative time string (e.g., "5m ago", "2h ago").
    /// </summary>
    public static string GetTimeAgo(DateTime timestamp)
    {
        var span = DateTime.UtcNow - timestamp;

        if (span.TotalMinutes < 1)
            return "just now";
        if (span.TotalMinutes < 60)
            return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24)
            return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7)
            return $"{(int)span.TotalDays}d ago";

        return timestamp.ToString("MMM d");
    }
}
