import SwiftUI
import DailyBreadKit

/// Victor's Home — the game, not the spreadsheet. Today's progress reframed as
/// a level that lights up in its rarity colour, an XP bar, quests remaining,
/// streak fire, gold, the latest badge, and the next quest to tackle. The
/// graphite backdrop stays calm so all this colour reads as the reward it is.
/// Level rules mirror the web dashboard exactly (% of today's chores done).
@MainActor
@Observable
final class KidHomeStore {
    var today: TodayChores?
    var balance: Money?
    var achievements: AchievementsList?
    var goal: Goal?
    var streak = 0
    var loading = false
    var celebrate = false
    var helpTarget: ChoreItem?

    var doneCount: Int { today?.items.filter(\.isDone).count ?? 0 }
    var totalCount: Int { today?.items.count ?? 0 }
    var pendingCount: Int { today?.items.filter { !$0.isDone && !$0.isHelp }.count ?? 0 }
    var isRestDay: Bool { totalCount == 0 }

    var progressPercent: Int {
        totalCount == 0 ? 0 : Int((Double(doneCount) / Double(totalCount) * 100).rounded())
    }

    var earnedToday: Money {
        let sum = (today?.items ?? [])
            .filter(\.isDone)
            .reduce(Decimal.zero) { $0 + $1.earnValue.amount }
        return Money(sum)
    }

    /// The next quest to tackle: first still-pending chore, or a Help-raised one
    /// waiting on a parent.
    var nextUp: ChoreItem? {
        today?.items.first { !$0.isDone && !$0.isHelp }
            ?? today?.items.first { $0.isHelp }
    }

    var latestBadge: Achievement? {
        achievements?.achievements
            .filter { $0.isEarned }
            .sorted { ($0.earnedAtUtc?.date ?? .distantPast) > ($1.earnedAtUtc?.date ?? .distantPast) }
            .first
    }

    func load(_ session: SessionStore) async {
        loading = today == nil
        defer { loading = false }

        async let todayTask = session.client.todayChores()
        async let balanceTask = session.client.balance()
        async let achievementsTask = session.client.achievements()

        today = try? await todayTask
        balance = (try? await balanceTask)?.balance
        achievements = try? await achievementsTask

        if session.features.enableGoals {
            let goals = (try? await session.client.goals()) ?? []
            goal = goals.first { $0.isPrimary } ?? goals.first
        }

        await loadStreak(session)
    }

    /// Consecutive all-complete days ending today (today unfinished doesn't break
    /// it). Same rule as the Today screen.
    private func loadStreak(_ session: SessionStore) async {
        let today = DayDate.todayLocal()
        let start = DayDate(year: today.month > 2 ? today.year : today.year - 1,
                            month: max(1, today.month - 2), day: 1)
        guard let range = try? await session.client.calendarRange(from: start, to: today) else { return }

        var count = 0
        for day in range.days.reversed() {
            switch day.status {
            case "AllComplete": count += 1
            case "NoChores", "Future": continue
            default:
                if day.date == today { continue }
                streak = count
                return
            }
        }
        streak = count
    }

    func markDone(_ item: ChoreItem, _ session: SessionStore) async {
        Haptics.tick()
        _ = try? await session.client.toggleChore(
            choreDefinitionId: item.choreDefinitionId, date: today?.date)
        await load(session)
        if progressPercent == 100 {
            celebrate = true
            Haptics.success()
            Task {
                try? await Task.sleep(for: .seconds(2.8))
                celebrate = false
            }
        }
    }

    func raiseHelp(_ item: ChoreItem, reason: String, _ session: SessionStore) async {
        try? await session.client.raiseHelp(
            choreDefinitionId: item.choreDefinitionId, date: today?.date, reason: reason)
        await load(session)
    }
}

struct KidHomeView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = KidHomeStore()

    private struct LevelTier {
        let level: Int
        let title: String
        let icon: String
        let color: Color
    }

    private func tier(for percent: Int) -> LevelTier {
        switch percent {
        case 100: return LevelTier(level: 5, title: "LEGENDARY", icon: "👑", color: Color(hex: 0xE0A93F))
        case 75...: return LevelTier(level: 4, title: "EPIC", icon: "💎", color: Color(hex: 0x9B7BE0))
        case 50...: return LevelTier(level: 3, title: "RARE", icon: "🔥", color: Color(hex: 0xE8894A))
        case 25...: return LevelTier(level: 2, title: "UNCOMMON", icon: "⚡", color: Color(hex: 0x4DA8C6))
        case 1...: return LevelTier(level: 1, title: "COMMON", icon: "⭐", color: Color(hex: 0x6BA368))
        default: return LevelTier(level: 0, title: "STARTER", icon: "🌱", color: Color(hex: 0x8A8F98))
        }
    }

    private var streakColor: Color { Color(hex: 0xE8894A) }

    var body: some View {
        let tier = tier(for: store.progressPercent)
        ScrollView {
            VStack(spacing: 14) {
                greeting
                heroCard(tier)
                goalCard
                achievementRow
                nextUpCard
            }
            .padding()
        }
        .navigationTitle("Home")
        .graphiteBackground()
        .refreshable { await store.load(session) }
        .refreshOnForeground { await store.load(session) }
        .task { await store.load(session) }
        .overlay {
            if store.celebrate && session.features.enableConfetti {
                ConfettiView(start: Date())
                    .allowsHitTesting(false)
            }
        }
        .sheet(item: $store.helpTarget) { item in
            HelpSheet(item: item) { reason in
                Task { await store.raiseHelp(item, reason: reason, session) }
            }
        }
    }

    // MARK: - Greeting

    private var greeting: some View {
        HStack {
            (Text("\(Greeting.current), ")
                + Text((session.currentUser?.userName ?? "there").capitalized)
                    .foregroundColor(Color.accentColor))
                .font(.title2.weight(.bold))
            Spacer()
        }
    }

    // MARK: - Hero

    private func heroCard(_ tier: LevelTier) -> some View {
        VStack(spacing: 18) {
            levelBadge(tier)
            xpSection(tier)
            Rectangle()
                .fill(Color.primary.opacity(0.08))
                .frame(height: 1)
            statsRow
            earnedTodayLine
        }
        .padding(20)
        .frame(maxWidth: .infinity)
        .background {
            RoundedRectangle(cornerRadius: 24, style: .continuous)
                .fill(.regularMaterial)
                .overlay {
                    RoundedRectangle(cornerRadius: 24, style: .continuous)
                        .fill(LinearGradient(
                            colors: [tier.color.opacity(0.20), tier.color.opacity(0.02)],
                            startPoint: .top, endPoint: .bottom))
                }
                .overlay {
                    RoundedRectangle(cornerRadius: 24, style: .continuous)
                        .strokeBorder(tier.color.opacity(0.28), lineWidth: 1)
                }
        }
    }

    private func levelBadge(_ tier: LevelTier) -> some View {
        VStack(spacing: 6) {
            HStack(spacing: 10) {
                Text(tier.icon).font(.system(size: 30))
                HStack(spacing: 4) {
                    Text("LVL").font(.headline.weight(.bold)).foregroundStyle(.white.opacity(0.9))
                    Text("\(tier.level)").font(.title.weight(.heavy)).foregroundStyle(.white)
                }
            }
            .padding(.horizontal, 24)
            .padding(.vertical, 12)
            .background(
                Capsule().fill(LinearGradient(
                    colors: [tier.color, tier.color.opacity(0.65)],
                    startPoint: .topLeading, endPoint: .bottomTrailing)))
            .shadow(color: tier.color.opacity(0.5), radius: 16, y: 5)

            Text(tier.title)
                .font(.caption.weight(.bold))
                .foregroundStyle(tier.color)
                .kerning(1.6)
        }
        .animation(.spring(duration: 0.4), value: tier.level)
    }

    private func xpSection(_ tier: LevelTier) -> some View {
        VStack(spacing: 10) {
            HStack {
                Text("TODAY'S PROGRESS")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.secondary)
                    .kerning(0.6)
                Spacer()
                Text("\(store.doneCount) / \(store.totalCount) XP")
                    .font(.subheadline.weight(.bold))
                    .foregroundStyle(Color.accentColor)
                    .monospacedDigit()
            }

            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.primary.opacity(0.10))
                    Capsule()
                        .fill(LinearGradient(
                            colors: [Color.accentColor, tier.color],
                            startPoint: .leading, endPoint: .trailing))
                        .frame(width: max(12, geo.size.width * CGFloat(store.progressPercent) / 100))
                        .animation(.spring(duration: 0.5), value: store.progressPercent)
                }
            }
            .frame(height: 10)

            milestones

            Text(store.isRestDay
                 ? "😎 Rest day — no quests"
                 : "\(store.pendingCount) quest\(store.pendingCount == 1 ? "" : "s") remaining")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    private var milestones: some View {
        HStack {
            ForEach(Array([("⚡", 25), ("🔥", 50), ("💎", 75), ("👑", 100)].enumerated()), id: \.offset) { index, m in
                let reached = store.progressPercent >= m.1
                Text(m.0)
                    .font(.system(size: 15))
                    .opacity(reached ? 1 : 0.28)
                    .grayscale(reached ? 0 : 1)
                    .scaleEffect(reached ? 1 : 0.85)
                    .animation(.spring(duration: 0.4), value: reached)
                if index < 3 { Spacer() }
            }
        }
    }

    private var statsRow: some View {
        HStack(spacing: 18) {
            HStack(spacing: 5) {
                Text("💰")
                Text(store.balance?.display ?? "$0.00")
                    .fontWeight(.bold)
                    .foregroundStyle(DB.gold(scheme))
            }
            if session.features.enableStreaks && store.streak > 0 {
                HStack(spacing: 5) {
                    Text("🔥")
                    Text("\(store.streak) day streak")
                        .fontWeight(.semibold)
                        .foregroundStyle(streakColor)
                }
            }
            Spacer()
        }
        .font(.subheadline)
    }

    @ViewBuilder
    private var earnedTodayLine: some View {
        if !store.earnedToday.isZero {
            HStack(spacing: 4) {
                Text("✨")
                Text("+\(store.earnedToday.display)")
                    .fontWeight(.heavy)
                    .foregroundStyle(DB.gold(scheme))
                Text("today")
                    .foregroundStyle(.secondary)
                Spacer()
            }
            .font(.subheadline)
        }
    }

    // MARK: - Goals

    @ViewBuilder
    private var goalCard: some View {
        if session.features.enableGoals {
            if let goal = store.goal {
                VStack(alignment: .leading, spacing: 8) {
                    Label("GOAL", systemImage: "target")
                        .font(.caption.weight(.bold))
                        .foregroundStyle(.secondary)
                    Text(goal.name).font(.subheadline.weight(.semibold))
                    ProgressView(value: min(1, Double(goal.progressPercent) / 100))
                        .tint(Color.accentColor)
                    Text("\(goal.currentBalance.display) of \(goal.targetAmount.display)")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .glassCard()
            } else {
                VStack(alignment: .leading, spacing: 4) {
                    Label("GOALS", systemImage: "target")
                        .font(.caption.weight(.bold))
                        .foregroundStyle(.secondary)
                    Text("Set a savings goal!").font(.subheadline.weight(.semibold))
                    Text("Something to work toward →")
                        .font(.caption)
                        .foregroundStyle(Color.accentColor)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .glassCard()
            }
        }
    }

    // MARK: - Achievements

    private var achievementRow: some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 6) {
                Label("LATEST", systemImage: "trophy.fill")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.secondary)
                if let badge = store.latestBadge {
                    HStack(spacing: 8) {
                        Text(badge.icon)
                        Text(badge.name)
                            .font(.subheadline.weight(.semibold))
                            .lineLimit(1)
                    }
                    Text("+\(badge.points) pts")
                        .font(.caption.weight(.bold))
                        .foregroundStyle(DB.gold(scheme))
                } else {
                    Text("No badges yet — go earn one!")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .glassCard()

            VStack(spacing: 4) {
                HStack(alignment: .firstTextBaseline, spacing: 2) {
                    Text("\(store.achievements?.earnedCount ?? 0)")
                        .font(.title.weight(.heavy))
                        .foregroundStyle(Color.accentColor)
                    Text("/ \(store.achievements?.totalCount ?? 0)")
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(.secondary)
                }
                Text("BADGES")
                    .font(.caption2.weight(.bold))
                    .foregroundStyle(.secondary)
                    .kerning(1)
            }
            .frame(minWidth: 96)
            .glassCard()
        }
    }

    // MARK: - Next up

    @ViewBuilder
    private var nextUpCard: some View {
        if let item = store.nextUp {
            VStack(alignment: .leading, spacing: 12) {
                Text("NEXT UP")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(Color.accentColor)
                    .kerning(1)

                HStack(spacing: 12) {
                    Text(item.icon ?? "🧺")
                        .font(.title2)
                        .frame(width: 46, height: 46)
                        .background(.quaternary, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                    VStack(alignment: .leading, spacing: 2) {
                        Text(item.name).font(.body.weight(.semibold))
                        if item.scheduleType == "WeeklyFrequency", item.weeklyTargetCount > 0 {
                            Text("\(item.weeklyCompletedCount) of \(item.weeklyTargetCount) this week")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        } else if item.isEarning {
                            Text(item.earnValue.display)
                                .font(.caption.weight(.semibold))
                                .foregroundStyle(DB.gold(scheme))
                        } else if item.isHelp {
                            Text("Help raised — waiting on a parent")
                                .font(.caption)
                                .foregroundStyle(DB.help(scheme))
                        }
                    }
                    Spacer()
                }

                if !item.isHelp {
                    HStack(spacing: 10) {
                        Button {
                            store.helpTarget = item
                        } label: {
                            Label("Help", systemImage: "questionmark.circle")
                                .frame(maxWidth: .infinity)
                                .padding(.vertical, 11)
                        }
                        .buttonStyle(.plain)
                        .background(DB.help(scheme).opacity(0.16), in: Capsule())
                        .foregroundStyle(DB.help(scheme))

                        Button {
                            Task { await store.markDone(item, session) }
                        } label: {
                            Label("Done", systemImage: "checkmark.circle.fill")
                                .frame(maxWidth: .infinity)
                                .padding(.vertical, 11)
                        }
                        .buttonStyle(.plain)
                        .background(Color.accentColor, in: Capsule())
                        .foregroundStyle(.white)
                    }
                    .font(.subheadline.weight(.semibold))
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .glassCard()
        } else if !store.isRestDay {
            VStack(spacing: 8) {
                Text("🎉").font(.system(size: 40))
                Text("Every quest done today!")
                    .font(.headline)
                    .foregroundStyle(DB.success(scheme))
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 8)
            .glassCard()
        }
    }
}
