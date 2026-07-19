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

/// ScrollView-based (not List): macOS List rows can miss redraws when data
/// arrives just after launch; plain SwiftUI layout renders reliably.
struct ParentHomeView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = ParentHomeStore()

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                if let dash = store.dashboard {
                    statsRow(dash)

                    if dash.childrenProgress.isEmpty && dash.todayTotalChores == 0 {
                        emptyState
                    }

                    if !dash.childrenProgress.isEmpty {
                        sectionHeader("Today")
                        VStack(spacing: 10) {
                            ForEach(dash.childrenProgress) { child in
                                childRow(child)
                            }
                        }
                    }

                    if !dash.childrenBalances.isEmpty {
                        sectionHeader("Balances")
                        VStack(spacing: 0) {
                            ForEach(dash.childrenBalances) { child in
                                HStack {
                                    Text(child.displayName)
                                    Spacer()
                                    Text(child.balance.display)
                                        .font(.subheadline.weight(.semibold))
                                        .foregroundStyle(DB.gold(scheme))
                                }
                                .padding(.vertical, 10)
                                if child.id != dash.childrenBalances.last?.id {
                                    Divider()
                                }
                            }
                        }
                        .glassCard()
                    }
                } else if store.loading {
                    ProgressView()
                        .frame(maxWidth: .infinity)
                        .padding(.top, 60)
                }

                if let error = store.errorMessage {
                    Label(error, systemImage: "wifi.exclamationmark")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
            .padding()
        }
        .navigationTitle("Home")
        .refreshable { await store.load(session) }
        .task { await store.load(session) }
    }

    private var emptyState: some View {
        ContentUnavailableView {
            Label("Quiet around here", systemImage: "house")
        } description: {
            Text("No family activity yet. If you're signed in as an admin account, sign in as a family member instead — or set up chores and family members in the web admin.")
        }
        .frame(maxWidth: .infinity)
        .padding(.top, 40)
    }

    private func sectionHeader(_ title: String) -> some View {
        Text(title.uppercased())
            .font(.caption.weight(.bold))
            .foregroundStyle(.secondary)
            .kerning(0.8)
            .padding(.top, 4)
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
        .glassCard()
    }
}
