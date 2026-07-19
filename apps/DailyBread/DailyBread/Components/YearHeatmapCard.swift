import SwiftUI
import DailyBreadKit

/// The year grid — the app's emotional high point, ported from the web
/// dashboard. One cell per day, colored by how the day went:
/// green = all done, gold = partial, dim red = missed, faint = nothing due.
/// Footer counts perfect days, like the original.
@MainActor
@Observable
final class YearHeatmapStore {
    var days: [DaySummary] = []
    var loaded = false

    func load(_ session: SessionStore, userId: String?) async {
        let today = DayDate.todayLocal()
        let jan1 = DayDate(year: today.year, month: 1, day: 1)
        if let range = try? await session.client.calendarRange(from: jan1, to: today, userId: userId) {
            days = range.days
        }
        loaded = true
    }

    var perfectDays: Int {
        days.filter { $0.status == "AllComplete" }.count
    }

    /// Days padded to full weeks (columns of 7, Sunday-first).
    var weeks: [[DaySummary?]] {
        guard let first = days.first else { return [] }
        let weekdayOffset = Calendar.current.component(.weekday, from: first.date.displayDate) - 1
        var cells: [DaySummary?] = Array(repeating: nil, count: weekdayOffset)
        cells.append(contentsOf: days.map { Optional($0) })
        while cells.count % 7 != 0 { cells.append(nil) }
        return stride(from: 0, to: cells.count, by: 7).map { start in
            Array(cells[start..<min(start + 7, cells.count)])
        }
    }
}

struct YearHeatmapCard: View {
    let title: String
    var userId: String?

    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = YearHeatmapStore()

    private let cellSize: CGFloat = 10
    private let cellGap: CGFloat = 2.5

    var body: some View {
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
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(alignment: .top, spacing: cellGap) {
                        ForEach(Array(store.weeks.enumerated()), id: \.offset) { _, week in
                            VStack(spacing: cellGap) {
                                ForEach(Array(week.enumerated()), id: \.offset) { _, day in
                                    RoundedRectangle(cornerRadius: 2.5, style: .continuous)
                                        .fill(color(for: day))
                                        .frame(width: cellSize, height: cellSize)
                                }
                            }
                        }
                    }
                }
                .defaultScrollAnchor(.trailing)

                HStack {
                    legend
                    Spacer()
                    if store.perfectDays > 0 {
                        Text("**\(store.perfectDays)** perfect day\(store.perfectDays == 1 ? "" : "s") this year ✨")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
            }
        }
        .glassCard()
        .task(id: userId) { await store.load(session, userId: userId) }
    }

    private var legend: some View {
        HStack(spacing: 4) {
            Text("less").font(.caption2).foregroundStyle(.tertiary)
            ForEach(["none", "NoneComplete", "PartialComplete", "AllComplete"], id: \.self) { key in
                RoundedRectangle(cornerRadius: 2, style: .continuous)
                    .fill(legendColor(key))
                    .frame(width: 8, height: 8)
            }
            Text("more").font(.caption2).foregroundStyle(.tertiary)
        }
    }

    private func color(for day: DaySummary?) -> Color {
        guard let day else { return .clear }
        return legendColor(day.status)
    }

    private func legendColor(_ status: String) -> Color {
        switch status {
        case "AllComplete": return DB.success(scheme)
        case "PartialComplete": return DB.gold(scheme).opacity(0.8)
        case "NoneComplete": return DB.help(scheme).opacity(0.45)
        default: return Color.primary.opacity(scheme == .dark ? 0.07 : 0.08)
        }
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
