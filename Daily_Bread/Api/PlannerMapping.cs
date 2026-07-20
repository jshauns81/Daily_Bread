using Daily_Bread.Data.Models;
using Daily_Bread.Services;

namespace Daily_Bread.Api;

/// <summary>
/// Pure wire mapping for the planner: entity → wire DTO and wire → service
/// DTO, plus the string↔enum translations the wire contract demands (enum
/// names as strings, day flags as full day names in week order). Deliberately
/// dependency-free — no DI, no database — so the controller and the unit
/// tests exercise the exact same translation.
/// </summary>
public static class PlannerMapping
{
    /// <summary>
    /// The week in wire order (Sunday…Saturday), pairing each flag with the
    /// full day name the contract sends. Single source of truth for both
    /// directions so a rename can never desynchronize them.
    /// </summary>
    private static readonly (DaysOfWeek Flag, string Name)[] WeekOrder =
    [
        (DaysOfWeek.Sunday, "Sunday"),
        (DaysOfWeek.Monday, "Monday"),
        (DaysOfWeek.Tuesday, "Tuesday"),
        (DaysOfWeek.Wednesday, "Wednesday"),
        (DaysOfWeek.Thursday, "Thursday"),
        (DaysOfWeek.Friday, "Friday"),
        (DaysOfWeek.Saturday, "Saturday")
    ];

    /// <summary>
    /// Maps a chore entity to the wire DTO. Enum values become their names,
    /// the day flags become full day names in week order, and the money value
    /// passes through raw — the DTO's MoneyStringConverter owns the string
    /// form. AssignedUserName reads the AssignedUser navigation, which the
    /// service Include()s on every read path.
    /// </summary>
    public static PlannerChoreDto FromEntity(ChoreDefinition chore) => new(
        chore.Id,
        chore.Name,
        chore.Description,
        chore.Icon,
        chore.AssignedUserId,
        chore.AssignedUser?.UserName,
        chore.Kind.ToString(),
        chore.EarnValue,
        chore.Importance,
        chore.AllOrNothing,
        chore.IsInverseFill,
        chore.InverseFillBaselineMinutes,
        chore.ScheduleType.ToString(),
        ToDayNames(chore.ActiveDays),
        chore.WeeklyTargetCount,
        chore.IsRepeatable,
        chore.StartDate,
        chore.EndDate,
        chore.IsActive,
        chore.AutoApprove,
        chore.SortOrder);

    /// <summary>
    /// Maps a wire write request to the service DTO. Pass the route id for
    /// updates; leave it 0 for creates. Throws ArgumentException on an unknown
    /// kind, schedule type, or day name — the controller validates with the
    /// TryParse helpers first, so a throw here means a controller bug, not bad
    /// client input. A blank assignedUserId is normalized to null so an empty
    /// string can never reach the foreign key.
    /// </summary>
    public static ChoreDefinitionDto ToServiceDto(ChoreWriteRequest request, int id = 0)
    {
        if (!TryParseKind(request.Kind, out var kind))
        {
            throw new ArgumentException($"Unknown chore kind '{request.Kind}'.", nameof(request));
        }

        if (!TryParseScheduleType(request.ScheduleType, out var scheduleType))
        {
            throw new ArgumentException($"Unknown schedule type '{request.ScheduleType}'.", nameof(request));
        }

        if (!TryParseDays(request.ActiveDays, out var days, out var unknownDay))
        {
            throw new ArgumentException($"Unknown day name '{unknownDay}'.", nameof(request));
        }

        return new ChoreDefinitionDto
        {
            Id = id,
            Name = request.Name,
            Description = request.Description,
            Icon = request.Icon,
            AssignedUserId = string.IsNullOrWhiteSpace(request.AssignedUserId) ? null : request.AssignedUserId,
            Kind = kind,
            EarnValue = request.EarnValue,
            Importance = request.Importance,
            AllOrNothing = request.AllOrNothing,
            IsInverseFill = request.IsInverseFill,
            InverseFillBaselineMinutes = request.InverseFillBaselineMinutes,
            ScheduleType = scheduleType,
            ActiveDays = days,
            WeeklyTargetCount = request.WeeklyTargetCount,
            IsRepeatable = request.IsRepeatable,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsActive = request.IsActive,
            AutoApprove = request.AutoApprove,
            SortOrder = request.SortOrder
        };
    }

    /// <summary>
    /// Day flags → full day names in week order (Sunday…Saturday), only the
    /// days present in the flags. None → empty list.
    /// </summary>
    public static List<string> ToDayNames(DaysOfWeek days)
    {
        var names = new List<string>();
        foreach (var (flag, name) in WeekOrder)
        {
            if ((days & flag) == flag)
            {
                names.Add(name);
            }
        }
        return names;
    }

    /// <summary>
    /// Day-name list → day flags. Fails (rather than silently dropping) on
    /// any name that isn't one of the seven exact full day names, so the
    /// controller can 400 instead of persisting a schedule the client didn't
    /// ask for. Composite names ("Weekdays", "All") are wire-illegal — the
    /// server always emits individual days, so it only accepts them too.
    /// A null or empty list parses to None.
    /// </summary>
    public static bool TryParseDays(IReadOnlyList<string>? names, out DaysOfWeek days)
        => TryParseDays(names, out days, out _);

    /// <summary>
    /// Same as the two-argument overload, but reports the first unknown name
    /// so the 400 message can point at the offending value.
    /// </summary>
    public static bool TryParseDays(IReadOnlyList<string>? names, out DaysOfWeek days, out string? firstUnknownName)
    {
        days = DaysOfWeek.None;
        firstUnknownName = null;

        if (names == null)
        {
            return true;
        }

        foreach (var name in names)
        {
            var matched = false;
            foreach (var (flag, dayName) in WeekOrder)
            {
                if (string.Equals(name, dayName, StringComparison.Ordinal))
                {
                    days |= flag;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                firstUnknownName = name;
                days = DaysOfWeek.None;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// "Task" | "Routine" → ChoreKind. Exact names only — deliberately not
    /// Enum.TryParse, which would also accept numeric strings off the wire.
    /// </summary>
    public static bool TryParseKind(string? value, out ChoreKind kind)
    {
        switch (value)
        {
            case "Task":
                kind = ChoreKind.Task;
                return true;
            case "Routine":
                kind = ChoreKind.Routine;
                return true;
            default:
                kind = ChoreKind.Task;
                return false;
        }
    }

    /// <summary>"SpecificDays" | "WeeklyFrequency" → ChoreScheduleType. Exact names only.</summary>
    public static bool TryParseScheduleType(string? value, out ChoreScheduleType scheduleType)
    {
        switch (value)
        {
            case "SpecificDays":
                scheduleType = ChoreScheduleType.SpecificDays;
                return true;
            case "WeeklyFrequency":
                scheduleType = ChoreScheduleType.WeeklyFrequency;
                return true;
            default:
                scheduleType = ChoreScheduleType.SpecificDays;
                return false;
        }
    }
}
