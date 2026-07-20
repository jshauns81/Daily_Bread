import SwiftUI
import DailyBreadKit

/// Role-based shell: kids get Home/Today/Earnings/Awards, parents get
/// Home/Planner/Approvals. iPhone = tabs (with a waiting-count badge);
/// macOS = sidebar split view.
struct MainView: View {
    let user: ApiUser

    @Environment(SessionStore.self) private var session

    private enum Section: String, CaseIterable, Identifiable {
        case kidHome = "Home"
        case today = "Today"
        case earnings = "Earnings"
        case awards = "Awards"
        case home = "Home "
        case planner = "Planner"
        case approvals = "Approvals"
        case settings = "Settings"

        var id: String { rawValue }

        /// Tab/sidebar label — kidHome and the parent home both read "Home".
        var label: String { self == .home ? "Home" : rawValue }

        var icon: String {
            switch self {
            case .kidHome: return "house"
            case .today: return "sun.max"
            case .earnings: return "dollarsign.circle"
            case .awards: return "trophy"
            case .home: return "house"
            case .planner: return "checklist"
            case .approvals: return "checkmark.circle"
            case .settings: return "gearshape"
            }
        }
    }

    private var sections: [Section] {
        user.isParent
            ? [.home, .planner, .approvals, .settings]
            : [.kidHome, .today, .earnings, .awards, .settings]
    }

    @State private var selection: Section?
    @State private var waitingCount = 0

    var body: some View {
        #if os(macOS)
        NavigationSplitView {
            List(sections, selection: $selection) { section in
                Label(section.label, systemImage: section.icon)
                    .badge(section == .approvals ? waitingCount : 0)
                    .tag(section)
            }
            .navigationTitle("Daily Bread")
        } detail: {
            NavigationStack {
                screen(for: selection ?? sections[0])
            }
        }
        .onAppear { if selection == nil { selection = sections.first } }
        .task { await refreshBadge() }
        .refreshOnForeground { await refreshBadge() }
        #else
        TabView {
            ForEach(sections) { section in
                NavigationStack {
                    screen(for: section)
                }
                .tabItem {
                    Label(section.label, systemImage: section.icon)
                }
                .badge(section == .approvals ? waitingCount : 0)
            }
        }
        .task { await refreshBadge() }
        .refreshOnForeground { await refreshBadge() }
        #endif
    }

    private func refreshBadge() async {
        guard user.isParent else { return }
        if let queue = try? await session.client.approvalsQueue() {
            waitingCount = queue.pendingApprovals.count + queue.helpRequests.count
        }
    }

    @ViewBuilder
    private func screen(for section: Section) -> some View {
        switch section {
        case .kidHome: KidHomeView()
        case .today: TodayView()
        case .earnings: EarningsView()
        case .awards: AchievementsView()
        case .home: ParentHomeView()
        case .planner: PlannerView()
        case .approvals: ApprovalsView()
        case .settings: SettingsView()
        }
    }
}
