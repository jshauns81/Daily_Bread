import SwiftUI
import DailyBreadKit

/// The weekly chore grid — chores down the side, the seven days across the top,
/// a tap-to-toggle cell at each crossing. The schedule at a glance, and editable
/// in place: tapping a cell adds or removes that day from the chore's week.
/// A WeeklyFrequency chore (an "N× a week" goal with no fixed days) shows its
/// target across the row instead of togglable cells. Tapping a chore's name
/// opens its full editor.
struct PlannerGridView: View {
    let chores: [PlannerChore]
    var showAssignee: Bool
    var onToggle: (PlannerChore, String) -> Void
    var onEdit: (PlannerChore) -> Void

    @Environment(\.colorScheme) private var scheme

    // Sunday-first, matching the editor's day row. Two letters repeat by design
    // (Tue/Thu, Sun/Sat) — that's the standard calendar header.
    private let days: [(full: String, letter: String)] = [
        ("Sunday", "S"), ("Monday", "M"), ("Tuesday", "T"), ("Wednesday", "W"),
        ("Thursday", "T"), ("Friday", "F"), ("Saturday", "S")]
    // macOS is the primary surface for the Planner — it gets room, not a
    // tighter version of the phone. A 3pt gap on a wide window packs the seven
    // columns into a clump hard against the right edge, which is what "snug"
    // was: not the marks, the spacing.
    #if os(macOS)
    private let cell: CGFloat = 36
    private let gap: CGFloat = 10
    /// Keeps the last column off the scroller.
    private let trailingInset: CGFloat = 10
    #else
    private let cell: CGFloat = 32
    private let gap: CGFloat = 3
    private let trailingInset: CGFloat = 0
    #endif

    private var weekSpan: CGFloat { cell * 7 + gap * 6 }

    var body: some View {
        ScrollView {
            LazyVStack(spacing: 0, pinnedViews: [.sectionHeaders]) {
                Section {
                    ForEach(chores) { chore in
                        row(chore)
                        if chore.id != chores.last?.id {
                            Divider().padding(.leading, 4)
                        }
                    }
                } header: {
                    header
                }
            }
            .padding(.horizontal)
            .padding(.bottom, 16)
        }
    }

    // MARK: - Header

    private var header: some View {
        HStack(spacing: gap) {
            Color.clear.frame(maxWidth: .infinity)
            ForEach(Array(days.enumerated()), id: \.offset) { _, d in
                Text(d.letter)
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.secondary)
                    .frame(width: cell)
            }
        }
        .padding(.vertical, 8)
        .padding(.leading, 4)
        .padding(.trailing, 4 + trailingInset)
        .background(.regularMaterial)
    }

    // MARK: - Row

    private func row(_ chore: PlannerChore) -> some View {
        HStack(spacing: gap) {
            Button {
                onEdit(chore)
            } label: {
                nameCell(chore)
            }
            .buttonStyle(.plain)
            .frame(maxWidth: .infinity, alignment: .leading)

            if chore.scheduleType == "WeeklyFrequency" {
                Text("\(chore.weeklyTargetCount)×/wk")
                    .font(.caption2.weight(.semibold))
                    .foregroundStyle(.secondary)
                    .frame(width: weekSpan)
            } else {
                ForEach(Array(days.enumerated()), id: \.offset) { _, d in
                    dayCell(chore, full: d.full)
                }
            }
        }
        .padding(.vertical, 7)
        .padding(.leading, 4)
        .padding(.trailing, 4 + trailingInset)
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
                if showAssignee, let who = chore.assignedUserName {
                    Text(who)
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                } else if chore.isTask {
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

    private func dayCell(_ chore: PlannerChore, full: String) -> some View {
        let on = chore.activeDays.contains(full)
        return Button {
            onToggle(chore, full)
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
                        .strokeBorder(Color.primary.opacity(on ? 0 : 0.06), lineWidth: 0.5)
                }
        }
        .buttonStyle(.plain)
    }
}
