import SwiftUI
import DailyBreadKit

/// Parent at-a-glance, in the old dashboard's spirit: a greeting that knows
/// you, the day's chips, the week strip, the kids, and a year of history.
@MainActor
@Observable
final class ParentHomeStore {
    var dashboard: ParentDashboard?
    var loading = false
    var errorMessage: String?
    var heatmapChild: ChildProgress?

    func load(_ session: SessionStore) async {
        loading = dashboard == nil
        defer { loading = false }
        do {
            dashboard = try await session.client.parentDashboard()
            errorMessage = nil
            if heatmapChild == nil {
                heatmapChild = dashboard?.childrenProgress.first
            }
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
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                if let dash = store.dashboard {
                    greetingCard(dash)
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

                    weekStrip(dash)

                    if !dash.childrenBalances.isEmpty {
                        sectionHeader("Balances")
                        balancesCard(dash)
                    }

                    if let child = store.heatmapChild, let userId = child.userId {
                        heatmapSection(dash, child: child, userId: userId)
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
        .graphiteBackground()
        .refreshable { await store.load(session) }
        .task { await store.load(session) }
    }

    // MARK: - Greeting

    private func greetingCard(_ dash: ParentDashboard) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                (Text("\(Greeting.current), ")
                    + Text(session.currentUser?.userName.capitalized ?? "")
                        .foregroundStyle(Color.accentColor))
                    .font(.title2.weight(.bold))
                Spacer()
                Text(DayDate.todayLocal().longDisplay)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            HStack(spacing: 8) {
                chip("✦ \(dash.pendingApprovals.count) awaiting approval",
                     color: DB.gold(scheme),
                     emphasized: !dash.pendingApprovals.isEmpty)
                chip("✓ \(dash.todayCompletedCount + dash.todayApprovedCount) done today",
                     color: .secondary,
                     emphasized: false)
                chip("! \(dash.helpRequests.count) needs help",
                     color: DB.help(scheme),
                     emphasized: !dash.helpRequests.isEmpty)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(padding: 16)
    }

    private func chip(_ text: String, color: Color, emphasized: Bool) -> some View {
        Text(text)
            .font(.caption.weight(.semibold))
            .foregroundStyle(emphasized ? color : Color.secondary)
            .padding(.horizontal, 9)
            .padding(.vertical, 5)
            .background(
                (emphasized ? color : Color.secondary).opacity(0.13),
                in: Capsule())
            .lineLimit(1)
            .minimumScaleFactor(0.75)
    }

    // MARK: - Stats / week

    private func statsRow(_ dash: ParentDashboard) -> some View {
        HStack(spacing: 12) {
            statTile(
                value: "\(dash.pendingApprovals.count + dash.helpRequests.count)",
                label: "waiting on you",
                color: .accentColor)
            statTile(
                value: dash.thisWeekEarnings.display,
                label: "earned this week",
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

    @ViewBuilder
    private func weekStrip(_ dash: ParentDashboard) -> some View {
        if !dash.weekEarnings.isEmpty {
            VStack(alignment: .leading, spacing: 8) {
                sectionHeader("Quick view — this week")
                HStack(spacing: 6) {
                    ForEach(dash.weekEarnings) { day in
                        VStack(spacing: 3) {
                            Text(dayLetter(day.date))
                                .font(.caption2.weight(.semibold))
                                .foregroundStyle(.secondary)
                            if day.amount.isZero {
                                Text("—")
                                    .font(.caption2)
                                    .foregroundStyle(.tertiary)
                            } else {
                                Text(day.amount.display)
                                    .font(.caption2.weight(.bold))
                                    .foregroundStyle(DB.gold(scheme))
                                    .lineLimit(1)
                                    .minimumScaleFactor(0.5)
                            }
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 8)
                        .background(.quaternary.opacity(0.5),
                                    in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                    }
                }
            }
        }
    }

    private func dayLetter(_ date: DayDate) -> String {
        String(date.displayDate.formatted(.dateTime.weekday(.narrow)))
    }

    // MARK: - Children

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
                } else if child.pendingChores > 0 {
                    Text("\(child.pendingChores) left today")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                } else {
                    Text("All done ✨")
                        .font(.caption)
                        .foregroundStyle(DB.gold(scheme))
                }
            }
            Spacer()
        }
        .glassCard()
    }

    private func balancesCard(_ dash: ParentDashboard) -> some View {
        VStack(spacing: 0) {
            ForEach(dash.childrenBalances) { child in
                HStack(spacing: 8) {
                    Text(child.displayName)
                    if child.isCashOutReady {
                        Text("Cash out ready")
                            .font(.caption2.weight(.bold))
                            .foregroundStyle(DB.success(scheme))
                            .padding(.horizontal, 7)
                            .padding(.vertical, 3)
                            .background(DB.success(scheme).opacity(0.15), in: Capsule())
                    }
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

    // MARK: - Heatmap

    private func heatmapSection(_ dash: ParentDashboard, child: ChildProgress, userId: String) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            if dash.childrenProgress.count > 1 {
                Menu {
                    ForEach(dash.childrenProgress) { option in
                        Button(option.displayName) { store.heatmapChild = option }
                    }
                } label: {
                    Label("\(child.displayName)'s year", systemImage: "chevron.up.chevron.down")
                        .font(.caption.weight(.semibold))
                }
            }
            YearHeatmapCard(title: "\(child.displayName)'s year", userId: userId)
        }
    }

    // MARK: - Misc

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
}
