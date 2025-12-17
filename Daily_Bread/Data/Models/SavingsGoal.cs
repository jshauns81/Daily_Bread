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

/// <summary>
/// Represents an achievement/badge that can be earned.
/// </summary>
public class Achievement
{
    public int Id { get; set; }

    /// <summary>
    /// Unique code for the achievement (e.g., "FIRST_CHORE", "STREAK_7").
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// Display name of the achievement.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Description of how to earn this achievement.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Emoji or icon for the achievement.
    /// </summary>
    public required string Icon { get; set; }

    /// <summary>
    /// Category of achievement.
    /// </summary>
    public AchievementCategory Category { get; set; }

    /// <summary>
    /// Points or value of this achievement (for display).
    /// </summary>
    public int Points { get; set; }

    /// <summary>
    /// Sort order for display.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Whether this achievement is currently available to earn.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Categories of achievements.
/// </summary>
public enum AchievementCategory
{
    /// <summary>Getting started achievements.</summary>
    GettingStarted = 1,
    /// <summary>Streak-related achievements.</summary>
    Streaks = 2,
    /// <summary>Earnings milestones.</summary>
    Earnings = 3,
    /// <summary>Consistency achievements.</summary>
    Consistency = 4,
    /// <summary>Special/seasonal achievements.</summary>
    Special = 5
}

/// <summary>
/// Tracks which achievements a user has earned.
/// </summary>
public class UserAchievement
{
    public int Id { get; set; }

    /// <summary>
    /// The user who earned the achievement.
    /// </summary>
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// The achievement that was earned.
    /// </summary>
    public int AchievementId { get; set; }
    public Achievement Achievement { get; set; } = null!;

    /// <summary>
    /// When the achievement was earned.
    /// </summary>
    public DateTime EarnedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the user has seen/acknowledged this achievement.
    /// </summary>
    public bool HasSeen { get; set; }
}
