import SwiftUI
import DailyBreadKit

/// Role-based shell: kids get Home/Today/Earnings/Awards, parents get
/// Home/Planner/Approvals. iPhone = tabs (with a waiting-count badge);
/// macOS = sidebar split view.
struct MainView: View {
    let user: ApiUser

    @Environment(SessionStore.self) private var session

    // The sidebar paints itself in the theme accent, and `Color.dbAccent` reads
    // storage rather than the environment — so the shell has to observe the
    // theme keys itself or a theme switch would leave stale chrome behind.
    @AppStorage(ThemeStore.key) private var themeRaw = DBTheme.sunroom.rawValue
    @AppStorage(ThemeStore.customKey) private var customThemeRaw = ""

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
        // Driving only earns a sidebar row when someone in the house actually
        // drives — a permanent row for a feature the family switched off is
        // the same clutter the per-child toggle exists to remove.
        let driving: [Section] = session.drivingVisible ? [.driving] : []
        return user.isParent
            ? [.home, .activity, .planner, .approvals] + driving + [.settings]
            : [.kidHome, .today, .earnings, .awards] + driving + [.settings]
        #else
        return user.isParent
            ? [.home, .activity, .planner, .approvals, .settings]
            : [.kidHome, .today, .earnings, .awards, .settings]
        #endif
    }

    /// Everything except Settings, which the macOS sidebar pins to its foot.
    private var navigationSections: [Section] {
        sections.filter { $0 != .settings }
    }

    @State private var selection: Section?

    var body: some View {
        #if os(macOS)
        NavigationSplitView {
            // Hand-rolled rows. `.tint` does *not* recolour a macOS sidebar's
            // selection — verified on screen, the pill stayed system blue in a
            // mulberry window — and the accent has to follow the theme, so a
            // static AccentColor asset is out too. Dropping `selection:` costs
            // arrow-key navigation, so the rows take ⌘1…⌘6 instead, which is
            // what Mail and Finder bind anyway.
            List {
                ForEach(Array(navigationSections.enumerated()), id: \.element) { index, section in
                    sidebarRow(section, shortcut: index + 1)
                        .listRowInsets(EdgeInsets(top: 1, leading: 6, bottom: 1, trailing: 6))
                        .listRowBackground(Color.clear)
                        .listRowSeparator(.hidden)
                }
            }
            // Settings sits at the foot of the sidebar, not in the flow of
            // places you go — the same shelf Mail and Finder put their
            // housekeeping on. safeAreaInset keeps the List's sidebar
            // styling while pinning the row to the bottom.
            .safeAreaInset(edge: .bottom, spacing: 0) {
                sidebarRow(.settings, shortcut: sections.count)
                    .padding(.horizontal, 6)
                    .padding(.top, 6)
                    .padding(.bottom, 10)
            }
            .navigationTitle("Daily Bread")
            // Shaun's first prod launch opened with the sidebar squeezed until
            // "Approvals" was literally "…" — give the column a floor that fits
            // the longest label plus a count badge.
            .navigationSplitViewColumnWidth(min: 190, ideal: 210, max: 280)
        } detail: {
            NavigationStack {
                screen(for: selection ?? sections[0])
            }
        }
        .onAppear { if selection == nil { selection = sections.first } }
        // A parent can switch driving off while sitting on the Driving screen;
        // don't strand them on a row that no longer exists.
        .onChange(of: session.drivingVisible) { _, _ in
            if let selection, !sections.contains(selection) { self.selection = sections.first }
        }
        .task {
            await refreshBadge()
            await session.refreshDrivingVisibility()
        }
        .refreshOnForeground {
            await refreshBadge()
            await session.refreshDrivingVisibility()
        }
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
        .task {
            await refreshBadge()
            // The Mac branch always refreshed this for its sidebar; the phone
            // never did because nothing consumed it — now Home's driving row does.
            await session.refreshDrivingVisibility()
        }
        .refreshOnForeground {
            await refreshBadge()
            await session.refreshDrivingVisibility()
        }
        .poll { await refreshBadge() }
        #endif
    }

    /// Covers the parent who never opens Approvals; in-app changes update the
    /// badge through SessionStore the moment the queue reloads.
    private func refreshBadge() async {
        await session.refreshApprovalsBadge()
    }

    #if os(macOS)
    /// One sidebar destination, painted in the theme rather than the system
    /// accent. Selected reads as a filled accent capsule with white content;
    /// unselected keeps the symbol accent-tinted and the label primary, so the
    /// row still scans as a group of six rather than six unrelated glyphs.
    private func sidebarRow(_ section: Section, shortcut: Int) -> some View {
        let selected = section == selection
        let waiting = section == .approvals ? session.approvalsWaiting : 0
        return Button {
            selection = section
        } label: {
            HStack(spacing: 8) {
                Image(systemName: section.icon)
                    .symbolRenderingMode(.monochrome)
                    .foregroundStyle(selected ? AnyShapeStyle(.white)
                                              : AnyShapeStyle(Color.dbAccent))
                    .frame(width: 20)
                Text(section.label)
                Spacer(minLength: 4)
                if waiting > 0 {
                    Text("\(waiting)")
                        .font(.caption.weight(.semibold))
                        .monospacedDigit()
                        .padding(.horizontal, 6)
                        .padding(.vertical, 1)
                        .background(selected ? AnyShapeStyle(.white.opacity(0.25))
                                             : AnyShapeStyle(Color.dbAccent),
                                    in: Capsule())
                        .foregroundStyle(.white)
                }
            }
            .font(.body)
            .foregroundStyle(selected ? AnyShapeStyle(.white) : AnyShapeStyle(Color.primary))
            .padding(.horizontal, 8)
            .padding(.vertical, 6)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(selected ? Color.dbAccent : Color.clear,
                        in: RoundedRectangle(cornerRadius: 8, style: .continuous))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .keyboardShortcut(KeyEquivalent(Character("\(shortcut)")), modifiers: .command)
        .accessibilityAddTraits(selected ? .isSelected : [])
    }
    #endif

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
