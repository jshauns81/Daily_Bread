namespace Daily_Bread.Components.Shared.Navigation;

/// <summary>
/// Chrome-level mapping of nav routes to Lucide sprite icon names (design #14).
/// Presentation concern only — NavigationService stays the source of truth for
/// routes/labels/roles; this just picks the mono stroke glyph for each route.
/// </summary>
public static class NavIcons
{
    private static readonly Dictionary<string, string> ByRoute = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/"] = "home",
        ["/tracker"] = "list-checks",
        ["/calendar"] = "calendar",
        ["/my-balance"] = "circle-dollar-sign",
        ["/achievements"] = "trophy",
        ["/chore-planner"] = "layout-grid",
        ["/ledger"] = "credit-card",
        ["/manage-achievements"] = "trophy",
        ["/reward-claims"] = "gift",
        ["/driving-log"] = "car",
        ["/driving-log/approvals"] = "check",
        ["/settings"] = "settings",
        ["/admin/users"] = "users",
        ["/appearance"] = "palette",
    };

    /// <summary>Lucide icon name for a nav route (falls back to "home").</summary>
    public static string ForRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)) return "home";
        var key = "/" + route.Trim().Trim('/');
        return ByRoute.TryGetValue(key, out var icon) ? icon : "home";
    }
}
