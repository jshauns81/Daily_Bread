using Daily_Bread.Api;
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
/// Household isolation for the API layer: children reach only themselves,
/// parents reach only their own household, and chore-log actions can never
/// cross households.
/// </summary>
public sealed class HouseholdGuardTests : IAsyncLifetime
{
    private const string ParentId = "parent-1";
    private const string ChildId = "child-1";
    private const string SiblingId = "child-2";
    private const string OutsiderId = "outsider-1";

    private static readonly Guid HomeId = Guid.NewGuid();
    private static readonly Guid OtherHomeId = Guid.NewGuid();

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private TestDbContextFactory _contextFactory = null!;
    private int _homeChoreLogId;
    private int _outsiderChoreLogId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _contextFactory = new TestDbContextFactory(_options);

        await using var db = new ApplicationDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        db.Households.AddRange(
            new Household { Id = HomeId, Name = "Home" },
            new Household { Id = OtherHomeId, Name = "Other" });

        db.Users.AddRange(
            new ApplicationUser { Id = ParentId, UserName = "mom", HouseholdId = HomeId },
            new ApplicationUser { Id = ChildId, UserName = "kid", HouseholdId = HomeId },
            new ApplicationUser { Id = SiblingId, UserName = "sib", HouseholdId = HomeId },
            new ApplicationUser { Id = OutsiderId, UserName = "stranger", HouseholdId = OtherHomeId });

        var homeChore = new ChoreDefinition { Name = "Dishes", AssignedUserId = ChildId };
        var outsiderChore = new ChoreDefinition { Name = "Alien dishes", AssignedUserId = OutsiderId };
        db.ChoreDefinitions.AddRange(homeChore, outsiderChore);
        await db.SaveChangesAsync();

        var homeLog = new ChoreLog { ChoreDefinitionId = homeChore.Id, Date = new DateOnly(2026, 7, 19) };
        var outsiderLog = new ChoreLog { ChoreDefinitionId = outsiderChore.Id, Date = new DateOnly(2026, 7, 19) };
        db.ChoreLogs.AddRange(homeLog, outsiderLog);
        await db.SaveChangesAsync();

        _homeChoreLogId = homeLog.Id;
        _outsiderChoreLogId = outsiderLog.Id;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private sealed class FakeCurrentUser(string userId, Guid? householdId, params string[] roles) : ICurrentUserContext
    {
        public string UserId => userId;
        public Guid? HouseholdId => householdId;
        public List<string> Roles => roles.ToList();
        public bool IsInRole(string role) => roles.Contains(role);
        public bool IsAuthenticated => true;
        public Task InitializeAsync() => Task.CompletedTask;
    }

    private HouseholdGuard CreateGuard(ICurrentUserContext currentUser)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        userManager
            .Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .Returns(async (string id) =>
            {
                await using var db = new ApplicationDbContext(_options);
                return await db.Users.FirstOrDefaultAsync(u => u.Id == id);
            });

        return new HouseholdGuard(currentUser, userManager.Object, _contextFactory);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new ApplicationDbContext(options));
    }

    // ---------- ResolveTargetUserAsync ----------

    [Fact]
    public async Task Child_Resolves_Self()
    {
        var guard = CreateGuard(new FakeCurrentUser(ChildId, HomeId, "Child"));
        var result = await guard.ResolveTargetUserAsync(null);
        Assert.Equal(GuardOutcome.Ok, result.Outcome);
        Assert.Equal(ChildId, result.User!.Id);
    }

    [Fact]
    public async Task Child_Cannot_Target_Sibling()
    {
        var guard = CreateGuard(new FakeCurrentUser(ChildId, HomeId, "Child"));
        var result = await guard.ResolveTargetUserAsync(SiblingId);
        Assert.Equal(GuardOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task Parent_Can_Target_Child_In_Household()
    {
        var guard = CreateGuard(new FakeCurrentUser(ParentId, HomeId, "Parent"));
        var result = await guard.ResolveTargetUserAsync(ChildId);
        Assert.Equal(GuardOutcome.Ok, result.Outcome);
        Assert.Equal(ChildId, result.User!.Id);
    }

    [Fact]
    public async Task Parent_Cannot_Target_Other_Household_User()
    {
        var guard = CreateGuard(new FakeCurrentUser(ParentId, HomeId, "Parent"));
        var result = await guard.ResolveTargetUserAsync(OutsiderId);
        Assert.Equal(GuardOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Parent_Cannot_Target_Unknown_User()
    {
        var guard = CreateGuard(new FakeCurrentUser(ParentId, HomeId, "Parent"));
        var result = await guard.ResolveTargetUserAsync("no-such-user");
        Assert.Equal(GuardOutcome.NotFound, result.Outcome);
    }

    // ---------- ChoreLogIsInCallerHouseholdAsync ----------

    [Fact]
    public async Task ChoreLog_In_Own_Household_Is_Allowed()
    {
        var guard = CreateGuard(new FakeCurrentUser(ParentId, HomeId, "Parent"));
        Assert.True(await guard.ChoreLogIsInCallerHouseholdAsync(_homeChoreLogId));
    }

    [Fact]
    public async Task ChoreLog_In_Other_Household_Is_Denied()
    {
        var guard = CreateGuard(new FakeCurrentUser(ParentId, HomeId, "Parent"));
        Assert.False(await guard.ChoreLogIsInCallerHouseholdAsync(_outsiderChoreLogId));
    }

    [Fact]
    public async Task Unknown_ChoreLog_Is_Denied()
    {
        var guard = CreateGuard(new FakeCurrentUser(ParentId, HomeId, "Parent"));
        Assert.False(await guard.ChoreLogIsInCallerHouseholdAsync(999999));
    }

    [Fact]
    public async Task Caller_Without_Household_Is_Denied()
    {
        var guard = CreateGuard(new FakeCurrentUser(ParentId, null, "Admin"));
        Assert.False(await guard.ChoreLogIsInCallerHouseholdAsync(_homeChoreLogId));
    }
}
