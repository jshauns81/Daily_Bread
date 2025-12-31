namespace Daily_Bread.Data.Models;

/// <summary>
/// Represents an achievement/badge that can be earned.
/// Achievements can be visible, hidden, or legendary with optional bonuses.
/// 
/// DESIGN PRINCIPLES:
/// - Data-driven: All unlock conditions are stored in database, not hardcoded
/// - Extensible: New condition types can be added via enum without schema changes
/// - Hidden achievement support: Some achievements are secret until unlocked
/// - Bonus system: Achievements can grant gameplay bonuses
/// </summary>
public class Achievement
{
    public int Id { get; set; }

    /// <summary>
    /// Unique code for the achievement (e.g., "FIRST_CHORE", "STREAK_7").
    /// Used for programmatic reference and seed data management.
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// Display name of the achievement.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Description of how to earn this achievement.
    /// For hidden achievements, this is revealed only after unlock.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Hint text shown for hidden achievements before unlock.
    /// Should be vague enough to not reveal the exact criteria.
    /// </summary>
    public string? HiddenHint { get; set; }

    /// <summary>
    /// Emoji or icon key for the achievement.
    /// </summary>
    public required string Icon { get; set; }

    /// <summary>
    /// Locked icon shown before achievement is unlocked.
    /// If null, a generic silhouette is used.
    /// </summary>
    public string? LockedIcon { get; set; }

    /// <summary>
    /// Category of achievement for grouping in UI.
    /// </summary>
    public AchievementCategory Category { get; set; }

    /// <summary>
    /// Rarity tier affecting visual treatment and perceived value.
    /// </summary>
    public AchievementRarity Rarity { get; set; } = AchievementRarity.Common;

    /// <summary>
    /// Points or value of this achievement (for display and totals).
    /// </summary>
    public int Points { get; set; }

    /// <summary>
    /// Sort order for display within category.
    /// </summary>
    public int SortOrder { get; set; }

    // ═══════════════════════════════════════════════════════════════
    // VISIBILITY SETTINGS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Whether this achievement is hidden from the achievement list until earned.
    /// Hidden achievements show as "???" with no description until unlocked.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Whether this achievement is legendary (special visual treatment, rare).
    /// Legendary achievements have unique animations and prominent display.
    /// </summary>
    public bool IsLegendary { get; set; }

    /// <summary>
    /// Whether to show this achievement in the locked list before unlock.
    /// If false and IsHidden is false, shows with locked visual.
    /// If false and IsHidden is true, completely hidden until unlock.
    /// </summary>
    public bool IsVisibleBeforeUnlock { get; set; } = true;

    /// <summary>
    /// Whether this achievement is currently available to earn.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // ═══════════════════════════════════════════════════════════════
    // DATA-DRIVEN UNLOCK CONDITIONS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Type of condition that unlocks this achievement.
    /// Determines how UnlockConditionValue is interpreted.
    /// </summary>
    public UnlockConditionType UnlockConditionType { get; set; } = UnlockConditionType.Manual;

    /// <summary>
    /// JSON-serialized condition parameters.
    /// Format depends on UnlockConditionType. Examples:
    /// - ChoresCompleted: {"count": 10}
    /// - StreakDays: {"days": 7}
    /// - TotalEarned: {"amount": 100.00}
    /// - ChoreCategory: {"category": "Cleaning", "count": 50}
    /// </summary>
    public string? UnlockConditionValue { get; set; }

    /// <summary>
    /// Target value for progress-based achievements.
    /// Used for progress bar display (e.g., 7 for "complete 7 chores").
    /// </summary>
    public int? ProgressTarget { get; set; }

    // ═══════════════════════════════════════════════════════════════
    // BONUS SYSTEM
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Type of bonus granted when this achievement is earned.
    /// Null if no bonus is granted.
    /// </summary>
    public AchievementBonusType? BonusType { get; set; }

    /// <summary>
    /// JSON-serialized bonus parameters.
    /// Format depends on BonusType. Examples:
    /// - PointMultiplier: {"multiplier": 1.1, "duration_days": 7}
    /// - OneTimeForgiveness: {"count": 1}
    /// - UnlockChore: {"chore_tier": "Advanced"}
    /// </summary>
    public string? BonusValue { get; set; }

    /// <summary>
    /// Human-readable description of the bonus for UI display.
    /// </summary>
    public string? BonusDescription { get; set; }

    // ═══════════════════════════════════════════════════════════════
    // TIMESTAMPS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// When this achievement was created in the system.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this achievement was last modified.
    /// </summary>
    public DateTime? ModifiedAt { get; set; }
}

/// <summary>
/// Categories of achievements for grouping and filtering.
/// </summary>
public enum AchievementCategory
{
    /// <summary>Getting started achievements for new users.</summary>
    GettingStarted = 1,
    
    /// <summary>Streak-related achievements (consecutive days).</summary>
    Streaks = 2,
    
    /// <summary>Earnings milestones (money earned).</summary>
    Earnings = 3,
    
    /// <summary>Consistency achievements (total completions, perfect days).</summary>
    Consistency = 4,
    
    /// <summary>Special/seasonal/event achievements.</summary>
    Special = 5,
    
    /// <summary>Hidden achievements (secret until unlocked).</summary>
    Secret = 6,
    
    /// <summary>Legendary achievements (rare, prestigious).</summary>
    Legendary = 7,
    
    /// <summary>Time-based achievements (early bird, night owl, etc.).</summary>
    TimeBased = 8,
    
    /// <summary>Social/family achievements (helping siblings, etc.).</summary>
    Social = 9,
    
    /// <summary>Savings and goal achievements.</summary>
    Savings = 10,
    
    /// <summary>Mastery achievements (perfection, expertise).</summary>
    Mastery = 11
}

/// <summary>
/// Rarity tier for achievements affecting visual treatment.
/// </summary>
public enum AchievementRarity
{
    /// <summary>Common achievements - expected to be earned by most users.</summary>
    Common = 1,
    
    /// <summary>Uncommon achievements - require some effort.</summary>
    Uncommon = 2,
    
    /// <summary>Rare achievements - require dedication.</summary>
    Rare = 3,
    
    /// <summary>Epic achievements - significant accomplishments.</summary>
    Epic = 4,
    
    /// <summary>Legendary achievements - extraordinary accomplishments.</summary>
    Legendary = 5
}

/// <summary>
/// Types of unlock conditions for data-driven achievement evaluation.
/// Each type determines how the UnlockConditionValue JSON is interpreted.
/// 
/// HOW TO ADD NEW CONDITION TYPES:
/// 1. Add enum value here
/// 2. Add evaluator case in AchievementConditionEvaluator.EvaluateAsync()
/// 3. Document the JSON format in this enum's XML comment
/// </summary>
public enum UnlockConditionType
{
    /// <summary>
    /// Manual unlock only (awarded by parent or system event).
    /// No automatic evaluation.
    /// </summary>
    Manual = 0,
    
    /// <summary>
    /// Total chores completed (any type).
    /// JSON: {"count": 10}
    /// </summary>
    ChoresCompleted = 1,
    
    /// <summary>
    /// Consecutive days with all chores completed.
    /// JSON: {"days": 7}
    /// </summary>
    StreakDays = 2,
    
    /// <summary>
    /// Total money earned (cumulative).
    /// JSON: {"amount": 100.00}
    /// </summary>
    TotalEarned = 3,
    
    /// <summary>
    /// Current balance threshold.
    /// JSON: {"amount": 50.00}
    /// </summary>
    BalanceReached = 4,
    
    /// <summary>
    /// Perfect days count (days with 100% completion).
    /// JSON: {"count": 30}
    /// </summary>
    PerfectDays = 5,
    
    /// <summary>
    /// Specific chore completed N times.
    /// JSON: {"chore_code": "MAKE_BED", "count": 100}
    /// </summary>
    SpecificChoreCount = 6,
    
    /// <summary>
    /// Chores completed before a specific time.
    /// JSON: {"before_hour": 12, "count": 1} (before noon)
    /// </summary>
    EarlyCompletion = 7,
    
    /// <summary>
    /// First chore of any type completed.
    /// JSON: {} (no parameters)
    /// </summary>
    FirstChore = 8,
    
    /// <summary>
    /// First savings goal created.
    /// JSON: {} (no parameters)
    /// </summary>
    FirstGoal = 9,
    
    /// <summary>
    /// Savings goal completed.
    /// JSON: {"count": 1} (number of goals completed)
    /// </summary>
    GoalCompleted = 10,
    
    /// <summary>
    /// First dollar earned.
    /// JSON: {} (no parameters)
    /// </summary>
    FirstDollar = 11,
    
    /// <summary>
    /// Earnings in a single week.
    /// JSON: {"amount": 20.00}
    /// </summary>
    WeeklyEarnings = 12,
    
    /// <summary>
    /// Chores completed on a specific day type.
    /// JSON: {"day_type": "Weekend", "count": 10}
    /// </summary>
    DayTypeCompletion = 13,
    
    /// <summary>
    /// Consecutive weeks with target met.
    /// JSON: {"weeks": 4}
    /// </summary>
    WeekStreak = 14,
    
    /// <summary>
    /// Account age milestone.
    /// JSON: {"days": 365}
    /// </summary>
    AccountAge = 15,
    
    /// <summary>
    /// Total bonus chores completed (above weekly target).
    /// JSON: {"count": 10}
    /// </summary>
    BonusChoresCompleted = 16,
    
    /// <summary>
    /// Recovery from missed chore (completed next day).
    /// JSON: {"count": 1}
    /// </summary>
    ChoreRecovery = 17,
    
    /// <summary>
    /// Help requests made.
    /// JSON: {"count": 1}
    /// </summary>
    HelpRequested = 18,
    
    /// <summary>
    /// Zero penalties in a time period.
    /// JSON: {"days": 30}
    /// </summary>
    PenaltyFree = 19,
    
    /// <summary>
    /// Specific achievement earned (for chained achievements).
    /// JSON: {"achievement_code": "STREAK_7"}
    /// </summary>
    AchievementUnlocked = 20,
    
    /// <summary>
    /// Multiple achievements in a category earned.
    /// JSON: {"category": "Streaks", "count": 3}
    /// </summary>
    CategoryMastery = 21,
    
    /// <summary>
    /// Total achievements earned.
    /// JSON: {"count": 10}
    /// </summary>
    TotalAchievements = 22,
    
    /// <summary>
    /// Login streak (consecutive days with any activity).
    /// JSON: {"days": 30}
    /// </summary>
    LoginStreak = 23,
    
    /// <summary>
    /// Specific time of day completion.
    /// JSON: {"hour_start": 5, "hour_end": 7, "count": 7} (5-7 AM)
    /// </summary>
    TimeOfDayCompletion = 24,
    
    /// <summary>
    /// Cash out performed.
    /// JSON: {"count": 1} or {"total_amount": 100.00}
    /// </summary>
    CashOut = 25
}

/// <summary>
/// Types of bonuses that can be granted by achievements.
/// Each type determines how the BonusValue JSON is interpreted.
/// </summary>
public enum AchievementBonusType
{
    /// <summary>
    /// No bonus granted.
    /// </summary>
    None = 0,
    
    /// <summary>
    /// Multiplier applied to all point earnings.
    /// JSON: {"multiplier": 1.1, "duration_days": 7, "max_earnings": 50.00}
    /// Scope: Temporary
    /// </summary>
    PointMultiplier = 1,
    
    /// <summary>
    /// One-time forgiveness for a missed chore (penalty waived).
    /// JSON: {"count": 1}
    /// Scope: One-time use
    /// </summary>
    OneTimeForgiveness = 2,
    
    /// <summary>
    /// Temporary suppression of reminder notifications.
    /// JSON: {"duration_days": 1}
    /// Scope: Temporary
    /// </summary>
    ReminderSuppression = 3,
    
    /// <summary>
    /// Double points for a specific day.
    /// JSON: {"count": 1} (number of double-point days)
    /// Scope: One-time use
    /// </summary>
    DoublePointDay = 4,
    
    /// <summary>
    /// Unlock access to higher-tier chores.
    /// JSON: {"tier": "Advanced"}
    /// Scope: Permanent
    /// </summary>
    UnlockChoreTier = 5,
    
    /// <summary>
    /// Increase trust/autonomy level (auto-approve threshold).
    /// JSON: {"level_increase": 1}
    /// Scope: Permanent
    /// </summary>
    TrustIncrease = 6,
    
    /// <summary>
    /// Flat bonus points added to balance.
    /// JSON: {"amount": 5.00}
    /// Scope: Immediate (one-time)
    /// </summary>
    BonusPoints = 7,
    
    /// <summary>
    /// Reduced penalty rate for missed chores.
    /// JSON: {"reduction_percent": 0.5, "duration_days": 7}
    /// Scope: Temporary
    /// </summary>
    PenaltyReduction = 8,
    
    /// <summary>
    /// Streak protection (one missed day doesn't break streak).
    /// JSON: {"count": 1}
    /// Scope: One-time use
    /// </summary>
    StreakProtection = 9,
    
    /// <summary>
    /// Early cash-out eligibility (lower threshold).
    /// JSON: {"threshold_reduction": 5.00, "duration_days": 30}
    /// Scope: Temporary
    /// </summary>
    EarlyCashOut = 10,
    
    /// <summary>
    /// Custom badge/flair for profile display.
    /// JSON: {"badge_key": "early_adopter"}
    /// Scope: Permanent
    /// </summary>
    ProfileBadge = 11,
    
    /// <summary>
    /// Bonus multiplier for specific chore category.
    /// JSON: {"category": "Cleaning", "multiplier": 1.25, "duration_days": 7}
    /// Scope: Temporary
    /// </summary>
    CategoryBonus = 12
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
    /// Used for "new achievement" notifications.
    /// </summary>
    public bool HasSeen { get; set; }

    /// <summary>
    /// Optional notes about how/when this was earned.
    /// Useful for manual awards or special circumstances.
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Tracks progress toward achievements that require cumulative progress.
/// One record per user per achievement.
/// </summary>
public class AchievementProgress
{
    public int Id { get; set; }

    /// <summary>
    /// The user this progress belongs to.
    /// </summary>
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// The achievement being tracked.
    /// </summary>
    public int AchievementId { get; set; }
    public Achievement Achievement { get; set; } = null!;

    /// <summary>
    /// Current progress value (e.g., 5 out of 10 chores).
    /// </summary>
    public int CurrentValue { get; set; }

    /// <summary>
    /// Target value for completion (cached from achievement for quick access).
    /// </summary>
    public int TargetValue { get; set; }

    /// <summary>
    /// When the progress was last updated.
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the progress was first started.
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// For streak-based achievements: the anchor date for streak calculation.
    /// </summary>
    public DateOnly? StreakAnchorDate { get; set; }

    /// <summary>
    /// JSON field for additional progress metadata.
    /// Example: {"last_increment_date": "2024-01-15", "highest_streak": 5}
    /// </summary>
    public string? Metadata { get; set; }
}

/// <summary>
/// Tracks active bonuses granted by achievements.
/// Bonuses can be one-time, temporary, or permanent.
/// </summary>
public class UserAchievementBonus
{
    public int Id { get; set; }

    /// <summary>
    /// The user this bonus belongs to.
    /// </summary>
    public required string UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// The achievement that granted this bonus.
    /// </summary>
    public int AchievementId { get; set; }
    public Achievement Achievement { get; set; } = null!;

    /// <summary>
    /// Type of bonus (for quick filtering without joining to Achievement).
    /// </summary>
    public AchievementBonusType BonusType { get; set; }

    /// <summary>
    /// JSON-serialized bonus parameters (copied from Achievement.BonusValue).
    /// </summary>
    public string? BonusValue { get; set; }

    /// <summary>
    /// Whether this bonus is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// For temporary bonuses: when the bonus expires.
    /// Null for permanent or one-time bonuses.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// For one-time bonuses: remaining uses.
    /// Null for temporary or permanent bonuses.
    /// </summary>
    public int? RemainingUses { get; set; }

    /// <summary>
    /// For capped bonuses: total value applied so far.
    /// Example: Point multiplier with max_earnings cap.
    /// </summary>
    public decimal? AppliedAmount { get; set; }

    /// <summary>
    /// For capped bonuses: maximum value that can be applied.
    /// </summary>
    public decimal? MaxAmount { get; set; }

    /// <summary>
    /// When this bonus was granted.
    /// </summary>
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this bonus was last used/applied.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }
}
