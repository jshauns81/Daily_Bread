import WidgetKit
import SwiftUI
import DailyBreadKit

// §1.2a — the rainbow year outside the app. The widget never talks to the
// server: the app writes a WidgetBridge snapshot on every load/toggle and this
// extension just renders it. The app reloads timelines whenever data changes;
// our own timeline only rolls the presentation over at midnight so yesterday's
// ring can't masquerade as this morning's.
//
// Deliberately deferred (additive per spec, not a replacement): the interactive
// Medium "Today" kind — checking chores from the widget needs App Intents plus
// shared-Keychain auth in this process, and ships after the family is proven.

@main
struct DailyBreadWidgetsBundle: WidgetBundle {
    var body: some Widget {
        RainbowYearWidget()
    }
}

struct RainbowYearWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "RainbowYear", provider: RainbowProvider()) { entry in
            RainbowWidgetRoot(entry: entry)
        }
        .configurationDisplayName("Rainbow Year")
        .description("Every day is a colour. Perfect days glow.")
        .supportedFamilies([.systemSmall, .systemMedium, .systemLarge,
                            .accessoryCircular, .accessoryRectangular, .accessoryInline])
    }
}

struct RainbowEntry: TimelineEntry {
    let date: Date
    /// nil = the app has never written a snapshot (not signed in yet).
    let data: RainbowWidgetData?
}

struct RainbowProvider: TimelineProvider {
    func placeholder(in context: Context) -> RainbowEntry {
        RainbowEntry(date: Date(), data: .sample)
    }

    func getSnapshot(in context: Context, completion: @escaping (RainbowEntry) -> Void) {
        completion(RainbowEntry(date: Date(),
                                data: RainbowWidgetData.current ?? .sample))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<RainbowEntry>) -> Void) {
        let entry = RainbowEntry(date: Date(), data: RainbowWidgetData.current)
        let midnight = Calendar.current.startOfDay(for: Date()).addingTimeInterval(24 * 3600)
        completion(Timeline(entries: [entry], policy: .after(midnight)))
    }
}

/// What the views draw — the snapshot resolved into render-ready pieces.
struct RainbowWidgetData {
    var cells: [RainbowDay?]
    /// nil when the snapshot predates today — the ring shows "–", never a lie.
    var todayDone: Int?
    var todayTotal: Int?
    var todayEarned: String?
    var balance: String?
    var streak: Int?
    var perfectDays: Int
    var theme: DBTheme
    var childName: String?

    var todayProgress: Double? {
        guard let done = todayDone, let total = todayTotal, total > 0 else { return nil }
        return Double(done) / Double(total)
    }

    static var current: RainbowWidgetData? {
        guard let s = WidgetBridge.read() else { return nil }
        let progress = s.todayProgress
        let fresh = s.generatedOn == DayDate.todayLocal()
        return RainbowWidgetData(
            cells: s.cells,
            todayDone: progress?.done,
            todayTotal: progress?.total,
            todayEarned: fresh ? s.todayEarned : nil,
            balance: s.balance,
            streak: s.streak,
            perfectDays: s.perfectDaysThisYear,
            theme: DBTheme(rawValue: s.theme) ?? .sunroom,
            childName: s.childName)
    }

    /// The gallery/placeholder rainbow — same deterministic pattern as the app's
    /// preview, so the gallery sells the real thing.
    static var sample: RainbowWidgetData {
        let today = DayDate.todayLocal()
        let count = 52 * 7
        let cells: [RainbowDay?] = (0..<count).map { i in
            let date = today.addingDays(i - count + 4)
            if i >= count - 4 { return RainbowDay(future: date) }
            let h = (i * 2654435761) % 100
            let total = 3 + i % 3
            let done = h > 62 ? total : h > 34 ? max(1, total - 1) : h > 22 ? 1 : 0
            return RainbowDay(date: date, requiredDone: done, requiredTotal: total)
        }
        return RainbowWidgetData(
            cells: cells, todayDone: 4, todayTotal: 6, todayEarned: "$4.50",
            balance: "$62.40", streak: 9, perfectDays: 128, theme: .sunroom,
            childName: nil)
    }
}
