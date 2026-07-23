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
    /// The child's birthdate, if known. Drives age-appropriate voice in the app
    /// (see AgeTiers). Nullable — an unset birthdate reads as the younger tier.
    /// </summary>
    public DateOnly? BirthDate { get; set; }

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

    // ============================================
    // Chore money & screen-time settings (per-child)
    // See CHORE_SCREENTIME_REDESIGN.md §3–4.
    // ============================================

    /// <summary>
    /// Flat weekly pool paid across all Routine instances, split into equal slices.
    /// Slice value = WeeklyRoutinePayout ÷ (routine instances scheduled that week).
    /// Default: $10.00.
    /// </summary>
    public decimal WeeklyRoutinePayout { get; set; } = 10.00m;

    /// <summary>
    /// Weekday screen-time budget stored as a POOL TOTAL across the 5 weekdays (hours).
    /// The per-day rate (e.g. 40h ÷ 5 = 8h/day) is derived for display. Default: 40h.
    /// </summary>
    public decimal WeekdayScreenTimeHours { get; set; } = 40m;

    /// <summary>
    /// Weekend screen-time budget stored as a POOL TOTAL across the 2 weekend days (hours).
    /// The per-day rate (e.g. 20h ÷ 2 = 10h/day) is derived for display. Default: 20h.
    /// </summary>
    public decimal WeekendScreenTimeHours { get; set; } = 20m;

    /// <summary>
    /// Maximum share (%) of the weekday pool that can be lost in a week — the penalty budget.
    /// The structural floor (100 − this) is the guaranteed zone. Default: 30. See MECHANICS_AMENDMENT.md §A.
    /// </summary>
    public int WeekdayAtRiskPercent { get; set; } = 30;

    /// <summary>
    /// Maximum share (%) of the weekend pool that can be lost in a week — the penalty budget.
    /// The structural floor (100 − this) is the guaranteed zone. Default: 50. See MECHANICS_AMENDMENT.md §A.
    /// </summary>
    public int WeekendAtRiskPercent { get; set; } = 20;

    /// <summary>
    /// Minutes of screen time each point of Importance puts at risk for one missed occurrence
    /// (MECHANICS_AMENDMENT_II.md rule 1; the "×6" made tunable). A missed occurrence costs
    /// min(Importance,10) × this. Default 6, so a max-importance chore costs up to one hour.
    /// </summary>
    public int MinutesPerImportancePoint { get; set; } = 6;

    /// <summary>
    /// Number of Time Machine (retro-correction) requests the child may make per week.
    /// Default: 3. The request flow that reads this is deferred; the column exists now to avoid a
    /// later migration. See MECHANICS_AMENDMENT.md §F.
    /// </summary>
    public int WeeklyFixRequestAllowance { get; set; } = 3;

    // Navigation properties
    public ICollection<ChoreScreenTimeState> ScreenTimeStates { get; set; } = [];
    public ICollection<ScreenTimeEntry> ScreenTimeEntries { get; set; } = [];
    public ICollection<QolShare> QolShares { get; set; } = [];
    public ICollection<LedgerAccount> LedgerAccounts { get; set; } = [];
}
