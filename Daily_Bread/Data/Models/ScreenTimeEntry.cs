namespace Daily_Bread.Data.Models;

/// <summary>
/// A single line item in the auditable screen-time ledger. Every change to a child's weekly ST
/// budget — a chore-miss deduction, a redemption/QOL earn-back, a manual parent adjustment, or a
/// Time Machine correction — lands here as a labeled row so the record is never silently edited.
///
/// <see cref="ChildWeeklyScreenTimeBudget"/> remains the per-(child, week) rollup; these entries are
/// the line items that sum into it. See MECHANICS_AMENDMENT.md §G (and §C/§D/§F for the kinds).
/// </summary>
public class ScreenTimeEntry
{
    public int Id { get; set; }

    /// <summary>The child whose budget this entry affects.</summary>
    public int ChildProfileId { get; set; }
    public ChildProfile ChildProfile { get; set; } = null!;

    /// <summary>Start date of the week this entry applies to.</summary>
    public DateOnly WeekStartDate { get; set; }

    /// <summary>Which pool (weekday / weekend) this entry moves.</summary>
    public ScreenTimePool Pool { get; set; }

    /// <summary>What produced this entry (deduction, earn-back, adjustment, Time Machine).</summary>
    public ScreenTimeEntryKind Kind { get; set; }

    /// <summary>
    /// The chore this entry is attributed to, if any. Null for pool-level adjustments not tied to a
    /// specific chore. Optional FK (SetNull on chore delete).
    /// </summary>
    public int? ChoreDefinitionId { get; set; }
    public ChoreDefinition? ChoreDefinition { get; set; }

    /// <summary>
    /// Signed minutes: negative removes budget (a Deduction), positive restores it (an EarnBack);
    /// Adjustment / TimeMachine may be either sign.
    /// </summary>
    public int Minutes { get; set; }

    /// <summary>
    /// The compounding streak multiplier applied when this entry was written, if relevant. Null for
    /// entries the multiplier does not apply to (e.g. earn-backs, adjustments).
    /// </summary>
    public decimal? StreakMultiplier { get; set; }

    /// <summary>
    /// Optional human-readable label for the record (e.g. "Parent adjustment",
    /// "Time Machine: Walk Gemma Tue").
    /// </summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Which screen-time pool a ledger entry moves.</summary>
public enum ScreenTimePool
{
    /// <summary>The weekday pool (Mon–Fri).</summary>
    Weekday = 0,

    /// <summary>The weekend pool (Sat–Sun).</summary>
    Weekend = 1
}

/// <summary>What produced a screen-time ledger entry. See MECHANICS_AMENDMENT.md §G.</summary>
public enum ScreenTimeEntryKind
{
    /// <summary>Budget removed for a missed chore instance (§A).</summary>
    Deduction = 0,

    /// <summary>Budget restored via redemption or QOL completion (§C/§D), capped at applied loss.</summary>
    EarnBack = 1,

    /// <summary>A manual parent adjustment to the budget (§F).</summary>
    Adjustment = 2,

    /// <summary>A correction applied by an approved Time Machine (retro-correction) request (§F).</summary>
    TimeMachine = 3
}
