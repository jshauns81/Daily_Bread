import SwiftUI
import DailyBreadKit

// The rainbow year — IMPLEMENTATION.md §1.2. Hue is the REAL day-of-year, never
// position within the window, so a given day is the same colour on every surface
// (card, full year, wall, widget — and across the year boundary, which makes
// multi-year scroll one unbroken wheel). Intensity is the required-chore ratio on
// a LINEAR ramp from a 34% saturation floor (the `forgiving` curve — steeper ones
// read as punishment on a wall of 365). A bad day is a drained colour, never a
// grey hole. Optionals only ever ADD bloom — dimming a square for declining an
// optional would punish a kid for taking a choice he was deliberately given.

/// One day-cell's payload. The backend has no optional-chore concept yet, so the
/// optional counts are always 0 today — the fields exist so the invariant
/// "optionals only raise bloomLevel, never completeness" is explicit in the model
/// and can't drift when optionals arrive.
struct RainbowDay {
    let date: DayDate
    let requiredDone: Int
    let requiredTotal: Int
    let optionalDone: Int
    let optionalTotal: Int
    let isFuture: Bool
    /// Backing summary for the day-detail sheet (nil for synthesized future days).
    let summary: DaySummary?

    /// Completeness never depends on optionals.
    var isComplete: Bool { !isFuture && requiredTotal > 0 && requiredDone >= requiredTotal }
    /// 0 = no bloom. A perfect day blooms; optionals only ever raise it.
    var bloomLevel: Int { isComplete ? 1 + optionalDone : 0 }
    var ratio: Double {
        requiredTotal > 0 ? min(1, Double(requiredDone) / Double(requiredTotal)) : 0
    }
    /// Future days and rest days (nothing scheduled) render as faint outlines.
    var isOutline: Bool { isFuture || requiredTotal == 0 }

    init(_ s: DaySummary) {
        date = s.date
        // completedChores excludes approved; skipped counts toward "done" exactly
        // like the server's AllComplete rule. Skipped is derivable, not on the wire.
        let skipped = max(0, s.totalChores - s.completedChores - s.approvedChores
                             - s.missedChores - s.pendingChores)
        requiredDone = s.completedChores + s.approvedChores + skipped
        requiredTotal = s.totalChores
        optionalDone = 0
        optionalTotal = 0
        isFuture = s.status == "Future"
        summary = s
    }

    init(future date: DayDate) {
        self.init(date: date, requiredDone: 0, requiredTotal: 0, isFuture: true)
    }

    /// Previews and the widget/dock renderers, where no wire summary exists.
    init(date: DayDate, requiredDone: Int, requiredTotal: Int,
         optionalDone: Int = 0, optionalTotal: Int = 0, isFuture: Bool = false) {
        self.date = date
        self.requiredDone = requiredDone
        self.requiredTotal = requiredTotal
        self.optionalDone = optionalDone
        self.optionalTotal = optionalTotal
        self.isFuture = isFuture
        summary = nil
    }
}

extension Color {
    /// The reference implementation speaks CSS `hsl()`; SwiftUI speaks HSB.
    init(hslHue hue: Double, saturation s: Double, lightness l: Double) {
        let b = l + s * min(l, 1 - l)
        let sb = b == 0 ? 0 : 2 * (1 - l / b)
        self.init(hue: hue / 360, saturation: sb, brightness: b)
    }
}

enum RainbowMath {
    /// Hue from the real day-of-year: day 1 ≈ 0°, day 365 wraps seamlessly into
    /// the next January. Never position-within-window.
    static func hue(for date: DayDate) -> Double {
        let doy = Calendar.current.ordinality(of: .day, in: .year, for: date.displayDate) ?? 1
        return (Double(doy % 365) / 365) * 360
    }

    /// Linear 34→86% saturation; lightness ramps opposite by scheme. Opaque by
    /// construction — mixing toward a translucent fill dropped drained days to
    /// ~15% saturation (the grey hole the spec forbids) and made the same day a
    /// different colour on every surface.
    static func fill(hue: Double, ratio: Double, dark: Bool) -> Color {
        let sat = 34 + ratio * 52
        let light = dark ? 16 + ratio * 40 : 84 - ratio * 40
        return Color(hslHue: hue, saturation: sat / 100, lightness: light / 100)
    }

    static func bloomColor(hue: Double) -> Color {
        Color(hslHue: hue, saturation: 0.9, lightness: 0.55).opacity(0.7)
    }
}

/// One renderer for every surface; only the cell edge and gap differ.
/// A day cell is square, always — the grid is sized from the cell edge,
/// never stretched to fill the available box.
struct RainbowYearGrid: View {
    enum Size {
        /// Last 12 weeks on Today — the default the kid sees daily.
        case card
        /// Scrollable year, tap a day to open it.
        case full
        /// Whole year + multi-year scroll on iPad / macOS.
        case wall

        var cell: CGFloat {
            switch self {
            case .card: return 18
            case .full: return 11
            case .wall: return 14
            }
        }

        var gap: CGFloat {
            switch self {
            case .card: return 3
            case .full: return 2.5
            case .wall: return 3
            }
        }
    }

    /// Columns of 7, Sunday-first; nil = leading pad before the loaded range.
    let cells: [RainbowDay?]
    let size: Size
    var onTap: ((RainbowDay) -> Void)? = nil

    @Environment(\.colorScheme) private var scheme

    private var columns: Int { (cells.count + 6) / 7 }
    private var step: CGFloat { size.cell + size.gap }

    var body: some View {
        Canvas { context, _ in
            let edge = size.cell
            let radius = 2.5
            let dark = scheme == .dark

            for (index, day) in cells.enumerated() {
                guard let day else { continue }
                let origin = CGPoint(x: CGFloat(index / 7) * step,
                                     y: CGFloat(index % 7) * step)
                let rect = CGRect(origin: origin, size: CGSize(width: edge, height: edge))
                let path = Path(roundedRect: rect, cornerRadius: radius, style: .continuous)

                if day.isOutline {
                    context.stroke(
                        Path(roundedRect: rect.insetBy(dx: 0.5, dy: 0.5),
                             cornerRadius: radius, style: .continuous),
                        with: .color(.primary.opacity(0.09)), lineWidth: 1)
                    continue
                }

                let hue = RainbowMath.hue(for: day.date)
                let fill = RainbowMath.fill(hue: hue, ratio: day.ratio, dark: dark)

                if day.bloomLevel > 0 {
                    // The bloom scales with the cell — a fixed radius smears into
                    // neighbouring days at year-wall density.
                    var glow = context
                    glow.addFilter(.shadow(color: RainbowMath.bloomColor(hue: hue),
                                           radius: edge * 0.55))
                    glow.fill(path, with: .color(fill))
                } else {
                    context.fill(path, with: .color(fill))
                }
            }
        }
        .frame(width: max(0, CGFloat(columns) * step - size.gap),
               height: 7 * step - size.gap)
        .onTapGesture { location in
            guard let onTap else { return }
            let index = Int(location.x / step) * 7 + Int(location.y / step)
            guard cells.indices.contains(index), let day = cells[index],
                  !day.isFuture, day.summary != nil else { return }
            Haptics.tick()
            onTap(day)
        }
    }
}

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
    var paddedCells: [RainbowDay?] {
        guard let first = days.first else { return [] }
        let weekdayOffset = Calendar.current.component(.weekday, from: first.date.displayDate) - 1
        var cells: [RainbowDay?] = Array(repeating: nil, count: weekdayOffset)
        cells.append(contentsOf: days.map { RainbowDay($0) })

        var next = (days.last?.date ?? DayDate.todayLocal()).addingDays(1)
        while cells.count % 7 != 0 {
            cells.append(RainbowDay(future: next))
            next = next.addingDays(1)
        }
        return cells
    }

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
                    RainbowYearGrid(cells: store.cells(lastWeeks: 12), size: .card)

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
