using Daily_Bread.Data.Models;
using Daily_Bread.Services;
using Xunit;

namespace Daily_Bread.Tests;

/// <summary>
/// Guards the pure §E at-risk rules (MECHANICS_AMENDMENT.md §E / AtRiskRules): urgency
/// classification (due tonight / must-do-daily / getting tight), the stakes math (all-or-nothing
/// pots vs per-rep money, per-instance minutes), the impossible-target redemption rule, ordering,
/// totals, and the calm state's single preview line. No database — everything runs on plain inputs.
/// </summary>
public sealed class AtRiskRulesTests
{
    private const string ChildId = "child-1";

    // Monday 2026-07-06 → week runs Mon 06 through Sun 12 (default WeekStartDay = Monday).
    private static readonly DateOnly Monday = new(2026, 7, 6);       // daysLeft 7
    private static readonly DateOnly Wednesday = new(2026, 7, 8);    // daysLeft 5
    private static readonly DateOnly Friday = new(2026, 7, 10);      // daysLeft 3
    private static readonly DateOnly Saturday = new(2026, 7, 11);    // daysLeft 2
    private static readonly DateOnly WeekEnd = new(2026, 7, 12);     // Sunday

    // ============================================================
    // DueTonight (SpecificDays)
    // ============================================================

    [Fact]
    public void SpecificDays_Pending_Today_Is_DueTonight_With_Money_And_Minutes()
    {
        var chores = new[] { Daily(1, "Dishes", earn: 2.50m) };
        var pricing = Pricing((1, 45));

        var result = Build(Monday, chores, pricing);

        var item = Assert.Single(result.Items);
        Assert.Equal(AtRiskUrgency.DueTonight, item.Urgency);
        Assert.Equal("due tonight", item.Detail);
        Assert.Equal(2.50m, item.MoneyAtRisk);
        Assert.Equal(45, item.MinutesAtRisk);
    }

    [Fact]
    public void SpecificDays_Help_Protected_And_Done_Chores_Are_Not_At_Risk()
    {
        // Help is protected (that is the point of Help); Completed/Approved are done;
        // Skipped is excused; Missed is already ruled — none belong on the card.
        var chores = new[]
        {
            Daily(1, "Dishes", earn: 2m, status: ChoreStatus.Help),
            Daily(2, "Trash", earn: 2m, status: ChoreStatus.Completed),
            Daily(3, "Vacuum", earn: 2m, status: ChoreStatus.Approved),
            Daily(4, "Sweep", earn: 2m, status: ChoreStatus.Skipped),
            Daily(5, "Mop", earn: 2m, status: ChoreStatus.Missed)
        };
        var pricing = Pricing((1, 30), (2, 30), (3, 30), (4, 30), (5, 30));

        var result = Build(Monday, chores, pricing);

        Assert.Empty(result.Items);
    }

    [Fact]
    public void SpecificDays_Without_A_Price_Still_Lists_With_Zero_Minutes()
    {
        var chores = new[] { Daily(1, "Dishes", earn: 3.00m) };

        var result = Build(Monday, chores, Pricing());

        var item = Assert.Single(result.Items);
        Assert.Equal(3.00m, item.MoneyAtRisk);
        Assert.Equal(0, item.MinutesAtRisk);
    }

    // ============================================================
    // Weekly escalation (MustDoDaily / GettingTight)
    // ============================================================

    [Fact]
    public void Weekly_When_DaysLeft_Equals_RepsLeft_Is_MustDoDaily()
    {
        // Friday: 3 days left (Fri/Sat/Sun), 3 reps left.
        var chores = new[] { Weekly(1, "Walk Gemma", target: 3, completed: 0) };
        var pricing = Pricing((1, 45));

        var result = Build(Friday, chores, pricing);

        var item = Assert.Single(result.Items);
        Assert.Equal(AtRiskUrgency.MustDoDaily, item.Urgency);
        Assert.Equal("needs one every day left", item.Detail);
        Assert.Equal(135, item.MinutesAtRisk); // 45 × 3 reps left
    }

    [Fact]
    public void Weekly_AllOrNothing_Puts_The_Whole_Pot_On_The_Line()
    {
        // Saturday: 2 days left, 2 reps left (target 3, 1 credited) — the whole $15 pot.
        var chores = new[]
        {
            Weekly(1, "Walk Gemma", target: 3, completed: 1, earn: 5m, allOrNothing: true)
        };

        var result = Build(Saturday, chores, Pricing((1, 40)));

        var item = Assert.Single(result.Items);
        Assert.Equal(AtRiskUrgency.MustDoDaily, item.Urgency);
        Assert.Equal(15.00m, item.MoneyAtRisk); // EarnValue × target
        Assert.Equal(80, item.MinutesAtRisk);   // 40 × 2 reps left
    }

    [Fact]
    public void Weekly_PerRep_Money_Is_EarnValue_Times_RepsLeft()
    {
        var chores = new[]
        {
            Weekly(1, "Walk Gemma", target: 3, completed: 1, earn: 5m, allOrNothing: false)
        };

        var result = Build(Saturday, chores, Pricing((1, 40)));

        var item = Assert.Single(result.Items);
        Assert.Equal(10.00m, item.MoneyAtRisk); // EarnValue × reps left, not the pot
    }

    [Fact]
    public void Weekly_One_Spare_Day_Is_GettingTight_Named_For_The_Last_Slack_Day()
    {
        // Wednesday: 5 days left, 4 reps left — one spare day. After today (with no
        // rep done) every remaining day is a must, so the detail names Wednesday.
        var chores = new[] { Weekly(1, "Walk Gemma", target: 4, completed: 0, earn: 5m) };

        var result = Build(Wednesday, chores, Pricing((1, 45)));

        var item = Assert.Single(result.Items);
        Assert.Equal(AtRiskUrgency.GettingTight, item.Urgency);
        Assert.Equal("gets tight Wednesday", item.Detail);
        Assert.Equal(180, item.MinutesAtRisk); // 45 × 4 reps left
    }

    [Fact]
    public void Weekly_Comfortably_Ahead_Is_Calm()
    {
        // Monday: 7 days left, 3 reps left — nothing on the line today.
        var chores = new[] { Weekly(1, "Walk Gemma", target: 3, completed: 0, earn: 5m) };

        var result = Build(Monday, chores, Pricing((1, 45)));

        Assert.Empty(result.Items);
    }

    [Fact]
    public void Weekly_Impossible_Target_Shows_Redemption_Only_When_Priced()
    {
        // Saturday: 2 days left but 3 reps left — the pot is already lost. Included
        // only because reps still earn screen-time back; money is no longer at risk.
        var chores = new[]
        {
            Weekly(1, "Walk Gemma", target: 4, completed: 1, earn: 5m, allOrNothing: true)
        };

        var priced = Build(Saturday, chores, Pricing((1, 40)));

        var item = Assert.Single(priced.Items);
        Assert.Equal(AtRiskUrgency.MustDoDaily, item.Urgency);
        Assert.Equal("can't reach target — reps still earn time back", item.Detail);
        Assert.Equal(0.00m, item.MoneyAtRisk);
        Assert.Equal(120, item.MinutesAtRisk); // 40 × 3 reps left

        // Without a screen-time price there is nothing to earn back — omit entirely.
        var unpriced = Build(Saturday, chores, Pricing());
        Assert.Empty(unpriced.Items);
    }

    [Fact]
    public void Weekly_Quota_Met_Is_Excluded()
    {
        var chores = new[] { Weekly(1, "Walk Gemma", target: 3, completed: 3, earn: 5m) };

        var result = Build(Friday, chores, Pricing((1, 45)));

        Assert.Empty(result.Items);
    }

    [Fact]
    public void Weekly_Help_Is_Protected_From_The_Card_And_The_Preview()
    {
        // Would be MustDoDaily on Friday, but a live help request shields it.
        var chores = new[]
        {
            Weekly(1, "Walk Gemma", target: 3, completed: 0, earn: 5m, status: ChoreStatus.Help)
        };

        var result = Build(Friday, chores, Pricing((1, 45)));

        Assert.Empty(result.Items);
        Assert.Null(result.PreviewLine);
    }

    // ============================================================
    // Ordering and totals
    // ============================================================

    [Fact]
    public void Items_Are_Ordered_By_Urgency_Then_Minutes_Descending()
    {
        // Friday (3 days left): two due tonight, one must-do-daily, one getting tight.
        var chores = new[]
        {
            Daily(1, "Dishes", earn: 1m),
            Daily(2, "Vacuum", earn: 2m),
            Weekly(3, "Walk Gemma", target: 3, completed: 0, earn: 5m),  // 3 reps / 3 days
            Weekly(4, "Reading", target: 2, completed: 0, earn: 4m)     // 2 reps / 3 days
        };
        var pricing = Pricing((1, 30), (2, 90), (3, 45), (4, 50));

        var result = Build(Friday, chores, pricing);

        Assert.Equal(
            new[] { 2, 1, 3, 4 },
            result.Items.Select(i => i.ChoreDefinitionId).ToArray());
        Assert.Equal(
            new[]
            {
                AtRiskUrgency.DueTonight,
                AtRiskUrgency.DueTonight,
                AtRiskUrgency.MustDoDaily,
                AtRiskUrgency.GettingTight
            },
            result.Items.Select(i => i.Urgency).ToArray());
    }

    [Fact]
    public void Totals_Sum_The_Items()
    {
        var chores = new[]
        {
            Daily(1, "Dishes", earn: 2.50m),
            Daily(2, "Trash", earn: 1.00m)
        };
        var pricing = Pricing((1, 45), (2, 15));

        var result = Build(Monday, chores, pricing);

        Assert.Equal(3.50m, result.TotalMoneyAtRisk);
        Assert.Equal(60, result.TotalMinutesAtRisk);
    }

    // ============================================================
    // Calm state / preview line
    // ============================================================

    [Fact]
    public void Calm_State_Shows_The_Nearest_Weekly_Pinch_As_The_Only_Preview()
    {
        // Monday, 3 reps left, 7 days left: calm. With no reps done it gets tight
        // on Thursday (after Thursday every remaining day is a must).
        var chores = new[] { Weekly(1, "Walk Gemma", target: 3, completed: 0, earn: 5m) };

        var result = Build(Monday, chores, Pricing((1, 45)));

        Assert.Empty(result.Items);
        Assert.Equal("Walk Gemma gets tight Thursday", result.PreviewLine);
    }

    [Fact]
    public void Calm_State_Preview_Falls_Back_To_The_Next_Priced_Scheduled_Chore()
    {
        // No weekly pinch coming; the next PRICED scheduled chore previews instead.
        // The unpriced Tuesday chore is skipped even though it is sooner.
        var upcoming = new[]
        {
            new UpcomingChoreInstance(2, "Fold Laundry", new DateOnly(2026, 7, 7)),  // Tuesday, unpriced
            new UpcomingChoreInstance(1, "Dishes", new DateOnly(2026, 7, 8))         // Wednesday, priced
        };

        var result = Build(Monday, Array.Empty<TrackerChoreItem>(), Pricing((1, 45)), upcoming);

        Assert.Empty(result.Items);
        Assert.Equal("Dishes due Wednesday", result.PreviewLine);
    }

    [Fact]
    public void Preview_Is_Never_More_Than_One_Line_And_Picks_The_Nearest()
    {
        // Two candidates: Dishes due Tuesday and Walk Gemma tight Thursday.
        // Only the nearest one is spoken — one line, never a list.
        var chores = new[] { Weekly(1, "Walk Gemma", target: 3, completed: 0, earn: 5m) };
        var upcoming = new[]
        {
            new UpcomingChoreInstance(2, "Dishes", new DateOnly(2026, 7, 7)) // Tuesday
        };

        var result = Build(Monday, chores, Pricing((1, 45), (2, 30)), upcoming);

        Assert.Empty(result.Items);
        Assert.Equal("Dishes due Tuesday", result.PreviewLine);
    }

    [Fact]
    public void Preview_Is_Suppressed_When_Anything_Is_On_The_Card()
    {
        // A due-tonight item plus a future weekly pinch: the card shows the item
        // and stays quiet about the future — real states only.
        var chores = new[]
        {
            Daily(1, "Dishes", earn: 2m),
            Weekly(2, "Walk Gemma", target: 3, completed: 0, earn: 5m)
        };

        var result = Build(Monday, chores, Pricing((1, 45), (2, 40)));

        Assert.Single(result.Items);
        Assert.Null(result.PreviewLine);
    }

    [Fact]
    public void Empty_Everything_Is_Calm_With_Null_Preview_And_Zero_Totals()
    {
        var result = Build(Monday, Array.Empty<TrackerChoreItem>(), Pricing());

        Assert.Empty(result.Items);
        Assert.Null(result.PreviewLine);
        Assert.Equal(0.00m, result.TotalMoneyAtRisk);
        Assert.Equal(0, result.TotalMinutesAtRisk);
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static AtRiskComputation Build(
        DateOnly today,
        IReadOnlyList<TrackerChoreItem> chores,
        WeekPricing pricing,
        IReadOnlyList<UpcomingChoreInstance>? upcoming = null)
        => AtRiskRules.Build(ChildId, today, WeekEnd, chores, pricing,
            upcoming ?? Array.Empty<UpcomingChoreInstance>());

    private static TrackerChoreItem Daily(
        int id, string name, decimal earn = 0m, ChoreStatus status = ChoreStatus.Pending) => new()
    {
        ChoreDefinitionId = id,
        ChoreName = name,
        ScheduleType = ChoreScheduleType.SpecificDays,
        EarnValue = earn,
        Status = status
    };

    private static TrackerChoreItem Weekly(
        int id,
        string name,
        int target,
        int completed,
        decimal earn = 0m,
        bool allOrNothing = false,
        ChoreStatus status = ChoreStatus.Pending) => new()
    {
        ChoreDefinitionId = id,
        ChoreName = name,
        ScheduleType = ChoreScheduleType.WeeklyFrequency,
        WeeklyTargetCount = target,
        WeeklyCompletedCount = completed,
        EarnValue = earn,
        AllOrNothing = allOrNothing,
        Status = status
    };

    /// <summary>Budgets mirror the seeded defaults (720/600); only per-instance prices matter here.</summary>
    private static WeekPricing Pricing(params (int ChoreId, int PerInstanceMinutes)[] prices) =>
        new(720, 600, prices.ToDictionary(
            p => p.ChoreId,
            p => new ChorePrice(ScreenTimePool.Weekday, ScheduledInstances: 1, p.PerInstanceMinutes)));
}
