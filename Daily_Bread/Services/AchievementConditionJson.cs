using System.Text.Json;
using Daily_Bread.Data.Models;

namespace Daily_Bread.Services;

/// <summary>
/// The structured parameters behind an achievement's unlock condition. Not every
/// field applies to every condition type — Build/Parse only read the ones the
/// type uses (see UnlockConditionType's JSON notes). Keeping this typed means the
/// clients never hand-author the condition JSON; the server owns that shape.
/// </summary>
public sealed record AchievementConditionParams(
    int? Count = null,
    int? Days = null,
    int? Weeks = null,
    decimal? Amount = null,
    int? ChoreId = null,
    int? BeforeHour = null,
    string? DayType = null);

/// <summary>
/// Canonical translation between an unlock condition's typed params and the JSON
/// string the achievement stores (and the evaluator reads). Pure and
/// dependency-free — round-trips are easy to unit-test without a database.
/// </summary>
public static class AchievementConditionJson
{
    /// <summary>Typed params → the stored JSON string for this condition type.</summary>
    public static string Build(UnlockConditionType type, AchievementConditionParams p)
    {
        var d = new Dictionary<string, object>();
        switch (type)
        {
            case UnlockConditionType.ChoresCompleted:
            case UnlockConditionType.PerfectDays:
            case UnlockConditionType.GoalCompleted:
            case UnlockConditionType.BonusChoresCompleted:
            case UnlockConditionType.ChoreRecovery:
                d["count"] = p.Count ?? 1;
                break;

            case UnlockConditionType.StreakDays:
            case UnlockConditionType.AccountAge:
                d["days"] = p.Days ?? 1;
                break;

            case UnlockConditionType.WeekStreak:
                d["weeks"] = p.Weeks ?? 1;
                break;

            case UnlockConditionType.TotalEarned:
            case UnlockConditionType.BalanceReached:
            case UnlockConditionType.WeeklyEarnings:
                d["amount"] = p.Amount ?? 0m;
                break;

            case UnlockConditionType.SpecificChoreCount:
                d["chore_id"] = p.ChoreId ?? 0;
                d["count"] = p.Count ?? 1;
                break;

            case UnlockConditionType.EarlyCompletion:
                d["before_hour"] = p.BeforeHour ?? 12;
                d["count"] = p.Count ?? 1;
                break;

            case UnlockConditionType.DayTypeCompletion:
                d["day_type"] = p.DayType ?? "Weekend";
                d["count"] = p.Count ?? 1;
                break;

            // Manual, FirstChore, FirstGoal, FirstDollar and any other no-param
            // types serialize to "{}".
        }

        return JsonSerializer.Serialize(d);
    }

    /// <summary>Stored JSON → typed params, for prefilling the editor.</summary>
    public static AchievementConditionParams Parse(UnlockConditionType type, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AchievementConditionParams();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int? GetInt(string key) =>
                root.TryGetProperty(key, out var v) && v.TryGetInt32(out var i) ? i : null;
            decimal? GetDecimal(string key) =>
                root.TryGetProperty(key, out var v) && v.TryGetDecimal(out var m) ? m : null;
            string? GetString(string key) =>
                root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            return new AchievementConditionParams(
                Count: GetInt("count"),
                Days: GetInt("days"),
                Weeks: GetInt("weeks"),
                Amount: GetDecimal("amount"),
                ChoreId: GetInt("chore_id"),
                BeforeHour: GetInt("before_hour"),
                DayType: GetString("day_type"));
        }
        catch (JsonException)
        {
            return new AchievementConditionParams();
        }
    }
}
