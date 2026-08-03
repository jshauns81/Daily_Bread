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
    var onEdit: (PlannerChore) -> Void

    @Environment(\.colorScheme) private var scheme

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
    private let nameWidth: CGFloat? = 240
    /// 240 name + 10 gap + 312 week + 32 padding = 594.
    private let gridMaxWidth: CGFloat = 600
    /// The phone has no room to spell days; the Mac does, and Sun/Sat and
    /// Tue/Thu stop colliding on one letter.
    private let spellDays = true
    #else
    private let cell: CGFloat = 32
    private let gap: CGFloat = 3
    private let trailingInset: CGFloat = 0
    private let nameWidth: CGFloat? = nil
    private let gridMaxWidth: CGFloat = .infinity
    private let spellDays = false
    #endif

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
                row(chore, vPad: 5) {
                    // Indented past the heading's glyph, and a step down in
                    // size — at equal size and a 5pt indent the child read as a
                    // sibling of the chore name rather than a child of it.
                    Text(chore.assignedUserName ?? "Unassigned")
                        .font(.subheadline)
                        .foregroundStyle(chore.isActive ? .primary : .secondary)
                        .lineLimit(1)
                        .padding(.leading, 34)
                }
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
                // Matches the cell height. Without it the row collapses to the
                // text's own height and an "N×/wk" chore sits visibly shorter
                // than its neighbours, breaking the grid's rhythm mid-table.
                dayStrip(vPad: vPad) {
                    Text("\(chore.weeklyTargetCount)×/wk")
                        .font(.caption2.weight(.semibold))
                        .foregroundStyle(.secondary)
                        .frame(width: weekSpan, height: cell)
                }
            } else {
                dayStrip(vPad: vPad) {
                    ForEach(Array(Self.days.enumerated()), id: \.offset) { _, d in
                        dayCell(chore, day: d)
                    }
                }
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
        @ViewBuilder content: () -> Content
    ) -> some View {
        HStack(spacing: gap) { content() }
            .padding(.vertical, vPad)
            .background(alignment: .leading) {
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(Color.dbAccent.opacity(0.09))
                    .frame(width: cell)
                    .offset(x: CGFloat(todayIndex) * (cell + gap))
            }
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

    private func dayCell(_ chore: PlannerChore, day: Day) -> some View {
        let on = chore.activeDays.contains(day.full)
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
