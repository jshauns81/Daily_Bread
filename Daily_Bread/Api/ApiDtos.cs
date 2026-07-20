using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daily_Bread.Api;

/// <summary>
/// Wire conventions for /api/v1 (see docs/IOS_APP_PLAN.md):
/// - camelCase JSON (framework default for controllers)
/// - DateOnly as "yyyy-MM-dd" (System.Text.Json default)
/// - Money serialized as a decimal STRING ("12.50") so clients can decode
///   straight into Swift Decimal without a double round-trip.
/// </summary>
public sealed class MoneyStringConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Accept both string ("12.50") and number (12.5) for robustness.
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            return decimal.Parse(s!, NumberStyles.Number, CultureInfo.InvariantCulture);
        }
        return reader.GetDecimal();
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("0.00", CultureInfo.InvariantCulture));
}

// ---------- Auth ----------

public sealed record LoginRequest(string UserName, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string? RefreshToken);

public sealed record ApiUserDto(
    string UserId,
    string UserName,
    IReadOnlyList<string> Roles,
    Guid? HouseholdId);

public sealed record TokenResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    ApiUserDto User);

public sealed record ApiError(string Code, string Message);

// ---------- Chores ----------

public sealed record ChoreItemDto(
    int ChoreDefinitionId,
    int? ChoreLogId,
    string Name,
    string? Description,
    string? Icon,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal EarnValue,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal PenaltyValue,
    string Status,
    string ScheduleType,
    int WeeklyTargetCount,
    int WeeklyCompletedCount,
    bool IsRepeatable,
    string? HelpReason,
    DateTime? HelpRequestedAtUtc,
    string? ApprovedByUserName,
    DateTime? ApprovedAtUtc);

public sealed record TodayChoresResponse(
    DateOnly Date,
    string UserId,
    string? UserName,
    IReadOnlyList<ChoreItemDto> Items);

// ---------- Chore actions ----------

public sealed record ChoreToggleRequest(DateOnly? Date, string? UserId);

public sealed record ChoreToggleResponse(string Status);

public sealed record HelpRaiseRequest(DateOnly? Date, string Reason);

/// <summary>Response: "CompletedByParent" | "Excused" | "Denied".</summary>
public sealed record HelpRespondRequest(string Response, string? Note);

// ---------- Week progress ----------

public sealed record WeekChoreProgressDto(
    int ChoreDefinitionId,
    string Name,
    int CompletedCount,
    int TargetCount);

public sealed record WeekProgressResponse(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    string UserId,
    IReadOnlyList<WeekChoreProgressDto> Chores);

// ---------- Ledger ----------

public sealed record BalanceResponse(
    string UserId,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal Balance);

public sealed record TransactionDto(
    int Id,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal Amount,
    string Type,
    string? Description,
    DateOnly Date,
    int? ChoreDefinitionId);

public sealed record LedgerHistoryResponse(
    string UserId,
    IReadOnlyList<TransactionDto> Transactions);

// ---------- Goals ----------

public sealed record GoalDto(
    int Id,
    string Name,
    string? Description,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal TargetAmount,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal CurrentBalance,
    int ProgressPercent,
    int Priority,
    bool IsPrimary,
    bool IsCompleted,
    string? ImageUrl);

public sealed record GoalWriteRequest(
    string Name,
    string? Description,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal TargetAmount,
    string? ImageUrl,
    int Priority,
    bool IsPrimary,
    string? UserId);

// ---------- Calendar / heatmap ----------

public sealed record DaySummaryDto(
    DateOnly Date,
    string Status,
    int TotalChores,
    int CompletedChores,
    int ApprovedChores,
    int MissedChores,
    int PendingChores,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal EarnedAmount);

public sealed record CalendarRangeResponse(
    string UserId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DaySummaryDto> Days);

// ---------- Approvals / parent dashboard ----------

public sealed record ApprovalItemDto(
    int ChoreLogId,
    int ChoreDefinitionId,
    string ChoreName,
    string ChildName,
    string? ChildUserId,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal EarnValue);

public sealed record HelpRequestDto(
    int ChoreLogId,
    int ChoreDefinitionId,
    string ChoreName,
    string ChildName,
    string? ChildUserId,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal EarnValue,
    string? Reason,
    DateOnly Date,
    DateTime? RequestedAtUtc);

public sealed record ApprovalsResponse(
    IReadOnlyList<ApprovalItemDto> PendingApprovals,
    IReadOnlyList<HelpRequestDto> HelpRequests);

public sealed record ChildProgressDto(
    string? UserId,
    string DisplayName,
    int TotalChores,
    int CompletedChores,
    int ApprovedChores,
    int PendingChores,
    int HelpRequests);

public sealed record ChildBalanceDto(
    string DisplayName,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal Balance,
    bool CanCashOut);

// ---------- Family features / achievements ----------

public sealed record FamilyFeaturesDto(
    bool EnableGoals,
    bool EnableConfetti,
    bool EnableStreaks);

public sealed record AchievementDto(
    int Id,
    string Name,
    string Description,
    string Icon,
    string Category,
    string Rarity,
    int Points,
    bool IsEarned,
    DateTime? EarnedAtUtc,
    bool IsNew,
    bool ShowProgress,
    int CurrentProgress,
    int TargetProgress,
    int ProgressPercent);

public sealed record AchievementsResponse(
    string UserId,
    int TotalPoints,
    int EarnedCount,
    int TotalCount,
    IReadOnlyList<AchievementDto> Achievements);

// ---------- Screen time ----------

/// <summary>
/// One screen-time pool (weekday or weekend) as the kid sees it: the base allowance,
/// what's left after any applied losses, the guaranteed floor, and the maximum that
/// can be lost this week. All values are minutes — the client formats hours.
/// </summary>
public sealed record ScreenTimePoolDto(
    string Pool,
    int BaseMinutes,
    int EffectiveMinutes,
    int FloorMinutes,
    int AtRiskMinutes);

/// <summary>
/// The live minute price of one chore this week ("miss once: −N min"). Importance is
/// deliberately never exposed — the UI contract is minutes only (MECHANICS_AMENDMENT.md §A).
/// </summary>
public sealed record ScreenTimeChorePriceDto(
    int ChoreDefinitionId,
    string Name,
    string Pool,
    int ScheduledInstances,
    int PerInstanceMinutes);

/// <summary>A labeled line from the screen-time ledger (deduction, earn-back, adjustment, Time Machine).</summary>
public sealed record ScreenTimeEntryDto(
    int Id,
    DateOnly WeekStart,
    string Pool,
    string Kind,
    string? ChoreName,
    int Minutes,
    string? Note,
    DateTime CreatedAtUtc);

public sealed record ScreenTimeResponse(
    string UserId,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    // "Pool" suffix keeps the wire name distinct from weekEnd — ASP.NET's
    // case-insensitive JSON binding treats weekEnd/weekend as a collision.
    ScreenTimePoolDto WeekdayPool,
    ScreenTimePoolDto WeekendPool,
    IReadOnlyList<ScreenTimeChorePriceDto> ChorePrices,
    IReadOnlyList<ScreenTimeEntryDto> RecentEntries);

/// <summary>
/// One chore on the kid's "At Risk Today" card (MECHANICS_AMENDMENT.md §E).
/// Urgency: "DueTonight" | "MustDoDaily" | "GettingTight". Detail is the
/// server-authored kid-facing one-liner ("due tonight", "gets tight Thursday").
/// MoneyAtRisk is "0.00" when none; MinutesAtRisk is 0 when the chore has no
/// screen-time price.
/// </summary>
public sealed record AtRiskItemDto(
    int ChoreDefinitionId,
    string Name,
    string Urgency,
    string Detail,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal MoneyAtRisk,
    int MinutesAtRisk);

/// <summary>
/// The "At Risk Today" card: items ordered for display with the day's totals.
/// PreviewLine is set only when items is empty — the single nearest future
/// pinch this week, or null. Never more than one line; never nag.
/// </summary>
public sealed record AtRiskResponse(
    string UserId,
    DateOnly Date,
    IReadOnlyList<AtRiskItemDto> Items,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal TotalMoneyAtRisk,
    int TotalMinutesAtRisk,
    string? PreviewLine);

/// <summary>
/// Parent-only settings update (PUT /api/v1/screentime/settings). UserId is
/// required — parents always tune a specific kid. Hours are plain JSON
/// numbers; the payout is a money string like every other money field.
/// </summary>
public sealed record ScreenTimeSettingsRequest(
    string UserId,
    decimal WeekdayHours,
    decimal WeekendHours,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal WeeklyRoutinePayout,
    int WeekdayAtRiskPercent,
    int WeekendAtRiskPercent);

public sealed record DailyEarningDto(
    DateOnly Date,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal Amount);

public sealed record ParentDashboardResponse(
    int TodayCompletedCount,
    int TodayPendingCount,
    int TodayApprovedCount,
    int TodayHelpCount,
    int TodayTotalChores,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal ThisWeekEarnings,
    [property: JsonConverter(typeof(MoneyStringConverter))] decimal WeeklyPotential,
    IReadOnlyList<DailyEarningDto> WeekEarnings,
    IReadOnlyList<ChildProgressDto> ChildrenProgress,
    IReadOnlyList<ChildBalanceDto> ChildrenBalances,
    IReadOnlyList<ApprovalItemDto> PendingApprovals,
    IReadOnlyList<HelpRequestDto> HelpRequests);
