import Foundation

// Mirrors of the /api/v1 DTOs. Wire conventions: camelCase JSON,
// DateOnly as "yyyy-MM-dd" (DayDate), money as decimal strings (Money).

// MARK: - Auth

public struct ApiUser: Codable, Hashable, Sendable {
    public var userId: String
    public var userName: String
    public var roles: [String]
    public var householdId: String?
    /// "younger" | "teen" — server-computed from the child's birthdate. Optional
    /// for forward/backward compatibility; absent reads as younger.
    public var ageTier: String?

    public var isParent: Bool { roles.contains("Parent") || roles.contains("Admin") }
    public var isChild: Bool { roles.contains("Child") }
}

public struct TokenResponse: Codable, Sendable {
    public var accessToken: String
    public var accessTokenExpiresAtUtc: LenientDate
    public var refreshToken: String
    public var refreshTokenExpiresAtUtc: LenientDate
    public var user: ApiUser
}

public struct ApiErrorPayload: Codable, Sendable {
    public var code: String
    public var message: String
}

// MARK: - Chores

public struct ChoreItem: Codable, Hashable, Identifiable, Sendable {
    public var choreDefinitionId: Int
    public var choreLogId: Int?
    public var name: String
    public var description: String?
    public var icon: String?
    public var earnValue: Money
    public var penaltyValue: Money
    public var status: String
    public var scheduleType: String
    public var weeklyTargetCount: Int
    public var weeklyCompletedCount: Int
    public var isRepeatable: Bool
    public var helpReason: String?
    public var helpRequestedAtUtc: LenientDate?
    public var approvedByUserName: String?
    public var approvedAtUtc: LenientDate?

    public var id: Int { choreDefinitionId }

    public var isDone: Bool { status == "Completed" || status == "Approved" }
    public var isApproved: Bool { status == "Approved" }
    public var isHelp: Bool { status == "Help" }
    public var isPending: Bool { status == "Pending" }
    public var isEarning: Bool { !earnValue.isZero }
}

public struct TodayChores: Codable, Sendable {
    public var date: DayDate
    public var userId: String
    public var userName: String?
    public var items: [ChoreItem]
}

public struct ChoreToggleResult: Codable, Sendable {
    public var status: String
}

public struct WeekChoreProgress: Codable, Hashable, Identifiable, Sendable {
    public var choreDefinitionId: Int
    public var name: String
    public var completedCount: Int
    public var targetCount: Int

    public var id: Int { choreDefinitionId }
}

public struct WeekProgress: Codable, Sendable {
    public var weekStart: DayDate
    public var weekEnd: DayDate
    public var userId: String
    public var chores: [WeekChoreProgress]
}

// MARK: - Planner (parents)

/// One chore definition as the PLANNER sees it — the full recipe, not a
/// day's instance. Returned in SortOrder (the kid's list order — never
/// re-sort).
public struct PlannerChore: Codable, Hashable, Identifiable, Sendable {
    public var id: Int
    public var name: String
    public var description: String?
    public var icon: String?
    public var assignedUserId: String?
    public var assignedUserName: String?
    public var kind: String                 // "Task" | "Routine"
    public var earnValue: Money
    public var importance: Int
    public var allOrNothing: Bool
    public var isInverseFill: Bool
    public var inverseFillBaselineMinutes: Int
    public var scheduleType: String         // "SpecificDays" | "WeeklyFrequency"
    public var activeDays: [String]
    public var weeklyTargetCount: Int
    public var isRepeatable: Bool
    public var startDate: DayDate?
    public var endDate: DayDate?
    public var isActive: Bool
    public var autoApprove: Bool
    public var sortOrder: Int
}

public extension PlannerChore {
    /// Tasks earn money; Routines are just expected.
    var isTask: Bool { kind == "Task" }

    /// "Mon · Wed · Fri" / "Every day" for fixed days, "3× a week" for a
    /// weekly goal. Day chips are displayed Sunday-first, matching the
    /// editor's S M T W T F S row (activeDays is a set — this is display
    /// formatting, not re-sorting a server list).
    var scheduleSummary: String {
        if scheduleType == "WeeklyFrequency" {
            return "\(weeklyTargetCount)× a week"
        }
        let week: [(full: String, short: String)] = [
            ("Sunday", "Sun"), ("Monday", "Mon"), ("Tuesday", "Tue"),
            ("Wednesday", "Wed"), ("Thursday", "Thu"), ("Friday", "Fri"),
            ("Saturday", "Sat")]
        let picked = week.filter { activeDays.contains($0.full) }
        if picked.count == 7 { return "Every day" }
        if picked.isEmpty { return "No days set" }
        return picked.map(\.short).joined(separator: " · ")
    }
}

public struct PlannerChoreList: Codable, Sendable {
    public var chores: [PlannerChore]
}

/// POST/PUT body for creating or editing a chore. The client always sends
/// sortOrder (existing value on edit; max + 1 on create).
public struct ChoreWrite: Codable, Sendable {
    public var name: String
    public var description: String?
    public var icon: String?
    public var assignedUserId: String?
    public var kind: String
    public var earnValue: Money
    public var importance: Int
    public var allOrNothing: Bool
    public var isInverseFill: Bool
    public var inverseFillBaselineMinutes: Int
    public var scheduleType: String
    public var activeDays: [String]
    public var weeklyTargetCount: Int
    public var isRepeatable: Bool
    public var startDate: DayDate?
    public var endDate: DayDate?
    public var isActive: Bool
    public var autoApprove: Bool
    public var sortOrder: Int

    public init(name: String,
                description: String? = nil,
                icon: String? = nil,
                assignedUserId: String? = nil,
                kind: String,
                earnValue: Money,
                importance: Int,
                allOrNothing: Bool,
                isInverseFill: Bool,
                inverseFillBaselineMinutes: Int,
                scheduleType: String,
                activeDays: [String],
                weeklyTargetCount: Int,
                isRepeatable: Bool,
                startDate: DayDate? = nil,
                endDate: DayDate? = nil,
                isActive: Bool,
                autoApprove: Bool,
                sortOrder: Int) {
        self.name = name
        self.description = description
        self.icon = icon
        self.assignedUserId = assignedUserId
        self.kind = kind
        self.earnValue = earnValue
        self.importance = importance
        self.allOrNothing = allOrNothing
        self.isInverseFill = isInverseFill
        self.inverseFillBaselineMinutes = inverseFillBaselineMinutes
        self.scheduleType = scheduleType
        self.activeDays = activeDays
        self.weeklyTargetCount = weeklyTargetCount
        self.isRepeatable = isRepeatable
        self.startDate = startDate
        self.endDate = endDate
        self.isActive = isActive
        self.autoApprove = autoApprove
        self.sortOrder = sortOrder
    }
}

/// One entry of the reorder PUT: { "choreDefinitionId": 12, "sortOrder": 0 }.
public struct ChoreOrderItem: Codable, Sendable {
    public var choreDefinitionId: Int
    public var sortOrder: Int

    public init(choreDefinitionId: Int, sortOrder: Int) {
        self.choreDefinitionId = choreDefinitionId
        self.sortOrder = sortOrder
    }
}

public struct AssignableChildren: Codable, Sendable {
    public var children: [AssignableChild]
}

public struct AssignableChild: Codable, Hashable, Identifiable, Sendable {
    public var userId: String
    public var userName: String

    public var id: String { userId }
}

// MARK: - Ledger

public struct Balance: Codable, Sendable {
    public var userId: String
    public var balance: Money
}

public struct LedgerTransaction: Codable, Hashable, Identifiable, Sendable {
    public var id: Int
    public var amount: Money
    public var type: String
    public var description: String?
    public var date: DayDate
    public var choreDefinitionId: Int?
}

public struct LedgerHistory: Codable, Sendable {
    public var userId: String
    public var transactions: [LedgerTransaction]
}

// MARK: - Goals

public struct Goal: Codable, Hashable, Identifiable, Sendable {
    public var id: Int
    public var name: String
    public var description: String?
    public var targetAmount: Money
    public var currentBalance: Money
    public var progressPercent: Int
    public var priority: Int
    public var isPrimary: Bool
    public var isCompleted: Bool
    public var imageUrl: String?
}

public struct GoalWrite: Codable, Sendable {
    public var name: String
    public var description: String?
    public var targetAmount: Money
    public var imageUrl: String?
    public var priority: Int
    public var isPrimary: Bool
    public var userId: String?

    public init(name: String, description: String? = nil, targetAmount: Money,
                imageUrl: String? = nil, priority: Int = 0, isPrimary: Bool = false,
                userId: String? = nil) {
        self.name = name
        self.description = description
        self.targetAmount = targetAmount
        self.imageUrl = imageUrl
        self.priority = priority
        self.isPrimary = isPrimary
        self.userId = userId
    }
}

// MARK: - Calendar (heatmap)

public struct DaySummary: Codable, Hashable, Identifiable, Sendable {
    public var date: DayDate
    public var status: String
    public var totalChores: Int
    public var completedChores: Int
    public var approvedChores: Int
    public var missedChores: Int
    public var pendingChores: Int
    public var earnedAmount: Money

    public var id: String { date.wireString }
}

public struct CalendarRange: Codable, Sendable {
    public var userId: String
    public var from: DayDate
    public var to: DayDate
    public var days: [DaySummary]
}

// MARK: - Reward claims

/// A real-world reward from a TangibleReward achievement — Cash (credited on
/// approval) or Item (fulfilled by a parent). Type and status ride the wire as
/// strings so an unexpected value can never fail the whole decode; the computed
/// helpers read them.
public struct RewardClaim: Codable, Hashable, Identifiable, Sendable {
    public var id: Int
    public var userId: String
    public var childName: String
    public var achievementName: String
    public var achievementIcon: String
    public var rewardType: String
    public var cashAmount: Money
    public var itemLabel: String?
    public var status: String
    public var createdAt: LenientDate?
    public var decidedAt: LenientDate?
    public var rejectionReason: String?

    public var isCash: Bool { rewardType == "Cash" }
    public var isPending: Bool { status == "PendingApproval" }
    public var isApproved: Bool { status == "Approved" || status == "FulfilledByParent" }
    public var isRejected: Bool { status == "Rejected" }
}

// MARK: - Approvals / dashboard

public struct ApprovalItem: Codable, Hashable, Identifiable, Sendable {
    public var choreLogId: Int
    public var choreDefinitionId: Int
    public var choreName: String
    public var childName: String
    public var childUserId: String?
    public var earnValue: Money

    public var id: Int { choreLogId }
}

public struct HelpRequest: Codable, Hashable, Identifiable, Sendable {
    public var choreLogId: Int
    public var choreDefinitionId: Int
    public var choreName: String
    public var childName: String
    public var childUserId: String?
    /// Optional so the app still decodes against older servers.
    public var earnValue: Money?
    public var reason: String?
    public var date: DayDate
    public var requestedAtUtc: LenientDate?

    public var id: Int { choreLogId }
}

public struct ApprovalsQueue: Codable, Sendable {
    public var pendingApprovals: [ApprovalItem]
    public var helpRequests: [HelpRequest]

    public var isEmpty: Bool { pendingApprovals.isEmpty && helpRequests.isEmpty }
}

public struct ChildProgress: Codable, Hashable, Identifiable, Sendable {
    public var userId: String?
    public var displayName: String
    public var totalChores: Int
    public var completedChores: Int
    public var approvedChores: Int
    public var pendingChores: Int
    public var helpRequests: Int

    public var id: String { userId ?? displayName }
}

public struct ChildBalance: Codable, Hashable, Identifiable, Sendable {
    public var displayName: String
    public var balance: Money
    /// Optional so the app still decodes against older servers.
    public var canCashOut: Bool?

    public var id: String { displayName }
    public var isCashOutReady: Bool { canCashOut ?? false }
}

public struct DailyEarning: Codable, Hashable, Identifiable, Sendable {
    public var date: DayDate
    public var amount: Money

    public var id: String { date.wireString }
}

public struct ParentDashboard: Codable, Sendable {
    public var todayCompletedCount: Int
    public var todayPendingCount: Int
    public var todayApprovedCount: Int
    public var todayHelpCount: Int
    public var todayTotalChores: Int
    public var thisWeekEarnings: Money
    public var weeklyPotential: Money
    public var weekEarnings: [DailyEarning]
    public var childrenProgress: [ChildProgress]
    public var childrenBalances: [ChildBalance]
    public var pendingApprovals: [ApprovalItem]
    public var helpRequests: [HelpRequest]
}

// MARK: - Family features / achievements

public struct FamilyFeatures: Codable, Hashable, Sendable {
    public var enableGoals: Bool
    public var enableConfetti: Bool
    public var enableStreaks: Bool

    public init(enableGoals: Bool = false, enableConfetti: Bool = true, enableStreaks: Bool = true) {
        self.enableGoals = enableGoals
        self.enableConfetti = enableConfetti
        self.enableStreaks = enableStreaks
    }
}

public struct Achievement: Codable, Hashable, Identifiable, Sendable {
    public var id: Int
    public var name: String
    public var description: String
    public var icon: String
    public var category: String
    public var rarity: String
    public var points: Int
    public var isEarned: Bool
    public var earnedAtUtc: LenientDate?
    public var isNew: Bool
    public var showProgress: Bool
    public var currentProgress: Int
    public var targetProgress: Int
    public var progressPercent: Int
}

public struct AchievementsList: Codable, Sendable {
    public var userId: String
    public var totalPoints: Int
    public var earnedCount: Int
    public var totalCount: Int
    public var achievements: [Achievement]
}

// MARK: - Screen time

/// One screen-time pool (weekday or weekend): the base allowance, what's left
/// after applied losses, the guaranteed floor, and the most that can be lost
/// this week. All minutes — format with `ScreenTimeFormat.minutes`.
public struct ScreenTimePool: Codable, Hashable, Sendable {
    public var pool: String
    public var baseMinutes: Int
    public var effectiveMinutes: Int
    public var floorMinutes: Int
    public var atRiskMinutes: Int
}

/// The live minute price of one chore this week ("miss once: −N min").
public struct ScreenTimeChorePrice: Codable, Hashable, Identifiable, Sendable {
    public var choreDefinitionId: Int
    public var name: String
    public var pool: String
    public var scheduledInstances: Int
    public var perInstanceMinutes: Int

    public var id: Int { choreDefinitionId }
}

/// A labeled line from the screen-time ledger — every budget change on the
/// record: Deduction, EarnBack, Adjustment, or TimeMachine.
public struct ScreenTimeEntry: Codable, Hashable, Identifiable, Sendable {
    public var id: Int
    public var weekStart: DayDate
    public var pool: String
    public var kind: String
    public var choreName: String?
    public var minutes: Int
    public var note: String?
    public var createdAtUtc: LenientDate?
}

public struct ScreenTimeSummary: Codable, Sendable {
    public var userId: String
    public var weekStart: DayDate
    public var weekEnd: DayDate
    public var weekdayPool: ScreenTimePool
    public var weekendPool: ScreenTimePool
    public var chorePrices: [ScreenTimeChorePrice]
    public var recentEntries: [ScreenTimeEntry]
    /// Current tunables, echoed back so the settings panel prefills from real values.
    public var weeklyRoutinePayout: Money
    public var minutesPerImportancePoint: Int
    /// The child's birthdate, if set (drives age-appropriate voice).
    public var birthDate: DayDate?
}

public enum ScreenTimeFormat {
    /// "6h 40m" / "6h" / "40m" / "0m" — same rules as the web meter.
    public static func minutes(_ total: Int) -> String {
        guard total > 0 else { return "0m" }
        let hours = total / 60
        let mins = total % 60
        if hours > 0 && mins > 0 { return "\(hours)h \(mins)m" }
        return hours > 0 ? "\(hours)h" : "\(mins)m"
    }
}

/// One chore with stakes on it today (MECHANICS §E). The server owns all the
/// urgency logic; the client only renders. Ordering is server-authored
/// (DueTonight → MustDoDaily → GettingTight) — never re-sort.
public struct AtRiskItem: Codable, Hashable, Identifiable, Sendable {
    public var choreDefinitionId: Int
    public var name: String
    public var urgency: String        // "DueTonight" | "MustDoDaily" | "GettingTight"
    public var detail: String
    public var moneyAtRisk: Money
    public var minutesAtRisk: Int

    public var id: Int { choreDefinitionId }
}

/// The kid's "At Risk Today" answer: what's on the line right now, with a
/// single optional preview line when nothing is (calm by design — never nag).
public struct AtRiskToday: Codable, Sendable {
    public var userId: String
    public var date: DayDate
    public var items: [AtRiskItem]
    public var totalMoneyAtRisk: Money
    public var totalMinutesAtRisk: Int
    public var previewLine: String?
}

/// Parent-only PUT body for tuning one child's screen-time settings.
/// `userId` is always explicit — parents tune a specific kid.
public struct ScreenTimeSettingsUpdate: Codable, Sendable {
    public var userId: String
    public var weekdayHours: Double
    public var weekendHours: Double
    public var weeklyRoutinePayout: Money
    public var weekdayAtRiskPercent: Int
    public var weekendAtRiskPercent: Int
    /// How many screen-time minutes one importance point is worth (the
    /// "Consequences" dial). Sent every time; the server keeps its value when
    /// omitted, so we always send it explicitly from the panel.
    public var minutesPerImportancePoint: Int
    /// The child's birthdate. Nil leaves it unchanged on the server.
    public var birthDate: DayDate?

    public init(userId: String,
                weekdayHours: Double,
                weekendHours: Double,
                weeklyRoutinePayout: Money,
                weekdayAtRiskPercent: Int,
                weekendAtRiskPercent: Int,
                minutesPerImportancePoint: Int,
                birthDate: DayDate? = nil) {
        self.userId = userId
        self.weekdayHours = weekdayHours
        self.weekendHours = weekendHours
        self.weeklyRoutinePayout = weeklyRoutinePayout
        self.weekdayAtRiskPercent = weekdayAtRiskPercent
        self.weekendAtRiskPercent = weekendAtRiskPercent
        self.minutesPerImportancePoint = minutesPerImportancePoint
        self.birthDate = birthDate
    }
}

/// Help response options — mirrors the API's accepted values.
public enum HelpResponseKind: String, CaseIterable, Sendable {
    case completedByParent = "CompletedByParent"
    case excused = "Excused"
    case denied = "Denied"

    public var label: String {
        switch self {
        case .completedByParent: return "I helped — give credit"
        case .excused: return "Forgive — no penalty"
        case .denied: return "Not this time"
        }
    }
}
