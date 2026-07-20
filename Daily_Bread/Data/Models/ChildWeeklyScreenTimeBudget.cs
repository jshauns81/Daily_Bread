namespace Daily_Bread.Data.Models;

/// <summary>
/// A per-(child, week) snapshot of the child's effective screen-time budget after chore misses
/// are applied. Written by <c>WeeklyReconciliationService</c> at week-end; read by the UI to show
/// "next week you have N hours". Storing a snapshot freezes the week's numbers rather than
/// recomputing from mutable streak state.
///
/// See CHORE_SCREENTIME_REDESIGN.md §4.1–4.5. Minutes are stored (not hours) to keep the math
/// exact; the base pools come from <see cref="ChildProfile.WeekdayScreenTimeHours"/> and
/// <see cref="ChildProfile.WeekendScreenTimeHours"/>.
/// </summary>
public class ChildWeeklyScreenTimeBudget
{
    public int Id { get; set; }

    /// <summary>The child this budget belongs to.</summary>
    public int ChildProfileId { get; set; }
    public ChildProfile ChildProfile { get; set; } = null!;

    /// <summary>
    /// Start date of the week this budget applies to (the upcoming week the reduction affects).
    /// </summary>
    public DateOnly WeekStartDate { get; set; }

    /// <summary>Weekday pool before reductions, in minutes (snapshot of the child's setting).</summary>
    public int WeekdayBasePoolMinutes { get; set; }

    /// <summary>Weekend pool before reductions, in minutes (snapshot of the child's setting).</summary>
    public int WeekendBasePoolMinutes { get; set; }

    /// <summary>
    /// Total screen-time minutes removed from the weekday pool this week, after the ×5.25 cap and
    /// the 70% floor clamp. Never exceeds 70% of <see cref="WeekdayBasePoolMinutes"/>.
    /// </summary>
    public int WeekdayMinutesLost { get; set; }

    /// <summary>
    /// Total screen-time minutes removed from the weekend pool this week, after the ×5.25 cap and
    /// the 70% floor clamp. Never exceeds 70% of <see cref="WeekendBasePoolMinutes"/>.
    /// </summary>
    public int WeekendMinutesLost { get; set; }

    /// <summary>
    /// Extra minutes added to each vacuum-fill (inverse) routine's target this week:
    /// total_ST_lost ÷ count(inverse routines). Display-only in v1 (soft target). See §4.5.
    /// </summary>
    public int InverseFillAddedMinutesPerRoutine { get; set; }

    /// <summary>Effective weekday pool after reductions, in minutes.</summary>
    public int WeekdayEffectiveMinutes => Math.Max(0, WeekdayBasePoolMinutes - WeekdayMinutesLost);

    /// <summary>Effective weekend pool after reductions, in minutes.</summary>
    public int WeekendEffectiveMinutes => Math.Max(0, WeekendBasePoolMinutes - WeekendMinutesLost);

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }
}
