import SwiftUI
import DailyBreadKit

/// A month at a glance: every day coloured by how it went (all done, partly
/// done, something missed, or still ahead), with a tap-through to that day's
/// numbers. Reads the same calendar range the charts use. `userId` is nil for
/// the signed-in kid's own month, or a child's id when a parent is looking.
@MainActor
@Observable
final class CalendarStore {
    var days: [String: DaySummary] = [:]
    var loading = false
    var errorMessage: String?

    func load(_ session: SessionStore, userId: String?, from: DayDate, to: DayDate) async {
        loading = days.isEmpty
        defer { loading = false }
        do {
            let range = try await session.client.calendarRange(from: from, to: to, userId: userId)
            var map: [String: DaySummary] = [:]
            for day in range.days { map[day.date.wireString] = day }
            days = map
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}

struct CalendarView: View {
    var userId: String?
    var title: String = "Calendar"

    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = CalendarStore()
    @State private var anchor = MonthMath.firstOfMonth(DayDate.todayLocal())
    @State private var selected: DayDate?

    private let columns = Array(repeating: GridItem(.flexible(), spacing: 6), count: 7)
    private let weekdayLabels = ["S", "M", "T", "W", "T", "F", "S"]

    var body: some View {
        ScrollView {
            VStack(spacing: 16) {
                monthHeader
                weekdayRow
                grid
                if let selected, let summary = store.days[selected.wireString] {
                    dayDetail(selected, summary)
                } else if let selected {
                    dayDetailEmpty(selected)
                }
                legend
                if let error = store.errorMessage {
                    Label(error, systemImage: "wifi.exclamationmark")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
            .padding()
        }
        .navigationTitle(title)
        .graphiteBackground()
        .task(id: anchor.wireString) { await reload() }
        .refreshOnForeground { await reload() }
    }

    // MARK: - Header / navigation

    private var monthHeader: some View {
        HStack {
            Button {
                step(-1)
            } label: {
                Image(systemName: "chevron.left")
                    .font(.body.weight(.semibold))
            }
            .buttonStyle(.plain)

            Spacer()
            Text(MonthMath.monthTitle(anchor))
                .font(.headline)
            Spacer()

            Button {
                step(1)
            } label: {
                Image(systemName: "chevron.right")
                    .font(.body.weight(.semibold))
                    .foregroundStyle(canGoForward ? Color.accentColor : Color.secondary.opacity(0.4))
            }
            .buttonStyle(.plain)
            .disabled(!canGoForward)
        }
        .glassCard(padding: 14)
    }

    private var weekdayRow: some View {
        HStack(spacing: 6) {
            ForEach(Array(weekdayLabels.enumerated()), id: \.offset) { _, label in
                Text(label)
                    .font(.caption2.weight(.bold))
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity)
            }
        }
    }

    private var grid: some View {
        LazyVGrid(columns: columns, spacing: 6) {
            ForEach(0..<MonthMath.leadingBlanks(anchor), id: \.self) { _ in
                Color.clear.frame(height: 44)
            }
            ForEach(MonthMath.days(in: anchor), id: \.wireString) { date in
                dayCell(date)
            }
        }
    }

    private func dayCell(_ date: DayDate) -> some View {
        let summary = store.days[date.wireString]
        let isFuture = MonthMath.isAfterToday(date)
        let isToday = date.wireString == DayDate.todayLocal().wireString
        let isSelected = date.wireString == selected?.wireString
        return Button {
            Haptics.tick()
            selected = (selected?.wireString == date.wireString) ? nil : date
        } label: {
            VStack(spacing: 4) {
                Text("\(date.day)")
                    .font(.subheadline.weight(isToday ? .bold : .regular))
                    .foregroundStyle(isFuture ? Color.secondary : Color.primary)
                Circle()
                    .fill(dotColor(summary, isFuture: isFuture))
                    .frame(width: 6, height: 6)
            }
            .frame(maxWidth: .infinity)
            .frame(height: 44)
            .background(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(isSelected ? Color.accentColor.opacity(0.16) : Color.secondary.opacity(0.06)))
            .overlay(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .strokeBorder(isToday ? Color.accentColor : Color.clear, lineWidth: 1.5))
        }
        .buttonStyle(.plain)
    }

    /// The status dot: green all-done, red something-missed, accent in-progress,
    /// faint when there was nothing (or the day hasn't happened yet).
    private func dotColor(_ summary: DaySummary?, isFuture: Bool) -> Color {
        guard let s = summary, s.totalChores > 0, !isFuture else {
            return Color.secondary.opacity(0.18)
        }
        if s.completedChores + s.approvedChores >= s.totalChores {
            return DB.success(scheme)
        }
        if s.missedChores > 0 {
            return DB.help(scheme)
        }
        return Color.accentColor
    }

    // MARK: - Day detail

    private func dayDetail(_ date: DayDate, _ s: DaySummary) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(date.longDisplay)
                .font(.subheadline.weight(.semibold))
            HStack(spacing: 18) {
                stat("\(s.completedChores + s.approvedChores)/\(s.totalChores)", "done", Color.accentColor)
                if s.missedChores > 0 {
                    stat("\(s.missedChores)", "missed", DB.help(scheme))
                }
                if !s.earnedAmount.isZero {
                    stat(s.earnedAmount.display, "earned", DB.gold(scheme))
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard()
    }

    private func dayDetailEmpty(_ date: DayDate) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(date.longDisplay)
                .font(.subheadline.weight(.semibold))
            Text(MonthMath.isAfterToday(date) ? "Still ahead." : "Nothing scheduled.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard()
    }

    private func stat(_ value: String, _ label: String, _ color: Color) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(value)
                .font(.title3.weight(.bold))
                .foregroundStyle(color)
            Text(label)
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
    }

    private var legend: some View {
        HStack(spacing: 14) {
            legendDot(DB.success(scheme), "All done")
            legendDot(Color.accentColor, "In progress")
            legendDot(DB.help(scheme), "Missed")
        }
        .font(.caption2)
        .foregroundStyle(.secondary)
        .frame(maxWidth: .infinity)
    }

    private func legendDot(_ color: Color, _ label: String) -> some View {
        HStack(spacing: 5) {
            Circle().fill(color).frame(width: 7, height: 7)
            Text(label)
        }
    }

    // MARK: - Loading / month stepping

    private var canGoForward: Bool {
        MonthMath.firstOfMonth(DayDate.todayLocal()).wireString != anchor.wireString
            && !MonthMath.isFutureMonth(anchor)
    }

    private func step(_ delta: Int) {
        let next = MonthMath.addingMonths(anchor, delta)
        if delta > 0 && MonthMath.isFutureMonth(next) { return }
        selected = nil
        anchor = next
    }

    private func reload() async {
        let from = anchor
        let monthEnd = MonthMath.lastOfMonth(anchor)
        let today = DayDate.todayLocal()
        let to = MonthMath.min(monthEnd, today)
        await store.load(session, userId: userId, from: from, to: to)
    }
}

/// Month arithmetic for the calendar grid — display-side only (the server owns
/// "today"; this never travels the wire). Sunday-first, matching the app.
enum MonthMath {
    private static var cal: Calendar { Calendar.current }

    private static func date(_ d: DayDate) -> Date {
        cal.date(from: DateComponents(year: d.year, month: d.month, day: d.day)) ?? Date()
    }

    private static func dayDate(_ date: Date) -> DayDate {
        let p = cal.dateComponents([.year, .month, .day], from: date)
        return DayDate(year: p.year ?? 2000, month: p.month ?? 1, day: p.day ?? 1)
    }

    static func firstOfMonth(_ d: DayDate) -> DayDate {
        DayDate(year: d.year, month: d.month, day: 1)
    }

    static func lastOfMonth(_ d: DayDate) -> DayDate {
        let start = date(firstOfMonth(d))
        let range = cal.range(of: .day, in: .month, for: start) ?? 1..<2
        return DayDate(year: d.year, month: d.month, day: range.count)
    }

    static func daysInMonth(_ d: DayDate) -> Int {
        let range = cal.range(of: .day, in: .month, for: date(firstOfMonth(d))) ?? 1..<2
        return range.count
    }

    static func days(in anchor: DayDate) -> [DayDate] {
        (1...daysInMonth(anchor)).map { DayDate(year: anchor.year, month: anchor.month, day: $0) }
    }

    /// How many empty cells before day 1 (Sunday = 0 … Saturday = 6).
    static func leadingBlanks(_ anchor: DayDate) -> Int {
        let weekday = cal.component(.weekday, from: date(firstOfMonth(anchor))) // 1 = Sunday
        return (weekday - 1 + 7) % 7
    }

    static func addingMonths(_ anchor: DayDate, _ delta: Int) -> DayDate {
        let shifted = cal.date(byAdding: .month, value: delta, to: date(firstOfMonth(anchor))) ?? date(anchor)
        return firstOfMonth(dayDate(shifted))
    }

    static func monthTitle(_ anchor: DayDate) -> String {
        let f = DateFormatter()
        f.calendar = cal
        f.dateFormat = "LLLL yyyy"
        return f.string(from: date(anchor))
    }

    static func isAfterToday(_ d: DayDate) -> Bool {
        date(d) > date(DayDate.todayLocal())
    }

    static func isFutureMonth(_ anchor: DayDate) -> Bool {
        let a = firstOfMonth(anchor)
        let today = firstOfMonth(DayDate.todayLocal())
        return date(a) > date(today)
    }

    static func min(_ a: DayDate, _ b: DayDate) -> DayDate {
        date(a) <= date(b) ? a : b
    }
}
