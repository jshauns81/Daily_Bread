import SwiftUI
import Charts
import DailyBreadKit

/// Balance hero, primary goal progress, last-two-weeks chart, recent history.
@MainActor
@Observable
final class EarningsStore {
    var balance: Balance?
    var summary: LedgerSummary?
    var goals: [Goal] = []
    var history: [LedgerTransaction] = []
    var last14: [DaySummary] = []
    var rangeLoaded = false
    var loading = false
    var errorMessage: String?

    func load(_ session: SessionStore) async {
        loading = balance == nil
        defer { loading = false }
        do {
            async let balanceTask = session.client.balance()
            async let summaryTask = session.client.ledgerSummary()
            async let goalsTask = session.client.goals()
            async let historyTask = session.client.history(limit: 30)
            balance = try await balanceTask
            summary = try await summaryTask
            goals = try await goalsTask
            history = try await historyTask.transactions
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
        await loadLast14(session)
    }

    /// The last 14 days of earnings for the bar chart. Fails quietly —
    /// the card shows a calm caption instead of an error.
    private func loadLast14(_ session: SessionStore) async {
        let today = DayDate.todayLocal()
        let from = today.addingDays(-13)
        // Server order — never re-sort.
        last14 = (try? await session.client.calendarRange(from: from, to: today))?.days ?? []
        rangeLoaded = true
    }

    var primaryGoal: Goal? {
        goals.first(where: { $0.isPrimary && !$0.isCompleted }) ?? goals.first(where: { !$0.isCompleted })
    }

    var hasRecentEarnings: Bool {
        last14.contains { !$0.earnedAmount.isZero }
    }
}

struct EarningsView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = EarningsStore()
    @State private var cashingOut: CashOutTarget?

    // No swipe actions anywhere on this screen, so it's authored cards in a
    // ScrollView — not a List neutralised row by row to host them.
    var body: some View {
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 14) {
                balanceCard

                if store.rangeLoaded {
                    last14Card
                }

                if session.features.enableGoals {
                    sectionHeader("Goals")
                    VStack(alignment: .leading, spacing: 12) {
                        if let goal = store.primaryGoal {
                            goalCard(goal)
                            Divider()
                        }
                        NavigationLink {
                            GoalsView()
                        } label: {
                            Label {
                                Text(store.goals.isEmpty ? "Set a savings goal" : "Manage goals")
                            } icon: {
                                DBIcon("target")
                            }
                            .font(.body.weight(.medium))
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .foregroundStyle(Color.dbAccent)
                    }
                    .glassCard()
                }

                if !store.history.isEmpty {
                    sectionHeader("Recent")
                    VStack(spacing: 10) {
                        ForEach(store.history) { txn in
                            transactionRow(txn)
                            if txn.id != store.history.last?.id {
                                Divider()
                            }
                        }
                    }
                    .glassCard()
                }

                if let error = store.errorMessage {
                    Label(error, systemImage: "wifi.exclamationmark")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
            .padding()
        }
        .navigationTitle("Earnings")
        .themeBackground()
        .refreshable { await store.load(session) }
        .refreshOnForeground { await store.load(session) }
        .task { await store.load(session) }
    }

    private func sectionHeader(_ title: String) -> some View {
        Text(title.uppercased())
            .font(.caption.weight(.bold))
            .foregroundStyle(.secondary)
            .kerning(0.8)
            .padding(.top, 4)
    }

    private var balanceCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            VStack(alignment: .leading, spacing: 4) {
                Text("BALANCE")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.secondary)
                    .kerning(1)
                Text(store.balance?.balance.display ?? "—")
                    .font(.system(size: 42, weight: .bold, design: .rounded))
                    .foregroundStyle(DB.gold(scheme))
                    .contentTransition(.numericText())
            }

            // The web's My Balance always answered "how close am I to cashing
            // out, and where has my money gone" — same answers, same card.
            if let summary = store.summary {
                if summary.canCashOut {
                    Button {
                        cashingOut = CashOutTarget(
                            userId: nil,
                            possessive: "your",
                            balance: summary.balance,
                            threshold: summary.cashOutThreshold)
                    } label: {
                        Label("Record a cash-out", systemImage: "banknote")
                            .frame(maxWidth: .infinity, minHeight: 40)
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(Color.dbAccent)
                } else if !summary.cashOutThreshold.isZero {
                    VStack(alignment: .leading, spacing: 6) {
                        ProgressView(value: min(max(thresholdFraction(summary), 0), 1))
                            .tint(Color.dbAccent)
                        Text("Cash-out unlocks at \(summary.cashOutThreshold.display).")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }

                HStack(spacing: 0) {
                    lifetimeStat("EARNED", summary.totalEarnings.display, DB.gold(scheme))
                    lifetimeStat("MISSED", summary.totalDeductions.display, DB.help(scheme))
                    lifetimeStat("PAID OUT", summary.totalPaidOut.display, Color.dbAccent)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(padding: 18)
        .sheet(item: $cashingOut) { target in
            CashOutSheet(target: target) { _ in
                Task { await store.load(session) }
            }
        }
    }

    private func thresholdFraction(_ summary: LedgerSummary) -> Double {
        let threshold = NSDecimalNumber(decimal: summary.cashOutThreshold.amount).doubleValue
        guard threshold > 0 else { return 1 }
        return NSDecimalNumber(decimal: summary.balance.amount).doubleValue / threshold
    }

    private func lifetimeStat(_ label: String, _ value: String, _ color: Color) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(label)
                .font(.caption2.weight(.bold))
                .foregroundStyle(.secondary)
                .kerning(0.6)
            Text(value)
                .font(.subheadline.weight(.semibold))
                .monospacedDigit()
                .foregroundStyle(color)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// Daily earnings, last two weeks. Gold bars — money is always gold.
    private var last14Card: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("LAST 14 DAYS")
                .font(.caption.weight(.bold))
                .foregroundStyle(.secondary)
                .kerning(1)

            if store.hasRecentEarnings {
                Chart(store.last14) { day in
                    BarMark(
                        x: .value("Day", day.date.displayDate, unit: .day),
                        y: .value("Earned", NSDecimalNumber(decimal: day.earnedAmount.amount).doubleValue))
                        .foregroundStyle(DB.gold(scheme))
                        .cornerRadius(3)
                }
                .chartXAxis {
                    AxisMarks(values: .stride(by: .day, count: 3)) { _ in
                        AxisValueLabel(format: .dateTime.weekday(.narrow), centered: true)
                            .font(.caption2)
                            .foregroundStyle(.tertiary)
                    }
                }
                .chartYAxis {
                    AxisMarks(position: .trailing, values: .automatic(desiredCount: 3)) { _ in
                        AxisValueLabel()
                            .font(.caption2)
                            .foregroundStyle(.tertiary)
                    }
                }
                .frame(height: 140)
            } else {
                Text("Nothing earned yet in the last two weeks.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard()
    }

    private func goalCard(_ goal: Goal) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text(goal.name)
                    .font(.body.weight(.semibold))
                Spacer()
                Text("\(goal.currentBalance.display) of \(goal.targetAmount.display)")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
            ProgressView(value: Double(goal.progressPercent), total: 100)
                .tint(Color.dbAccent)
            Text("\(goal.progressPercent)% there")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .padding(.vertical, 4)
    }

    private func transactionRow(_ txn: LedgerTransaction) -> some View {
        HStack {
            VStack(alignment: .leading, spacing: 2) {
                Text(txn.description ?? txn.type)
                    .font(.body)
                    .lineLimit(1)
                Text(txn.date.shortDisplay)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Text(txn.amount.signedDisplay)
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(txn.amount.isNegative ? Color.secondary : DB.gold(scheme))
        }
    }
}

