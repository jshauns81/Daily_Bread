namespace Daily_Bread.Components.Shared.Navigation;

/// <summary>
/// Represents a navigation item in the app.
/// This is the single source of truth for all navigation across desktop sidebar and mobile bottom nav.
/// </summary>
/// <param name="Route">The route/href for the navigation link</param>
/// <param name="Icon">Bootstrap icon class (e.g., "bi-house-fill")</param>
/// <param name="Label">Display label for the nav item</param>
/// <param name="Visibility">Which user roles can see this item</param>
/// <param name="MobileOrder">Order in mobile bottom nav (1-5 appear in nav, higher values go to overflow/hidden)</param>
/// <param name="ShowInMobile">Whether to show in mobile bottom nav</param>
/// <param name="ShowInDesktop">Whether to show in desktop sidebar</param>
/// <param name="Section">Optional section label for grouping in sidebar (e.g., "Management", "Admin")</param>
public record NavItem(
    string Route,
    string Icon,
    string Label,
    NavVisibility Visibility = NavVisibility.All,
    int MobileOrder = 99,
    bool ShowInMobile = true,
    bool ShowInDesktop = true,
    string? Section = null
);

/// <summary>
/// Flags indicating which user roles can see a navigation item.
/// Can be combined with bitwise OR for items visible to multiple roles.
/// </summary>
[Flags]
public enum NavVisibility
{
    /// <summary>Item is hidden from all users.</summary>
    None = 0,
    
    /// <summary>Item is visible to Parent users.</summary>
    Parent = 1,
    
    /// <summary>Item is visible to Child users.</summary>
    Child = 2,
    
    /// <summary>Item is visible to Admin users.</summary>
    Admin = 4,
    
    /// <summary>Item is visible to all authenticated users.</summary>
    All = Parent | Child | Admin
}
