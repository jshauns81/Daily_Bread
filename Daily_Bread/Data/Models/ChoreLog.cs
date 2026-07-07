namespace Daily_Bread.Data.Models;

/// <summary>
/// Records the status of a chore for a specific date.
/// One ChoreLog per ChoreDefinition per date.
/// </summary>
public class ChoreLog
{
    public int Id { get; set; }

    /// <summary>
    /// The chore definition this log entry is for.
    /// </summary>
    public int ChoreDefinitionId { get; set; }
    public ChoreDefinition ChoreDefinition { get; set; } = null!;

    /// <summary>
    /// The date this chore log is for.
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Current status of the chore for this date.
    /// </summary>
    public ChoreStatus Status { get; set; } = ChoreStatus.Pending;

    /// <summary>
    /// Whether this chore allowed more than one log per (ChoreDefinitionId, Date) at the moment
    /// this row was created. Stamped once from ChoreDefinition.ScheduleType at insert time and
    /// never updated afterward - a log carries the rule that applied when it was created, even if
    /// the chore's ScheduleType changes later. True for WeeklyFrequency, false for SpecificDays.
    /// </summary>
    public bool AllowsMultiplePerDay { get; set; }

    /// <summary>
    /// The user  who completed/updated this chore log. 
    /// </summary>
    public string? CompletedByUserId { get; set; }
    public ApplicationUser? CompletedByUser { get; set; }

    /// <summary>
    /// Timestamp when the chore test was marked as completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Optional notes about completion or issues.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// The user who approved this chore (Parent role).
    /// </summary>
    public string? ApprovedByUserId { get; set; }
    public ApplicationUser? ApprovedByUser { get; set; }

    /// <summary>
    /// Timestamp when the chore was approved.
    /// </summary>
    public DateTime? ApprovedAt { get; set; }
    
    /// <summary>
    /// Reason provided when child requests help.
    /// </summary>
    public string? HelpReason { get; set; }
    
    /// <summary>
    /// Timestamp when help was requested.
    /// </summary>
    public DateTime? HelpRequestedAt { get; set; }

    /// <summary>
    /// Timestamp when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the record was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
    
    /// <summary>
    /// Concurrency token for optimistic locking.
    /// Incremented on each update. Cross-database compatible.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// The child's money-vs-screen-time choice for a redemptive rep. Meaningful only for reps that
    /// end up redemptive (over-target reps in a made-target week, or any rep in a busted week);
    /// ignored otherwise. <see cref="RedemptionChoice.None"/> is treated as
    /// <see cref="RedemptionChoice.ScreenTime"/> at reconciliation. See MECHANICS_AMENDMENT.md §D.
    /// </summary>
    public RedemptionChoice RedemptionChoice { get; set; } = RedemptionChoice.None;

    // Navigation property
    public LedgerTransaction? LedgerTransaction { get; set; }
}

/// <summary>
/// A child's redemption choice for a redemptive rep: take money or screen-time earn-back.
/// See MECHANICS_AMENDMENT.md §D.
/// </summary>
public enum RedemptionChoice
{
    /// <summary>No explicit choice made; treated as <see cref="ScreenTime"/> at reconciliation.</summary>
    None = 0,

    /// <summary>Redeem the rep for money (EarnValue × 0.5). Only available in a made-target week.</summary>
    Money = 1,

    /// <summary>Redeem the rep for screen-time earn-back (half the per-instance ST price).</summary>
    ScreenTime = 2
}

/// <summary>
/// Status of a chore for a given date.
/// </summary>
public enum ChoreStatus
{
    /// <summary>
    /// Chore has not been addressed yet.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Chore was marked as completed by the child.
    /// </summary>
    Completed = 1,

    /// <summary>
    /// Chore was approved by a parent.
    /// </summary>
    Approved = 2,

    /// <summary>
    /// Chore was missed/not completed (penalty applies).
    /// This happens when a chore is still Pending at end of day.
    /// </summary>
    Missed = 3,

    /// <summary>
    /// Chore was skipped/excused by a parent. Counts as credited: pays its routine slice and
    /// takes no screen-time hit (as if the child did it). See CHORE_SCREENTIME_REDESIGN.md §3.3.
    /// </summary>
    Skipped = 4,
    
    /// <summary>
    /// Child requested help - waiting for parent response.
    /// Protected from auto-penalty until parent responds.
    /// </summary>
    Help = 5
}
