import SwiftUI
import DailyBreadKit

/// Parent at-a-glance: today's family state, week earnings, kids' progress.
@MainActor
@Observable
final class ParentHomeStore {
    var dashboard: ParentDashboard?
    var loading = false
    var errorMessage: String?

    func load(_ session: SessionStore) async {
        loading = dashboard == nil
        defer { loading = false }
        do {
            dashboard = try await session.client.parentDashboard()
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

struct ParentHomeView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = ParentHomeStore()

    var body: some View {
        List {
            if let dash = store.dashboard {
                Section {
                    statsRow(dash)
                        .listRowInsets(EdgeInsets(top: 8, leading: 16, bottom: 8, trailing: 16))
                        .listRowBackground(Color.clear)
                }

                if !dash.childrenProgress.isEmpty {
                    Section("Today") {
                        ForEach(dash.childrenProgress) { child in
                            childRow(child)
                        }
                    }
                }

                if !dash.childrenBalances.isEmpty {
                    Section("Balances") {
                        ForEach(dash.childrenBalances) { child in
                            HStack {
                                Text(child.displayName)
                                Spacer()
                                Text(child.balance.display)
                                    .font(.subheadline.weight(.semibold))
                                    .foregroundStyle(DB.gold(scheme))
                            }
                        }
                    }
                }
            } else if store.loading {
                ProgressView().frame(maxWidth: .infinity)
            }

            if let error = store.errorMessage {
                Section {
                    Label(error, systemImage: "wifi.exclamationmark")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
        }
        .navigationTitle("Home")
        .refreshable { await store.load(session) }
        .task { await store.load(session) }
    }

    private func statsRow(_ dash: ParentDashboard) -> some View {
        HStack(spacing: 12) {
            statTile(
                value: "\(dash.pendingApprovals.count + dash.helpRequests.count)",
                label: "waiting on you",
                color: .accentColor)
            statTile(
                value: dash.thisWeekEarnings.display,
                label: "this week",
                color: DB.gold(scheme))
            statTile(
                value: "\(dash.todayCompletedCount + dash.todayApprovedCount)/\(dash.todayTotalChores)",
                label: "chores today",
                color: .primary)
        }
    }

    private func statTile(value: String, label: String, color: Color) -> some View {
        VStack(spacing: 3) {
            Text(value)
                .font(.title3.weight(.bold))
                .foregroundStyle(color)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
            Text(label)
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
        .glassCard(padding: 12)
    }

    private func childRow(_ child: ChildProgress) -> some View {
        HStack(spacing: 12) {
            ProgressRing(
                progress: child.totalChores == 0
                    ? 0
                    : Double(child.completedChores + child.approvedChores) / Double(child.totalChores),
                label: "\(child.completedChores + child.approvedChores)/\(child.totalChores)")
                .frame(width: 44, height: 44)

            VStack(alignment: .leading, spacing: 2) {
                Text(child.displayName)
                    .font(.body.weight(.medium))
                if child.helpRequests > 0 {
                    Text("\(child.helpRequests) help request\(child.helpRequests == 1 ? "" : "s")")
                        .font(.caption)
                        .foregroundStyle(DB.help(scheme))
                } else {
                    Text("\(child.pendingChores) left today")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
        }
    }
}
