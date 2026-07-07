using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Guards the Driving Log contract: either the child or a parent can create a session;
/// parent-created sessions auto-approve, child-created ones start PendingApproval and only
/// count toward hour totals once a parent approves; rejecting excludes them entirely.
/// </summary>
public sealed class DrivingLogServiceTests : IAsyncLifetime
{
    private const string ChildId = "child-1";
    private const string ParentId = "parent-1";

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

        var child = new ApplicationUser { Id = ChildId, UserName = "kid" };
        var parent = new ApplicationUser { Id = ParentId, UserName = "mom" };
        context.Users.AddRange(child, parent);

        var profile = new ChildProfile { UserId = ChildId, User = child, DisplayName = "Kid" };
        context.ChildProfiles.Add(profile);

        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private DrivingLogService CreateService() =>
        new(_contextFactory, NullLogger<DrivingLogService>.Instance);

    private static DrivingLogEntry MakeDraft(TimeOnly start, TimeOnly end) => new()
    {
        ChildUserId = ChildId,
        Date = new DateOnly(2026, 7, 5),
        StartTime = start,
        EndTime = end,
        SupervisorName = "Grandma",
        CreatedByUserId = ChildId
    };

    [Fact]
    public async Task Child_Created_Entry_Starts_Pending_And_Does_Not_Count_Toward_Progress()
    {
        var service = CreateService();

        var result = await service.CreateEntryAsync(MakeDraft(new TimeOnly(14, 0), new TimeOnly(15, 0)), ChildId, createdByParent: false);
        Assert.True(result.Success);

        var pending = await service.GetPendingApprovalsAsync();
        var entry = Assert.Single(pending);
        Assert.Equal(DrivingLogStatus.PendingApproval, entry.Status);

        var progress = await service.GetProgressAsync(ChildId);
        Assert.Equal(0m, progress.TotalHours);
    }

    [Fact]
    public async Task Parent_Created_Entry_Auto_Approves_And_Counts_Immediately()
    {
        var service = CreateService();

        var result = await service.CreateEntryAsync(MakeDraft(new TimeOnly(14, 0), new TimeOnly(15, 30)), ParentId, createdByParent: true);
        Assert.True(result.Success);

        var pending = await service.GetPendingApprovalsAsync();
        Assert.Empty(pending);

        var progress = await service.GetProgressAsync(ChildId);
        Assert.Equal(1.5m, progress.TotalHours);

        var entries = await service.GetEntriesAsync(ChildId);
        var entry = Assert.Single(entries);
        Assert.Equal(DrivingLogStatus.Approved, entry.Status);
        Assert.NotNull(entry.DecidedAt);
    }

    [Fact]
    public async Task Approving_A_Pending_Entry_Makes_It_Count_Toward_Progress()
    {
        var service = CreateService();
        var created = await service.CreateEntryAsync(MakeDraft(new TimeOnly(14, 0), new TimeOnly(16, 0)), ChildId, createdByParent: false);

        var approve = await service.ApproveEntryAsync(created.Data, ParentId);
        Assert.True(approve.Success);

        var progress = await service.GetProgressAsync(ChildId);
        Assert.Equal(2.0m, progress.TotalHours);
    }

    [Fact]
    public async Task Rejecting_A_Pending_Entry_Excludes_It_From_Progress_And_Records_Reason()
    {
        var service = CreateService();
        var created = await service.CreateEntryAsync(MakeDraft(new TimeOnly(14, 0), new TimeOnly(16, 0)), ChildId, createdByParent: false);

        var reject = await service.RejectEntryAsync(created.Data, ParentId, "Times don't match the odometer log");
        Assert.True(reject.Success);

        var progress = await service.GetProgressAsync(ChildId);
        Assert.Equal(0m, progress.TotalHours);

        var entries = await service.GetEntriesAsync(ChildId);
        var entry = Assert.Single(entries);
        Assert.Equal(DrivingLogStatus.Rejected, entry.Status);
        Assert.Equal("Times don't match the odometer log", entry.RejectionReason);
    }

    [Fact]
    public async Task Cannot_Approve_An_Entry_That_Was_Already_Decided()
    {
        var service = CreateService();
        var created = await service.CreateEntryAsync(MakeDraft(new TimeOnly(14, 0), new TimeOnly(15, 0)), ChildId, createdByParent: false);

        var firstApprove = await service.ApproveEntryAsync(created.Data, ParentId);
        Assert.True(firstApprove.Success);

        var secondApprove = await service.ApproveEntryAsync(created.Data, ParentId);
        Assert.False(secondApprove.Success);
        Assert.Contains("already", secondApprove.ErrorMessage);
    }

    [Fact]
    public async Task Session_Fully_Inside_Night_Window_Is_Auto_Flagged_As_Night_Driving()
    {
        var service = CreateService();

        var nightResult = await service.CreateEntryAsync(MakeDraft(new TimeOnly(22, 0), new TimeOnly(23, 0)), ParentId, createdByParent: true);
        var dayResult = await service.CreateEntryAsync(MakeDraft(new TimeOnly(14, 0), new TimeOnly(15, 0)), ParentId, createdByParent: true);

        var entries = await service.GetEntriesAsync(ChildId);
        var night = entries.Single(e => e.Id == nightResult.Data);
        var day = entries.Single(e => e.Id == dayResult.Data);

        Assert.True(night.IsNightDriving);
        Assert.False(day.IsNightDriving);

        var progress = await service.GetProgressAsync(ChildId);
        Assert.Equal(1.0m, progress.NightHours);
    }

    [Fact]
    public async Task Manual_Override_Wins_Over_The_Auto_Derived_Night_Flag()
    {
        var service = CreateService();

        var draft = MakeDraft(new TimeOnly(14, 0), new TimeOnly(15, 0)); // daytime by the clock
        draft.IsNightDriving = true;
        draft.NightDrivingSource = NightDrivingSource.ManualOverride;

        var result = await service.CreateEntryAsync(draft, ParentId, createdByParent: true);
        var entries = await service.GetEntriesAsync(ChildId);
        var entry = Assert.Single(entries);

        Assert.True(entry.IsNightDriving);
    }

    [Fact]
    public async Task Session_Crossing_Midnight_Computes_Duration_Correctly()
    {
        var service = CreateService();

        var result = await service.CreateEntryAsync(MakeDraft(new TimeOnly(23, 0), new TimeOnly(0, 30)), ParentId, createdByParent: true);
        var entries = await service.GetEntriesAsync(ChildId);
        var entry = Assert.Single(entries);

        Assert.Equal(90, entry.DurationMinutes);
    }

    [Fact]
    public async Task Missing_Supervisor_Is_Rejected_Before_Hitting_The_Database()
    {
        var service = CreateService();

        var draft = MakeDraft(new TimeOnly(14, 0), new TimeOnly(15, 0));
        draft.SupervisorName = null;

        var result = await service.CreateEntryAsync(draft, ChildId, createdByParent: false);

        Assert.False(result.Success);
        Assert.Contains("supervising adult", result.ErrorMessage);
    }

    [Fact]
    public async Task Child_Can_Delete_Their_Own_Pending_Entry_But_Not_An_Approved_One()
    {
        var service = CreateService();
        var pending = await service.CreateEntryAsync(MakeDraft(new TimeOnly(14, 0), new TimeOnly(15, 0)), ChildId, createdByParent: false);
        var approved = await service.CreateEntryAsync(MakeDraft(new TimeOnly(9, 0), new TimeOnly(10, 0)), ParentId, createdByParent: true);

        var deletePending = await service.DeleteEntryAsync(pending.Data, ChildId, isParent: false);
        Assert.True(deletePending.Success);

        var deleteApproved = await service.DeleteEntryAsync(approved.Data, ChildId, isParent: false);
        Assert.False(deleteApproved.Success);
    }

    [Fact]
    public async Task Csv_Export_Escapes_Commas_And_Quotes_In_Notes()
    {
        var service = CreateService();
        var draft = MakeDraft(new TimeOnly(14, 0), new TimeOnly(15, 0));
        draft.RouteNotes = "Highway practice, \"parallel parking\" by the mall";

        await service.CreateEntryAsync(draft, ParentId, createdByParent: true);
        var entries = await service.GetEntriesAsync(ChildId);

        var csv = DrivingLogCsvBuilder.Build(entries);

        Assert.Contains("\"Highway practice, \"\"parallel parking\"\" by the mall\"", csv);
        Assert.StartsWith("Date,Start Time,End Time,Duration (hrs),Day/Night,Supervisor,Weather,Route/Notes,Status,Approved By,Approved At", csv);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
