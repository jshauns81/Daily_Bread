using System.Text;

namespace Daily_Bread.Services;

/// <summary>
/// Builds a DMV-proof-shaped CSV export of driving log entries. Hand-rolled RFC4180
/// quoting since this is one flat table - no need for a CSV NuGet dependency.
/// </summary>
public static class DrivingLogCsvBuilder
{
    public static string Build(IEnumerable<DrivingLogEntryDisplay> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Date,Start Time,End Time,Duration (hrs),Day/Night,Supervisor,Weather,Route/Notes,Status,Approved By,Approved At");

        foreach (var e in entries)
        {
            var fields = new[]
            {
                e.Date.ToString("yyyy-MM-dd"),
                e.StartTime.ToString("h:mm tt"),
                e.EndTime.ToString("h:mm tt"),
                (e.DurationMinutes / 60m).ToString("0.##"),
                e.IsNightDriving ? "Night" : "Day",
                e.SupervisorLabel,
                e.Weather.ToString(),
                e.RouteNotes ?? "",
                e.Status.ToString(),
                e.DecidedByLabel ?? "",
                e.DecidedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? ""
            };

            sb.AppendLine(string.Join(",", fields.Select(Escape)));
        }

        return sb.ToString();
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
