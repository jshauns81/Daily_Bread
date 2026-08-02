import Foundation
#if canImport(WidgetKit)
import WidgetKit
#endif

// §1.2a: the widget process never talks to the server. The app writes everything
// the widget family needs into the shared app-group container whenever it learns
// something (Today loads, a chore toggles, the year loads, the theme changes) and
// pokes WidgetKit to re-render. Each surface contributes only what it knows, so
// writes are merges, not replacements — the parent's Home (which loads the year
// but not Today) must not blank the ring the kid's Today wrote an hour ago.

/// Everything the §1.2a widget family renders. All fields the app may not have
/// learned yet are optional; the widget derives today's counts from `days` when
/// the explicit ones are missing (a day summary carries its own counts).
public struct WidgetSnapshot: Codable, Sendable {
    public var childName: String?
    /// Real loaded day summaries, oldest first, through today.
    public var days: [DaySummary]
    public var todayDone: Int?
    public var todayTotal: Int?
    /// Display strings — the app owns formatting; the widget just shows them.
    public var todayEarned: String?
    public var balance: String?
    public var streak: Int?
    /// DBTheme rawValue — the widget renders true theme surfaces.
    public var theme: String
    /// The local day this was written. A snapshot from yesterday still shows the
    /// grid, but its today-counts are stale and the widget must not present them
    /// as this morning's ring.
    public var generatedOn: DayDate

    /// Sunday-first cells for the grid, exactly as the app's cards build them.
    public var cells: [RainbowDay?] { RainbowCells.padded(days) }

    public var perfectDaysThisYear: Int {
        let year = DayDate.todayLocal().year
        return days.filter { $0.date.year == year && RainbowDay($0).isComplete }.count
    }

    /// Today's counts, preferring what Today wrote, falling back to the day
    /// summary. Nil when the snapshot predates today — an honest "don't know".
    public var todayProgress: (done: Int, total: Int)? {
        guard generatedOn == DayDate.todayLocal() else { return nil }
        if let done = todayDone, let total = todayTotal { return (done, total) }
        guard let today = days.last(where: { $0.date == generatedOn }) else { return nil }
        let day = RainbowDay(today)
        return (day.requiredDone, day.requiredTotal)
    }
}

/// App-side writer / widget-side reader for the snapshot file. Deliberately not
/// actor-isolated: it's atomic file IO + UserDefaults, and the widget's timeline
/// provider reads it off the main thread.
public enum WidgetBridge {
    public static let appGroupID = "group.org.dailybread.shared"

    private static var fileURL: URL? {
        FileManager.default
            .containerURL(forSecurityApplicationGroupIdentifier: appGroupID)?
            .appendingPathComponent("widget-snapshot.json")
    }

    public static func read() -> WidgetSnapshot? {
        guard let url = fileURL, let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(WidgetSnapshot.self, from: data)
    }

    /// The year loaded (Today's card, the parent's Home) — contribute the days.
    public static func mergeDays(_ days: [DaySummary], childName: String?) {
        guard !days.isEmpty else { return }
        var s = base()
        s.days = days
        if let childName { s.childName = childName }
        write(s)
    }

    /// Today loaded or a chore toggled — contribute the day's live numbers.
    public static func mergeToday(done: Int, total: Int, earned: String?,
                                  balance: String?, streak: Int?, childName: String?) {
        var s = base()
        s.todayDone = done
        s.todayTotal = total
        s.todayEarned = earned
        if let balance { s.balance = balance }
        if let streak { s.streak = streak }
        if let childName { s.childName = childName }
        // Keep the grid honest without waiting for the year to reload: patch
        // today's cell so the ring and the rainbow never disagree about today.
        if let i = s.days.firstIndex(where: { $0.date == DayDate.todayLocal() }) {
            s.days[i].completedChores = done
            s.days[i].approvedChores = 0
            s.days[i].totalChores = total
            s.days[i].pendingChores = total - done
            s.days[i].status = total > 0 && done >= total ? "AllComplete"
                             : done > 0 ? "PartialComplete" : s.days[i].status
        }
        write(s)
    }

    /// Theme picked in Settings — restamp so the widgets re-skin instantly.
    public static func themeChanged() {
        guard var s = read() else { return }
        s.theme = ThemeStore.current.rawValue
        write(s)
    }

    /// Rolls a stale snapshot forward to today: yesterday's today-fields must
    /// not survive midnight as if they were this morning's.
    private static func base() -> WidgetSnapshot {
        let today = DayDate.todayLocal()
        guard var s = read() else {
            return WidgetSnapshot(days: [], theme: ThemeStore.current.rawValue, generatedOn: today)
        }
        if s.generatedOn != today {
            s.todayDone = nil
            s.todayTotal = nil
            s.todayEarned = nil
            s.generatedOn = today
        }
        s.theme = ThemeStore.current.rawValue
        return s
    }

    private static func write(_ snapshot: WidgetSnapshot) {
        guard let url = fileURL, let data = try? JSONEncoder().encode(snapshot) else { return }
        try? data.write(to: url, options: .atomic)
        reloadWidgets()
    }

    private static func reloadWidgets() {
        #if os(iOS) && canImport(WidgetKit)
        WidgetCenter.shared.reloadAllTimelines()
        #endif
    }
}
