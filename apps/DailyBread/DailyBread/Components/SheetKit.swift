import SwiftUI
import DailyBreadKit

// Shared chrome for the app's designed sheets. macOS renders SwiftUI `Form`
// rows cramped and misaligned — labels jammed into a leading column, prefixes
// ("$") flung away from their fields, controls stretched oddly. Our sheets are
// built from these explicit, label-above-control pieces instead, so they read
// the same clean way on iOS and macOS. Graphite & Glass throughout.

/// A short title row at the top of a sheet.
struct SheetHeader: View {
    var title: String

    var body: some View {
        HStack {
            Text(title)
                .font(.headline.weight(.semibold))
            Spacer()
        }
        .padding(.horizontal)
        .padding(.top, 18)
        .padding(.bottom, 6)
    }
}

/// A titled group of fields on a glass surface.
struct SheetCard<Content: View>: View {
    var title: String?
    var content: Content

    init(title: String? = nil, @ViewBuilder content: () -> Content) {
        self.title = title
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            if let title {
                Text(title.uppercased())
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.secondary)
                    .kerning(0.8)
            }
            content
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard()
    }
}

/// A single field: a caption label (with an optional live value on the right)
/// above its control.
struct SheetField<Content: View>: View {
    var label: String
    var value: String?
    var valueColor: Color?
    var content: Content

    init(label: String,
         value: String? = nil,
         valueColor: Color? = nil,
         @ViewBuilder content: () -> Content) {
        self.label = label
        self.value = value
        self.valueColor = valueColor
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text(label)
                    .font(.subheadline.weight(.medium))
                Spacer(minLength: 8)
                if let value {
                    Text(value)
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(valueColor ?? Color.secondary)
                        .monospacedDigit()
                }
            }
            content
        }
    }
}

/// Cancel + primary action, side by side, pinned at the sheet's foot — one
/// action zone, not a Save-in-the-form plus Cancel-in-the-toolbar split.
struct SheetActionBar: View {
    var saveTitle: String
    var saving: Bool
    var canSave: Bool
    var onCancel: () -> Void
    var onSave: () -> Void

    var body: some View {
        HStack(spacing: 12) {
            Button(action: onCancel) {
                Text("Cancel")
                    .font(.body.weight(.medium))
                    .frame(maxWidth: .infinity, minHeight: 44)
            }
            .buttonStyle(.plain)
            .background(.quaternary.opacity(0.5),
                        in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            .disabled(saving)

            Button(action: onSave) {
                Group {
                    if saving {
                        ProgressView()
                    } else {
                        Text(saveTitle).font(.body.weight(.semibold))
                    }
                }
                .frame(maxWidth: .infinity, minHeight: 44)
                .foregroundStyle(.white)
            }
            .buttonStyle(.plain)
            .background(canSave ? Color.accentColor : Color.secondary.opacity(0.3),
                        in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            .disabled(saving || !canSave)
        }
    }
}

/// Compact Sunday-first day toggles (S M T W T F S) — accent-filled when on.
/// Reads as a day selector, not a row of big buttons.
struct DayPicker: View {
    @Binding var selected: Set<String>   // full day names ("Sunday" … "Saturday")

    private struct Day: Identifiable {
        let full: String
        let letter: String
        var id: String { full }
    }

    private static let days: [Day] = [
        .init(full: "Sunday", letter: "S"),
        .init(full: "Monday", letter: "M"),
        .init(full: "Tuesday", letter: "T"),
        .init(full: "Wednesday", letter: "W"),
        .init(full: "Thursday", letter: "T"),
        .init(full: "Friday", letter: "F"),
        .init(full: "Saturday", letter: "S"),
    ]

    /// Every day, Sunday-first — the canonical order for building activeDays.
    static let allDays: [String] = days.map(\.full)

    var body: some View {
        HStack(spacing: 6) {
            ForEach(Self.days) { day in
                let on = selected.contains(day.full)
                Button {
                    Haptics.tick()
                    withAnimation(.snappy) {
                        if on { selected.remove(day.full) } else { selected.insert(day.full) }
                    }
                } label: {
                    Text(day.letter)
                        .font(.subheadline.weight(.semibold))
                        .frame(width: 36, height: 36)
                        .background(on ? Color.accentColor : Color.secondary.opacity(0.14),
                                    in: Circle())
                        .foregroundStyle(on ? Color.white : Color.primary)
                }
                .buttonStyle(.plain)
                .frame(maxWidth: .infinity)
            }
        }
    }
}

extension View {
    /// Bordered inline-field styling — a soft rounded background so text fields
    /// read as tappable fields on both platforms.
    func sheetFieldBackground() -> some View {
        self
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
            .background(.quaternary.opacity(0.4),
                        in: RoundedRectangle(cornerRadius: 10, style: .continuous))
    }
}
