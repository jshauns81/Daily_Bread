import SwiftUI
import DailyBreadKit

/// Balance hero, primary goal progress, recent history.
@MainActor
@Observable
final class EarningsStore {
    var balance: Balance?
    var goals: [Goal] = []
    var history: [LedgerTransaction] = []
    var loading = false
    var errorMessage: String?

    func load(_ session: SessionStore) async {
        loading = balance == nil
        defer { loading = false }
        do {
            async let balanceTask = session.client.balance()
            async let goalsTask = session.client.goals()
            async let historyTask = session.client.history(limit: 30)
            balance = try await balanceTask
            goals = try await goalsTask
            history = try await historyTask.transactions
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    var primaryGoal: Goal? {
        goals.first(where: { $0.isPrimary && !$0.isCompleted }) ?? goals.first(where: { !$0.isCompleted })
    }
}

struct EarningsView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = EarningsStore()

    var body: some View {
        List {
            Section {
                balanceCard
                    .listRowInsets(EdgeInsets(top: 8, leading: 16, bottom: 8, trailing: 16))
            }

            if let goal = store.primaryGoal {
                Section {
                    goalCard(goal)
                }
            }

            if !store.history.isEmpty {
                Section("Recent") {
                    ForEach(store.history) { txn in
                        transactionRow(txn)
                    }
                }
            }

            if let error = store.errorMessage {
                Section {
                    Label(error, systemImage: "wifi.exclamationmark")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
        }
        .navigationTitle("Earnings")
        .graphiteBackground()
        .refreshable { await store.load(session) }
        .task { await store.load(session) }
    }

    private var balanceCard: some View {
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
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(padding: 18)
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
                .tint(Color.accentColor)
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
