namespace Daily_Bread.Data.Models;

/// <summary>
/// Durable per-(chore, child) screen-time streak state. Holds the compounding memory used by
/// <c>WeeklyReconciliationService</c>: how many consecutive weeks this chore has been missed and
/// the resulting screen-time reduction for the most recently evaluated week.
///
/// See CHORE_SCREENTIME_REDESIGN.md §4.3–4.4. Compounding multiplier grows ×1 → ×1.5 → ×2.625 →
/// ×5.25 (capped). Completing the chore resets <see cref="ConsecutiveMissWeeks"/> to 0.
/// </summary>
public class ChoreScreenTimeState
{
    public int Id { get; set; }

    /// <summary>The chore this streak tracks.</summary>
    public int ChoreDefinitionId { get; set; }
    public ChoreDefinition ChoreDefinition { get; set; } = null!;

    /// <summary>The child this streak belongs to.</summary>
    public int ChildProfileId { get; set; }
    public ChildProfile ChildProfile { get; set; } = null!;

    /// <summary>
    /// Number of consecutive weeks this chore has been missed (per §4.3). 0 = not currently on a
    /// miss streak. Reset to 0 the first week the chore is credited again.
    /// </summary>
    public int ConsecutiveMissWeeks { get; set; }

    /// <summary>
    /// Start date of the most recent week this state was evaluated for. Guards against double
    /// processing the same week.
    /// </summary>
    public DateOnly? LastEvaluatedWeekStart { get; set; }

    /// <summary>
    /// Screen-time minutes this chore removed for the last evaluated week, after the per-occurrence
    /// tally and streak multiplier and the ×5.25 cap, but BEFORE the per-pool 70% floor clamp
    /// (which is applied across all chores at the child level).
    /// </summary>
    public int CurrentWeeklyMinutesLost { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }
}
