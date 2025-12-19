namespace Daily_Bread.Data.Models;

/// <summary>
/// Represents a savings goal for a child - something they're saving up for.
/// </summary>
public class SavingsGoal
{
    public int Id { get; set; }

    /// <summary>
    /// The user this goal belongs to.
    /// </summary>
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Name of the goal (e.g., "Nintendo Switch Game", "New Bike").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Optional description or notes about the goal.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Target amount to save.
    /// </summary>
    public decimal TargetAmount { get; set; }

    /// <summary>
    /// Optional URL to an image of the item.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Priority order (lower = higher priority).
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Whether this is the primary/active goal.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Whether this goal is still active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether this goal has been completed/achieved.
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// When the goal was completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// When the goal was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the goal was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
}
