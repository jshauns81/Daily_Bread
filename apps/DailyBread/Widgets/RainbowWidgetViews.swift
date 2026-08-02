import WidgetKit
import SwiftUI
import DailyBreadKit

// The §1.2a family, from ui_kits/widgets (balanced reading, forgiving ramp —
// both decided). Tiles are the theme's real card surface; grids are the kit's
// one renderer at widget densities. Cells stay square at every size: grids fit
// by dropping weeks, never by stretching a cell.

struct RainbowWidgetRoot: View {
    @Environment(\.widgetFamily) private var family
    @Environment(\.showsWidgetContainerBackground) private var showsBackground
    let entry: RainbowEntry

    var body: some View {
        switch family {
        // Lock Screen — monochrome by platform decree; the rainbow cannot
        // appear here, so these speak in counts and pips.
        case .accessoryCircular:
            AccessoryCircular(data: entry.data)
                .containerBackground(for: .widget) { Color.clear }
        case .accessoryRectangular:
            AccessoryRectangular(data: entry.data)
                .containerBackground(for: .widget) { Color.clear }
        case .accessoryInline:
            AccessoryInline(data: entry.data)
                .containerBackground(for: .widget) { Color.clear }
        default:
            if let data = entry.data {
                systemTile(data)
                    .environment(\.colorScheme, standby ? .dark : (data.theme.isDark ? .dark : .light))
                    .containerBackground(for: .widget) {
                        // StandBy strips this anyway; black keeps the removal seamless.
                        standby ? Color.black : data.theme.cardColor
                    }
            } else {
                EmptyTile()
                    .containerBackground(for: .widget) { DBTheme.sunroom.cardColor }
            }
        }
    }

    /// StandBy is systemSmall with the container background removed — the one
    /// non-accessory surface that isn't a card (§1.2a: full colour, 26 weeks).
    private var standby: Bool { family == .systemSmall && !showsBackground }

    @ViewBuilder
    private func systemTile(_ data: RainbowWidgetData) -> some View {
        switch family {
        case .systemSmall: standby ? AnyView(StandByTile(data: data)) : AnyView(SmallTile(data: data))
        case .systemMedium: AnyView(MediumYearTile(data: data))
        default: AnyView(LargeYearTile(data: data))
        }
    }
}

// MARK: - Home screen tiles

/// Small — last 4 weeks + today's ring.
struct SmallTile: View {
    let data: RainbowWidgetData
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Eyebrow("TODAY")
                Spacer(minLength: 0)
                if let earned = data.todayEarned {
                    Text(earned)
                        .font(.system(size: 12, weight: .heavy))
                        .foregroundStyle(DB.gold(scheme))
                }
            }
            Spacer(minLength: 0)
            HStack(spacing: 10) {
                WidgetRing(progress: data.todayProgress,
                           label: ringLabel,
                           size: 48, stroke: 6,
                           accent: data.theme.accent())
                FittedGrid(cells: data.cells, size: .widgetSmall, targetWeeks: 4)
            }
            Spacer(minLength: 0)
        }
    }

    private var ringLabel: String {
        guard let d = data.todayDone, let t = data.todayTotal else { return "–" }
        return "\(d)/\(t)"
    }
}

/// Medium — last 12 weeks, matching the Today card, with the day's numbers.
struct MediumYearTile: View {
    let data: RainbowWidgetData
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        HStack(spacing: 14) {
            FittedGrid(cells: data.cells, size: .widgetMedium, targetWeeks: 12)
            VStack(alignment: .leading, spacing: 3) {
                Eyebrow("LAST 12 WEEKS")
                if let streak = data.streak, streak > 1 {
                    Text("\(streak) day streak")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(.secondary)
                }
                Spacer(minLength: 0)
                if let balance = data.balance {
                    Text(balance)
                        .font(.system(size: 19, weight: .heavy))
                        .foregroundStyle(DB.gold(scheme))
                }
                Spacer(minLength: 0)
                HStack(spacing: 8) {
                    WidgetRing(progress: data.todayProgress, label: nil,
                               size: 26, stroke: 4, accent: data.theme.accent())
                    Text(countLine)
                        .font(.system(size: 11.5, weight: .semibold))
                        .foregroundStyle(.secondary)
                }
            }
            .frame(width: 96, alignment: .leading)
        }
    }

    private var countLine: String {
        guard let d = data.todayDone, let t = data.todayTotal else { return "today" }
        return "\(d) of \(t)"
    }
}

/// Large — the full year as two half-year bands (double the cell of a single
/// 52-week strip; that doubling is what makes a streak legible).
struct LargeYearTile: View {
    let data: RainbowWidgetData
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 2) {
                    Eyebrow("THIS YEAR")
                    Text("\(data.perfectDays) day\(data.perfectDays == 1 ? "" : "s")")
                        .font(.system(size: 24, weight: .heavy))
                        .kerning(-0.4)
                }
                Spacer(minLength: 0)
                WidgetRing(progress: data.todayProgress, label: ringLabel,
                           size: 54, stroke: 5, accent: data.theme.accent())
            }
            Spacer(minLength: 0)
            BandedGrid(cells: data.cells)
            Spacer(minLength: 0)
            HStack(alignment: .lastTextBaseline) {
                VStack(alignment: .leading, spacing: 2) {
                    Eyebrow("BALANCE")
                    Text(data.balance ?? "—")
                        .font(.system(size: 20, weight: .heavy))
                        .foregroundStyle(DB.gold(scheme))
                }
                Spacer(minLength: 0)
                VStack(alignment: .trailing, spacing: 2) {
                    Eyebrow("STREAK")
                    Text("\(data.streak ?? 0) day\((data.streak ?? 0) == 1 ? "" : "s")")
                        .font(.system(size: 20, weight: .heavy))
                }
            }
        }
    }

    private var ringLabel: String {
        guard let d = data.todayDone, let t = data.todayTotal else { return "–" }
        return "\(d)/\(t)"
    }
}

/// StandBy — full colour on black, the densest legible cell (§1.2a: 26 weeks).
struct StandByTile: View {
    let data: RainbowWidgetData

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline) {
                Eyebrow("LAST 26 WEEKS")
                Spacer(minLength: 0)
                if let streak = data.streak, streak > 1 {
                    Text("\(streak) day streak")
                        .font(.system(size: 11, weight: .heavy))
                        .foregroundStyle(DB.gold(.dark))
                }
            }
            FittedGrid(cells: data.cells, size: .standby, targetWeeks: 26)
        }
    }
}

/// No snapshot yet — the app has never run signed-in on this device.
struct EmptyTile: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Eyebrow("DAILY BREAD")
            Text("Open the app to light up your year.")
                .font(.footnote.weight(.medium))
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
    }
}

// MARK: - Lock Screen accessories (system renders these monochrome)

struct AccessoryCircular: View {
    let data: RainbowWidgetData?

    var body: some View {
        Gauge(value: data?.todayProgress ?? 0) {
            Text("DB")
        } currentValueLabel: {
            Text(data?.todayDone.map(String.init) ?? "–")
                .font(.system(size: 15, weight: .heavy))
        }
        .gaugeStyle(.accessoryCircularCapacity)
    }
}

struct AccessoryRectangular: View {
    let data: RainbowWidgetData?

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text("DAILY BREAD")
                .font(.system(size: 10, weight: .bold))
                .kerning(0.8)
                .opacity(0.7)
            if let data, let done = data.todayDone, let total = data.todayTotal, total > 0 {
                Text("\(done) of \(total) done")
                    .font(.system(size: 16, weight: .heavy))
                HStack(spacing: 3) {
                    ForEach(0..<min(total, 12), id: \.self) { i in
                        Capsule()
                            .fill(i < done ? AnyShapeStyle(.primary)
                                           : AnyShapeStyle(.primary.opacity(0.3)))
                            .frame(height: 4)
                    }
                }
                .padding(.top, 3)
            } else {
                Text("Open to update")
                    .font(.system(size: 16, weight: .semibold))
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

struct AccessoryInline: View {
    let data: RainbowWidgetData?

    var body: some View {
        if let data, let done = data.todayDone, let total = data.todayTotal {
            Text("\(done) of \(total) chores")
        } else {
            Text("Daily Bread")
        }
    }
}

// MARK: - Shared pieces

/// The card eyebrow, exactly as the app's cards set it.
struct Eyebrow: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text)
            .font(.caption.weight(.bold))
            .foregroundStyle(.secondary)
            .kerning(0.8)
            .lineLimit(1)
            .minimumScaleFactor(0.8)
    }
}

/// Today's ring. nil progress renders the empty track — an honest "don't know".
struct WidgetRing: View {
    var progress: Double?
    var label: String?
    var size: CGFloat
    var stroke: CGFloat
    var accent: Color

    var body: some View {
        ZStack {
            Circle().stroke(.quaternary, lineWidth: stroke)
            if let progress {
                Circle()
                    .trim(from: 0, to: min(1, progress))
                    .stroke(accent, style: StrokeStyle(lineWidth: stroke, lineCap: .round))
                    .rotationEffect(.degrees(-90))
            }
            if let label {
                Text(label)
                    .font(.system(size: size * 0.28, weight: .bold))
                    .lineLimit(1)
                    .minimumScaleFactor(0.6)
            }
        }
        .frame(width: size, height: size)
    }
}

/// Fits as many whole weeks as the space allows, up to the target — cells stay
/// square, extra history is dropped from the leading edge, never squeezed.
struct FittedGrid: View {
    let cells: [RainbowDay?]
    let size: RainbowYearGrid.Size
    let targetWeeks: Int

    var body: some View {
        GeometryReader { geo in
            let step = size.cell + size.gap
            let fit = max(1, min(targetWeeks, Int((geo.size.width + size.gap) / step)))
            RainbowYearGrid(cells: Array(cells.suffix(fit * 7)), size: size)
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        }
        .frame(height: 7 * (size.cell + size.gap) - size.gap)
    }
}

/// The large widget's two half-year bands, sharing one fitted width so the
/// bands stay column-aligned.
struct BandedGrid: View {
    let cells: [RainbowDay?]

    var body: some View {
        GeometryReader { geo in
            let size = RainbowYearGrid.Size.widgetBand
            let step = size.cell + size.gap
            let weeks = max(1, min(26, Int((geo.size.width + size.gap) / step)))
            let recent = Array(cells.suffix(weeks * 7))
            let earlier = Array(cells.suffix(2 * weeks * 7).dropLast(weeks * 7))
            VStack(alignment: .leading, spacing: 9) {
                RainbowYearGrid(cells: earlier, size: size)
                RainbowYearGrid(cells: recent, size: size)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
        }
        .frame(height: 2 * (7 * (RainbowYearGrid.Size.widgetBand.cell + RainbowYearGrid.Size.widgetBand.gap) - RainbowYearGrid.Size.widgetBand.gap) + 9)
    }
}

// MARK: - Previews

#Preview("Small", as: .systemSmall) {
    RainbowYearWidget()
} timeline: {
    RainbowEntry(date: .now, data: .sample)
}

#Preview("Medium", as: .systemMedium) {
    RainbowYearWidget()
} timeline: {
    RainbowEntry(date: .now, data: .sample)
}

#Preview("Large", as: .systemLarge) {
    RainbowYearWidget()
} timeline: {
    RainbowEntry(date: .now, data: .sample)
}

#Preview("Lock rectangular", as: .accessoryRectangular) {
    RainbowYearWidget()
} timeline: {
    RainbowEntry(date: .now, data: .sample)
}
