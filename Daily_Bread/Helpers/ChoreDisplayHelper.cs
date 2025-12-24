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
        _ => ""
    };

    /// <summary>
    /// Gets the CSS class for a mobile card based on chore status.
    /// </summary>
    public static string GetCardClass(ChoreStatus status) => status switch
    {
        ChoreStatus.Approved => "approved",
        ChoreStatus.Completed => "completed",
        ChoreStatus.Missed => "missed",
        ChoreStatus.Skipped => "skipped",
        _ => ""
    };

    /// <summary>
    /// Gets the CSS class for a checkbox based on chore status.
    /// </summary>
    public static string GetCheckboxClass(ChoreStatus status, bool isCompleted)
    {
        if (status == ChoreStatus.Approved) return "checked approved";
        if (isCompleted) return "checked pending";
        if (status == ChoreStatus.Missed) return "missed";
        if (status == ChoreStatus.Skipped) return "skipped";
        return "";
    }

    /// <summary>
    /// Gets the Bootstrap badge class for a status.
    /// </summary>
    public static string GetStatusBadgeClass(ChoreStatus status) => status switch
    {
        ChoreStatus.Pending => "bg-warning text-dark",
        ChoreStatus.Completed => "bg-info",
        ChoreStatus.Approved => "bg-success",
        ChoreStatus.Missed => "bg-danger",
        ChoreStatus.Skipped => "bg-secondary",
        _ => "bg-secondary"
    };

    /// <summary>
    /// Gets the display text for a chore status.
    /// </summary>
    public static string GetStatusDisplay(ChoreStatus status) => status switch
    {
        ChoreStatus.Pending => "Pending",
        ChoreStatus.Completed => "Completed",
        ChoreStatus.Approved => "Approved",
        ChoreStatus.Missed => "Missed",
        ChoreStatus.Skipped => "Skipped",
        _ => status.ToString()
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
