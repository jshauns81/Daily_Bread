import SwiftUI
import DailyBreadKit

/// Role-based shell: kids get Today/Earnings, parents get Home/Approvals.
/// iPhone = tabs; macOS = sidebar split view. Same screens either way.
struct MainView: View {
    let user: ApiUser

    private enum Section: String, CaseIterable, Identifiable {
        case today = "Today"
        case earnings = "Earnings"
        case home = "Home"
        case approvals = "Approvals"
        case settings = "Settings"

        var id: String { rawValue }

        var icon: String {
            switch self {
            case .today: return "sun.max"
            case .earnings: return "dollarsign.circle"
            case .home: return "house"
            case .approvals: return "checkmark.circle"
            case .settings: return "gearshape"
            }
        }
    }

    private var sections: [Section] {
        user.isParent
            ? [.home, .approvals, .settings]
            : [.today, .earnings, .settings]
    }

    @State private var selection: Section?

    var body: some View {
        #if os(macOS)
        NavigationSplitView {
            List(sections, selection: $selection) { section in
                Label(section.rawValue, systemImage: section.icon)
                    .tag(section)
            }
            .navigationTitle("Daily Bread")
        } detail: {
            screen(for: selection ?? sections[0])
        }
        .onAppear { if selection == nil { selection = sections.first } }
        #else
        TabView {
            ForEach(sections) { section in
                NavigationStack {
                    screen(for: section)
                }
                .tabItem {
                    Label(section.rawValue, systemImage: section.icon)
                }
            }
        }
        #endif
    }

    @ViewBuilder
    private func screen(for section: Section) -> some View {
        switch section {
        case .today: TodayView()
        case .earnings: EarningsView()
        case .home: ParentHomeView()
        case .approvals: ApprovalsView()
        case .settings: SettingsView()
        }
    }
}
