using Daily_Bread.Components.Shared.Navigation;

namespace Daily_Bread.Services;

/// <summary>
/// Service for retrieving navigation items based on user roles.
/// This is the single source of truth for all navigation configuration.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Gets all navigation items visible to the user based on their roles.
    /// </summary>
    IReadOnlyList<NavItem> GetNavItems(bool isParent, bool isChild, bool isAdmin);
    
    /// <summary>
    /// Gets navigation items for mobile bottom nav (max 5 items, ordered by MobileOrder).
    /// </summary>
    IReadOnlyList<NavItem> GetMobileNavItems(bool isParent, bool isChild, bool isAdmin);
    
    /// <summary>
    /// Gets navigation items for desktop sidebar.
    /// </summary>
    IReadOnlyList<NavItem> GetDesktopNavItems(bool isParent, bool isChild, bool isAdmin);
    
    /// <summary>
    /// Gets overflow items that don't fit in mobile bottom nav.
    /// </summary>
    IReadOnlyList<NavItem> GetMobileOverflowItems(bool isParent, bool isChild, bool isAdmin);
}

/// <summary>
/// Implementation of navigation service with all nav items defined in one place.
/// </summary>
public class NavigationService : INavigationService
{
    /// <summary>
    /// Master list of all navigation items in the application.
    /// This is the SINGLE SOURCE OF TRUTH for navigation.
    /// 
    /// Order matters for desktop sidebar display.
    /// MobileOrder controls order in mobile bottom nav (1-5 appear, 6+ are overflow/hidden).
    /// 
    /// NOTE: Mobile nav shows up to 5 items. Items are filtered by role first, then ordered.
    /// - Child sees: Home(1), Balance(2), Achievements(3), Goals(4) = 4 items
    /// - Parent sees: Home(1), Planner(2), Treasury(3), Tracker(4) = 4 items
    /// </summary>
    private static readonly List<NavItem> AllItems =
    [
        // ============================================
        // CORE NAVIGATION (visible to most users)
        // ============================================
        
        // Home - visible to all, position 1 in mobile for everyone
        new("/", "bi-house-door-fill", "Home", NavVisibility.All, MobileOrder: 1),
        
        // Book of Deeds / Tracker - visible to parents and children
        // Position 4 for parents on mobile, hidden for children (they use swipe cards on home)
        new("/tracker", "bi-check2-square", "Book of Deeds", NavVisibility.Parent | NavVisibility.Child, MobileOrder: 4),
        
        // Calendar - visible to parents and children, in overflow menu
        new("/calendar", "bi-calendar3", "Calendar", NavVisibility.Parent | NavVisibility.Child, MobileOrder: 6, ShowInMobile: false),
        
        // ============================================
        // CHILD-ONLY NAVIGATION
        // ============================================
        
        // My Balance - child-only, position 2 in mobile
        new("/my-balance", "bi-wallet2", "My Balance", NavVisibility.Child, MobileOrder: 2),
        
        // Achievements - child-only, position 3 in mobile
        new("/achievements", "bi-trophy-fill", "Achievements", NavVisibility.Child, MobileOrder: 3),
        
        // Goals - child-only, position 4 in mobile
        new("/goals", "bi-bullseye", "Goals", NavVisibility.Child, MobileOrder: 4),
        
        // ============================================
        // PARENT MANAGEMENT (Section: "Management")
        // ============================================
        
        // Labor Planner - parent-only, position 2 in mobile
        new("/chore-planner", "bi-calendar-check", "Labor Planner", NavVisibility.Parent, MobileOrder: 2, Section: "Management"),
        
        // Treasury / Ledger - parent-only, position 3 in mobile
        new("/ledger", "bi-journal-text", "Treasury", NavVisibility.Parent, MobileOrder: 3, Section: "Management"),
        
        // Settings - parent-only, in overflow (position 5)
        new("/settings", "bi-gear-fill", "Settings", NavVisibility.Parent, MobileOrder: 5, Section: "Management"),
        
        // ============================================
        // ADMIN NAVIGATION (Section: "Admin")
        // ============================================
        
        // Users - admin-only, not in mobile nav
        new("/admin/users", "bi-people-fill", "Users", NavVisibility.Admin, MobileOrder: 99, ShowInMobile: false, Section: "Admin"),
    ];

    public IReadOnlyList<NavItem> GetNavItems(bool isParent, bool isChild, bool isAdmin)
    {
        var visibility = GetVisibilityFlags(isParent, isChild, isAdmin);
        
        return AllItems
            .Where(item => HasVisibility(item.Visibility, visibility))
            .ToList();
    }

    public IReadOnlyList<NavItem> GetDesktopNavItems(bool isParent, bool isChild, bool isAdmin)
    {
        var visibility = GetVisibilityFlags(isParent, isChild, isAdmin);
        
        return AllItems
            .Where(item => item.ShowInDesktop && HasVisibility(item.Visibility, visibility))
            .ToList();
    }

    public IReadOnlyList<NavItem> GetMobileNavItems(bool isParent, bool isChild, bool isAdmin)
    {
        var visibility = GetVisibilityFlags(isParent, isChild, isAdmin);
        
        return AllItems
            .Where(item => item.ShowInMobile && item.MobileOrder <= 5 && HasVisibility(item.Visibility, visibility))
            .OrderBy(item => item.MobileOrder)
            .Take(5)
            .ToList();
    }

    public IReadOnlyList<NavItem> GetMobileOverflowItems(bool isParent, bool isChild, bool isAdmin)
    {
        var visibility = GetVisibilityFlags(isParent, isChild, isAdmin);
        
        return AllItems
            .Where(item => item.ShowInMobile && item.MobileOrder > 5 && HasVisibility(item.Visibility, visibility))
            .OrderBy(item => item.MobileOrder)
            .ToList();
    }

    /// <summary>
    /// Converts role booleans to NavVisibility flags.
    /// </summary>
    private static NavVisibility GetVisibilityFlags(bool isParent, bool isChild, bool isAdmin)
    {
        var visibility = NavVisibility.None;
        
        if (isParent) visibility |= NavVisibility.Parent;
        if (isChild) visibility |= NavVisibility.Child;
        if (isAdmin) visibility |= NavVisibility.Admin;
        
        return visibility;
    }

    /// <summary>
    /// Checks if an item's visibility includes any of the user's roles.
    /// </summary>
    private static bool HasVisibility(NavVisibility itemVisibility, NavVisibility userVisibility)
    {
        return (itemVisibility & userVisibility) != NavVisibility.None;
    }
}
