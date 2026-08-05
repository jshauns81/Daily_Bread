import SwiftUI
import DailyBreadKit

// The rainbow year's app surfaces — the Today card, the full-year sheet, and the
// macOS dock. The renderer itself (RainbowDay / RainbowMath / RainbowYearGrid)
// lives in DailyBreadKit so the widget extension draws with the exact same code
// (IMPLEMENTATION.md §1.2: one renderer for every surface).

/// Fetches day summaries a calendar year at a time, keeps them contiguous from the
/// earliest loaded January 1st through the end of the current week.
@MainActor
@Observable
final class RainbowYearStore {
    var days: [DaySummary] = []
    var loaded = false
    var earliestYear = DayDate.todayLocal().year
    var hasMoreHistory = true
    var loadingMore = false

    func load(_ session: SessionStore, userId: String?) async {
        let today = DayDate.todayLocal()
        let jan1 = DayDate(year: today.year, month: 1, day: 1)
        if let range = try? await session.client.calendarRange(from: jan1, to: today, userId: userId) {
            days = range.days
            earliestYear = today.year
            // The widget family draws from this same data (§1.2a).
            WidgetBridge.mergeDays(days, childName: nil)
        }
        loaded = true
    }

    /// One more year of history, prepended. A year with no chores at all is the
    /// start of time: it isn't shown and stops further loading.
    func loadPreviousYear(_ session: SessionStore, userId: String?) async {
        guard hasMoreHistory, !loadingMore else { return }
        loadingMore = true
        defer { loadingMore = false }

        let year = earliestYear - 1
        guard let range = try? await session.client.calendarRange(
            from: DayDate(year: year, month: 1, day: 1),
            to: DayDate(year: year, month: 12, day: 31),
            userId: userId) else { return }

        if range.days.allSatisfy({ $0.totalChores == 0 }) {
            hasMoreHistory = false
        } else {
            days = range.days + days
            earliestYear = year
        }
    }

    var perfectDaysThisYear: Int {
        let year = DayDate.todayLocal().year
        return days.filter { $0.date.year == year && RainbowDay($0).isComplete }.count
    }

    /// Sunday-first columns of 7: nil-padded before the range, real future days
    /// (as outlines) through the end of the current week.
    var paddedCells: [RainbowDay?] { RainbowCells.padded(days) }

    func cells(lastWeeks weeks: Int) -> [RainbowDay?] {
        Array(paddedCells.suffix(weeks * 7))
    }
}

/// The 12-week card on Today. The full year is one tap away.
struct RainbowYearCard: View {
    let title: String
    var userId: String?
    /// macOS: mirror this card's 12 weeks onto the Dock icon whenever it loads.
    var updatesDockIcon = false

    @Environment(SessionStore.self) private var session
    @State private var store = RainbowYearStore()
    @State private var showingFullYear = false
    /// Measured card width — how much year we can actually show (macOS).
    @State private var gridWidth: CGFloat = 0

    /// The phone has room for a quarter; a Mac window has room for the year,
    /// so it draws at the denser `.full` cell rather than squeezing 52 weeks
    /// of `.card` cells into a column that can't hold them.
    private var gridSize: RainbowYearGrid.Size {
        #if os(macOS)
        return .full
        #else
        return .card
        #endif
    }

    /// As many weeks as fit, up to a full year and change. Charmaine's first
    /// question about this card was "why is it so short" — on a wide window
    /// the honest answer was "no reason", so now it fills the width it has.
    private var weeksToShow: Int {
        #if os(macOS)
        guard gridWidth > 0 else { return 12 }
        let step = gridSize.cell + gridSize.gap
        return min(53, max(12, Int(gridWidth / step)))
        #else
        return 12
        #endif
    }

    var body: some View {
        Button {
            showingFullYear = true
        } label: {
            VStack(alignment: .leading, spacing: 10) {
                Text(title.uppercased())
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.secondary)
                    .kerning(0.8)

                if store.loaded && store.days.isEmpty {
                    Text("No activity yet this year.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                } else {
                    RainbowYearGrid(cells: store.cells(lastWeeks: weeksToShow), size: gridSize)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background {
                            GeometryReader { proxy in
                                Color.clear
                                    .onAppear { gridWidth = proxy.size.width }
                                    .onChange(of: proxy.size.width) { _, new in gridWidth = new }
                            }
                        }

                    // No less→more legend — a rainbow has nothing to explain.
                    if store.perfectDaysThisYear > 0 {
                        Text("**\(store.perfectDaysThisYear)** perfect day\(store.perfectDaysThisYear == 1 ? "" : "s") this year ✨")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .glassCard()
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("\(title). \(store.perfectDaysThisYear) perfect days this year. Opens the full year.")
        .task(id: userId) { await store.load(session, userId: userId) }
        .refreshOnForeground { [weak store] in
            await store?.load(session, userId: userId)
        }
        #if os(macOS)
        .onChange(of: store.days, initial: true) {
            if updatesDockIcon { DockIcon.update(cells: store.cells(lastWeeks: 12)) }
        }
        #endif
        .sheet(isPresented: $showingFullYear) {
            RainbowYearView(title: title, userId: userId)
        }
    }
}

/// The scrollable year (`.full` on iPhone, `.wall` density on iPad/macOS), with
/// multi-year history one tap away at the leading edge. Years join with no gap or
/// divider — the boundary column looks like any other week. The invisible seam is
/// the whole idea.
struct RainbowYearView: View {
    let title: String
    var userId: String?

    @Environment(SessionStore.self) private var session
    @Environment(\.dismiss) private var dismiss
    #if os(iOS)
    @Environment(\.horizontalSizeClass) private var sizeClass
    #endif
    @State private var store = RainbowYearStore()
    @State private var selectedDay: DaySummary?

    private var gridSize: RainbowYearGrid.Size {
        #if os(macOS)
        return .wall
        #else
        return sizeClass == .regular ? .wall : .full
        #endif
    }

    var body: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 12) {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(alignment: .center, spacing: 10) {
                        if store.hasMoreHistory, !store.days.isEmpty {
                            Button {
                                Task { await store.loadPreviousYear(session, userId: userId) }
                            } label: {
                                if store.loadingMore {
                                    ProgressView()
                                } else {
                                    Label {
                                        Text(String(store.earliestYear - 1))
                                    } icon: {
                                        DBIcon("chevron.left", size: .caption)
                                    }
                                    .font(.caption.weight(.semibold))
                                }
                            }
                            .buttonStyle(.bordered)
                        }

                        RainbowYearGrid(cells: store.paddedCells, size: gridSize) { day in
                            selectedDay = day.summary
                        }
                    }
                    .padding()
                }
                .defaultScrollAnchor(.trailing)

                if store.perfectDaysThisYear > 0 {
                    Text("**\(store.perfectDaysThisYear)** perfect day\(store.perfectDaysThisYear == 1 ? "" : "s") this year ✨")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .padding(.horizontal)
                }

                Spacer(minLength: 0)
            }
            .navigationTitle(title)
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
                }
            }
            .themeBackground()
        }
        .task(id: userId) { await store.load(session, userId: userId) }
        .sheet(item: $selectedDay) { day in
            DayDetailSheet(day: day)
        }
        #if os(iOS)
        .presentationDetents([.medium, .large])
        #endif
    }
}

#if os(macOS)
/// The living rainbow on the macOS Dock — rendered at runtime, which iOS cannot
/// do (there, the living version is the widget; the app icon stays a stable
/// identity mark).
@MainActor
enum DockIcon {
    static func update(cells: [RainbowDay?]) {
        guard !cells.isEmpty else { return }
        let grid = RainbowYearGrid(cells: cells, size: .card)
            .environment(\.colorScheme, .dark)
            .padding(28)
            .background(
                LinearGradient(colors: [Color(hex: 0x223049), Color(hex: 0x161E2C)],
                               startPoint: .top, endPoint: .bottom),
                in: RoundedRectangle(cornerRadius: 56, style: .continuous))

        let renderer = ImageRenderer(content: grid)
        renderer.scale = 3
        if let image = renderer.nsImage {
            NSApp.applicationIconImage = image
        }
    }
}
#endif

/// One day's story, from a rainbow cell tap.
struct DayDetailSheet: View {
    let day: DaySummary

    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 16) {
                VStack(alignment: .leading, spacing: 4) {
                    Text(day.date.longDisplay)
                        .font(.title3.weight(.bold))
                    Text(statusLine)
                        .font(.subheadline)
                        .foregroundStyle(statusColor)
                }

                HStack(spacing: 12) {
                    tile("\(day.approvedChores + day.completedChores)/\(day.totalChores)", "done")
                    tile(day.earnedAmount.display, "earned", color: DB.gold(scheme))
                    if day.missedChores > 0 {
                        tile("\(day.missedChores)", "missed", color: DB.help(scheme))
                    }
                }

                Spacer()
            }
            .padding()
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
                }
            }
        }
        .themeBackground()
        #if os(iOS)
        .presentationDetents([.height(260)])
        #endif
    }

    private var statusLine: String {
        switch day.status {
        case "AllComplete": return "Perfect day ✨"
        case "PartialComplete": return "Partly done"
        case "NoneComplete": return "A rough one"
        default: return "No chores scheduled"
        }
    }

    private var statusColor: Color {
        switch day.status {
        case "AllComplete": return DB.success(scheme)
        case "PartialComplete": return DB.gold(scheme)
        case "NoneComplete": return DB.help(scheme)
        default: return .secondary
        }
    }

    private func tile(_ value: String, _ label: String, color: Color = .primary) -> some View {
        VStack(spacing: 3) {
            Text(value)
                .font(.headline.weight(.bold))
                .foregroundStyle(color)
            Text(label)
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
        .glassCard(padding: 12)
    }
}

/// "Good morning" / "Good afternoon" / "Good evening" — same rules as the
/// web dashboard.
enum Greeting {
    static var current: String {
        let hour = Calendar.current.component(.hour, from: Date())
        if hour < 12 { return "Good morning" }
        if hour < 17 { return "Good afternoon" }
        return "Good evening"
    }
}

#Preview("Rainbow grid — all three sizes") {
    let today = DayDate.todayLocal()
    let count = 52 * 7
    let sample: [RainbowDay?] = (0..<count).map { i in
        let date = today.addingDays(i - count + 4)
        if i >= count - 4 { return RainbowDay(future: date) }
        let h = (i * 2654435761) % 100
        let total = 3 + i % 3
        let done = h > 62 ? total : h > 34 ? max(1, total - 1) : h > 22 ? 1 : 0
        return RainbowDay(date: date, requiredDone: done, requiredTotal: total)
    }
    return ScrollView {
        VStack(alignment: .leading, spacing: 24) {
            RainbowYearGrid(cells: Array(sample.suffix(12 * 7)), size: .card)
            ScrollView(.horizontal) { RainbowYearGrid(cells: sample, size: .full) }
            ScrollView(.horizontal) { RainbowYearGrid(cells: sample, size: .wall) }
        }
        .padding()
    }
}
