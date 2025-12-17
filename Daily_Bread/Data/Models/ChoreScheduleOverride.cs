namespace Daily_Bread.Data.Models;

/// <summary>
/// Represents a one-time schedule override for a chore.
/// Used for drag-and-drop scheduling or ad-hoc assignments.
/// </summary>
public class ChoreScheduleOverride
{
    public int Id { get; set; }

    /// <summary>
    /// The chore definition this override applies to.
    /// </summary>
    public int ChoreDefinitionId { get; set; }
    public ChoreDefinition ChoreDefinition { get; set; } = null!;

    /// <summary>
    /// The specific date this override is for.
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// The type of override.
    /// </summary>
    public ScheduleOverrideType Type { get; set; }

    /// <summary>
    /// Optional: Override the assigned user for this specific date.
    /// Null means use the default from ChoreDefinition.
    /// </summary>
    public string? OverrideAssignedUserId { get; set; }
    public ApplicationUser? OverrideAssignedUser { get; set; }

    /// <summary>
    /// Optional: Override the value for this specific date.
    /// Null means use the default from ChoreDefinition.
    /// </summary>
    public decimal? OverrideValue { get; set; }

    /// <summary>
    /// Who created this override.
    /// </summary>
    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    /// <summary>
    /// When the override was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Types of schedule overrides.
/// </summary>
public enum ScheduleOverrideType
{
    /// <summary>
    /// Add the chore on a day it wouldn't normally be scheduled.
    /// </summary>
    Add = 1,

    /// <summary>
    /// Remove/skip the chore on a day it would normally be scheduled.
    /// </summary>
    Remove = 2,

    /// <summary>
    /// Move the chore from its normal day to this day.
    /// (Source day is tracked by a corresponding Remove override)
    /// </summary>
    Move = 3
}
