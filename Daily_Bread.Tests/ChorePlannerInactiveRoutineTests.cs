using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Guards the Planner/Routines display contract: deactivating a routine must never
/// remove it from the database (soft delete only), the default Planner view must keep
/// showing active routines only in unchanged SortOrder, and turning on "Show inactive"
/// must reveal the deactivated routine in its correct ordered slot without disturbing
/// the relative order of the still-active routines around it.
/// </summary>
public sealed class ChorePlannerInactiveRoutineTests : IAsyncLifetime
{
    private const string ChildId = "child-1";
    private readonly DateOnly _today = new(2026, 6, 21); // a Sunday

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestDbContextFactory _contextFactory = null!;
    private int _brushTeethAmId;
    private int _makeBedId;
    private int _brushTeethPmId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _contextFactory = new TestDbContextFactory(options);

        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();

        var child = new ApplicationUser { Id = ChildId, UserName = "child" };
        context.Users.Add(child);

        // Three daily routines in a specific, relied-upon SortOrder: AM brush, make bed, PM brush.
        var brushTeethAm = new ChoreDefinition
        {
            Name = "Brush Teeth AM",
            AssignedUserId = ChildId,
            EarnValue = 0,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.All,
            SortOrder = 10,
            IsActive = true
        };
        var makeBed = new ChoreDefinition
        {
            Name = "Make Bed",
            AssignedUserId = ChildId,
            EarnValue = 0,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.All,
            SortOrder = 20,
            IsActive = true
        };
        var brushTeethPm = new ChoreDefinition
        {
            Name = "Brush Teeth PM",
            AssignedUserId = ChildId,
            EarnValue = 0,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.All,
            SortOrder = 30,
            IsActive = true
        };

        context.ChoreDefinitions.AddRange(brushTeethAm, makeBed, brushTeethPm);
        await context.SaveChangesAsync();

        _brushTeethAmId = brushTeethAm.Id;
        _makeBedId = makeBed.Id;
        _brushTeethPmId = brushTeethPm.Id;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Default_View_Shows_Only_Active_Routines_In_Unchanged_Order()
    {
        var planner = CreatePlannerService();

        var data = await planner.GetPlannerDataAsync(planner.GetWeekStart(_today), ChildId, includeStreaks: false);

        var names = data.AllChores.Select(c => c.ChoreName).ToList();
        Assert.Equal(new[] { "Brush Teeth AM", "Make Bed", "Brush Teeth PM" }, names);
        Assert.All(data.AllChores, c => Assert.True(c.IsActive));
    }

    [Fact]
    public async Task Deactivating_A_Routine_Is_A_Soft_Delete_That_Disappears_From_Default_View_But_Keeps_The_Row()
    {
        var choreService = CreateChoreManagementService();
        var planner = CreatePlannerService();

        var toggleResult = await choreService.ToggleActiveAsync(_makeBedId);
        Assert.True(toggleResult.Success, toggleResult.ErrorMessage);

        // The row must survive - this is a soft delete, not a hard delete.
        await using var context = await _contextFactory.CreateDbContextAsync();
        var makeBed = await context.ChoreDefinitions.FindAsync(_makeBedId);
        Assert.NotNull(makeBed);
        Assert.False(makeBed!.IsActive);

        // Default Planner view (includeInactive: false) must keep matching today's behavior:
        // the deactivated routine disappears, and the remaining active routines keep their
        // original relative order (AM brush still before PM brush).
        var defaultView = await planner.GetPlannerDataAsync(planner.GetWeekStart(_today), ChildId, includeStreaks: false);
        var defaultNames = defaultView.AllChores.Select(c => c.ChoreName).ToList();
        Assert.Equal(new[] { "Brush Teeth AM", "Brush Teeth PM" }, defaultNames);
    }

    [Fact]
    public async Task Show_Inactive_Toggle_Reveals_The_Deactivated_Routine_Greyed_In_Its_Correct_Slot()
    {
        var choreService = CreateChoreManagementService();
        var planner = CreatePlannerService();

        await choreService.ToggleActiveAsync(_makeBedId);

        var toggledOnView = await planner.GetPlannerDataAsync(planner.GetWeekStart(_today), ChildId, includeStreaks: false, includeInactive: true);
        var rows = toggledOnView.AllChores.ToList();

        // The inactive routine reappears in its original SortOrder slot - active ordering
        // around it (AM before PM) must not be disrupted by interleaving it back in.
        Assert.Equal(new[] { "Brush Teeth AM", "Make Bed", "Brush Teeth PM" }, rows.Select(c => c.ChoreName));

        var makeBedRow = Assert.Single(rows, c => c.ChoreName == "Make Bed");
        Assert.False(makeBedRow.IsActive);
        var amRow = Assert.Single(rows, c => c.ChoreName == "Brush Teeth AM");
        var pmRow = Assert.Single(rows, c => c.ChoreName == "Brush Teeth PM");
        Assert.True(amRow.IsActive);
        Assert.True(pmRow.IsActive);

        // Day-column stats (e.g. total scheduled count) must stay based on active routines
        // only, so showing the inactive row for visibility never changes the day's totals.
        var sunday = toggledOnView.DayColumns.Single(d => d.DayOfWeek == DayOfWeek.Sunday);
        Assert.Equal(2, sunday.TotalChores);
    }

    [Fact]
    public async Task Reactivating_A_Routine_Returns_It_To_Its_Correct_Ordered_Slot_In_The_Default_View()
    {
        var choreService = CreateChoreManagementService();
        var planner = CreatePlannerService();

        await choreService.ToggleActiveAsync(_makeBedId); // deactivate
        await choreService.ToggleActiveAsync(_makeBedId); // reactivate

        var data = await planner.GetPlannerDataAsync(planner.GetWeekStart(_today), ChildId, includeStreaks: false);
        var names = data.AllChores.Select(c => c.ChoreName).ToList();

        Assert.Equal(new[] { "Brush Teeth AM", "Make Bed", "Brush Teeth PM" }, names);
        Assert.All(data.AllChores, c => Assert.True(c.IsActive));
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

    private ChoreManagementService CreateChoreManagementService()
        => new(_contextFactory, CreateUserManager().Object, Mock.Of<IChoreScheduleService>());

    private static Mock<UserManager<ApplicationUser>> CreateUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
