import SwiftUI
import DailyBreadKit

// §2.5 — the curated icon picker (R10). The old TextField accepted "asdf",
// three emoji, or nothing — then rendered whatever it got at 40pt in the kid's
// list every day. The chore set is guidelines/icons-emoji.html verbatim, and
// the GROUPS are the point: an ungrouped wall of 54 is worse than the text
// field it replaces. Free entry stays as the escape hatch, validated to
// exactly one grapheme cluster.

enum EmojiSet: String {
    case chores, awards

    var groups: [(name: String, glyphs: [String])] {
        switch self {
        case .chores:
            return [
                ("Kitchen", ["🍽", "🍳", "🧊", "🥡", "🧽", "☕", "🍞", "🥣"]),
                ("Cleaning", ["🧹", "🧼", "🪣", "🚿", "🪟", "🧴", "🚽", "🪠"]),
                ("Laundry & bedroom", ["🧺", "👕", "🧦", "🛏", "🪑", "🚪", "🧸", "📦"]),
                ("Outdoors & rubbish", ["🗑", "♻️", "🌱", "🍂", "🪴", "🚗", "🚲", "❄️"]),
                ("Pets", ["🐕", "🐈", "🐟", "🦴", "🐾", "🥩"]),
                ("Self & school", ["🦷", "📚", "✏️", "🎒", "🎹", "🏃", "💊", "🛁"]),
                ("Helping & general", ["🤝", "💰", "📮", "🔑", "💡", "🔧", "✅"]),
            ]
        case .awards:
            return [
                ("Trophies & medals", ["🏆", "🥇", "🥈", "🥉", "🏅", "🎖", "👑", "💎"]),
                ("Sparks & streaks", ["⭐", "🌟", "✨", "🔥", "⚡", "🎯", "🚀", "🎉"]),
            ]
        }
    }

    var fallback: String {
        switch self {
        case .chores: return "🧺"
        case .awards: return "🏆"
        }
    }
}

/// The square icon well that opens the picker — drop-in for the old TextField.
struct EmojiPickerField: View {
    @Binding var icon: String
    var set: EmojiSet = .chores
    var edge: CGFloat = 46

    @State private var picking = false

    var body: some View {
        Button {
            picking = true
        } label: {
            Text(icon.isEmpty ? set.fallback : icon)
                .font(.title2)
                .opacity(icon.isEmpty ? 0.35 : 1)
                .frame(width: edge, height: edge)
                .background(.quaternary.opacity(0.4),
                            in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        }
        .buttonStyle(.plain)
        .accessibilityLabel(icon.isEmpty ? "Pick an icon" : "Icon \(icon). Tap to change.")
        .sheet(isPresented: $picking) {
            EmojiPickerSheet(icon: $icon, set: set)
        }
    }
}

private struct EmojiPickerSheet: View {
    @Binding var icon: String
    let set: EmojiSet

    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme
    @AppStorage private var recentsRaw: String
    @State private var freeEntry = ""

    init(icon: Binding<String>, set: EmojiSet) {
        _icon = icon
        self.set = set
        _recentsRaw = AppStorage(wrappedValue: "", "db.emoji.recents.\(set.rawValue)")
    }

    private var recents: [String] {
        recentsRaw.split(separator: " ").map(String.init)
    }

    private let columns = [GridItem(.adaptive(minimum: 44), spacing: 8)]

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: "Pick an icon")
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    if !recents.isEmpty {
                        section("Recent", recents)
                    }
                    ForEach(set.groups, id: \.name) { group in
                        section(group.name, group.glyphs)
                    }

                    // The escape hatch — any emoji, but exactly ONE grapheme
                    // cluster (that's the bug the old field had).
                    VStack(alignment: .leading, spacing: 8) {
                        eyebrow("Something else")
                        TextField("Type any emoji", text: $freeEntry)
                            .sheetFieldBackground()
                            .onChange(of: freeEntry) { _, value in
                                guard let first = value.first else { return }
                                pick(String(first))
                            }
                    }
                }
                .padding(.horizontal).padding(.top, 4).padding(.bottom, 16)
            }
            HStack {
                Button("Cancel") { dismiss() }
                    .buttonStyle(.plain)
                    .foregroundStyle(.secondary)
                Spacer()
            }
            .padding()
        }
        .themeBackground()
        #if os(macOS)
        .frame(minWidth: 420, idealWidth: 460, minHeight: 480, idealHeight: 540)
        #endif
        #if os(iOS)
        .presentationDetents([.medium, .large])
        #endif
    }

    private func section(_ name: String, _ glyphs: [String]) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            eyebrow(name)
            LazyVGrid(columns: columns, spacing: 8) {
                ForEach(glyphs, id: \.self) { glyph in
                    cell(glyph)
                }
            }
        }
    }

    private func eyebrow(_ text: String) -> some View {
        Text(text.uppercased())
            .font(.caption.weight(.bold))
            .foregroundStyle(.secondary)
            .kerning(0.8)
    }

    private func cell(_ glyph: String) -> some View {
        let selected = glyph == icon
        return Button {
            pick(glyph)
        } label: {
            Text(glyph)
                .font(.title2)
                .frame(width: 44, height: 44)
                .background(selected ? AnyShapeStyle(Color.accentColor.opacity(0.18))
                                     : AnyShapeStyle(DB.fillSubtle(scheme)),
                            in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .strokeBorder(selected ? Color.accentColor : Color.clear, lineWidth: 1.5))
        }
        .contentShape(Rectangle())
        .buttonStyle(.plain)
        .accessibilityLabel(glyph)
    }

    private func pick(_ glyph: String) {
        Haptics.tick()
        icon = glyph
        var r = recents.filter { $0 != glyph }
        r.insert(glyph, at: 0)
        recentsRaw = r.prefix(8).joined(separator: " ")
        dismiss()
    }
}
