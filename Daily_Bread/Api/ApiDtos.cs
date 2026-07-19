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
