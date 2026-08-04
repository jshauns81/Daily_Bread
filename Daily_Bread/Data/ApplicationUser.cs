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

    /// <summary>
    /// What the family calls this person ("Emma", "Mom") — usernames are
    /// credentials, not identity, and should never be what the UI shows.
    /// Canonical for every role; children's ChildProfile.DisplayName predates
    /// this and is backfilled into it. Null falls back to UserName on clients.
    /// </summary>
    public string? DisplayName { get; set; }
}
