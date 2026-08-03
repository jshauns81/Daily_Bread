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
/// Chore-name uniqueness is scoped to the assignee, not the whole table.
/// Siblings legitimately share chore names — the seed data gives every child an
/// "Empty Dishwasher" — and the original global check made any common name
/// unusable after the first kid, and (worse) made *editing* such a chore
/// impossible: the unchanged name always collided with a sibling's copy.
/// </summary>
public sealed class ChoreNameScopeTests : IAsyncLifetime
{
    private const string KidA = "kid-a";
    private const string KidB = "kid-b";

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

        context.Users.Add(new ApplicationUser { Id = KidA, UserName = "kid-a" });
        context.Users.Add(new ApplicationUser { Id = KidB, UserName = "kid-b" });
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task Siblings_Can_Share_A_Chore_Name()
    {
        var service = CreateService();

        var first = await service.CreateChoreAsync(Dto("Empty Dishwasher", KidA));
        Assert.True(first.Success, first.ErrorMessage);

        var second = await service.CreateChoreAsync(Dto("Empty Dishwasher", KidB));
        Assert.True(second.Success, second.ErrorMessage);
    }

    [Fact]
    public async Task Same_Kid_Cannot_Get_The_Same_Chore_Twice()
    {
        var service = CreateService();

        var first = await service.CreateChoreAsync(Dto("Empty Dishwasher", KidA));
        Assert.True(first.Success, first.ErrorMessage);

        var duplicate = await service.CreateChoreAsync(Dto("empty dishwasher", KidA));
        Assert.False(duplicate.Success);
        Assert.Contains("already exists", duplicate.ErrorMessage);
    }

    [Fact]
    public async Task Editing_A_Chore_Whose_Name_A_Sibling_Shares_Succeeds()
    {
        var service = CreateService();

        var a = await service.CreateChoreAsync(Dto("Empty Dishwasher", KidA));
        var b = await service.CreateChoreAsync(Dto("Empty Dishwasher", KidB));
        Assert.True(a.Success && b.Success);

        // The screenshot bug: saving kid A's chore without renaming it tripped
        // the global check against kid B's copy.
        var edit = Dto("Empty Dishwasher", KidA);
        edit.Id = a.Data!.Id;
        edit.EarnValue = 2.50m;
        var result = await service.UpdateChoreAsync(edit);

        Assert.True(result.Success, result.ErrorMessage);
        var edited = await service.GetChoreByIdAsync(a.Data.Id);
        Assert.Equal(2.50m, edited!.EarnValue);
    }

    [Fact]
    public async Task Reassigning_Onto_A_Name_The_Target_Kid_Already_Has_Fails()
    {
        var service = CreateService();

        var a = await service.CreateChoreAsync(Dto("Empty Dishwasher", KidA));
        var b = await service.CreateChoreAsync(Dto("Empty Dishwasher", KidB));
        Assert.True(a.Success && b.Success);

        // Moving A's chore to kid B would give B two "Empty Dishwasher"s.
        var edit = Dto("Empty Dishwasher", KidB);
        edit.Id = a.Data!.Id;
        var result = await service.UpdateChoreAsync(edit);

        Assert.False(result.Success);
        Assert.Contains("already exists", result.ErrorMessage);
    }

    private static ChoreDefinitionDto Dto(string name, string assignedUserId) => new()
    {
        Name = name,
        AssignedUserId = assignedUserId,
        Kind = ChoreKind.Task,
        EarnValue = 1.00m,
        Importance = 5,
        ScheduleType = ChoreScheduleType.SpecificDays,
        ActiveDays = DaysOfWeek.All
    };

    private ChoreManagementService CreateService()
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
