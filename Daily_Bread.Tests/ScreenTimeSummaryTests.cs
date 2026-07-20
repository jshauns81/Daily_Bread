using Daily_Bread.Api;
using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// The pool math behind /api/v1/screentime: snapshot fallback, guaranteed-floor
/// computation, zero-price filtering, and name joining. Pure — no DB.
/// </summary>
public class ScreenTimeSummaryTests
{
    private static readonly DateOnly WeekStart = new(2026, 7, 13);
    private static readonly DateOnly WeekEnd = new(2026, 7, 19);

    private static WeekPricing Pricing(
        int weekdayBudget = 720,
        int weekendBudget = 600,
        Dictionary<int, ChorePrice>? prices = null) =>
        new(weekdayBudget, weekendBudget, prices ?? new Dictionary<int, ChorePrice>());

    private static ScreenTimeResponse Build(
        WeekPricing pricing,
        ChildWeeklyScreenTimeBudget? snapshot = null,
        Dictionary<int, string>? names = null,
        decimal weekdayHours = 40m,
        decimal weekendHours = 20m) =>
        ScreenTimeSummary.Build(
            "child-1", WeekStart, WeekEnd, weekdayHours, weekendHours,
            pricing, snapshot, names ?? new Dictionary<int, string>(), []);

    [Fact]
    public void Without_A_Snapshot_Pools_Fall_Back_To_Live_Profile_Settings()
    {
        var response = Build(Pricing());

        // 40h → 2400 min, untouched because no reconciliation has run.
        Assert.Equal(2400, response.WeekdayPool.BaseMinutes);
        Assert.Equal(2400, response.WeekdayPool.EffectiveMinutes);
        Assert.Equal(1200, response.WeekendPool.BaseMinutes);
        Assert.Equal(1200, response.WeekendPool.EffectiveMinutes);
    }

    [Fact]
    public void With_A_Snapshot_Pools_Use_The_Frozen_Week_Numbers()
    {
        var snapshot = new ChildWeeklyScreenTimeBudget
        {
            WeekStartDate = WeekStart,
            WeekdayBasePoolMinutes = 2400,
            WeekendBasePoolMinutes = 1200,
            WeekdayMinutesLost = 90,
            WeekendMinutesLost = 45
        };

        var response = Build(Pricing(), snapshot);

        Assert.Equal(2400, response.WeekdayPool.BaseMinutes);
        Assert.Equal(2310, response.WeekdayPool.EffectiveMinutes);
        Assert.Equal(1155, response.WeekendPool.EffectiveMinutes);
    }

    [Fact]
    public void Floor_Is_Base_Minus_AtRisk_Budget_And_Never_Negative()
    {
        // Weekday: 2400 base − 720 at risk = 1680 guaranteed.
        var response = Build(Pricing(weekdayBudget: 720, weekendBudget: 600));
        Assert.Equal(1680, response.WeekdayPool.FloorMinutes);
        Assert.Equal(720, response.WeekdayPool.AtRiskMinutes);
        Assert.Equal(600, response.WeekendPool.FloorMinutes);

        // Degenerate config: at-risk budget bigger than the pool → floor clamps to 0.
        var extreme = Build(Pricing(weekdayBudget: 99999), weekdayHours: 1m);
        Assert.Equal(0, extreme.WeekdayPool.FloorMinutes);
    }

    [Fact]
    public void Zero_Priced_Chores_Are_Filtered_And_Names_Join_With_Fallback()
    {
        var prices = new Dictionary<int, ChorePrice>
        {
            [1] = new(ScreenTimePool.Weekday, 5, 45),
            [2] = new(ScreenTimePool.Weekend, 2, 0),   // zero price → hidden
            [3] = new(ScreenTimePool.Weekend, 1, 30)   // no name row → "Chore"
        };
        var names = new Dictionary<int, string> { [1] = "Dishes" };

        var response = Build(Pricing(prices: prices), names: names);

        Assert.Equal(2, response.ChorePrices.Count);
        // Ordered by price descending.
        Assert.Equal("Dishes", response.ChorePrices[0].Name);
        Assert.Equal(45, response.ChorePrices[0].PerInstanceMinutes);
        Assert.Equal("Chore", response.ChorePrices[1].Name);
        Assert.Equal("Weekend", response.ChorePrices[1].Pool);
    }

    [Fact]
    public void HoursToMinutes_Rounds_Half_Away_From_Zero()
    {
        Assert.Equal(90, ScreenTimeSummary.HoursToMinutes(1.5m));
        Assert.Equal(161, ScreenTimeSummary.HoursToMinutes(2.675m)); // 160.5 → 161, away from zero
    }
}
