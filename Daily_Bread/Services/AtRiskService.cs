using Daily_Bread.Data.Models;

namespace Daily_Bread.Services;

/// <summary>
/// How urgent one at-risk chore is on the kid's "At Risk Today" card
/// (MECHANICS_AMENDMENT.md §E). The numeric order doubles as the card's sort
/// order: hard deadlines first, then daily musts, then amber warnings.
/// </summary>
public enum AtRiskUrgency
{
    /// <summary>A day-prescribed chore scheduled today and not yet done.</summary>
    DueTonight = 0,

    /// <summary>A weekly chore that now needs one rep every remaining day of the week.</summary>
    MustDoDaily = 1,

    /// <summary>A weekly chore with exactly one spare day left before it becomes must-do-daily.</summary>
    GettingTight = 2
}

/// <summary>
/// One chore on the at-risk card: what it is, how urgent, the kid-facing
/// one-liner, and the exact stakes. Detail strings are warm and plain — the
/// importance number is never surfaced, only money and minutes.
/// </summary>
public sealed record AtRiskChore(
    int ChoreDefinitionId,
    string Name,
    AtRiskUrgency Urgency,
    string Detail,
    decimal MoneyAtRisk,
    int MinutesAtRisk);

/// <summary>
/// The full "At Risk Today" computation for one child on one date. Items are
/// already ordered for display; the preview line is set only in the calm state
/// (no items) and there is never more than one — the card must not nag.
/// </summary>
public sealed record AtRiskComputation(
    string UserId,
    DateOnly Date,
    IReadOnlyList<AtRiskChore> Items,
    decimal TotalMoneyAtRisk,
    int TotalMinutesAtRisk,
    string? PreviewLine);

/// <summary>
/// A future scheduled instance of a day-prescribed chore within the current
/// week, used to pick the calm state's "next pinch" preview line.
/// </summary>
public sealed record UpcomingChoreInstance(
    int ChoreDefinitionId,
    string Name,
    DateOnly Date);

/// <summary>
/// Computes the kid's "At Risk Today" card (MECHANICS_AMENDMENT.md §E): which
/// incomplete chores put money or screen-time on the line today, and the single
/// preview line for the next pinch when nothing does.
/// </summary>
public interface IAtRiskService
{
    /// <summary>
    /// Computes the at-risk card for a child on a date. Stakes come from the
    /// same pricing the meter shows (<see cref="IScreenTimePricingService"/>),
    /// so the card and the live prices always agree.
    /// </summary>
    Task<AtRiskComputation> ComputeAsync(string userId, int childProfileId, DateOnly today);
}

/// <summary>
/// Pure §E decision logic (urgency classification, stakes math, preview-line
/// selection). Kept static and dependency-free — like ScreenTimePricing — so
/// the card, the Friday-style push, and the tests all run the exact same rules
/// without a database.
/// </summary>
public static class AtRiskRules
{
    /// <summary>
    /// Assembles the full computation from already-loaded pieces. Items are
    /// classified and ordered; totals sum the items; the preview line is
    /// selected only when the card is calm (no items), never otherwise.
    /// </summary>
    public static AtRiskComputation Build(
        string userId,
        DateOnly today,
        DateOnly weekEnd,
        IReadOnlyList<TrackerChoreItem> todaysChores,
        WeekPricing pricing,
        IReadOnlyList<UpcomingChoreInstance> upcomingInstances)
    {
        var items = BuildItems(today, weekEnd, todaysChores, pricing);

        var previewLine = items.Count == 0
            ? SelectPreviewLine(today, weekEnd, todaysChores, pricing, upcomingInstances)
            : null;

        return new AtRiskComputation(
            userId,
            today,
            items,
            items.Sum(i => i.MoneyAtRisk),
            items.Sum(i => i.MinutesAtRisk),
            previewLine);
    }

    /// <summary>
    /// Classifies today's chores into at-risk items and orders them for the
    /// card: DueTonight, then MustDoDaily, then GettingTight; within a group by
    /// minutes at risk descending (name breaks ties so the order is stable).
    /// </summary>
    public static IReadOnlyList<AtRiskChore> BuildItems(
        DateOnly today,
        DateOnly weekEnd,
        IReadOnlyList<TrackerChoreItem> todaysChores,
        WeekPricing pricing)
    {
        var daysLeft = DaysLeft(today, weekEnd);

        var items = new List<AtRiskChore>();
        foreach (var chore in todaysChores)
        {
            var price = pricing.ChorePrices.TryGetValue(chore.ChoreDefinitionId, out var p) ? p : null;

            var item = chore.ScheduleType == ChoreScheduleType.WeeklyFrequency
                ? EvaluateWeekly(chore, price, daysLeft, today)
                : EvaluateSpecificDays(chore, price);

            if (item != null)
            {
                items.Add(item);
            }
        }

        return items
            .OrderBy(i => i.Urgency)
            .ThenByDescending(i => i.MinutesAtRisk)
            .ThenBy(i => i.Name)
            .ToList();
    }

    /// <summary>
    /// Picks the calm state's single preview line: the nearest future pinch
    /// within this week — the earliest GettingTight-to-be among the weekly
    /// chores, or the next scheduled priced day-prescribed chore — else null.
    /// Never more than one line; never nag.
    /// </summary>
    public static string? SelectPreviewLine(
        DateOnly today,
        DateOnly weekEnd,
        IReadOnlyList<TrackerChoreItem> todaysChores,
        WeekPricing pricing,
        IReadOnlyList<UpcomingChoreInstance> upcomingInstances)
    {
        var daysLeft = DaysLeft(today, weekEnd);

        // (Date, KindRank, Name) orders candidates: nearest first, weekly
        // pinches before scheduled ones on the same day, name for stability.
        var candidates = new List<(DateOnly Date, int KindRank, string Name, string Line)>();

        foreach (var chore in todaysChores)
        {
            if (chore.ScheduleType != ChoreScheduleType.WeeklyFrequency) continue;
            if (chore.Status == ChoreStatus.Help) continue; // Help-protected — that is the point of Help.

            var repsLeft = chore.WeeklyTargetCount - chore.WeeklyCompletedCount;
            if (repsLeft <= 0) continue;                 // quota met — nothing coming
            if (daysLeft <= repsLeft + 1) continue;      // tight or worse would be an item, not a preview

            var perInstance = pricing.ChorePrices.TryGetValue(chore.ChoreDefinitionId, out var price)
                ? price.PerInstanceMinutes
                : 0;
            if (chore.EarnValue <= 0 && perInstance <= 0) continue; // no stakes, no pinch

            // The last day with slack: after it (with no reps done) every
            // remaining day is a must — the same date the item's GettingTight
            // detail will name when it arrives.
            var tightDate = today.AddDays(daysLeft - repsLeft - 1);
            candidates.Add((tightDate, 0, chore.ChoreName,
                $"{chore.ChoreName} gets tight {tightDate.DayOfWeek}"));
        }

        foreach (var instance in upcomingInstances)
        {
            if (instance.Date <= today || instance.Date > weekEnd) continue;

            var perInstance = pricing.ChorePrices.TryGetValue(instance.ChoreDefinitionId, out var price)
                ? price.PerInstanceMinutes
                : 0;
            if (perInstance <= 0) continue; // only priced chores make a pinch worth previewing

            candidates.Add((instance.Date, 1, instance.Name,
                $"{instance.Name} due {instance.Date.DayOfWeek}"));
        }

        return candidates
            .OrderBy(c => c.Date)
            .ThenBy(c => c.KindRank)
            .ThenBy(c => c.Name)
            .Select(c => c.Line)
            .FirstOrDefault();
    }

    /// <summary>
    /// A day-prescribed chore is at risk on its day only while still Pending:
    /// Completed/Approved are done, Help is protected (that is the point of
    /// Help), Skipped is excused (counts as done, no hit), and Missed is
    /// already ruled — no longer savable tonight.
    /// </summary>
    private static AtRiskChore? EvaluateSpecificDays(TrackerChoreItem chore, ChorePrice? price)
    {
        if (chore.Status != ChoreStatus.Pending)
        {
            return null;
        }

        return new AtRiskChore(
            chore.ChoreDefinitionId,
            chore.ChoreName,
            AtRiskUrgency.DueTonight,
            "due tonight",
            chore.EarnValue,
            price?.PerInstanceMinutes ?? 0);
    }

    /// <summary>
    /// A weekly chore escalates on pure arithmetic: repsLeft against the days
    /// remaining in the family week (including today). All-or-nothing chores
    /// put the whole pot on the line; per-rep chores only the remaining reps.
    /// </summary>
    private static AtRiskChore? EvaluateWeekly(
        TrackerChoreItem chore, ChorePrice? price, int daysLeft, DateOnly today)
    {
        if (chore.Status == ChoreStatus.Help)
        {
            return null; // Help-protected — that is the point of Help.
        }

        var repsLeft = chore.WeeklyTargetCount - chore.WeeklyCompletedCount;
        if (repsLeft <= 0)
        {
            return null; // quota met
        }

        var perInstance = price?.PerInstanceMinutes ?? 0;
        var minutesAtRisk = perInstance * repsLeft;

        if (repsLeft > daysLeft)
        {
            // Already impossible: the loss is pending, not preventable, so the
            // money is no longer "at risk" — but reps still earn time back
            // (§D redemption), which is the only reason to show it at all.
            if (minutesAtRisk <= 0)
            {
                return null;
            }

            return new AtRiskChore(
                chore.ChoreDefinitionId,
                chore.ChoreName,
                AtRiskUrgency.MustDoDaily,
                "can't reach target — reps still earn time back",
                0m,
                minutesAtRisk);
        }

        // Falling short by any amount forfeits the whole pot on an
        // all-or-nothing chore; per-rep chores only lose the remaining reps.
        var moneyAtRisk = chore.AllOrNothing
            ? chore.EarnValue * chore.WeeklyTargetCount
            : chore.EarnValue * repsLeft;

        if (daysLeft == repsLeft)
        {
            return new AtRiskChore(
                chore.ChoreDefinitionId,
                chore.ChoreName,
                AtRiskUrgency.MustDoDaily,
                "needs one every day left",
                moneyAtRisk,
                minutesAtRisk);
        }

        if (daysLeft == repsLeft + 1)
        {
            // Name the day AFTER which it becomes must-do-daily (the last day
            // with slack). With one spare day left that is today; the formula
            // is kept general so it matches the preview line's future date.
            var tightDay = today.AddDays(daysLeft - repsLeft - 1);

            return new AtRiskChore(
                chore.ChoreDefinitionId,
                chore.ChoreName,
                AtRiskUrgency.GettingTight,
                $"gets tight {tightDay.DayOfWeek}",
                moneyAtRisk,
                minutesAtRisk);
        }

        return null; // comfortably ahead — calm
    }

    /// <summary>Days remaining in the family week, INCLUDING today.</summary>
    private static int DaysLeft(DateOnly today, DateOnly weekEnd) =>
        Math.Max(0, weekEnd.DayNumber - today.DayNumber + 1);
}

/// <summary>
/// Orchestration only: loads today's tracker items, the week's pricing, and —
/// only when the card is calm — the rest of the week's schedule, then hands
/// everything to the pure <see cref="AtRiskRules"/>.
/// </summary>
public sealed class AtRiskService : IAtRiskService
{
    private readonly ITrackerService _trackerService;
    private readonly IScreenTimePricingService _pricingService;
    private readonly IFamilySettingsService _familySettingsService;
    private readonly IChoreScheduleService _scheduleService;

    public AtRiskService(
        ITrackerService trackerService,
        IScreenTimePricingService pricingService,
        IFamilySettingsService familySettingsService,
        IChoreScheduleService scheduleService)
    {
        _trackerService = trackerService;
        _pricingService = pricingService;
        _familySettingsService = familySettingsService;
        _scheduleService = scheduleService;
    }

    public async Task<AtRiskComputation> ComputeAsync(string userId, int childProfileId, DateOnly today)
    {
        var weekEnd = await _familySettingsService.GetWeekEndForDateAsync(today);
        var todaysChores = await _trackerService.GetTrackerItemsForUserOnDateAsync(userId, today);
        var pricing = await _pricingService.GetWeekPricingAsync(childProfileId, today);

        // The preview line only renders in the calm state, so the scan of the
        // rest of the week is skipped entirely when anything is on the card.
        var items = AtRiskRules.BuildItems(today, weekEnd, todaysChores, pricing);
        var upcoming = items.Count == 0
            ? await LoadUpcomingInstancesAsync(userId, today, weekEnd)
            : Array.Empty<UpcomingChoreInstance>();

        return AtRiskRules.Build(userId, today, weekEnd, todaysChores, pricing, upcoming);
    }

    /// <summary>
    /// Day-prescribed instances scheduled after today through the end of the
    /// family week, honoring per-date overrides via the schedule service (which
    /// caches, so this stays cheap).
    /// </summary>
    private async Task<IReadOnlyList<UpcomingChoreInstance>> LoadUpcomingInstancesAsync(
        string userId, DateOnly today, DateOnly weekEnd)
    {
        var upcoming = new List<UpcomingChoreInstance>();

        for (var date = today.AddDays(1); date <= weekEnd; date = date.AddDays(1))
        {
            var chores = await _scheduleService.GetChoresForUserOnDateAsync(userId, date);
            foreach (var chore in chores)
            {
                if (chore.ScheduleType != ChoreScheduleType.SpecificDays) continue;
                upcoming.Add(new UpcomingChoreInstance(chore.Id, chore.Name, date));
            }
        }

        return upcoming;
    }
}
