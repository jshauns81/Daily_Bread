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
        case activity = "Activity"
        case planner = "Planner"
        case approvals = "Approvals"
        case driving = "Driving"
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
            case .activity: return "list.bullet.clipboard"
            case .planner: return "checklist"
            case .approvals: return "checkmark.circle"
            case .driving: return "car.fill"
            case .settings: return "gearshape"
            }
        }
    }

    /// Driving is a first-class destination on macOS, where the sidebar has the
    /// room — Shaun's call, and it's the surface his son uses most. iPhone keeps
    /// five tabs: a sixth crowds the bar, and the kid's Home already carries the
    /// driving card plus a header shortcut.
    private var sections: [Section] {
        #if os(macOS)
        return user.isParent
            ? [.home, .activity, .planner, .approvals, .driving, .settings]
            : [.kidHome, .today, .earnings, .awards, .driving, .settings]
        #else
        return user.isParent
            ? [.home, .activity, .planner, .approvals, .settings]
            : [.kidHome, .today, .earnings, .awards, .settings]
        #endif
    }

    @State private var selection: Section?

    var body: some View {
        #if os(macOS)
        NavigationSplitView {
            List(sections, selection: $selection) { section in
                Label(section.label, systemImage: section.icon)
                    .badge(section == .approvals ? session.approvalsWaiting : 0)
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
        .poll { await refreshBadge() }
        #else
        TabView {
            ForEach(sections) { section in
                NavigationStack {
                    screen(for: section)
                }
                .tabItem {
                    Label(section.label, systemImage: section.icon)
                }
                .badge(section == .approvals ? session.approvalsWaiting : 0)
            }
        }
        .task { await refreshBadge() }
        .refreshOnForeground { await refreshBadge() }
        .poll { await refreshBadge() }
        #endif
    }

    /// Covers the parent who never opens Approvals; in-app changes update the
    /// badge through SessionStore the moment the queue reloads.
    private func refreshBadge() async {
        await session.refreshApprovalsBadge()
    }

    @ViewBuilder
    private func screen(for section: Section) -> some View {
        switch section {
        case .kidHome: KidHomeView()
        case .today: TodayView()
        case .earnings: EarningsView()
        case .awards: AchievementsView()
        case .home: ParentHomeView()
        case .activity: ActivityView()
        case .planner: PlannerView()
        case .approvals: ApprovalsView()
        case .driving: DrivingLogView(mode: user.isParent ? .parent : .kid)
        case .settings: SettingsView()
        }
    }
}
