using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Daily_Bread.Services;

/// <summary>
/// Result of applying a bonus to an operation.
/// </summary>
public record BonusApplicationResult
{
    /// <summary>
    /// Whether a bonus was applied.
    /// </summary>
    public bool Applied { get; init; }
    
    /// <summary>
    /// The original value before bonus.
    /// </summary>
    public decimal OriginalValue { get; init; }
    
    /// <summary>
    /// The modified value after bonus.
    /// </summary>
    public decimal ModifiedValue { get; init; }
    
    /// <summary>
    /// Description of what bonus was applied.
    /// </summary>
    public string? BonusDescription { get; init; }
    
    /// <summary>
    /// The bonus that was applied (if any).
    /// </summary>
    public UserAchievementBonus? AppliedBonus { get; init; }
    
    public static BonusApplicationResult NoBonus(decimal value) 
        => new() { Applied = false, OriginalValue = value, ModifiedValue = value };
    
    public static BonusApplicationResult WithBonus(decimal original, decimal modified, string description, UserAchievementBonus bonus)
        => new() { Applied = true, OriginalValue = original, ModifiedValue = modified, BonusDescription = description, AppliedBonus = bonus };
}

/// <summary>
/// Summary of active bonuses for a user.
/// </summary>
public record ActiveBonusSummary
{
    /// <summary>
    /// Current point multiplier (1.0 = no bonus).
    /// </summary>
    public decimal PointMultiplier { get; init; } = 1.0m;
    
    /// <summary>
    /// Current penalty reduction (0.0 = no reduction, 0.5 = 50% reduction).
    /// </summary>
    public decimal PenaltyReduction { get; init; }
    
    /// <summary>
    /// Number of one-time forgiveness uses available.
    /// </summary>
    public int ForgivenessUsesAvailable { get; init; }
    
    /// <summary>
    /// Number of double-point days available.
    /// </summary>
    public int DoublePointDaysAvailable { get; init; }
    
    /// <summary>
    /// Number of streak protection uses available.
    /// </summary>
    public int StreakProtectionAvailable { get; init; }
    
    /// <summary>
    /// Whether reminders are currently suppressed.
    /// </summary>
    public bool RemindersSupressed { get; init; }
    
    /// <summary>
    /// Cash out threshold reduction amount.
    /// </summary>
    public decimal CashOutThresholdReduction { get; init; }
    
    /// <summary>
    /// Trust/autonomy level increases earned.
    /// </summary>
    public int TrustLevelIncrease { get; init; }
    
    /// <summary>
    /// List of active profile badges.
    /// </summary>
    public List<string> ProfileBadges { get; init; } = new();
    
    /// <summary>
    /// List of active bonuses with details.
    /// </summary>
    public List<ActiveBonusDetail> ActiveBonuses { get; init; } = new();
}

/// <summary>
/// Details about a specific active bonus.
/// </summary>
public record ActiveBonusDetail
{
    public int BonusId { get; init; }
    public string AchievementName { get; init; } = "";
    public string Description { get; init; } = "";
    public AchievementBonusType BonusType { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public int? RemainingUses { get; init; }
}

/// <summary>
/// Interface for managing achievement bonuses.
/// </summary>
public interface IAchievementBonusService
{
    /// <summary>
    /// Grants a bonus to a user when they earn an achievement.
    /// </summary>
    Task GrantBonusAsync(string userId, Achievement achievement);
    
    /// <summary>
    /// Gets a summary of all active bonuses for a user.
    /// </summary>
    Task<ActiveBonusSummary> GetActiveBonusesAsync(string userId);
    
    /// <summary>
    /// Applies point multiplier bonuses to a chore earning amount.
    /// </summary>
    Task<BonusApplicationResult> ApplyPointMultiplierAsync(string userId, decimal baseAmount);
    
    /// <summary>
    /// Applies penalty reduction bonuses to a penalty amount.
    /// </summary>
    Task<BonusApplicationResult> ApplyPenaltyReductionAsync(string userId, decimal basePenalty);
    
    /// <summary>
    /// Uses a one-time forgiveness bonus if available.
    /// </summary>
    Task<bool> UseForgivenessAsync(string userId);
    
    /// <summary>
    /// Uses a double-point day bonus if available.
    /// </summary>
    Task<bool> UseDoublePointDayAsync(string userId);
    
    /// <summary>
    /// Uses a streak protection bonus if available.
    /// </summary>
    Task<bool> UseStreakProtectionAsync(string userId);
    
    /// <summary>
    /// Checks if reminders should be suppressed for a user.
    /// </summary>
    Task<bool> AreRemindersSuppressedAsync(string userId);
    
    /// <summary>
    /// Gets the effective cash-out threshold after bonuses.
    /// </summary>
    Task<decimal> GetEffectiveCashOutThresholdAsync(string userId, decimal baseThreshold);
    
    /// <summary>
    /// Expires any bonuses that have passed their expiration date.
    /// </summary>
    Task ExpireOldBonusesAsync();
}

/// <summary>
/// Service for managing and applying achievement bonuses.
/// 
/// BONUS STACKING RULES:
/// - Point multipliers stack multiplicatively up to 2.0x cap
/// - Penalty reductions stack additively up to 75% cap
/// - One-time use bonuses are consumed in order earned
/// - Temporary bonuses expire and are marked inactive
/// - Permanent bonuses never expire
/// </summary>
public class AchievementBonusService : IAchievementBonusService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IDateProvider _dateProvider;
    private readonly ILogger<AchievementBonusService> _logger;

    // Stacking caps to prevent runaway bonuses
    private const decimal MaxPointMultiplier = 2.0m;
    private const decimal MaxPenaltyReduction = 0.75m;

    public AchievementBonusService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        IDateProvider dateProvider,
        ILogger<AchievementBonusService> logger)
    {
        _contextFactory = contextFactory;
        _dateProvider = dateProvider;
        _logger = logger;
    }

    public async Task GrantBonusAsync(string userId, Achievement achievement)
    {
        if (!achievement.BonusType.HasValue || achievement.BonusType == AchievementBonusType.None)
            return;

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Check if bonus already granted for this achievement
        var existing = await context.UserAchievementBonuses
            .FirstOrDefaultAsync(b => b.UserId == userId && b.AchievementId == achievement.Id);
        
        if (existing != null)
        {
            _logger.LogInformation("Bonus for achievement {Code} already granted to user {UserId}", 
                achievement.Code, userId);
            return;
        }

        var bonus = new UserAchievementBonus
        {
            UserId = userId,
            AchievementId = achievement.Id,
            BonusType = achievement.BonusType.Value,
            BonusValue = achievement.BonusValue,
            IsActive = true,
            GrantedAt = DateTime.UtcNow
        };

        // Configure bonus based on type
        ConfigureBonus(bonus, achievement);

        // Handle immediate bonuses
        if (achievement.BonusType == AchievementBonusType.BonusPoints)
        {
            await GrantImmediateBonusPointsAsync(context, userId, achievement);
            bonus.IsActive = false; // Mark as consumed
        }

        context.UserAchievementBonuses.Add(bonus);
        await context.SaveChangesAsync();

        _logger.LogInformation("Granted {BonusType} bonus from achievement {Code} to user {UserId}",
            achievement.BonusType, achievement.Code, userId);
    }

    public async Task<ActiveBonusSummary> GetActiveBonusesAsync(string userId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var now = DateTime.UtcNow;
        var activeBonuses = await context.UserAchievementBonuses
            .Include(b => b.Achievement)
            .Where(b => b.UserId == userId && b.IsActive)
            .Where(b => !b.ExpiresAt.HasValue || b.ExpiresAt > now)
            .Where(b => !b.RemainingUses.HasValue || b.RemainingUses > 0)
            .ToListAsync();

        var summary = new ActiveBonusSummary
        {
            PointMultiplier = CalculatePointMultiplier(activeBonuses),
            PenaltyReduction = CalculatePenaltyReduction(activeBonuses),
            ForgivenessUsesAvailable = CountUsesAvailable(activeBonuses, AchievementBonusType.OneTimeForgiveness),
            DoublePointDaysAvailable = CountUsesAvailable(activeBonuses, AchievementBonusType.DoublePointDay),
            StreakProtectionAvailable = CountUsesAvailable(activeBonuses, AchievementBonusType.StreakProtection),
            RemindersSupressed = activeBonuses.Any(b => b.BonusType == AchievementBonusType.ReminderSuppression),
            CashOutThresholdReduction = CalculateCashOutReduction(activeBonuses),
            TrustLevelIncrease = CalculateTrustIncrease(activeBonuses),
            ProfileBadges = GetProfileBadges(activeBonuses),
            ActiveBonuses = activeBonuses.Select(b => new ActiveBonusDetail
            {
                BonusId = b.Id,
                AchievementName = b.Achievement.Name,
                Description = b.Achievement.BonusDescription ?? GetDefaultBonusDescription(b),
                BonusType = b.BonusType,
                ExpiresAt = b.ExpiresAt,
                RemainingUses = b.RemainingUses
            }).ToList()
        };

        return summary;
    }

    public async Task<BonusApplicationResult> ApplyPointMultiplierAsync(string userId, decimal baseAmount)
    {
        var summary = await GetActiveBonusesAsync(userId);
        
        if (summary.PointMultiplier <= 1.0m)
            return BonusApplicationResult.NoBonus(baseAmount);

        var modifiedAmount = Math.Round(baseAmount * summary.PointMultiplier, 2);
        return new BonusApplicationResult
        {
            Applied = true,
            OriginalValue = baseAmount,
            ModifiedValue = modifiedAmount,
            BonusDescription = $"{summary.PointMultiplier:P0} point bonus applied"
        };
    }

    public async Task<BonusApplicationResult> ApplyPenaltyReductionAsync(string userId, decimal basePenalty)
    {
        var summary = await GetActiveBonusesAsync(userId);
        
        if (summary.PenaltyReduction <= 0)
            return BonusApplicationResult.NoBonus(basePenalty);

        var reduction = basePenalty * summary.PenaltyReduction;
        var modifiedPenalty = Math.Round(basePenalty - reduction, 2);
        
        return new BonusApplicationResult
        {
            Applied = true,
            OriginalValue = basePenalty,
            ModifiedValue = modifiedPenalty,
            BonusDescription = $"{summary.PenaltyReduction:P0} penalty reduction applied"
        };
    }

    public async Task<bool> UseForgivenessAsync(string userId)
    {
        return await UseOneTimeBonusAsync(userId, AchievementBonusType.OneTimeForgiveness);
    }

    public async Task<bool> UseDoublePointDayAsync(string userId)
    {
        return await UseOneTimeBonusAsync(userId, AchievementBonusType.DoublePointDay);
    }

    public async Task<bool> UseStreakProtectionAsync(string userId)
    {
        return await UseOneTimeBonusAsync(userId, AchievementBonusType.StreakProtection);
    }

    public async Task<bool> AreRemindersSuppressedAsync(string userId)
    {
        var summary = await GetActiveBonusesAsync(userId);
        return summary.RemindersSupressed;
    }

    public async Task<decimal> GetEffectiveCashOutThresholdAsync(string userId, decimal baseThreshold)
    {
        var summary = await GetActiveBonusesAsync(userId);
        return Math.Max(0, baseThreshold - summary.CashOutThresholdReduction);
    }

    public async Task ExpireOldBonusesAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var now = DateTime.UtcNow;
        var expiredBonuses = await context.UserAchievementBonuses
            .Where(b => b.IsActive && b.ExpiresAt.HasValue && b.ExpiresAt <= now)
            .ToListAsync();

        foreach (var bonus in expiredBonuses)
        {
            bonus.IsActive = false;
            _logger.LogInformation("Expired bonus {BonusId} (type {Type}) for user {UserId}",
                bonus.Id, bonus.BonusType, bonus.UserId);
        }

        if (expiredBonuses.Count > 0)
        {
            await context.SaveChangesAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PRIVATE HELPER METHODS
    // ═══════════════════════════════════════════════════════════════

    private void ConfigureBonus(UserAchievementBonus bonus, Achievement achievement)
    {
        var bonusValue = achievement.BonusValue;

        switch (achievement.BonusType)
        {
            case AchievementBonusType.PointMultiplier:
            case AchievementBonusType.PenaltyReduction:
            case AchievementBonusType.CategoryBonus:
            case AchievementBonusType.ReminderSuppression:
            case AchievementBonusType.EarlyCashOut:
                // Temporary bonuses with duration
                var durationDays = ParseInt(bonusValue, "duration_days", 7);
                bonus.ExpiresAt = DateTime.UtcNow.AddDays(durationDays);
                bonus.MaxAmount = ParseDecimal(bonusValue, "max_earnings", null);
                break;

            case AchievementBonusType.OneTimeForgiveness:
            case AchievementBonusType.DoublePointDay:
            case AchievementBonusType.StreakProtection:
                // One-time use bonuses
                bonus.RemainingUses = ParseInt(bonusValue, "count", 1);
                break;

            case AchievementBonusType.UnlockChoreTier:
            case AchievementBonusType.TrustIncrease:
            case AchievementBonusType.ProfileBadge:
                // Permanent bonuses - no expiration
                break;

            case AchievementBonusType.BonusPoints:
                // Immediate - handled separately
                break;
        }
    }

    private async Task GrantImmediateBonusPointsAsync(ApplicationDbContext context, string userId, Achievement achievement)
    {
        var amount = ParseDecimal(achievement.BonusValue, "amount", 0);
        if (amount <= 0)
            return;

        // Find the user's default ledger account
        var childProfile = await context.ChildProfiles
            .Include(p => p.LedgerAccounts)
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (childProfile == null)
        {
            _logger.LogWarning("No child profile found for user {UserId} when granting bonus points", userId);
            return;
        }

        var defaultAccount = childProfile.LedgerAccounts
            .FirstOrDefault(a => a.IsDefault && a.IsActive)
            ?? childProfile.LedgerAccounts.FirstOrDefault(a => a.IsActive);

        if (defaultAccount == null)
        {
            _logger.LogWarning("No ledger account found for user {UserId} when granting bonus points", userId);
            return;
        }

        var transaction = new LedgerTransaction
        {
            LedgerAccountId = defaultAccount.Id,
            UserId = userId,
            Amount = amount,
            Type = TransactionType.Bonus,
            Description = $"Achievement Bonus: {achievement.Name}",
            TransactionDate = _dateProvider.Today,
            CreatedAt = DateTime.UtcNow
        };

        context.LedgerTransactions.Add(transaction);
        _logger.LogInformation("Granted ${Amount} bonus points to user {UserId} for achievement {Code}",
            amount, userId, achievement.Code);
    }

    private async Task<bool> UseOneTimeBonusAsync(string userId, AchievementBonusType bonusType)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var bonus = await context.UserAchievementBonuses
            .Where(b => b.UserId == userId && b.BonusType == bonusType && b.IsActive)
            .Where(b => b.RemainingUses.HasValue && b.RemainingUses > 0)
            .OrderBy(b => b.GrantedAt) // Use oldest first
            .FirstOrDefaultAsync();

        if (bonus == null)
            return false;

        bonus.RemainingUses--;
        bonus.LastUsedAt = DateTime.UtcNow;
        
        if (bonus.RemainingUses <= 0)
        {
            bonus.IsActive = false;
        }

        await context.SaveChangesAsync();
        
        _logger.LogInformation("Used {BonusType} bonus for user {UserId}, {Remaining} uses remaining",
            bonusType, userId, bonus.RemainingUses);
        
        return true;
    }

    private decimal CalculatePointMultiplier(List<UserAchievementBonus> bonuses)
    {
        var multiplier = 1.0m;
        
        foreach (var bonus in bonuses.Where(b => b.BonusType == AchievementBonusType.PointMultiplier))
        {
            var bonusMultiplier = ParseDecimal(bonus.BonusValue, "multiplier", 1.0m);
            multiplier *= bonusMultiplier;
        }

        // Check for double point day (adds +100%)
        if (bonuses.Any(b => b.BonusType == AchievementBonusType.DoublePointDay))
        {
            multiplier *= 2.0m;
        }

        return Math.Min(multiplier, MaxPointMultiplier);
    }

    private decimal CalculatePenaltyReduction(List<UserAchievementBonus> bonuses)
    {
        var totalReduction = 0m;
        
        foreach (var bonus in bonuses.Where(b => b.BonusType == AchievementBonusType.PenaltyReduction))
        {
            var reduction = ParseDecimal(bonus.BonusValue, "reduction_percent", 0);
            totalReduction += reduction;
        }

        return Math.Min(totalReduction, MaxPenaltyReduction);
    }

    private int CountUsesAvailable(List<UserAchievementBonus> bonuses, AchievementBonusType type)
    {
        return bonuses
            .Where(b => b.BonusType == type && b.RemainingUses.HasValue)
            .Sum(b => b.RemainingUses!.Value);
    }

    private decimal CalculateCashOutReduction(List<UserAchievementBonus> bonuses)
    {
        return bonuses
            .Where(b => b.BonusType == AchievementBonusType.EarlyCashOut)
            .Sum(b => ParseDecimal(b.BonusValue, "threshold_reduction", 0));
    }

    private int CalculateTrustIncrease(List<UserAchievementBonus> bonuses)
    {
        return bonuses
            .Where(b => b.BonusType == AchievementBonusType.TrustIncrease)
            .Sum(b => ParseInt(b.BonusValue, "level_increase", 0));
    }

    private List<string> GetProfileBadges(List<UserAchievementBonus> bonuses)
    {
        return bonuses
            .Where(b => b.BonusType == AchievementBonusType.ProfileBadge)
            .Select(b => ParseString(b.BonusValue, "badge_key", ""))
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    private string GetDefaultBonusDescription(UserAchievementBonus bonus)
    {
        return bonus.BonusType switch
        {
            AchievementBonusType.PointMultiplier => $"{ParseDecimal(bonus.BonusValue, "multiplier", 1):P0} point multiplier",
            AchievementBonusType.OneTimeForgiveness => $"Forgiveness ({bonus.RemainingUses} use{(bonus.RemainingUses != 1 ? "s" : "")} left)",
            AchievementBonusType.ReminderSuppression => "Reminders paused",
            AchievementBonusType.DoublePointDay => $"Double points ({bonus.RemainingUses} day{(bonus.RemainingUses != 1 ? "s" : "")} left)",
            AchievementBonusType.PenaltyReduction => $"{ParseDecimal(bonus.BonusValue, "reduction_percent", 0):P0} penalty reduction",
            AchievementBonusType.StreakProtection => $"Streak protection ({bonus.RemainingUses} use{(bonus.RemainingUses != 1 ? "s" : "")} left)",
            AchievementBonusType.EarlyCashOut => $"${ParseDecimal(bonus.BonusValue, "threshold_reduction", 0):F2} lower cash-out",
            AchievementBonusType.TrustIncrease => "Increased autonomy",
            AchievementBonusType.ProfileBadge => "Profile badge",
            _ => "Active bonus"
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // JSON PARSING HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static int ParseInt(string? json, string key, int defaultValue)
    {
        if (string.IsNullOrEmpty(json))
            return defaultValue;
        
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var value))
                return value.GetInt32();
        }
        catch { }
        return defaultValue;
    }

    private static decimal ParseDecimal(string? json, string key, decimal? defaultValue)
    {
        if (string.IsNullOrEmpty(json))
            return defaultValue ?? 0;
        
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var value))
                return value.GetDecimal();
        }
        catch { }
        return defaultValue ?? 0;
    }

    private static string ParseString(string? json, string key, string defaultValue)
    {
        if (string.IsNullOrEmpty(json))
            return defaultValue;
        
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var value))
                return value.GetString() ?? defaultValue;
        }
        catch { }
        return defaultValue;
    }
}
