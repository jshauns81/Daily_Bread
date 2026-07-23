using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Round-trips between an unlock condition's typed params and its stored JSON.
/// Pure — no database. The JSON keys must match UnlockConditionType's contract
/// (what the evaluator reads).
/// </summary>
public class AchievementConditionJsonTests
{
    [Fact]
    public void Count_Types_Emit_A_Count_Key()
    {
        var json = AchievementConditionJson.Build(
            UnlockConditionType.ChoresCompleted, new AchievementConditionParams(Count: 10));
        Assert.Contains("\"count\":10", json);

        var back = AchievementConditionJson.Parse(UnlockConditionType.ChoresCompleted, json);
        Assert.Equal(10, back.Count);
    }

    [Fact]
    public void Streak_Days_Emit_A_Days_Key()
    {
        var json = AchievementConditionJson.Build(
            UnlockConditionType.StreakDays, new AchievementConditionParams(Days: 7));
        Assert.Contains("\"days\":7", json);
        Assert.Equal(7, AchievementConditionJson.Parse(UnlockConditionType.StreakDays, json).Days);
    }

    [Fact]
    public void Amount_Types_Emit_An_Amount_Key()
    {
        var json = AchievementConditionJson.Build(
            UnlockConditionType.TotalEarned, new AchievementConditionParams(Amount: 100.50m));
        Assert.Contains("\"amount\":100.50", json);
        Assert.Equal(100.50m, AchievementConditionJson.Parse(UnlockConditionType.TotalEarned, json).Amount);
    }

    [Fact]
    public void Specific_Chore_Count_Emits_Chore_Id_And_Count()
    {
        var json = AchievementConditionJson.Build(
            UnlockConditionType.SpecificChoreCount,
            new AchievementConditionParams(ChoreId: 5, Count: 100));
        var back = AchievementConditionJson.Parse(UnlockConditionType.SpecificChoreCount, json);
        Assert.Equal(5, back.ChoreId);
        Assert.Equal(100, back.Count);
    }

    [Fact]
    public void Day_Type_Completion_Emits_Day_Type_And_Count()
    {
        var json = AchievementConditionJson.Build(
            UnlockConditionType.DayTypeCompletion,
            new AchievementConditionParams(DayType: "Weekend", Count: 10));
        var back = AchievementConditionJson.Parse(UnlockConditionType.DayTypeCompletion, json);
        Assert.Equal("Weekend", back.DayType);
        Assert.Equal(10, back.Count);
    }

    [Fact]
    public void Early_Completion_Defaults_Before_Hour_To_Noon()
    {
        var json = AchievementConditionJson.Build(
            UnlockConditionType.EarlyCompletion, new AchievementConditionParams(Count: 1));
        Assert.Contains("\"before_hour\":12", json);
    }

    [Fact]
    public void Manual_And_No_Param_Types_Emit_Empty_Object()
    {
        Assert.Equal("{}", AchievementConditionJson.Build(
            UnlockConditionType.Manual, new AchievementConditionParams()));
        Assert.Equal("{}", AchievementConditionJson.Build(
            UnlockConditionType.FirstChore, new AchievementConditionParams()));
    }

    [Fact]
    public void Parse_Of_Garbage_Is_Empty_Not_A_Throw()
    {
        var back = AchievementConditionJson.Parse(UnlockConditionType.ChoresCompleted, "not json");
        Assert.Null(back.Count);
    }
}
