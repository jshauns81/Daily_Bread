import SwiftUI
import DailyBreadKit

/// One child's money, whole: the balance against the family threshold, the
/// lifetime totals the web ledger always reported, and every transaction
/// typed and signed. Both writes — Adjust and the cash-out — live here, so
/// the balance row a parent taps on Home lands on the single money home for
/// that kid. Cash-out is bookkeeping only; the sheet says so.
@MainActor
@Observable
final class LedgerStore {
    let userId: String?

    var summary: LedgerSummary?
    var history: [LedgerTransaction] = []
    var loading = false
    var errorMessage: String?

    init(userId: String?) {
        self.userId = userId
    }

    func load(_ session: SessionStore) async {
        loading = summary == nil
        defer { loading = false }
        do {
            async let summaryTask = session.client.ledgerSummary(userId: userId)
            async let historyTask = session.client.history(userId: userId, limit: 100)
            summary = try await summaryTask
            history = try await historyTask.transactions
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

struct LedgerView: View {
    let displayName: String

    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store: LedgerStore
    @State private var adjusting: AdjustBalanceTarget?
    @State private var cashingOut: CashOutTarget?

    init(userId: String?, displayName: String) {
        self.displayName = displayName
        _store = State(initialValue: LedgerStore(userId: userId))
    }

    var body: some View {
        ScrollView {
            LazyVStack(alignment: .leading, spacing: 14) {
                heroCard

                if let summary = store.summary {
                    sectionHeader("All time")
                    tiles(summary)
                }

                if !store.history.isEmpty {
                    sectionHeader("History")
                    VStack(spacing: 10) {
                        ForEach(store.history) { txn in
                            row(txn)
                            if txn.id != store.history.last?.id {
                                Divider()
                            }
                        }
                    }
                    .glassCard()
                } else if store.loading {
                    ProgressView().frame(maxWidth: .infinity).padding(.top, 40)
                }

                if let error = store.errorMessage {
                    Label(error, systemImage: "wifi.exclamationmark")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
            .padding()
        }
        .navigationTitle("\(displayName)'s money")
        #if os(iOS)
        .navigationBarTitleDisplayMode(.inline)
        #endif
        .themeBackground()
        .refreshable { await store.load(session) }
        .refreshOnForeground { await store.load(session) }
        .task { await store.load(session) }
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button {
                    guard let summary = store.summary, let userId = store.userId else { return }
                    adjusting = AdjustBalanceTarget(
                        userId: userId, name: displayName, balance: summary.balance)
                } label: {
                    Label("Adjust balance", systemImage: "slider.horizontal.3")
                }
                .disabled(store.summary == nil || store.userId == nil)
            }
        }
        .sheet(item: $adjusting) { target in
            AdjustBalanceSheet(target: target) { _ in
                Task { await store.load(session) }
            }
        }
        .sheet(item: $cashingOut) { target in
            CashOutSheet(target: target) { _ in
                Task { await store.load(session) }
            }
        }
    }

    // MARK: - Hero

    private var heroCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            VStack(alignment: .leading, spacing: 4) {
                Text("BALANCE")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.secondary)
                    .kerning(1)
                Text(store.summary?.balance.display ?? "—")
                    .font(.system(size: 42, weight: .bold, design: .rounded))
                    .foregroundStyle(DB.gold(scheme))
                    .contentTransition(.numericText())
            }

            if let summary = store.summary {
                if summary.canCashOut {
                    Button {
                        cashingOut = CashOutTarget(
                            userId: store.userId,
                            possessive: "\(displayName)'s",
                            balance: summary.balance,
                            threshold: summary.cashOutThreshold)
                    } label: {
                        Label("Record a cash-out", systemImage: "banknote")
                            .frame(maxWidth: .infinity, minHeight: 40)
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(Color.dbAccent)
                } else {
                    thresholdProgress(summary)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(padding: 18)
    }

    /// How far the balance is from the family's cash-out floor.
    private func thresholdProgress(_ summary: LedgerSummary) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            ProgressView(
                value: min(max(progressFraction(summary), 0), 1))
                .tint(Color.dbAccent)
            Text("Cash-out unlocks at \(summary.cashOutThreshold.display).")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    private func progressFraction(_ summary: LedgerSummary) -> Double {
        let threshold = NSDecimalNumber(decimal: summary.cashOutThreshold.amount).doubleValue
        guard threshold > 0 else { return 1 }
        return NSDecimalNumber(decimal: summary.balance.amount).doubleValue / threshold
    }

    // MARK: - Lifetime tiles

    private func tiles(_ summary: LedgerSummary) -> some View {
        LazyVGrid(columns: [GridItem(.flexible(), spacing: 10),
                            GridItem(.flexible(), spacing: 10)],
                  spacing: 10) {
            tile("Earned", summary.totalEarnings.display, DB.gold(scheme))
            tile("Deducted", summary.totalDeductions.display, DB.help(scheme))
            tile("Bonuses", summary.totalBonuses.display, DB.gold(scheme))
            tile("Penalties", summary.totalPenalties.display, DB.help(scheme))
            tile("Paid out", summary.totalPaidOut.display, Color.dbAccent)
            tile("Entries", "\(summary.transactionCount)", Color.primary)
        }
    }

    private func tile(_ label: String, _ value: String, _ color: Color) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(label.uppercased())
                .font(.caption2.weight(.bold))
                .foregroundStyle(.secondary)
                .kerning(0.6)
            Text(value)
                .font(.headline.weight(.semibold))
                .monospacedDigit()
                .foregroundStyle(color)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(padding: 12)
    }

    // MARK: - History rows

    private func row(_ txn: LedgerTransaction) -> some View {
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 3) {
                Text(txn.description ?? TxnKind(txn.type).label)
                    .font(.body)
                    .lineLimit(2)
                HStack(spacing: 6) {
                    Text(TxnKind(txn.type).label)
                        .font(.caption2.weight(.bold))
                        .padding(.horizontal, 7)
                        .padding(.vertical, 2)
                        .background(TxnKind(txn.type).color(scheme).opacity(0.15), in: Capsule())
                        .foregroundStyle(TxnKind(txn.type).color(scheme))
                    Text(txn.date.shortDisplay)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
            Text(txn.amount.signedDisplay)
                .font(.subheadline.weight(.semibold))
                .monospacedDigit()
                .foregroundStyle(txn.amount.isNegative ? Color.secondary : DB.gold(scheme))
        }
    }

    /// The server's `TransactionType` names, humanised and coloured by role.
    /// Unknown strings fall through legibly rather than crashing a decode —
    /// the enum lives here, not in the wire model, on purpose.
    private struct TxnKind {
        let raw: String
        init(_ raw: String) { self.raw = raw }

        // The server's actual enum: ChoreEarning, ChoreDeduction, Bonus,
        // Penalty, Adjustment, Payout, Transfer, AchievementReward. The
        // weekly routine pool posts as ChoreEarning with its own description.
        var label: String {
            switch raw {
            case "ChoreEarning": return "Chore"
            case "ChoreDeduction": return "Missed"
            case "Payout": return "Paid out"
            case "Bonus": return "Bonus"
            case "Penalty": return "Penalty"
            case "Adjustment": return "Adjustment"
            case "Transfer": return "Transfer"
            case "AchievementReward": return "Award"
            default: return raw
            }
        }

        func color(_ scheme: ColorScheme) -> Color {
            switch raw {
            case "ChoreEarning", "Bonus", "AchievementReward": return DB.gold(scheme)
            case "ChoreDeduction", "Penalty": return DB.help(scheme)
            case "Payout": return Color.dbAccent
            default: return Color.secondary
            }
        }
    }

    private func sectionHeader(_ title: String) -> some View {
        Text(title.uppercased())
            .font(.caption.weight(.bold))
            .foregroundStyle(.secondary)
            .kerning(0.8)
            .padding(.top, 4)
    }
}
