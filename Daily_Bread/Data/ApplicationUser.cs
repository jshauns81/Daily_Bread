using Microsoft.AspNetCore.Identity;
using System;

namespace Daily_Bread.Data;

/// <summary>
/// Custom application user extending IdentityUser.
/// Each user belongs to a household for data isolation.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// The household this user belongs to.
    /// Null for admin-only accounts.
    /// </summary>
    public Guid? HouseholdId { get; set; }
    
    /// <summary>
    /// Navigation property to the household.
    /// </summary>
    public Data.Models.Household? Household { get; set; }
}
