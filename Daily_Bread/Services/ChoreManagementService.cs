using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Services;

/// <summary>
/// DTO for creating/updating chore definitions.
/// </summary>
public class ChoreDefinitionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AssignedUserId { get; set; }
    public decimal Value { get; set; }
    public ChoreScheduleType ScheduleType { get; set; } = ChoreScheduleType.SpecificDays;
    public DaysOfWeek ActiveDays { get; set; } = DaysOfWeek.All;
    public int WeeklyTargetCount { get; set; } = 1;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AutoApprove { get; set; } = false;
    public int SortOrder { get; set; }
}

/// <summary>
/// DTO for user selection dropdowns.
/// </summary>
public class UserSelectItem
{
    public required string Id { get; init; }
    public required string UserName { get; init; }
    public required string Role { get; init; }
}

/// <summary>
/// Service for managing chore definitions (CRUD operations).
/// </summary>
public interface IChoreManagementService
{
    Task<List<ChoreDefinition>> GetAllChoresAsync(bool includeInactive = false);
    Task<ChoreDefinition?> GetChoreByIdAsync(int id);
    Task<ServiceResult<ChoreDefinition>> CreateChoreAsync(ChoreDefinitionDto dto);
    Task<ServiceResult> UpdateChoreAsync(ChoreDefinitionDto dto);
    Task<ServiceResult> DeleteChoreAsync(int id);
    Task<ServiceResult> ToggleActiveAsync(int id);
    Task<List<UserSelectItem>> GetAssignableUsersAsync();
}

public class ChoreManagementService : IChoreManagementService
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly UserManager<ApplicationUser> _userManager;

    public ChoreManagementService(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        UserManager<ApplicationUser> userManager)
    {
        _contextFactory = contextFactory;
        _userManager = userManager;
    }

    public async Task<List<ChoreDefinition>> GetAllChoresAsync(bool includeInactive = false)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var query = context.ChoreDefinitions
            .Include(c => c.AssignedUser)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<ChoreDefinition?> GetChoreByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        return await context.ChoreDefinitions
            .Include(c => c.AssignedUser)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<ServiceResult<ChoreDefinition>> CreateChoreAsync(ChoreDefinitionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return ServiceResult<ChoreDefinition>.Fail("Chore name is required.");
        }

        if (dto.Value < 0)
        {
            return ServiceResult<ChoreDefinition>.Fail("Value cannot be negative.");
        }

        // Validate weekly target count for frequency-based chores
        if (dto.ScheduleType == ChoreScheduleType.WeeklyFrequency && dto.WeeklyTargetCount < 1)
        {
            return ServiceResult<ChoreDefinition>.Fail("Weekly target count must be at least 1.");
        }

        if (dto.ScheduleType == ChoreScheduleType.WeeklyFrequency && dto.WeeklyTargetCount > 7)
        {
            return ServiceResult<ChoreDefinition>.Fail("Weekly target count cannot exceed 7.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        // Check for duplicate name
        var exists = await context.ChoreDefinitions
            .AnyAsync(c => c.Name.ToLower() == dto.Name.ToLower());

        if (exists)
        {
            return ServiceResult<ChoreDefinition>.Fail($"A chore named '{dto.Name}' already exists.");
        }

        var chore = new ChoreDefinition
        {
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            AssignedUserId = dto.AssignedUserId,
            Value = dto.Value,
            ScheduleType = dto.ScheduleType,
            ActiveDays = dto.ActiveDays,
            WeeklyTargetCount = dto.ScheduleType == ChoreScheduleType.WeeklyFrequency ? dto.WeeklyTargetCount : 1,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            IsActive = dto.IsActive,
            AutoApprove = dto.AutoApprove,
            SortOrder = dto.SortOrder,
            CreatedAt = DateTime.UtcNow
        };

        context.ChoreDefinitions.Add(chore);
        await context.SaveChangesAsync();

        // Reload with navigation properties
        await context.Entry(chore).Reference(c => c.AssignedUser).LoadAsync();

        return ServiceResult<ChoreDefinition>.Ok(chore);
    }

    public async Task<ServiceResult> UpdateChoreAsync(ChoreDefinitionDto dto)
    {
        if (dto.Id <= 0)
        {
            return ServiceResult.Fail("Invalid chore ID.");
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return ServiceResult.Fail("Chore name is required.");
        }

        if (dto.Value < 0)
        {
            return ServiceResult.Fail("Value cannot be negative.");
        }

        // Validate weekly target count for frequency-based chores
        if (dto.ScheduleType == ChoreScheduleType.WeeklyFrequency && dto.WeeklyTargetCount < 1)
        {
            return ServiceResult.Fail("Weekly target count must be at least 1.");
        }

        if (dto.ScheduleType == ChoreScheduleType.WeeklyFrequency && dto.WeeklyTargetCount > 7)
        {
            return ServiceResult.Fail("Weekly target count cannot exceed 7.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var chore = await context.ChoreDefinitions.FindAsync(dto.Id);
        if (chore == null)
        {
            return ServiceResult.Fail("Chore not found.");
        }

        // Check for duplicate name (excluding current chore)
        var exists = await context.ChoreDefinitions
            .AnyAsync(c => c.Id != dto.Id && c.Name.ToLower() == dto.Name.ToLower());

        if (exists)
        {
            return ServiceResult.Fail($"A chore named '{dto.Name}' already exists.");
        }

        chore.Name = dto.Name.Trim();
        chore.Description = dto.Description?.Trim();
        chore.AssignedUserId = dto.AssignedUserId;
        chore.Value = dto.Value;
        chore.ScheduleType = dto.ScheduleType;
        chore.ActiveDays = dto.ActiveDays;
        chore.WeeklyTargetCount = dto.ScheduleType == ChoreScheduleType.WeeklyFrequency ? dto.WeeklyTargetCount : 1;
        chore.StartDate = dto.StartDate;
        chore.EndDate = dto.EndDate;
        chore.IsActive = dto.IsActive;
        chore.AutoApprove = dto.AutoApprove;
        chore.SortOrder = dto.SortOrder;
        chore.ModifiedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteChoreAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var chore = await context.ChoreDefinitions
            .Include(c => c.ChoreLogs)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (chore == null)
        {
            return ServiceResult.Fail("Chore not found.");
        }

        // If chore has logs, soft delete by deactivating
        if (chore.ChoreLogs.Count > 0)
        {
            chore.IsActive = false;
            chore.ModifiedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return ServiceResult.Ok();
        }

        // No logs, can hard delete
        context.ChoreDefinitions.Remove(chore);
        await context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ToggleActiveAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        
        var chore = await context.ChoreDefinitions.FindAsync(id);
        if (chore == null)
        {
            return ServiceResult.Fail("Chore not found.");
        }

        chore.IsActive = !chore.IsActive;
        chore.ModifiedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<List<UserSelectItem>> GetAssignableUsersAsync()
    {
        var users = await _userManager.Users.ToListAsync();
        var result = new List<UserSelectItem>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var primaryRole = roles.Contains("Parent") ? "Parent" : 
                             roles.Contains("Child") ? "Child" : "User";

            result.Add(new UserSelectItem
            {
                Id = user.Id,
                UserName = user.UserName ?? "Unknown",
                Role = primaryRole
            });
        }

        return result.OrderBy(u => u.Role).ThenBy(u => u.UserName).ToList();
    }
}
