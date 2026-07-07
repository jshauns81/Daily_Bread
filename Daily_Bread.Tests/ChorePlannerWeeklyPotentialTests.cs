using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Guards <see cref="IChorePlannerService.GetWeeklyPotentialAsync"/> - the lightweight
/// "max possible earnings this week" figure that will feed the parent dashboard's
/// "$earned / $possible" row. Must match <see cref="ChorePlannerRow.WeeklyPotential"/>'s
/// counting rules exactly: weekly-goal chores contribute Value x target count regardless
/// of day; fixed-days chores contribute Value x however many scheduled days fall in the week.
/// </summary>
public sealed class ChorePlannerWeeklyPotentialTests : IAsyncLifetime
{
    private const string ChildId = "child-1";
    private readonly DateOnly _today = new(2026, 6, 21); // a Sunday (week start)

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestDbContextFactory _contextFactory = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _contextFactory = new TestDbContextFactory(options);

        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();

        context.Users.Add(new ApplicationUser { Id = ChildId, UserName = "child" });
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task WeeklyGoalChore_Contributes_Value_Times_TargetCount_Regardless_Of_Days()
    {
        // "3x any day" at $2 each = $6 potential, even though ActiveDays only allows weekdays
        // (WeeklyFrequency chores ignore ActiveDays for the potential calc, same as the Planner).
        await AddChoreAsync(new ChoreDefinition
        {
            Name = "Read for 20 minutes",
            AssignedUserId = ChildId,
            EarnValue = 2m,
            ScheduleType = ChoreScheduleType.WeeklyFrequency,
            WeeklyTargetCount = 3,
            ActiveDays = DaysOfWeek.Weekdays,
            IsActive = true
        });

        var planner = CreatePlannerService();
        var potential = await planner.GetWeeklyPotentialAsync(_today, ChildId);

        Assert.Equal(6m, potential);
    }

    [Fact]
    public async Task FixedDaysChore_Contributes_Value_Times_ScheduledDayCount_Within_Week()
    {
        // Mon/Wed/Fri at $5 = 3 scheduled days x $5 = $15 for this specific week.
        await AddChoreAsync(new ChoreDefinition
        {
            Name = "Take out trash",
            AssignedUserId = ChildId,
            EarnValue = 5m,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday,
            IsActive = true
        });

        var planner = CreatePlannerService();
        var potential = await planner.GetWeeklyPotentialAsync(_today, ChildId);

        Assert.Equal(15m, potential);
    }

    [Fact]
    public async Task Mix_Of_WeeklyGoal_And_FixedDays_Chores_Sums_Correctly()
    {
        // Weekly-goal: 3x at $2 = $6. Fixed-days: Mon/Wed/Fri at $5 = $15. Total = $21.
        await AddChoreAsync(new ChoreDefinition
        {
            Name = "Read for 20 minutes",
            AssignedUserId = ChildId,
            EarnValue = 2m,
            ScheduleType = ChoreScheduleType.WeeklyFrequency,
            WeeklyTargetCount = 3,
            IsActive = true
        });
        await AddChoreAsync(new ChoreDefinition
        {
            Name = "Take out trash",
            AssignedUserId = ChildId,
            EarnValue = 5m,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday,
            IsActive = true
        });

        var planner = CreatePlannerService();
        var potential = await planner.GetWeeklyPotentialAsync(_today, ChildId);

        Assert.Equal(21m, potential);
    }

    [Fact]
    public async Task EmptyWeek_With_No_Chores_Returns_Zero()
    {
        var planner = CreatePlannerService();
        var potential = await planner.GetWeeklyPotentialAsync(_today, ChildId);

        Assert.Equal(0m, potential);
    }

    [Fact]
    public async Task ZeroValueChore_Contributes_Nothing_Even_Though_It_Is_Scheduled()
    {
        // An "expectation" chore (a Routine, EarnValue = 0) is scheduled every day but must add
        // $0 to the potential - it isn't a paid Task.
        await AddChoreAsync(new ChoreDefinition
        {
            Name = "Make bed",
            AssignedUserId = ChildId,
            Kind = ChoreKind.Routine,
            EarnValue = 0m,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.All,
            IsActive = true
        });

        var planner = CreatePlannerService();
        var potential = await planner.GetWeeklyPotentialAsync(_today, ChildId);

        Assert.Equal(0m, potential);
    }

    [Fact]
    public async Task FixedDaysChore_Honors_Add_And_Remove_Overrides_For_The_Week()
    {
        // Base schedule: every weekday (Mon-Fri) at $4 = 5 days = $20.
        // Remove override on Monday (-1 day), Add override on Saturday (+1 day): net still 5 days = $20.
        var chore = new ChoreDefinition
        {
            Name = "Walk the dog",
            AssignedUserId = ChildId,
            EarnValue = 4m,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.Weekdays,
            IsActive = true
        };
        await AddChoreAsync(chore);

        await using var context = await _contextFactory.CreateDbContextAsync();
        var monday = _today.AddDays(1);
        var saturday = _today.AddDays(6);
        context.ChoreScheduleOverrides.AddRange(
            new ChoreScheduleOverride { ChoreDefinitionId = chore.Id, Date = monday, Type = ScheduleOverrideType.Remove },
            new ChoreScheduleOverride { ChoreDefinitionId = chore.Id, Date = saturday, Type = ScheduleOverrideType.Add });
        await context.SaveChangesAsync();

        var planner = CreatePlannerService();
        var potential = await planner.GetWeeklyPotentialAsync(_today, ChildId);

        Assert.Equal(20m, potential);
    }

    [Fact]
    public async Task NullUserId_Aggregates_Potential_Across_All_Children()
    {
        const string secondChildId = "child-2";
        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            context.Users.Add(new ApplicationUser { Id = secondChildId, UserName = "child2" });
            await context.SaveChangesAsync();
        }

        await AddChoreAsync(new ChoreDefinition
        {
            Name = "Chore A",
            AssignedUserId = ChildId,
            EarnValue = 3m,
            ScheduleType = ChoreScheduleType.WeeklyFrequency,
            WeeklyTargetCount = 2,
            IsActive = true
        });
        await AddChoreAsync(new ChoreDefinition
        {
            Name = "Chore B",
            AssignedUserId = secondChildId,
            EarnValue = 10m,
            ScheduleType = ChoreScheduleType.WeeklyFrequency,
            WeeklyTargetCount = 1,
            IsActive = true
        });

        var planner = CreatePlannerService();
        var potential = await planner.GetWeeklyPotentialAsync(_today, userId: null);

        Assert.Equal(16m, potential); // (3 x 2) + (10 x 1)
    }

    [Fact]
    public async Task InactiveChore_Is_Excluded_From_Potential()
    {
        await AddChoreAsync(new ChoreDefinition
        {
            Name = "Retired chore",
            AssignedUserId = ChildId,
            EarnValue = 100m,
            ScheduleType = ChoreScheduleType.WeeklyFrequency,
            WeeklyTargetCount = 5,
            IsActive = false
        });

        var planner = CreatePlannerService();
        var potential = await planner.GetWeeklyPotentialAsync(_today, ChildId);

        Assert.Equal(0m, potential);
    }

    [Fact]
    public async Task FixedDaysChore_StartingMidWeek_Only_Counts_Days_From_StartDate_Onward()
    {
        // ActiveDays = All, but StartDate = Wednesday of this week -> only Wed/Thu/Fri/Sat
        // are in range, regardless of the chore being "scheduled every day" on paper.
        await AddChoreAsync(new ChoreDefinition
        {
            Name = "New chore mid-week",
            AssignedUserId = ChildId,
            EarnValue = 3m,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.All,
            StartDate = _today.AddDays(3), // Wednesday
            IsActive = true
        });

        var planner = CreatePlannerService();
        var potential = await planner.GetWeeklyPotentialAsync(_today, ChildId);

        Assert.Equal(12m, potential); // 4 days (Wed-Sat) x $3
    }

    [Fact]
    public async Task FixedDaysChore_EndingMidWeek_Only_Counts_Days_Up_To_EndDate()
    {
        // ActiveDays = All, EndDate = Wednesday of this week -> only Sun/Mon/Tue/Wed count.
        await AddChoreAsync(new ChoreDefinition
        {
            Name = "Ending chore mid-week",
            AssignedUserId = ChildId,
            EarnValue = 3m,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.All,
            EndDate = _today.AddDays(3), // Wednesday
            IsActive = true
        });

        var planner = CreatePlannerService();
        var potential = await planner.GetWeeklyPotentialAsync(_today, ChildId);

        Assert.Equal(12m, potential); // 4 days (Sun-Wed) x $3
    }

    [Fact]
    public async Task FixedDaysChore_Overlapping_Week_By_One_Boundary_Day_Counts_Only_That_Day()
    {
        // StartDate = Saturday of this week, ActiveDays = All -> only Saturday is in range,
        // even though the outer date-range query loads the chore for the whole week.
        await AddChoreAsync(new ChoreDefinition
        {
            Name = "Just starting Saturday",
            AssignedUserId = ChildId,
            EarnValue = 7m,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.All,
            StartDate = _today.AddDays(6), // Saturday
            IsActive = true
        });

        var planner = CreatePlannerService();
        var potential = await planner.GetWeeklyPotentialAsync(_today, ChildId);

        Assert.Equal(7m, potential); // 1 day x $7
    }

    [Fact]
    public async Task WeeklyGoalChore_With_Zero_TargetCount_Contributes_Nothing()
    {
        await AddChoreAsync(new ChoreDefinition
        {
            Name = "Misconfigured weekly goal",
            AssignedUserId = ChildId,
            EarnValue = 5m,
            ScheduleType = ChoreScheduleType.WeeklyFrequency,
            WeeklyTargetCount = 0,
            IsActive = true
        });

        var planner = CreatePlannerService();
        var potential = await planner.GetWeeklyPotentialAsync(_today, ChildId);

        Assert.Equal(0m, potential);
    }

    [Fact]
    public async Task LoneMoveOverride_Without_A_Paired_Remove_Still_Counts_As_Scheduled()
    {
        // Base schedule: Monday only, $4. A Move override lands the chore on Thursday too,
        // with no corresponding Remove anywhere - the method must mirror the Planner's
        // literal "Add || Move" check rather than trying to infer/cancel a "real" move.
        var chore = new ChoreDefinition
        {
            Name = "Moved chore",
            AssignedUserId = ChildId,
            EarnValue = 4m,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.Monday,
            IsActive = true
        };
        await AddChoreAsync(chore);

        await using var context = await _contextFactory.CreateDbContextAsync();
        var thursday = _today.AddDays(4);
        context.ChoreScheduleOverrides.Add(
            new ChoreScheduleOverride { ChoreDefinitionId = chore.Id, Date = thursday, Type = ScheduleOverrideType.Move });
        await context.SaveChangesAsync();

        var planner = CreatePlannerService();
        var potential = await planner.GetWeeklyPotentialAsync(_today, ChildId);

        Assert.Equal(8m, potential); // Monday (base) + Thursday (Move) = 2 days x $4
    }

    [Fact]
    public async Task Potential_Matches_GetPlannerDataAsync_TotalWeeklyPotential_For_A_Varied_Chore_Set()
    {
        // Golden/equivalence check: whatever this method computes must equal what the
        // full Planner grid computes for the same week, for a mix of schedule types,
        // overrides, and a zero-value chore thrown in to make sure it nets to nothing.
        var fixedDaysChore = new ChoreDefinition
        {
            Name = "Take out trash",
            AssignedUserId = ChildId,
            EarnValue = 5m,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday,
            IsActive = true
        };
        await AddChoreAsync(fixedDaysChore);
        await AddChoreAsync(new ChoreDefinition
        {
            Name = "Read for 20 minutes",
            AssignedUserId = ChildId,
            EarnValue = 2m,
            ScheduleType = ChoreScheduleType.WeeklyFrequency,
            WeeklyTargetCount = 3,
            IsActive = true
        });
        await AddChoreAsync(new ChoreDefinition
        {
            Name = "Make bed (unpaid)",
            AssignedUserId = ChildId,
            Kind = ChoreKind.Routine,
            EarnValue = 0m,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.All,
            IsActive = true
        });

        await using (var context = await _contextFactory.CreateDbContextAsync())
        {
            context.ChoreScheduleOverrides.Add(new ChoreScheduleOverride
            {
                ChoreDefinitionId = fixedDaysChore.Id,
                Date = _today.AddDays(2), // Tuesday - an Add on top of Mon/Wed/Fri
                Type = ScheduleOverrideType.Add
            });
            await context.SaveChangesAsync();
        }

        var planner = CreatePlannerService();
        var lightweightPotential = await planner.GetWeeklyPotentialAsync(_today, ChildId);
        var plannerData = await planner.GetPlannerDataAsync(_today, ChildId, includeStreaks: false);

        Assert.Equal(plannerData.TotalWeeklyPotential, lightweightPotential);
    }

    private async Task AddChoreAsync(ChoreDefinition chore)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.ChoreDefinitions.Add(chore);
        await context.SaveChangesAsync();
    }

    private ChorePlannerService CreatePlannerService()
    {
        var dateProvider = new Mock<IDateProvider>();
        dateProvider.SetupGet(p => p.Today).Returns(_today);

        return new ChorePlannerService(
            _contextFactory,
            dateProvider.Object,
            Mock.Of<IChildProfileService>());
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
