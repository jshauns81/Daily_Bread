namespace Daily_Bread.Data.Models;

/// <summary>
/// Defines a chore that can be assigned and scheduled.
/// </summary>
public class ChoreDefinition
{
    public int Id { get; set; }

    /// <summary>
    /// Name of the chore (e.g., "Make Bed", "Take Out Trash").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Optional description with details about the chore.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Icon/emoji for visual display (e.g., "???", "???", "??").
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// The user assigned to this chore. Nullable for unassigned chores.
    /// </summary>
    public string? AssignedUserId { get; set; }
    public ApplicationUser? AssignedUser { get; set; }

    /// <summary>
    /// Amount earned when this chore is completed.
    /// Set to 0 for "expectation" chores that don't earn money.
    /// </summary>
    public decimal EarnValue { get; set; } = 0;
    
    /// <summary>
    /// Amount deducted when this chore is missed (not done and not asked for help).
    /// Set to 0 for no penalty.
    /// </summary>
    public decimal PenaltyValue { get; set; } = 0;

    /// <summary>
    /// Legacy property for backward compatibility.
    /// Returns EarnValue if set, otherwise PenaltyValue.
    /// New code should use EarnValue and PenaltyValue directly.
    /// </summary>
    [Obsolete("Use EarnValue and PenaltyValue instead")]
    public decimal Value
    {
        get => EarnValue > 0 ? EarnValue : PenaltyValue;
        set => EarnValue = value; // For backward compatibility during migration
    }

    /// <summary>
    /// The scheduling type for this chore.
    /// </summary>
    public ChoreScheduleType ScheduleType { get; set; } = ChoreScheduleType.SpecificDays;

    /// <summary>
    /// Days of the week this chore is active (flags enum).
    /// For SpecificDays: The exact days the chore must be done.
    /// For WeeklyFrequency: The days the chore CAN be done (defaults to All).
    /// </summary>
    public DaysOfWeek ActiveDays { get; set; } = DaysOfWeek.All;

    /// <summary>
    /// For WeeklyFrequency schedule type: how many times per week the chore should be completed.
    /// Ignored for SpecificDays schedule type.
    /// </summary>
    public int WeeklyTargetCount { get; set; } = 1;

    /// <summary>
    /// Optional start date for the chore schedule. Null means no start restriction.
    /// </summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>
    /// Optional end date for the chore schedule. Null means no end restriction.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Whether this chore is currently active and should appear in schedules.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When true, the chore is automatically approved when marked complete.
    /// This is the DEFAULT behavior - most chores don't need parent inspection.
    /// When false, a parent must manually approve the completed chore.
    /// </summary>
    public bool AutoApprove { get; set; } = true;

    /// <summary>
    /// Sort order for display purposes.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Timestamp when the chore was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the chore was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }

    // Navigation property
    public ICollection<ChoreLog> ChoreLogs { get; set; } = [];
    
    // Helper properties
    
    /// <summary>
    /// True if this is an "expectation" chore (no earn, only penalty).
    /// </summary>
    public bool IsExpectation => EarnValue == 0 && PenaltyValue > 0;
    
    /// <summary>
    /// True if this is an "earning" chore (can earn money).
    /// </summary>
    public bool IsEarning => EarnValue > 0;
}

/// <summary>
/// Defines how a chore is scheduled.
/// </summary>
public enum ChoreScheduleType
{
    /// <summary>
    /// Chore is assigned to specific days of the week (e.g., Mon, Wed, Fri).
    /// </summary>
    SpecificDays = 0,

    /// <summary>
    /// Chore must be completed X times per week, any days.
    /// The ActiveDays property can optionally restrict which days it can be done.
    /// </summary>
    WeeklyFrequency = 1
}

/// <summary>
/// Flags enum for days of the week when a chore is active.
/// </summary>
[Flags]
public enum DaysOfWeek
{
    None = 0,
    Sunday = 1,
    Monday = 2,
    Tuesday = 4,
    Wednesday = 8,
    Thursday = 16,
    Friday = 32,
    Saturday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekends = Saturday | Sunday,
    All = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday
}
