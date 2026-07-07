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
/// Guards the screen-time Importance data path exercised by the ChoreForm: the value a
/// parent sets on the form (carried on <see cref="ChoreDefinitionDto.Importance"/>) must
/// persist on create and survive an edit that changes it. The form's private FormModel↔DTO
/// mapping is not directly unit-testable without bUnit, so this covers the DTO→entity→read
/// round trip the form ultimately drives.
/// </summary>
public sealed class ChoreImportanceRoundTripTests : IAsyncLifetime
{
    private const string ChildId = "child-1";

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
    public async Task Importance_Persists_On_Create_And_Survives_An_Edit()
    {
        var service = CreateChoreManagementService();

        // Create with a non-zero Importance (as the form now supplies).
        var createResult = await service.CreateChoreAsync(new ChoreDefinitionDto
        {
            Name = "Wash dishes",
            AssignedUserId = ChildId,
            Kind = ChoreKind.Task,
            EarnValue = 1.00m,
            Importance = 7,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.All
        });
        Assert.True(createResult.Success, createResult.ErrorMessage);
        Assert.Equal(7, createResult.Data!.Importance);

        var choreId = createResult.Data.Id;

        // Reading it back yields the same Importance.
        var created = await service.GetChoreByIdAsync(choreId);
        Assert.NotNull(created);
        Assert.Equal(7, created!.Importance);

        // Edit that changes Importance is persisted.
        var updateResult = await service.UpdateChoreAsync(new ChoreDefinitionDto
        {
            Id = choreId,
            Name = "Wash dishes",
            AssignedUserId = ChildId,
            Kind = ChoreKind.Task,
            EarnValue = 1.00m,
            Importance = 3,
            ScheduleType = ChoreScheduleType.SpecificDays,
            ActiveDays = DaysOfWeek.All
        });
        Assert.True(updateResult.Success, updateResult.ErrorMessage);

        var edited = await service.GetChoreByIdAsync(choreId);
        Assert.NotNull(edited);
        Assert.Equal(3, edited!.Importance);
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
