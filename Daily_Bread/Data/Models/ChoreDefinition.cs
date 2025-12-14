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
    /// The user assigned to this chore. Nullable for unassigned chores.
    /// </summary>
    public string? AssignedUserId { get; set; }
    public ApplicationUser? AssignedUser { get; set; }

    /// <summary>
    /// Amount earned (or deducted if missed) for this chore.
    /// </summary>
    public decimal Value { get; set; }

    /// <summary>
    /// Days of the week this chore is active (flags enum).
    /// </summary>
    public DaysOfWeek ActiveDays { get; set; } = DaysOfWeek.All;

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
