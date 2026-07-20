using Daily_Bread.Api;
using Daily_Bread.Data;
using Daily_Bread.Data.Models;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Covers <see cref="PlannerMapping"/>: the pure entity↔wire translation the
/// planner controller relies on. Enum values become their names, day flags
/// become full day names in week order (Sunday…Saturday) and back, and the
/// write request maps field-for-field onto the service DTO. Service-side
/// behavior (Routine zeroes earnValue, weekly-target clamping) is deliberately
/// NOT tested here — the mapping passes values through raw.
/// </summary>
public sealed class PlannerMappingTests
{
    private static ChoreDefinition MakeChore(
        DaysOfWeek activeDays = DaysOfWeek.All,
        ChoreKind kind = ChoreKind.Task,
        ChoreScheduleType scheduleType = ChoreScheduleType.SpecificDays,
        ApplicationUser? assignedUser = null) => new()
    {
        Id = 12,
        Name = "Walk Gemma",
        Description = "Around the block",
        Icon = "D",
        AssignedUserId = assignedUser?.Id,
        AssignedUser = assignedUser,
        Kind = kind,
        EarnValue = 5.00m,
        Importance = 6,
        AllOrNothing = true,
        IsInverseFill = false,
        InverseFillBaselineMinutes = 20,
        ScheduleType = scheduleType,
        ActiveDays = activeDays,
        WeeklyTargetCount = 3,
        IsRepeatable = true,
        StartDate = new DateOnly(2026, 1, 5),
        EndDate = null,
        IsActive = true,
        AutoApprove = true,
        SortOrder = 4
    };

    private static ChoreWriteRequest MakeWrite(
        string kind = "Task",
        string scheduleType = "SpecificDays",
        IReadOnlyList<string>? activeDays = null,
        string? assignedUserId = null) => new(
        Name: "Dishes",
        Description: "After dinner",
        Icon: "P",
        AssignedUserId: assignedUserId,
        Kind: kind,
        EarnValue: 2.50m,
        Importance: 5,
        AllOrNothing: false,
        IsInverseFill: true,
        InverseFillBaselineMinutes: 30,
        ScheduleType: scheduleType,
        ActiveDays: activeDays ?? ["Monday"],
        WeeklyTargetCount: 4,
        IsRepeatable: true,
        StartDate: new DateOnly(2026, 2, 1),
        EndDate: new DateOnly(2026, 6, 30),
        IsActive: false,
        AutoApprove: false,
        SortOrder: 7);

    [Fact]
    public void FromEntity_Maps_Enum_Names_As_Strings()
    {
        var dto = PlannerMapping.FromEntity(MakeChore(
            kind: ChoreKind.Routine, scheduleType: ChoreScheduleType.WeeklyFrequency));

        Assert.Equal("Routine", dto.Kind);
        Assert.Equal("WeeklyFrequency", dto.ScheduleType);
    }

    [Fact]
    public void FromEntity_Maps_Task_And_SpecificDays_Names()
    {
        var dto = PlannerMapping.FromEntity(MakeChore(
            kind: ChoreKind.Task, scheduleType: ChoreScheduleType.SpecificDays));

        Assert.Equal("Task", dto.Kind);
        Assert.Equal("SpecificDays", dto.ScheduleType);
    }

    [Fact]
    public void FromEntity_Weekdays_Lists_Monday_Through_Friday()
    {
        var dto = PlannerMapping.FromEntity(MakeChore(activeDays: DaysOfWeek.Weekdays));

        Assert.Equal(
            new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" },
            dto.ActiveDays);
    }

    [Fact]
    public void FromEntity_All_Lists_Sunday_Through_Saturday_In_Week_Order()
    {
        var dto = PlannerMapping.FromEntity(MakeChore(activeDays: DaysOfWeek.All));

        Assert.Equal(
            new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
            dto.ActiveDays);
    }

    [Fact]
    public void FromEntity_Weekends_Orders_Sunday_Before_Saturday()
    {
        // Week order (Sunday first), not flag-value order.
        var dto = PlannerMapping.FromEntity(MakeChore(activeDays: DaysOfWeek.Weekends));

        Assert.Equal(new[] { "Sunday", "Saturday" }, dto.ActiveDays);
    }

    [Fact]
    public void FromEntity_None_Yields_Empty_Day_List()
    {
        var dto = PlannerMapping.FromEntity(MakeChore(activeDays: DaysOfWeek.None));

        Assert.Empty(dto.ActiveDays);
    }

    [Fact]
    public void FromEntity_Maps_Assigned_User_Name_From_Navigation()
    {
        var user = new ApplicationUser { Id = "child-1", UserName = "noah_test" };
        var dto = PlannerMapping.FromEntity(MakeChore(assignedUser: user));

        Assert.Equal("child-1", dto.AssignedUserId);
        Assert.Equal("noah_test", dto.AssignedUserName);
    }

    [Fact]
    public void FromEntity_Unassigned_Chore_Has_Null_User_Fields()
    {
        var dto = PlannerMapping.FromEntity(MakeChore(assignedUser: null));

        Assert.Null(dto.AssignedUserId);
        Assert.Null(dto.AssignedUserName);
    }

    [Fact]
    public void FromEntity_Passes_Scalars_And_Raw_Money_Through()
    {
        var dto = PlannerMapping.FromEntity(MakeChore());

        Assert.Equal(12, dto.Id);
        Assert.Equal("Walk Gemma", dto.Name);
        Assert.Equal("Around the block", dto.Description);
        Assert.Equal(5.00m, dto.EarnValue); // raw decimal; the converter owns the "5.00" form
        Assert.Equal(6, dto.Importance);
        Assert.True(dto.AllOrNothing);
        Assert.False(dto.IsInverseFill);
        Assert.Equal(20, dto.InverseFillBaselineMinutes);
        Assert.Equal(3, dto.WeeklyTargetCount);
        Assert.True(dto.IsRepeatable);
        Assert.Equal(new DateOnly(2026, 1, 5), dto.StartDate);
        Assert.Null(dto.EndDate);
        Assert.True(dto.IsActive);
        Assert.True(dto.AutoApprove);
        Assert.Equal(4, dto.SortOrder);
    }

    [Fact]
    public void TryParseDays_Maps_Names_To_Flags()
    {
        var ok = PlannerMapping.TryParseDays(
            ["Monday", "Wednesday", "Friday"], out var days);

        Assert.True(ok);
        Assert.Equal(DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday, days);
    }

    [Fact]
    public void Day_Names_Round_Trip_Through_Flags_Preserving_Week_Order()
    {
        var names = new List<string> { "Monday", "Wednesday", "Friday" };

        Assert.True(PlannerMapping.TryParseDays(names, out var days));
        var dto = PlannerMapping.FromEntity(MakeChore(activeDays: days));

        Assert.Equal(names, dto.ActiveDays);
    }

    [Fact]
    public void TryParseDays_Fails_On_Unknown_Name_And_Reports_It()
    {
        var ok = PlannerMapping.TryParseDays(
            ["Monday", "Funday"], out var days, out var unknown);

        Assert.False(ok);
        Assert.Equal(DaysOfWeek.None, days);
        Assert.Equal("Funday", unknown);
    }

    [Fact]
    public void TryParseDays_Is_Case_Sensitive()
    {
        // The wire contract sends exact full day names; anything else is a
        // client bug the server should surface, not paper over.
        Assert.False(PlannerMapping.TryParseDays(["monday"], out _));
    }

    [Fact]
    public void TryParseDays_Empty_And_Null_Parse_To_None()
    {
        Assert.True(PlannerMapping.TryParseDays([], out var fromEmpty));
        Assert.Equal(DaysOfWeek.None, fromEmpty);

        Assert.True(PlannerMapping.TryParseDays(null, out var fromNull));
        Assert.Equal(DaysOfWeek.None, fromNull);
    }

    [Fact]
    public void ToServiceDto_Passes_Kind_ScheduleType_And_Target_Through()
    {
        var dto = PlannerMapping.ToServiceDto(MakeWrite(
            kind: "Routine", scheduleType: "WeeklyFrequency"));

        // Raw passthrough — zeroing a Routine's earnValue is the service's job.
        Assert.Equal(ChoreKind.Routine, dto.Kind);
        Assert.Equal(ChoreScheduleType.WeeklyFrequency, dto.ScheduleType);
        Assert.Equal(4, dto.WeeklyTargetCount);
        Assert.Equal(2.50m, dto.EarnValue);
    }

    [Fact]
    public void ToServiceDto_Maps_All_Write_Fields()
    {
        var dto = PlannerMapping.ToServiceDto(MakeWrite(assignedUserId: "child-1"));

        Assert.Equal("Dishes", dto.Name);
        Assert.Equal("After dinner", dto.Description);
        Assert.Equal("P", dto.Icon);
        Assert.Equal("child-1", dto.AssignedUserId);
        Assert.Equal(5, dto.Importance);
        Assert.False(dto.AllOrNothing);
        Assert.True(dto.IsInverseFill);
        Assert.Equal(30, dto.InverseFillBaselineMinutes);
        Assert.True(dto.IsRepeatable);
        Assert.Equal(new DateOnly(2026, 2, 1), dto.StartDate);
        Assert.Equal(new DateOnly(2026, 6, 30), dto.EndDate);
        Assert.False(dto.IsActive);
        Assert.False(dto.AutoApprove);
        Assert.Equal(7, dto.SortOrder);
    }

    [Fact]
    public void ToServiceDto_Maps_Day_Names_To_Flags()
    {
        var dto = PlannerMapping.ToServiceDto(MakeWrite(
            activeDays: ["Sunday", "Saturday"]));

        Assert.Equal(DaysOfWeek.Weekends, dto.ActiveDays);
    }

    [Fact]
    public void ToServiceDto_Id_Defaults_To_Zero_For_Create()
    {
        Assert.Equal(0, PlannerMapping.ToServiceDto(MakeWrite()).Id);
    }

    [Fact]
    public void ToServiceDto_Passes_Explicit_Id_Through_For_Update()
    {
        Assert.Equal(42, PlannerMapping.ToServiceDto(MakeWrite(), id: 42).Id);
    }

    [Fact]
    public void ToServiceDto_Normalizes_Blank_AssignedUserId_To_Null()
    {
        var dto = PlannerMapping.ToServiceDto(MakeWrite(assignedUserId: ""));

        Assert.Null(dto.AssignedUserId);
    }

    [Fact]
    public void ToServiceDto_Throws_On_Unknown_Kind()
    {
        // The controller validates via TryParseKind first; a throw here is a
        // controller bug surfacing loudly rather than a silent default.
        Assert.Throws<ArgumentException>(
            () => PlannerMapping.ToServiceDto(MakeWrite(kind: "Chore")));
    }

    [Fact]
    public void TryParseKind_And_ScheduleType_Reject_Unknown_And_Numeric_Names()
    {
        Assert.True(PlannerMapping.TryParseKind("Task", out var task));
        Assert.Equal(ChoreKind.Task, task);
        Assert.True(PlannerMapping.TryParseKind("Routine", out var routine));
        Assert.Equal(ChoreKind.Routine, routine);
        Assert.False(PlannerMapping.TryParseKind("task", out _));
        Assert.False(PlannerMapping.TryParseKind("1", out _)); // Enum.TryParse would accept this

        Assert.True(PlannerMapping.TryParseScheduleType("SpecificDays", out var specific));
        Assert.Equal(ChoreScheduleType.SpecificDays, specific);
        Assert.True(PlannerMapping.TryParseScheduleType("WeeklyFrequency", out var weekly));
        Assert.Equal(ChoreScheduleType.WeeklyFrequency, weekly);
        Assert.False(PlannerMapping.TryParseScheduleType("Weekly", out _));
        Assert.False(PlannerMapping.TryParseScheduleType("0", out _));
    }
}
