using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// DTO for savings goal with progress info.
/// </summary>
public class SavingsGoalProgress
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public decimal TargetAmount { get; init; }
    public decimal CurrentBalance { get; init; }
    public string? ImageUrl { get; init; }
    public int Priority { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsCompleted { get; init; }
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public int ProgressPercent => TargetAmount > 0 
        ? (int)Math.Min(100, Math.Round(CurrentBalance / TargetAmount * 100)) 
        : 0;

    /// <summary>
    /// Amount remaining to reach goal.
    /// </summary>
    public decimal AmountRemaining => Math.Max(0, TargetAmount - CurrentBalance);

    /// <summary>
    /// Whether the goal can be completed (balance >= target).
    /// </summary>
    public bool CanComplete => CurrentBalance >= TargetAmount && !IsCompleted;
}

/// <summary>
/// DTO for creating/updating a savings goal.
/// </summary>
public class SavingsGoalDto
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal TargetAmount { get; set; }
    public string? ImageUrl { get; set; }
    public int Priority { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>
/// Service interface for savings goals.
/// </summary>
public interface ISavingsGoalService
{
    /// <summary>
    /// Gets all active savings goals for a user with progress.
    /// </summary>
    Task<List<SavingsGoalProgress>> GetGoalsWithProgressAsync(string userId);

    /// <summary>
    /// Gets the primary savings goal for a user.
    /// </summary>
    Task<SavingsGoalProgress?> GetPrimaryGoalAsync(string userId);

    /// <summary>
    /// Gets a specific goal by ID.
    /// </summary>
    Task<SavingsGoalProgress?> GetGoalByIdAsync(int goalId, string userId);

    /// <summary>
    /// Creates a new savings goal.
    /// </summary>
    Task<ServiceResult<int>> CreateGoalAsync(string userId, SavingsGoalDto dto);

    /// <summary>
    /// Updates an existing savings goal.
    /// </summary>
    Task<ServiceResult> UpdateGoalAsync(string userId, SavingsGoalDto dto);

    /// <summary>
    /// Sets a goal as the primary goal.
    /// </summary>
    Task<ServiceResult> SetPrimaryGoalAsync(string userId, int goalId);

    /// <summary>
    /// Marks a goal as completed/achieved.
    /// </summary>
    Task<ServiceResult> CompleteGoalAsync(string userId, int goalId);

    /// <summary>
    /// Deletes/deactivates a savings goal.
    /// </summary>
    Task<ServiceResult> DeleteGoalAsync(string userId, int goalId);

    /// <summary>
    /// Reorders goals by priority.
    /// </summary>
    Task<ServiceResult> ReorderGoalsAsync(string userId, List<int> goalIdsInOrder);
}

/// <summary>
/// Service for managing savings goals.
/// </summary>
public class SavingsGoalService : ISavingsGoalService
{
    private readonly ApplicationDbContext _context;
    private readonly ILedgerService _ledgerService;

    public SavingsGoalService(ApplicationDbContext context, ILedgerService ledgerService)
    {
        _context = context;
        _ledgerService = ledgerService;
    }

    public async Task<List<SavingsGoalProgress>> GetGoalsWithProgressAsync(string userId)
    {
        var balance = await _ledgerService.GetUserBalanceAsync(userId);

        var goals = await _context.SavingsGoals
            .Where(g => g.UserId == userId && g.IsActive)
            .OrderBy(g => g.Priority)
            .ThenBy(g => g.CreatedAt)
            .ToListAsync();

        return goals.Select(g => MapToProgress(g, balance)).ToList();
    }

    public async Task<SavingsGoalProgress?> GetPrimaryGoalAsync(string userId)
    {
        var balance = await _ledgerService.GetUserBalanceAsync(userId);

        var goal = await _context.SavingsGoals
            .Where(g => g.UserId == userId && g.IsActive && g.IsPrimary && !g.IsCompleted)
            .FirstOrDefaultAsync();

        // If no primary, get the first active goal
        goal ??= await _context.SavingsGoals
            .Where(g => g.UserId == userId && g.IsActive && !g.IsCompleted)
            .OrderBy(g => g.Priority)
            .FirstOrDefaultAsync();

        return goal != null ? MapToProgress(goal, balance) : null;
    }

    public async Task<SavingsGoalProgress?> GetGoalByIdAsync(int goalId, string userId)
    {
        var balance = await _ledgerService.GetUserBalanceAsync(userId);

        var goal = await _context.SavingsGoals
            .Where(g => g.Id == goalId && g.UserId == userId)
            .FirstOrDefaultAsync();

        return goal != null ? MapToProgress(goal, balance) : null;
    }

    public async Task<ServiceResult<int>> CreateGoalAsync(string userId, SavingsGoalDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return ServiceResult<int>.Fail("Goal name is required.");
        }

        if (dto.TargetAmount <= 0)
        {
            return ServiceResult<int>.Fail("Target amount must be greater than zero.");
        }

        // If this is set as primary, unset other primary goals
        if (dto.IsPrimary)
        {
            await UnsetPrimaryGoalsAsync(userId);
        }

        var goal = new SavingsGoal
        {
            UserId = userId,
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            TargetAmount = dto.TargetAmount,
            ImageUrl = dto.ImageUrl?.Trim(),
            Priority = dto.Priority,
            IsPrimary = dto.IsPrimary,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.SavingsGoals.Add(goal);
        await _context.SaveChangesAsync();

        return ServiceResult<int>.Ok(goal.Id);
    }

    public async Task<ServiceResult> UpdateGoalAsync(string userId, SavingsGoalDto dto)
    {
        var goal = await _context.SavingsGoals
            .FirstOrDefaultAsync(g => g.Id == dto.Id && g.UserId == userId);

        if (goal == null)
        {
            return ServiceResult.Fail("Goal not found.");
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return ServiceResult.Fail("Goal name is required.");
        }

        if (dto.TargetAmount <= 0)
        {
            return ServiceResult.Fail("Target amount must be greater than zero.");
        }

        // If this is set as primary, unset other primary goals
        if (dto.IsPrimary && !goal.IsPrimary)
        {
            await UnsetPrimaryGoalsAsync(userId);
        }

        goal.Name = dto.Name.Trim();
        goal.Description = dto.Description?.Trim();
        goal.TargetAmount = dto.TargetAmount;
        goal.ImageUrl = dto.ImageUrl?.Trim();
        goal.Priority = dto.Priority;
        goal.IsPrimary = dto.IsPrimary;
        goal.ModifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SetPrimaryGoalAsync(string userId, int goalId)
    {
        var goal = await _context.SavingsGoals
            .FirstOrDefaultAsync(g => g.Id == goalId && g.UserId == userId && g.IsActive);

        if (goal == null)
        {
            return ServiceResult.Fail("Goal not found.");
        }

        await UnsetPrimaryGoalsAsync(userId);

        goal.IsPrimary = true;
        goal.ModifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> CompleteGoalAsync(string userId, int goalId)
    {
        var goal = await _context.SavingsGoals
            .FirstOrDefaultAsync(g => g.Id == goalId && g.UserId == userId && g.IsActive);

        if (goal == null)
        {
            return ServiceResult.Fail("Goal not found.");
        }

        if (goal.IsCompleted)
        {
            return ServiceResult.Fail("Goal is already completed.");
        }

        goal.IsCompleted = true;
        goal.CompletedAt = DateTime.UtcNow;
        goal.ModifiedAt = DateTime.UtcNow;

        // If this was primary, find next goal to make primary
        if (goal.IsPrimary)
        {
            goal.IsPrimary = false;
            var nextGoal = await _context.SavingsGoals
                .Where(g => g.UserId == userId && g.IsActive && !g.IsCompleted && g.Id != goalId)
                .OrderBy(g => g.Priority)
                .FirstOrDefaultAsync();

            if (nextGoal != null)
            {
                nextGoal.IsPrimary = true;
            }
        }

        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteGoalAsync(string userId, int goalId)
    {
        var goal = await _context.SavingsGoals
            .FirstOrDefaultAsync(g => g.Id == goalId && g.UserId == userId);

        if (goal == null)
        {
            return ServiceResult.Fail("Goal not found.");
        }

        // Soft delete
        goal.IsActive = false;
        goal.ModifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ReorderGoalsAsync(string userId, List<int> goalIdsInOrder)
    {
        var goals = await _context.SavingsGoals
            .Where(g => g.UserId == userId && g.IsActive)
            .ToListAsync();

        for (int i = 0; i < goalIdsInOrder.Count; i++)
        {
            var goal = goals.FirstOrDefault(g => g.Id == goalIdsInOrder[i]);
            if (goal != null)
            {
                goal.Priority = i;
                goal.ModifiedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    private async Task UnsetPrimaryGoalsAsync(string userId)
    {
        var primaryGoals = await _context.SavingsGoals
            .Where(g => g.UserId == userId && g.IsPrimary)
            .ToListAsync();

        foreach (var g in primaryGoals)
        {
            g.IsPrimary = false;
            g.ModifiedAt = DateTime.UtcNow;
        }
    }

    private static SavingsGoalProgress MapToProgress(SavingsGoal goal, decimal currentBalance)
    {
        return new SavingsGoalProgress
        {
            Id = goal.Id,
            Name = goal.Name,
            Description = goal.Description,
            TargetAmount = goal.TargetAmount,
            CurrentBalance = currentBalance,
            ImageUrl = goal.ImageUrl,
            Priority = goal.Priority,
            IsPrimary = goal.IsPrimary,
            IsCompleted = goal.IsCompleted,
            CompletedAt = goal.CompletedAt
        };
    }
}
