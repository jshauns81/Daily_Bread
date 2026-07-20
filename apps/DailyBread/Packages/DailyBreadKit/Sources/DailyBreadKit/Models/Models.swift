import Foundation

// Mirrors of the /api/v1 DTOs. Wire conventions: camelCase JSON,
// DateOnly as "yyyy-MM-dd" (DayDate), money as decimal strings (Money).

// MARK: - Auth

public struct ApiUser: Codable, Hashable, Sendable {
    public var userId: String
    public var userName: String
    public var roles: [String]
    public var householdId: String?

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
