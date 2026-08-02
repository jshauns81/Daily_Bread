import SwiftUI

// The rainbow year — IMPLEMENTATION.md §1.2. Hue is the REAL day-of-year, never
// position within the window, so a given day is the same colour on every surface
// (card, full year, wall, widget, dock — and across the year boundary, which makes
// multi-year scroll one unbroken wheel). Intensity is the required-chore ratio on
// a LINEAR ramp from a 34% saturation floor (the `forgiving` curve — steeper ones
// read as punishment on a wall of 365). A bad day is a drained colour, never a
// grey hole. Optionals only ever ADD bloom — dimming a square for declining an
// optional would punish a kid for taking a choice he was deliberately given.
//
// Lives in the kit because §1.2a puts this renderer on four surfaces across two
// processes: the app's cards, the macOS dock, and the widget extension.

/// One day-cell's payload. The backend has no optional-chore concept yet, so the
/// optional counts are always 0 today — the fields exist so the invariant
/// "optionals only raise bloomLevel, never completeness" is explicit in the model
/// and can't drift when optionals arrive.
public struct RainbowDay: Sendable {
    public let date: DayDate
    public let requiredDone: Int
    public let requiredTotal: Int
    public let optionalDone: Int
    public let optionalTotal: Int
    public let isFuture: Bool
    /// Backing summary for the day-detail sheet (nil for synthesized future days).
    public let summary: DaySummary?

    /// Completeness never depends on optionals.
    public var isComplete: Bool { !isFuture && requiredTotal > 0 && requiredDone >= requiredTotal }
    /// 0 = no bloom. A perfect day blooms; optionals only ever raise it.
    public var bloomLevel: Int { isComplete ? 1 + optionalDone : 0 }
    public var ratio: Double {
        requiredTotal > 0 ? min(1, Double(requiredDone) / Double(requiredTotal)) : 0
    }
    /// Future days and rest days (nothing scheduled) render as faint outlines.
    public var isOutline: Bool { isFuture || requiredTotal == 0 }

    public init(_ s: DaySummary) {
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

    public init(future date: DayDate) {
        self.init(date: date, requiredDone: 0, requiredTotal: 0, isFuture: true)
    }

    /// Previews and the widget/dock renderers, where no wire summary exists.
    public init(date: DayDate, requiredDone: Int, requiredTotal: Int,
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

public extension Color {
    /// The reference implementation speaks CSS `hsl()`; SwiftUI speaks HSB.
    init(hslHue hue: Double, saturation s: Double, lightness l: Double) {
        let b = l + s * min(l, 1 - l)
        let sb = b == 0 ? 0 : 2 * (1 - l / b)
        self.init(hue: hue / 360, saturation: sb, brightness: b)
    }
}

public enum RainbowMath {
    /// Hue from the real day-of-year: day 1 ≈ 0°, day 365 wraps seamlessly into
    /// the next January. Never position-within-window.
    public static func hue(for date: DayDate) -> Double {
        let doy = Calendar.current.ordinality(of: .day, in: .year, for: date.displayDate) ?? 1
        return (Double(doy % 365) / 365) * 360
    }

    /// Linear 34→86% saturation; lightness ramps opposite by scheme. Opaque by
    /// construction — mixing toward a translucent fill dropped drained days to
    /// ~15% saturation (the grey hole the spec forbids) and made the same day a
    /// different colour on every surface.
    public static func fill(hue: Double, ratio: Double, dark: Bool) -> Color {
        let sat = 34 + ratio * 52
        let light = dark ? 16 + ratio * 40 : 84 - ratio * 40
        return Color(hslHue: hue, saturation: sat / 100, lightness: light / 100)
    }

    public static func bloomColor(hue: Double) -> Color {
        Color(hslHue: hue, saturation: 0.9, lightness: 0.55).opacity(0.7)
    }
}

public enum RainbowCells {
    /// Sunday-first columns of 7: nil-padded before the range, real future days
    /// (as outlines) through the end of the current week. The result is always a
    /// whole number of weeks, so any `suffix(weeks * 7)` keeps column alignment.
    public static func padded(_ days: [DaySummary]) -> [RainbowDay?] {
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
}

/// One renderer for every surface; only the cell edge, gap and corner differ.
/// A day cell is square, always — the grid is sized from the cell edge,
/// never stretched to fill the available box.
public struct RainbowYearGrid: View {
    public enum Size {
        /// Last 12 weeks on Today — the default the kid sees daily.
        case card
        /// Scrollable year, tap a day to open it.
        case full
        /// Whole year + multi-year scroll on iPad / macOS.
        case wall
        /// §1.2a widget family — sizes from ui_kits/widgets (balanced mode).
        case widgetSmall
        case widgetMedium
        /// The large widget's half-year bands.
        case widgetBand
        /// StandBy — full colour, dark, densest legible cell.
        case standby

        public var cell: CGFloat {
            switch self {
            case .card: return 18
            case .full: return 11
            case .wall: return 14
            case .widgetSmall: return 12
            case .widgetMedium: return 15
            case .widgetBand: return 10.5
            case .standby: return 8.5
            }
        }

        public var gap: CGFloat {
            switch self {
            case .card: return 3
            case .full: return 2.5
            case .wall: return 3
            case .widgetSmall, .widgetMedium: return 2
            case .widgetBand, .standby: return 1.5
            }
        }

        /// The app surfaces keep their shipped 2.5; widget corners come from the
        /// reference set (cell × 0.28, with the band/StandBy overrides).
        public var radius: CGFloat {
            switch self {
            case .card, .full, .wall: return 2.5
            case .widgetSmall: return 3.4
            case .widgetMedium: return 4.2
            case .widgetBand: return 1
            case .standby: return 1.5
            }
        }
    }

    /// Columns of 7, Sunday-first; nil = leading pad before the loaded range.
    let cells: [RainbowDay?]
    let size: Size
    var onTap: ((RainbowDay) -> Void)?

    @Environment(\.colorScheme) private var scheme

    public init(cells: [RainbowDay?], size: Size, onTap: ((RainbowDay) -> Void)? = nil) {
        self.cells = cells
        self.size = size
        self.onTap = onTap
    }

    private var columns: Int { (cells.count + 6) / 7 }
    private var step: CGFloat { size.cell + size.gap }

    public var body: some View {
        Canvas { context, _ in
            let edge = size.cell
            let radius = size.radius
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
