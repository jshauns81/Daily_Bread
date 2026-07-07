namespace Daily_Bread.Data.Models;

/// <summary>
/// Represents a child's profile in the system.
/// Links an Identity user to their ledger accounts and chore assignments.
/// </summary>
public class ChildProfile
{
    public int Id { get; set; }

    /// <summary>
    /// The Identity user this profile belongs to.
    /// </summary>
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Display name for the child (can differ from username).
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Whether this profile is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Timestamp when the profile was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the profile was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// Total supervised-driving hours goal (e.g. state permit requirement).
    /// Null = driving log goal not configured for this child (hides the progress bar).
    /// </summary>
    public decimal? DrivingGoalTotalHours { get; set; }

    /// <summary>
    /// Night-driving hours sub-goal (subset of DrivingGoalTotalHours). Null = not configured.
    /// </summary>
    public decimal? DrivingGoalNightHours { get; set; }

    // Navigation properties
    public ICollection<LedgerAccount> LedgerAccounts { get; set; } = [];
}
