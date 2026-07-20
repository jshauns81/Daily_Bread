using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Guards <see cref="QolShareService"/>: missing inverse-fill routines seed at 0%, setting a share
/// rebalances the others via <see cref="QolRebalancer"/> and persists, locks are respected, and an
/// all-others-locked drag is rejected.
/// </summary>
public sealed class QolShareServiceTests : IAsyncLifetime
{
    private const string ChildId = "child-1";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private TestDbContextFactory _contextFactory = null!;
    private int _profileId;
    private int _readId;
    private int _activeId;
    private int _brainId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _contextFactory = new TestDbContextFactory(options);

        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();

        var child = new ApplicationUser { Id = ChildId, UserName = "kid" };
        context.Users.Add(child);

        var profile = new ChildProfile { UserId = ChildId, User = child, DisplayName = "Kid" };
        context.ChildProfiles.Add(profile);

        var read = MakeRoutine("Read");
        var active = MakeRoutine("Active");
        var brain = MakeRoutine("Brain");
        context.ChoreDefinitions.AddRange(read, active, brain);

        await context.SaveChangesAsync();

        _profileId = profile.Id;
        _readId = read.Id;
        _activeId = active.Id;
        _brainId = brain.Id;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private static ChoreDefinition MakeRoutine(string name) => new()
    {
        Name = name,
        Kind = ChoreKind.Routine,
        IsInverseFill = true,
        IsActive = true
    };

    private QolShareService CreateService() => new(_contextFactory);

    private async Task SeedSharesAsync(int read, int active, int brain, bool lockActive = false)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.QolShares.AddRange(
            new QolShare { ChildProfileId = _profileId, ChoreDefinitionId = _readId, SharePercent = read },
            new QolShare { ChildProfileId = _profileId, ChoreDefinitionId = _activeId, SharePercent = active, IsLocked = lockActive },
            new QolShare { ChildProfileId = _profileId, ChoreDefinitionId = _brainId, SharePercent = brain });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetShares_Seeds_Missing_InverseFill_Routines_At_Zero()
    {
        var service = CreateService();

        var shares = await service.GetSharesAsync(_profileId);

        Assert.Equal(3, shares.Count);
        Assert.All(shares, s => Assert.Equal(0, s.SharePercent));
        Assert.Contains(shares, s => s.ChoreDefinitionId == _readId);
        Assert.Contains(shares, s => s.ChoreDefinitionId == _activeId);
        Assert.Contains(shares, s => s.ChoreDefinitionId == _brainId);
    }

    [Fact]
    public async Task SetShare_Rebalances_And_Persists()
    {
        await SeedSharesAsync(read: 40, active: 40, brain: 20);
        var service = CreateService();

        var result = await service.SetShareAsync(_profileId, _readId, 60);
        Assert.True(result.Success);

        var shares = await service.GetSharesAsync(_profileId);
        Assert.Equal(60, Pct(shares, _readId));
        Assert.Equal(100, shares.Sum(s => s.SharePercent));
        // Both absorbers shrank.
        Assert.True(Pct(shares, _activeId) < 40);
        Assert.True(Pct(shares, _brainId) < 20);
    }

    [Fact]
    public async Task SetShare_Respects_A_Locked_Segment()
    {
        await SeedSharesAsync(read: 50, active: 30, brain: 20, lockActive: true);
        var service = CreateService();

        var result = await service.SetShareAsync(_profileId, _readId, 70);
        Assert.True(result.Success);

        var shares = await service.GetSharesAsync(_profileId);
        Assert.Equal(70, Pct(shares, _readId));
        Assert.Equal(30, Pct(shares, _activeId)); // locked: unchanged
        Assert.Equal(0, Pct(shares, _brainId));   // sole unlocked absorber
        Assert.Equal(100, shares.Sum(s => s.SharePercent));
    }

    [Fact]
    public async Task SetLock_Then_SetShare_Keeps_The_Locked_Value()
    {
        await SeedSharesAsync(read: 50, active: 30, brain: 20);
        var service = CreateService();

        var lockResult = await service.SetLockAsync(_profileId, _activeId, true);
        Assert.True(lockResult.Success);

        var setResult = await service.SetShareAsync(_profileId, _readId, 70);
        Assert.True(setResult.Success);

        var shares = await service.GetSharesAsync(_profileId);
        Assert.Equal(30, Pct(shares, _activeId));
        Assert.Equal(100, shares.Sum(s => s.SharePercent));
    }

    [Fact]
    public async Task SetShare_Blocked_When_All_Other_Segments_Locked()
    {
        await SeedSharesAsync(read: 50, active: 30, brain: 20);
        var service = CreateService();
        await service.SetLockAsync(_profileId, _activeId, true);
        await service.SetLockAsync(_profileId, _brainId, true);

        var result = await service.SetShareAsync(_profileId, _readId, 70);

        Assert.False(result.Success);
        var shares = await service.GetSharesAsync(_profileId);
        Assert.Equal(50, Pct(shares, _readId)); // unchanged
    }

    [Fact]
    public async Task SetShare_Rejects_Out_Of_Range_Percent()
    {
        await SeedSharesAsync(read: 50, active: 30, brain: 20);
        var service = CreateService();

        var result = await service.SetShareAsync(_profileId, _readId, 150);

        Assert.False(result.Success);
    }

    private static int Pct(IReadOnlyList<QolShare> shares, int choreId) =>
        shares.First(s => s.ChoreDefinitionId == choreId).SharePercent;

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
