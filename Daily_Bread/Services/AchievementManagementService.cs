using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Daily_Bread.Services;

/// <summary>
/// DTO for creating/updating achievements from the parent management UI.
/// </summary>
public class AchievementDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? HiddenHint { get; set; }
    public string Icon { get; set; } = string.Empty;
    public string? LockedIcon { get; set; }
    public AchievementCategory Category { get; set; } = AchievementCategory.Special;
    public AchievementRarity Rarity { get; set; } = AchievementRarity.Common;
    public int Points { get; set; } = 10;
    public int SortOrder { get; set; }
    public bool IsHidden { get; set; }
    public bool IsLegendary { get; set; }
    public bool IsVisibleBeforeUnlock { get; set; } = true;
    public bool IsActive { get; set; } = true;

    public UnlockConditionType UnlockConditionType { get; set; } = UnlockConditionType.Manual;
    public string? UnlockConditionValue { get; set; }
    public int? ProgressTarget { get; set; }

    /// <summary>
    /// Real-world reward (cash or item), gated on parent approval when earned. Null means
    /// no tangible reward. Maps to Achievement.BonusType == TangibleReward - mutually
    /// exclusive with the gameplay-bonus system (PointMultiplier, StreakProtection, etc.);
    /// setting a reward here overwrites any existing gameplay bonus on this achievement.
    /// </summary>
    public RewardClaimType? RewardType { get; set; }

    /// <summary>For RewardType == Cash: the amount credited to the child on approval.</summary>
    public decimal? RewardCashAmount { get; set; }

    /// <summary>For RewardType == Item: the child-visible name of the item.</summary>
    public string? RewardItemLabel { get; set; }

    /// <summary>For RewardType == Item: parent's budgeting estimate (reporting only).</summary>
    public decimal? RewardItemEstValue { get; set; }
}

/// <summary>
/// Service for managing achievements (CRUD) from the parent UI.
/// Achievements are never hard-deleted - deactivating sets IsActive = false so
/// earned history (UserAchievement rows) and progress remain intact.
/// </summary>
public interface IAchievementManagementService
{
    Task<List<Achievement>> GetAllAchievementsAsync(bool includeInactive = false);
    Task<Achievement?> GetAchievementByIdAsync(int id);
    Task<ServiceResult<Achievement>> CreateAchievementAsync(AchievementDto dto);
    Task<ServiceResult> UpdateAchievementAsync(AchievementDto dto);
    Task<ServiceResult> ToggleActiveAsync(int id);
}

public class AchievementManagementService : IAchievementManagementService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

    public AchievementManagementService(IDbContextFactory<ApplicationDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<Achievement>> GetAllAchievementsAsync(bool includeInactive = false)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var query = context.Achievements.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(a => a.IsActive);
        }

        return await query
            .OrderBy(a => a.Category)
            .ThenBy(a => a.SortOrder)
            .ThenBy(a => a.Rarity)
            .ToListAsync();
    }

    public async Task<Achievement?> GetAchievementByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Achievements.FindAsync(id);
    }

    public async Task<ServiceResult<Achievement>> CreateAchievementAsync(AchievementDto dto)
    {
        var validationError = Validate(dto);
        if (validationError != null)
        {
            return ServiceResult<Achievement>.Fail(validationError);
        }

        await using var context = await _contextFactory.CreateDbContextAsync();

        var existingCodes = await context.Achievements.Select(a => a.Code).ToListAsync();
        var code = GenerateUniqueCode(dto.Name, existingCodes);

        var achievement = new Achievement
        {
            Code = code,
            Name = dto.Name.Trim(),
            Description = dto.Description.Trim(),
            HiddenHint = dto.HiddenHint?.Trim(),
            Icon = dto.Icon.Trim(),
            LockedIcon = dto.LockedIcon?.Trim(),
            Category = dto.Category,
            Rarity = dto.Rarity,
            Points = dto.Points,
            SortOrder = dto.SortOrder,
            IsHidden = dto.IsHidden,
            IsLegendary = dto.IsLegendary,
            IsVisibleBeforeUnlock = dto.IsVisibleBeforeUnlock,
            IsActive = dto.IsActive,
            UnlockConditionType = dto.UnlockConditionType,
            UnlockConditionValue = dto.UnlockConditionValue,
            ProgressTarget = dto.ProgressTarget,
            CreatedAt = DateTime.UtcNow
        };

        ApplyReward(achievement, dto);

        context.Achievements.Add(achievement);
        await context.SaveChangesAsync();

        return ServiceResult<Achievement>.Ok(achievement);
    }

    public async Task<ServiceResult> UpdateAchievementAsync(AchievementDto dto)
    {
        if (dto.Id <= 0)
        {
            return ServiceResult.Fail("Invalid achievement ID.");
        }

        var validationError = Validate(dto);
        if (validationError != null)
        {
            return ServiceResult.Fail(validationError);
        }

        await using var context = await _contextFactory.CreateDbContextAsync();

        var achievement = await context.Achievements.FindAsync(dto.Id);
        if (achievement == null)
        {
            return ServiceResult.Fail("Achievement not found.");
        }

        achievement.Name = dto.Name.Trim();
        achievement.Description = dto.Description.Trim();
        achievement.HiddenHint = dto.HiddenHint?.Trim();
        achievement.Icon = dto.Icon.Trim();
        achievement.LockedIcon = dto.LockedIcon?.Trim();
        achievement.Category = dto.Category;
        achievement.Rarity = dto.Rarity;
        achievement.Points = dto.Points;
        achievement.SortOrder = dto.SortOrder;
        achievement.IsHidden = dto.IsHidden;
        achievement.IsLegendary = dto.IsLegendary;
        achievement.IsVisibleBeforeUnlock = dto.IsVisibleBeforeUnlock;
        achievement.IsActive = dto.IsActive;
        achievement.UnlockConditionType = dto.UnlockConditionType;
        achievement.UnlockConditionValue = dto.UnlockConditionValue;
        achievement.ProgressTarget = dto.ProgressTarget;
        achievement.ModifiedAt = DateTime.UtcNow;

        ApplyReward(achievement, dto);

        await context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    /// <summary>
    /// Soft delete only. Earned achievements (UserAchievement rows) and progress
    /// records reference AchievementId and must survive deactivation.
    /// </summary>
    public async Task<ServiceResult> ToggleActiveAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var achievement = await context.Achievements.FindAsync(id);
        if (achievement == null)
        {
            return ServiceResult.Fail("Achievement not found.");
        }

        achievement.IsActive = !achievement.IsActive;
        achievement.ModifiedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    private static string? Validate(AchievementDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return "Achievement name is required.";
        }

        if (string.IsNullOrWhiteSpace(dto.Description))
        {
            return "Description is required.";
        }

        if (string.IsNullOrWhiteSpace(dto.Icon))
        {
            return "Icon is required.";
        }

        if (dto.Points < 0)
        {
            return "Points cannot be negative.";
        }

        if (dto.ProgressTarget is < 1)
        {
            return "Progress target must be at least 1.";
        }

        if (dto.IsHidden && string.IsNullOrWhiteSpace(dto.HiddenHint) && !dto.IsVisibleBeforeUnlock)
        {
            return "Hidden achievements that are invisible before unlock should have a hint, or earning them will show no clue at all.";
        }

        if (dto.RewardType == RewardClaimType.Cash && dto.RewardCashAmount is not > 0)
        {
            return "Cash reward amount must be greater than zero.";
        }

        if (dto.RewardType == RewardClaimType.Item && string.IsNullOrWhiteSpace(dto.RewardItemLabel))
        {
            return "Item reward needs a name.";
        }

        return null;
    }

    /// <summary>
    /// Maps the DTO's reward selection onto Achievement.BonusType/BonusValue/BonusDescription.
    /// Leaves an existing non-reward gameplay bonus (PointMultiplier, StreakProtection, etc.)
    /// untouched unless the parent explicitly sets a tangible reward, since the two systems
    /// share the same BonusType slot and this form only edits the reward side of it.
    /// </summary>
    private static void ApplyReward(Achievement achievement, AchievementDto dto)
    {
        if (dto.RewardType == RewardClaimType.Cash)
        {
            achievement.BonusType = AchievementBonusType.TangibleReward;
            achievement.BonusValue = JsonSerializer.Serialize(new { type = "cash", amount = dto.RewardCashAmount ?? 0 });
            achievement.BonusDescription = $"${dto.RewardCashAmount:F2} reward (parent approval required)";
        }
        else if (dto.RewardType == RewardClaimType.Item)
        {
            achievement.BonusType = AchievementBonusType.TangibleReward;
            achievement.BonusValue = JsonSerializer.Serialize(new
            {
                type = "item",
                label = dto.RewardItemLabel,
                est_value = dto.RewardItemEstValue
            });
            achievement.BonusDescription = $"{dto.RewardItemLabel} (parent approval required)";
        }
        else if (achievement.BonusType == AchievementBonusType.TangibleReward)
        {
            achievement.BonusType = null;
            achievement.BonusValue = null;
            achievement.BonusDescription = null;
        }
    }

    private static string GenerateUniqueCode(string name, List<string> existingCodes)
    {
        var slug = Regex.Replace(name.ToUpperInvariant(), @"[^A-Z0-9]+", "_").Trim('_');
        if (string.IsNullOrEmpty(slug))
        {
            slug = "ACHIEVEMENT";
        }

        var code = $"CUSTOM_{slug}";
        var suffix = 2;
        while (existingCodes.Contains(code))
        {
            code = $"CUSTOM_{slug}_{suffix}";
            suffix++;
        }

        return code;
    }
}
