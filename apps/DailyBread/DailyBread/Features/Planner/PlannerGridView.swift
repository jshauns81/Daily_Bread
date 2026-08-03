import SwiftUI
import DailyBreadKit

/// The weekly chore grid — chores down the side, the seven days across the top,
/// a tap-to-toggle cell at each crossing. The schedule at a glance, and editable
/// in place: tapping a cell adds or removes that day from the chore's week.
/// A WeeklyFrequency chore (an "N× a week" goal with no fixed days) shows its
/// target across the row instead of togglable cells. Tapping a chore's name
/// opens its full editor.
///
/// On Everyone, the same chore is assigned to several children, so the grid
/// groups by chore name: the name is a heading printed once, and each child is
/// a single-line row beneath it. Flat before, that read as four identical
/// "Empty Dishwasher" rows differing only in the quietest text on screen.
struct PlannerGridView: View {
    let chores: [PlannerChore]
    /// On Everyone, say whose chore each row is — and, because that is exactly
    /// when a chore repeats per child, the trigger for grouped layout.
    var showAssignee: Bool
    var onToggle: (PlannerChore, String) -> Void
    /// One commit for a whole dragged week — see `PlannerStore.setDays`.
    var onSetDays: (PlannerChore, [String]) -> Void
    var onEdit: (PlannerChore) -> Void

    @Environment(\.colorScheme) private var scheme

    // Drag-to-paint state. Held here rather than per-row because only one row
    // can be painted at a time, and the row views are rebuilt as it changes.
    @State private var paintingChoreId: Int?
    @State private var paintOn = true
    @State private var paintDays: Set<String> = []

    private struct Day {
        let full: String
        let short: String
        let letter: String
    }

    // Sunday-first, matching the editor's day row.
    private static let days: [Day] = [
        .init(full: "Sunday", short: "Sun", letter: "S"),
        .init(full: "Monday", short: "Mon", letter: "M"),
        .init(full: "Tuesday", short: "Tue", letter: "T"),
        .init(full: "Wednesday", short: "Wed", letter: "W"),
        .init(full: "Thursday", short: "Thu", letter: "T"),
        .init(full: "Friday", short: "Fri", letter: "F"),
        .init(full: "Saturday", short: "Sat", letter: "S"),
    ]

    // macOS is the primary surface for the Planner, so it gets a table's
    // proportions rather than a stretched phone's. The name column is *fixed*:
    // letting it take the slack (maxWidth: .infinity) pushed the seven columns
    // to the far right of a wide window and left ~320pt of dead air mid-row,
    // which no amount of cell sizing fixes. Capping the whole grid keeps label
    // and marks inside one glance instead of one scan.
    #if os(macOS)
    private let cell: CGFloat = 36
    private let gap: CGFloat = 10
    /// Keeps the last column off the scroller.
    private let trailingInset: CGFloat = 10
    /// The phone has no room to spell days; the Mac does, and Sun/Sat and
    /// Tue/Thu stop colliding on one letter.
    private let spellDays = true
    #else
    private let cell: CGFloat = 32
    private let gap: CGFloat = 3
    private let trailingInset: CGFloat = 0
    private let spellDays = false
    #endif

    /// Leading padding, trailing padding, and the horizontal page padding —
    /// everything between the grid's content and its outer width.
    private var chrome: CGFloat { 32 + 8 + trailingInset }

    /// Widest leading label actually on screen, measured rather than guessed.
    @State private var measuredName: CGFloat = 0

    /// The label column sizes to its contents, the way a table column should.
    /// A fixed 240 was a guess at "wide enough for a long chore name", and on a
    /// real list it left half the column empty — which reads as the names being
    /// stranded away from their marks. Clamped so one absurd name can't blow the
    /// column out, and a family with short names still gets a column wide enough
    /// to look deliberate.
    ///
    /// Left-aligned, not centred: this column is scanned vertically, and
    /// centring gives it two ragged edges. Sizing it correctly is what closes
    /// the gap; changing the alignment would only move it.
    private var nameWidth: CGFloat? {
        #if os(macOS)
        return min(max(measuredName, 140), 280)
        #else
        return nil
        #endif
    }

    /// Hug the content instead of holding a fixed 600 — with a short list the
    /// grid now sits as a compact, centred block.
    private var gridMaxWidth: CGFloat {
        #if os(macOS)
        return (nameWidth ?? 0) + gap + weekSpan + chrome
        #else
        return .infinity
        #endif
    }

    private var weekSpan: CGFloat { cell * 7 + gap * 6 }

    /// Sunday = 0 … Saturday = 6, lining up with `days`. `.weekday` is
    /// 1-for-Sunday in the Gregorian calendar regardless of `firstWeekday`.
    private var todayIndex: Int {
        Calendar.current.component(.weekday, from: Date()) - 1
    }

    var body: some View {
        ScrollView {
            LazyVStack(spacing: 0, pinnedViews: [.sectionHeaders]) {
                Section {
                    if showAssignee {
                        groupedRows
                    } else {
                        flatRows
                    }
                } header: {
                    header
                }
            }
            .padding(.horizontal)
            .padding(.bottom, 16)
            .frame(maxWidth: gridMaxWidth)
            .frame(maxWidth: .infinity)
        }
        .background(alignment: .topLeading) { measuringLayer }
        .onPreferenceChange(NameWidthKey.self) { measuredName = $0 }
    }

    /// An invisible copy of every leading label at its natural width, so the
    /// column can be sized from the whole list rather than whichever rows happen
    /// to be scrolled into view — a lazy per-row measurement would make the
    /// column twitch as you scroll. Sits behind the ScrollView, so it never
    /// contributes to scrollable content.
    private var measuringLayer: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(chores) { chore in
                if showAssignee {
                    assigneeLabel(chore)
                } else {
                    nameCell(chore)
                }
            }
        }
        .fixedSize(horizontal: true, vertical: false)
        .background(GeometryReader { geo in
            Color.clear.preference(key: NameWidthKey.self, value: geo.size.width)
        })
        .opacity(0)
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }

    // MARK: - Grouping

    private struct ChoreGroup: Identifiable {
        let id: String
        let name: String
        let icon: String
        var chores: [PlannerChore]
    }

    /// Chores sharing a name, in the order the server sent them — the list is
    /// already sorted so identical chores cluster; this only draws it.
    private var groups: [ChoreGroup] {
        var order: [String] = []
        var byName: [String: ChoreGroup] = [:]
        for chore in chores {
            if byName[chore.name] == nil {
                order.append(chore.name)
                byName[chore.name] = ChoreGroup(
                    id: chore.name, name: chore.name, icon: iconFor(chore), chores: [])
            }
            byName[chore.name]?.chores.append(chore)
        }
        return order.compactMap { byName[$0] }
    }

    @ViewBuilder
    private var groupedRows: some View {
        ForEach(groups) { group in
            groupHeader(group)
            ForEach(group.chores) { chore in
                row(chore, vPad: 5) { assigneeLabel(chore) }
            }
            Color.clear.frame(height: 6)
        }
    }

    @ViewBuilder
    private var flatRows: some View {
        ForEach(chores) { chore in
            row(chore, vPad: 7) { nameCell(chore) }
            if chore.id != chores.last?.id {
                Divider().padding(.leading, 4)
            }
        }
    }

    private func groupHeader(_ group: ChoreGroup) -> some View {
        Button {
            if let first = group.chores.first { onEdit(first) }
        } label: {
            HStack(spacing: 8) {
                Text(group.icon).font(.body)
                Text(group.name)
                    .font(.callout.weight(.semibold))
                    .lineLimit(1)
                Text("\(group.chores.count)")
                    .font(.caption2.weight(.medium))
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, 5)
                    .padding(.vertical, 1)
                    .background(DB.fillOff(scheme), in: Capsule())
                Spacer(minLength: 0)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .padding(.top, 8)
        .padding(.bottom, 3)
        .padding(.leading, 4)
    }

    // MARK: - Header

    private var header: some View {
        HStack(spacing: gap) {
            nameSlot { Color.clear.frame(height: 1) }
            dayStrip(vPad: 8) {
                ForEach(Array(Self.days.enumerated()), id: \.offset) { i, d in
                    Text(spellDays ? d.short : d.letter)
                        .font(.caption.weight(.bold))
                        .foregroundStyle(i == todayIndex ? Color.dbAccent : Color.secondary)
                        .frame(width: cell)
                }
            }
        }
        .padding(.leading, 4)
        .padding(.trailing, 4 + trailingInset)
        .background(.regularMaterial)
    }

    // MARK: - Row

    private func row<Leading: View>(
        _ chore: PlannerChore,
        vPad: CGFloat,
        @ViewBuilder leading: () -> Leading
    ) -> some View {
        HStack(spacing: gap) {
            nameSlot {
                Button { onEdit(chore) } label: {
                    leading()
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }

            if chore.scheduleType == "WeeklyFrequency" {
                // Height matches the cell so the row doesn't collapse to its
                // text and break the table's rhythm mid-list. No today band:
                // an "N×/wk" chore has no columns, so highlighting one is
                // meaningless — it rendered as a stray stripe floating in an
                // otherwise empty row.
                dayStrip(vPad: vPad, showsToday: false) {
                    Text("\(chore.weeklyTargetCount)×/wk")
                        .font(.caption2.weight(.semibold))
                        .foregroundStyle(.secondary)
                        .frame(width: weekSpan, height: cell)
                }
            } else {
                let strip = dayStrip(vPad: vPad) {
                    ForEach(Array(Self.days.enumerated()), id: \.offset) { _, d in
                        dayCell(chore, day: d)
                    }
                }
                #if os(macOS)
                // macOS only. The Mac scrolls by wheel and trackpad, so a
                // horizontal drag here has nothing to fight; on iPhone the same
                // gesture would wrestle the ScrollView for every swipe.
                strip.gesture(paintGesture(chore))
                #else
                strip
                #endif
            }
        }
        .padding(.leading, 4)
        .padding(.trailing, 4 + trailingInset)
        .modifier(HoverHighlight())
    }

    /// The label column. Fixed on macOS so every row's marks start at the same
    /// x; elastic on the phone, where there is no slack to misuse.
    @ViewBuilder
    private func nameSlot<Content: View>(@ViewBuilder _ content: () -> Content) -> some View {
        if let nameWidth {
            content().frame(width: nameWidth, alignment: .leading)
        } else {
            content().frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    /// The seven-column strip, with today's column running behind it. Drawn as
    /// the strip's own background rather than the row's, so it needs no
    /// knowledge of the name column's width — and because the vertical padding
    /// is inside the background, consecutive rows' bands meet into one
    /// continuous column.
    private func dayStrip<Content: View>(
        vPad: CGFloat,
        showsToday: Bool = true,
        @ViewBuilder content: () -> Content
    ) -> some View {
        HStack(spacing: gap) { content() }
            .padding(.vertical, vPad)
            .background(alignment: .leading) {
                if showsToday {
                    RoundedRectangle(cornerRadius: 8, style: .continuous)
                        .fill(Color.dbAccent.opacity(0.09))
                        .frame(width: cell)
                        .offset(x: CGFloat(todayIndex) * (cell + gap))
                }
            }
    }

    /// Indented past the heading's glyph, and a step down in size — at equal
    /// size and a 5pt indent the child read as a sibling of the chore name
    /// rather than a child of it.
    private func assigneeLabel(_ chore: PlannerChore) -> some View {
        Text(chore.assignedUserName ?? "Unassigned")
            .font(.subheadline)
            .foregroundStyle(chore.isActive ? .primary : .secondary)
            .lineLimit(1)
            .padding(.leading, 34)
    }

    private func nameCell(_ chore: PlannerChore) -> some View {
        HStack(spacing: 8) {
            Text(iconFor(chore))
                .font(.body)
            VStack(alignment: .leading, spacing: 1) {
                Text(chore.name)
                    .font(.subheadline.weight(.medium))
                    .foregroundStyle(chore.isActive ? .primary : .secondary)
                    .lineLimit(2)
                if chore.isTask {
                    Text(chore.earnValue.display)
                        .font(.caption2)
                        .foregroundStyle(DB.gold(scheme))
                }
            }
        }
    }

    private func iconFor(_ chore: PlannerChore) -> String {
        if let icon = chore.icon, !icon.isEmpty { return icon }
        return chore.isTask ? "💰" : "✅"
    }

    // MARK: - Cell

    /// Drag across a row to set a run of days in one gesture — "every weekday"
    /// was five separate clicks, and setting up a family's week is mostly runs.
    /// The first cell touched decides the direction: start on an empty day and
    /// you're painting days on, start on a filled one and you're wiping them —
    /// the same rule a spreadsheet's drag-select uses.
    private func paintGesture(_ chore: PlannerChore) -> some Gesture {
        DragGesture(minimumDistance: 6)
            .onChanged { value in
                guard let index = cellIndex(atX: value.location.x) else { return }
                let day = Self.days[index].full
                if paintingChoreId != chore.id {
                    paintingChoreId = chore.id
                    paintDays = Set(chore.activeDays)
                    paintOn = !paintDays.contains(day)
                }
                let changed = paintOn
                    ? paintDays.insert(day).inserted
                    : paintDays.remove(day) != nil
                if changed { Haptics.tick() }
            }
            .onEnded { _ in
                guard paintingChoreId == chore.id else { return }
                onSetDays(chore, Self.days.map(\.full).filter { paintDays.contains($0) })
                paintingChoreId = nil
            }
    }

    /// Which column a point falls in. Gaps round into the cell before them, so
    /// a drag never skips a day by passing through the space between two.
    private func cellIndex(atX x: CGFloat) -> Int? {
        guard x >= 0 else { return nil }
        let index = Int(x / (cell + gap))
        return (0...6).contains(index) ? index : nil
    }

    private func dayCell(_ chore: PlannerChore, day: Day) -> some View {
        // Mid-drag the row shows the paint, not the saved value.
        let on = paintingChoreId == chore.id
            ? paintDays.contains(day.full)
            : chore.activeDays.contains(day.full)
        let who = chore.assignedUserName.map { ", \($0)" } ?? ""
        return Button {
            onToggle(chore, day.full)
        } label: {
            // Solid accent + white tick — Shaun picked this over the quieter
            // variants. The density complaint was spacing, not the mark.
            RoundedRectangle(cornerRadius: 7, style: .continuous)
                .fill(on ? Color.dbAccent : DB.fillOff(scheme))
                .frame(width: cell, height: cell)
                .overlay {
                    if on {
                        Image(systemName: "checkmark")
                            .font(.system(size: cell * 0.4, weight: .bold))
                            .foregroundStyle(.white)
                    }
                }
                .overlay {
                    RoundedRectangle(cornerRadius: 7, style: .continuous)
                        .strokeBorder(Color.primary.opacity(on ? 0 : 0.12), lineWidth: 0.5)
                }
        }
        .buttonStyle(.plain)
        .help("\(chore.name)\(who) — \(day.full)")
        .accessibilityLabel("\(chore.name)\(who), \(day.full)")
        .accessibilityValue(on ? "Scheduled" : "Off")
    }
}

/// Widest leading label in the list — `max` because the column has to fit the
/// longest one, not the last one measured.
private struct NameWidthKey: PreferenceKey {
    static let defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) {
        value = max(value, nextValue())
    }
}

/// A pointer-follows row highlight. macOS gives the Planner a cursor and no
/// feedback for it; this is the cheapest way to keep the eye on one row while
/// crossing to the marks. Never fires on a touch-only iPhone.
private struct HoverHighlight: ViewModifier {
    @State private var hovering = false

    func body(content: Content) -> some View {
        content
            .background(
                hovering ? Color.primary.opacity(0.05) : Color.clear,
                in: RoundedRectangle(cornerRadius: 8, style: .continuous))
            .onHover { hovering = $0 }
    }
}
