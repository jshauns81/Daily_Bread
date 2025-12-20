using Daily_Bread.Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Daily_Bread.Data;

/// <summary>
/// Seeds default chores for new child users.
/// Based on Weekly Chore Chart with ~$15/week total value.
/// </summary>
public static class SeedChores
{
    /// <summary>
    /// Seeds default chores for all Child users who don't have any chores assigned.
    /// Called from Program.cs after user seeding.
    /// </summary>
    public static async Task SeedDefaultChoresAsync(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        // Check if chore seeding is enabled
        var seedChores = configuration.GetValue<bool?>("Seed:SeedChores") ?? true;
        if (!seedChores)
        {
            Console.WriteLine("Seed:SeedChores is false. Skipping chore seeding.");
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Get all users in the Child role
        var childUsers = await userManager.GetUsersInRoleAsync(SeedData.ChildRole);
        
        foreach (var child in childUsers)
        {
            // Check if this child already has chores
            var hasChores = await context.ChoreDefinitions
                .AnyAsync(c => c.AssignedUserId == child.Id);
            
            if (!hasChores)
            {
                Console.WriteLine($"Seeding default chores for child: {child.UserName}");
                await CreateDefaultChoresForChildAsync(context, child.Id);
            }
        }
    }

    /// <summary>
    /// Creates default chores for a specific child user.
    /// Can be called when a new Child user is created.
    /// </summary>
    public static async Task CreateDefaultChoresForChildAsync(ApplicationDbContext context, string childUserId)
    {
        var chores = GetDefaultChores(childUserId);
        
        context.ChoreDefinitions.AddRange(chores);
        await context.SaveChangesAsync();
        
        Console.WriteLine($"Created {chores.Count} default chores for user {childUserId}");
    }

    /// <summary>
    /// Returns the list of default chores.
    /// Values are distributed to total ~$15/week when all chores are completed.
    /// 
    /// Daily chores (9 chores x 7 days = 63 instances):
    ///   - Simple daily tasks: $0.10-$0.15 each
    ///   - Total daily: ~$7-8/week
    /// 
    /// Weekly chores (6 chores, various frequencies):
    ///   - Higher value tasks: $0.50-$1.50 each
    ///   - Total weekly: ~$7-8/week
    /// </summary>
    private static List<ChoreDefinition> GetDefaultChores(string childUserId)
    {
        var sortOrder = 0;
        
        return new List<ChoreDefinition>
        {
            // ============================================
            // DAILY CHORES (Every day, Mon-Sun)
            // ============================================
            
            new()
            {
                Name = "Make Bed",
                Description = "Make your bed neatly each morning",
                AssignedUserId = childUserId,
                Value = 0.10m,
                ScheduleType = ChoreScheduleType.SpecificDays,
                ActiveDays = DaysOfWeek.All,
                AutoApprove = true,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Tidy Room",
                Description = "Keep your room clean and organized",
                AssignedUserId = childUserId,
                Value = 0.15m,
                ScheduleType = ChoreScheduleType.SpecificDays,
                ActiveDays = DaysOfWeek.All,
                AutoApprove = false,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Trash/Recycling (Room)",
                Description = "Empty trash and recycling from your room",
                AssignedUserId = childUserId,
                Value = 0.10m,
                ScheduleType = ChoreScheduleType.SpecificDays,
                ActiveDays = DaysOfWeek.All,
                AutoApprove = true,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Feed Gemma - Morning",
                Description = "Feed Gemma breakfast",
                AssignedUserId = childUserId,
                Value = 0.15m,
                ScheduleType = ChoreScheduleType.SpecificDays,
                ActiveDays = DaysOfWeek.All,
                AutoApprove = true,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Feed Gemma - Evening",
                Description = "Feed Gemma dinner",
                AssignedUserId = childUserId,
                Value = 0.15m,
                ScheduleType = ChoreScheduleType.SpecificDays,
                ActiveDays = DaysOfWeek.All,
                AutoApprove = true,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Set & Clear Table",
                Description = "Set the table for dinner and clear it after",
                AssignedUserId = childUserId,
                Value = 0.15m,
                ScheduleType = ChoreScheduleType.SpecificDays,
                ActiveDays = DaysOfWeek.All,
                AutoApprove = false,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Load/Unload Dishwasher",
                Description = "Help with the dishwasher",
                AssignedUserId = childUserId,
                Value = 0.15m,
                ScheduleType = ChoreScheduleType.SpecificDays,
                ActiveDays = DaysOfWeek.All,
                AutoApprove = false,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Have Bag Ready",
                Description = "Pack your bag for the next day",
                AssignedUserId = childUserId,
                Value = 0.10m,
                ScheduleType = ChoreScheduleType.SpecificDays,
                ActiveDays = DaysOfWeek.Weekdays, // School days only
                AutoApprove = true,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Reading Time",
                Description = "Read for 20-40 minutes",
                AssignedUserId = childUserId,
                Value = 0.25m,
                ScheduleType = ChoreScheduleType.SpecificDays,
                ActiveDays = DaysOfWeek.All,
                AutoApprove = true,
                SortOrder = ++sortOrder
            },

            // ============================================
            // WEEKLY CHORES (Specific days or X times/week)
            // ============================================
            
            new()
            {
                Name = "Use Your Brain",
                Description = "Educational activity - 20 mins on non-school days",
                AssignedUserId = childUserId,
                Value = 0.75m,
                ScheduleType = ChoreScheduleType.WeeklyFrequency,
                WeeklyTargetCount = 2,
                ActiveDays = DaysOfWeek.Weekends, // Sat/Sun only
                AutoApprove = false,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Trash to Curb",
                Description = "Take trash and recycling bins to the curb",
                AssignedUserId = childUserId,
                Value = 0.75m,
                ScheduleType = ChoreScheduleType.SpecificDays,
                ActiveDays = DaysOfWeek.Sunday,
                AutoApprove = false,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Bring Trash Up",
                Description = "Bring empty trash and recycling bins back up",
                AssignedUserId = childUserId,
                Value = 0.50m,
                ScheduleType = ChoreScheduleType.SpecificDays,
                ActiveDays = DaysOfWeek.Monday,
                AutoApprove = false,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Sort Laundry",
                Description = "Sort dirty laundry into whites and colors",
                AssignedUserId = childUserId,
                Value = 0.75m,
                ScheduleType = ChoreScheduleType.SpecificDays,
                ActiveDays = DaysOfWeek.Saturday,
                AutoApprove = false,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Cleanup Yard",
                Description = "Help clean up the yard",
                AssignedUserId = childUserId,
                Value = 1.00m,
                ScheduleType = ChoreScheduleType.SpecificDays,
                ActiveDays = DaysOfWeek.Saturday,
                AutoApprove = false,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Walk Gemma",
                Description = "Take Gemma for a walk",
                AssignedUserId = childUserId,
                Value = 0.75m,
                ScheduleType = ChoreScheduleType.WeeklyFrequency,
                WeeklyTargetCount = 3,
                ActiveDays = DaysOfWeek.All, // Can be any day
                AutoApprove = false,
                SortOrder = ++sortOrder
            },
            new()
            {
                Name = "Cook Dinner",
                Description = "Pick a night to help cook dinner",
                AssignedUserId = childUserId,
                Value = 1.50m,
                ScheduleType = ChoreScheduleType.WeeklyFrequency,
                WeeklyTargetCount = 1,
                ActiveDays = DaysOfWeek.All, // Can be any day
                AutoApprove = false,
                SortOrder = ++sortOrder
            }
        };
    }

    /// <summary>
    /// Calculates the maximum weekly value if all chores are completed.
    /// Useful for displaying potential earnings.
    /// </summary>
    public static decimal CalculateMaxWeeklyValue(IEnumerable<ChoreDefinition> chores)
    {
        decimal total = 0;
        
        foreach (var chore in chores.Where(c => c.IsActive))
        {
            if (chore.ScheduleType == ChoreScheduleType.SpecificDays)
            {
                // Count the number of active days
                var dayCount = CountActiveDays(chore.ActiveDays);
                total += chore.Value * dayCount;
            }
            else // WeeklyFrequency
            {
                total += chore.Value * chore.WeeklyTargetCount;
            }
        }
        
        return total;
    }

    private static int CountActiveDays(DaysOfWeek days)
    {
        int count = 0;
        if (days.HasFlag(DaysOfWeek.Sunday)) count++;
        if (days.HasFlag(DaysOfWeek.Monday)) count++;
        if (days.HasFlag(DaysOfWeek.Tuesday)) count++;
        if (days.HasFlag(DaysOfWeek.Wednesday)) count++;
        if (days.HasFlag(DaysOfWeek.Thursday)) count++;
        if (days.HasFlag(DaysOfWeek.Friday)) count++;
        if (days.HasFlag(DaysOfWeek.Saturday)) count++;
        return count;
    }
}
