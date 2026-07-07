namespace Daily_Bread.Data.Models;

/// <summary>
/// How a DrivingLogEntry's IsNightDriving flag was set. Stamped at creation so a later
/// change to what "night" means doesn't retroactively reclassify old rows.
/// </summary>
public enum NightDrivingSource
{
    /// <summary>Derived automatically from Start/EndTime against the app's night window.</summary>
    DerivedFromTime = 0,

    /// <summary>Manually flagged by whoever logged the session (dusk/dawn edge cases).</summary>
    ManualOverride = 1
}

public enum WeatherCondition
{
    Clear = 0,
    Rain = 1,
    Snow = 2,
    Fog = 3,
    Ice = 4,
    Other = 5
}

/// <summary>
/// State machine for a driving-log entry. Only Approved rows count toward hour totals -
/// mirrors RewardClaimStatus/ChoreStatus's "act only on rows still Pending" pattern.
/// </summary>
public enum DrivingLogStatus
{
    /// <summary>Self-reported by the child, awaiting a parent decision.</summary>
    PendingApproval = 0,

    /// <summary>Approved - parent-created entries land here immediately.</summary>
    Approved = 1,

    /// <summary>Parent declined the self-reported entry - does not count toward hours.</summary>
    Rejected = 2
}

/// <summary>
/// A single supervised driving-practice session logged toward a state permit-hours
/// requirement. Created either by the child (self-reported, needs parent approval) or
/// by a parent (auto-approved, since the parent is asserting the record directly).
/// </summary>
public class DrivingLogEntry
{
    public int Id { get; set; }

    /// <summary>The child (Identity user) this session counts toward.</summary>
    public required string ChildUserId { get; set; }
    public ApplicationUser ChildUser { get; set; } = null!;

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>
    /// Duration in minutes, computed and stored at write time from Start/EndTime
    /// (handles a session that crosses midnight by adding 24h). Denormalized so
    /// hour totals never need to reparse start/end across the whole history.
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>True if this session counts toward the night-hours sub-goal.</summary>
    public bool IsNightDriving { get; set; }

    public NightDrivingSource NightDrivingSource { get; set; } = NightDrivingSource.DerivedFromTime;

    /// <summary>
    /// The supervising adult, if they're a registered household user (usually a Parent).
    /// Null when the supervisor doesn't have an app account (e.g. grandparent, instructor) -
    /// in that case SupervisorName is used instead.
    /// </summary>
    public string? SupervisorUserId { get; set; }
    public ApplicationUser? SupervisorUser { get; set; }

    /// <summary>Free-text supervisor name, used when SupervisorUserId is null.</summary>
    public string? SupervisorName { get; set; }

    public WeatherCondition Weather { get; set; } = WeatherCondition.Clear;

    /// <summary>Free-text route/notes (e.g. "Highway to grandma's, parallel parking practice").</summary>
    public string? RouteNotes { get; set; }

    /// <summary>Who created this row - determines the approval flow.</summary>
    public required string CreatedByUserId { get; set; }
    public ApplicationUser CreatedByUser { get; set; } = null!;

    public DrivingLogStatus Status { get; set; } = DrivingLogStatus.PendingApproval;

    public DateTime? DecidedAt { get; set; }
    public string? DecidedByUserId { get; set; }
    public ApplicationUser? DecidedByUser { get; set; }
    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }

    /// <summary>Concurrency token for optimistic locking (same pattern as ChoreLog/AchievementRewardClaim).</summary>
    public int Version { get; set; }
}
