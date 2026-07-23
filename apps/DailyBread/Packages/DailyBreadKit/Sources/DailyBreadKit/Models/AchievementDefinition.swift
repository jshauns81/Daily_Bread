import Foundation

/// An achievement definition for the parent authoring screen. Enums arrive as
/// strings; the unlock condition is flattened into typed params (only the ones
/// the type uses matter). Money fields are 0 when not applicable — read them
/// through `unlock`/`reward` which know which apply.
public struct AchievementDefinition: Codable, Hashable, Identifiable, Sendable {
    public var id: Int
    public var code: String
    public var name: String
    public var description: String
    public var hiddenHint: String?
    public var icon: String
    public var lockedIcon: String?
    public var category: String
    public var rarity: String
    public var points: Int
    public var sortOrder: Int
    public var isHidden: Bool
    public var isLegendary: Bool
    public var isVisibleBeforeUnlock: Bool
    public var isActive: Bool
    public var unlockConditionType: String
    public var count: Int?
    public var days: Int?
    public var weeks: Int?
    public var conditionAmount: Money
    public var choreId: Int?
    public var beforeHour: Int?
    public var dayType: String?
    public var progressTarget: Int?
    public var rewardType: String?
    public var rewardCashAmount: Money
    public var rewardItemLabel: String?
    public var rewardItemEstValue: Money

    public var unlock: UnlockCondition { UnlockCondition(rawValue: unlockConditionType) ?? .manual }
    public var rarityKind: AchievementRarityKind { AchievementRarityKind(rawValue: rarity) ?? .common }
    public var categoryKind: AchievementCategoryKind { AchievementCategoryKind(rawValue: category) ?? .special }
    public var hasCashReward: Bool { rewardType == "Cash" }
    public var hasItemReward: Bool { rewardType == "Item" }
}

/// The create/update body. Code is server-generated, so it's not here.
public struct AchievementDefinitionWrite: Codable, Sendable {
    public var name: String
    public var description: String
    public var hiddenHint: String?
    public var icon: String
    public var lockedIcon: String?
    public var category: String
    public var rarity: String
    public var points: Int
    public var sortOrder: Int
    public var isHidden: Bool
    public var isLegendary: Bool
    public var isVisibleBeforeUnlock: Bool
    public var isActive: Bool
    public var unlockConditionType: String
    public var count: Int?
    public var days: Int?
    public var weeks: Int?
    public var conditionAmount: Money
    public var choreId: Int?
    public var beforeHour: Int?
    public var dayType: String?
    public var progressTarget: Int?
    public var rewardType: String?
    public var rewardCashAmount: Money
    public var rewardItemLabel: String?
    public var rewardItemEstValue: Money

    public init(name: String, description: String, hiddenHint: String? = nil,
                icon: String, lockedIcon: String? = nil,
                category: String, rarity: String, points: Int, sortOrder: Int,
                isHidden: Bool, isLegendary: Bool, isVisibleBeforeUnlock: Bool, isActive: Bool,
                unlockConditionType: String,
                count: Int? = nil, days: Int? = nil, weeks: Int? = nil,
                conditionAmount: Money = .zero, choreId: Int? = nil,
                beforeHour: Int? = nil, dayType: String? = nil, progressTarget: Int? = nil,
                rewardType: String? = nil, rewardCashAmount: Money = .zero,
                rewardItemLabel: String? = nil, rewardItemEstValue: Money = .zero) {
        self.name = name; self.description = description; self.hiddenHint = hiddenHint
        self.icon = icon; self.lockedIcon = lockedIcon
        self.category = category; self.rarity = rarity; self.points = points; self.sortOrder = sortOrder
        self.isHidden = isHidden; self.isLegendary = isLegendary
        self.isVisibleBeforeUnlock = isVisibleBeforeUnlock; self.isActive = isActive
        self.unlockConditionType = unlockConditionType
        self.count = count; self.days = days; self.weeks = weeks
        self.conditionAmount = conditionAmount; self.choreId = choreId
        self.beforeHour = beforeHour; self.dayType = dayType; self.progressTarget = progressTarget
        self.rewardType = rewardType; self.rewardCashAmount = rewardCashAmount
        self.rewardItemLabel = rewardItemLabel; self.rewardItemEstValue = rewardItemEstValue
    }
}

// MARK: - Authoring enums

public enum AchievementRarityKind: String, CaseIterable, Identifiable, Sendable {
    case common = "Common", uncommon = "Uncommon", rare = "Rare", epic = "Epic", legendary = "Legendary"
    public var id: String { rawValue }
    public var label: String { rawValue }
}

public enum AchievementCategoryKind: String, CaseIterable, Identifiable, Sendable {
    case gettingStarted = "GettingStarted", streaks = "Streaks", earnings = "Earnings"
    case consistency = "Consistency", special = "Special", secret = "Secret"
    public var id: String { rawValue }
    public var label: String {
        switch self {
        case .gettingStarted: return "Getting started"
        case .streaks: return "Streaks"
        case .earnings: return "Earnings"
        case .consistency: return "Consistency"
        case .special: return "Special"
        case .secret: return "Secret"
        }
    }
}

/// A single input the condition needs.
public enum ConditionParam: Sendable {
    case count, days, weeks, amount, choreId, beforeHour, dayType
}

public enum UnlockCondition: String, CaseIterable, Identifiable, Sendable {
    case manual = "Manual"
    case choresCompleted = "ChoresCompleted"
    case streakDays = "StreakDays"
    case totalEarned = "TotalEarned"
    case balanceReached = "BalanceReached"
    case perfectDays = "PerfectDays"
    case specificChoreCount = "SpecificChoreCount"
    case earlyCompletion = "EarlyCompletion"
    case firstChore = "FirstChore"
    case firstGoal = "FirstGoal"
    case goalCompleted = "GoalCompleted"
    case firstDollar = "FirstDollar"
    case weeklyEarnings = "WeeklyEarnings"
    case dayTypeCompletion = "DayTypeCompletion"
    case weekStreak = "WeekStreak"
    case accountAge = "AccountAge"
    case bonusChoresCompleted = "BonusChoresCompleted"
    case choreRecovery = "ChoreRecovery"

    public var id: String { rawValue }

    public var label: String {
        switch self {
        case .manual: return "Awarded by hand"
        case .choresCompleted: return "Total chores done"
        case .streakDays: return "Day streak"
        case .totalEarned: return "Total earned"
        case .balanceReached: return "Balance reaches"
        case .perfectDays: return "Perfect days"
        case .specificChoreCount: return "A specific chore, N times"
        case .earlyCompletion: return "Done before a time"
        case .firstChore: return "First chore ever"
        case .firstGoal: return "First goal created"
        case .goalCompleted: return "Goals completed"
        case .firstDollar: return "First dollar earned"
        case .weeklyEarnings: return "Earned in a week"
        case .dayTypeCompletion: return "Weekend/weekday days done"
        case .weekStreak: return "Week streak"
        case .accountAge: return "Account age (days)"
        case .bonusChoresCompleted: return "Bonus chores done"
        case .choreRecovery: return "Recovered a missed chore"
        }
    }

    /// The inputs this condition needs shown in the editor.
    public var params: [ConditionParam] {
        switch self {
        case .manual, .firstChore, .firstGoal, .firstDollar:
            return []
        case .choresCompleted, .perfectDays, .goalCompleted, .bonusChoresCompleted, .choreRecovery:
            return [.count]
        case .streakDays, .accountAge:
            return [.days]
        case .weekStreak:
            return [.weeks]
        case .totalEarned, .balanceReached, .weeklyEarnings:
            return [.amount]
        case .specificChoreCount:
            return [.choreId, .count]
        case .earlyCompletion:
            return [.beforeHour, .count]
        case .dayTypeCompletion:
            return [.dayType, .count]
        }
    }

    public var isManual: Bool { self == .manual }
}
