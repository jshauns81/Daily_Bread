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
    /// The user who completed/updated this chore log.
    /// </summary>
    public string? CompletedByUserId { get; set; }
    public ApplicationUser? CompletedByUser { get; set; }

    /// <summary>
    /// Timestamp when the chore was marked as completed.
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

    // Navigation property
    public LedgerTransaction? LedgerTransaction { get; set; }
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
    /// Chore was skipped/excused by a parent.
    /// No penalty, no earning.
    /// </summary>
    Skipped = 4,
    
    /// <summary>
    /// Child requested help - waiting for parent response.
    /// Protected from auto-penalty until parent responds.
    /// </summary>
    Help = 5
}
