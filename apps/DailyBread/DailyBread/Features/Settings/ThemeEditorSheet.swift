import SwiftUI
import DailyBreadKit

// §3.6 — one sheet, two modes. A text editor alone serves the person who
// enjoys config files and fails the person who just wants a purple app; a form
// alone removes the thing worth discovering. Both views edit the SAME draft, so
// switching is lossless: drag a colour well and the YAML updates, fix a hex in
// the text and the well moves.

struct ThemeEditorSheet: View {
    /// nil = new theme, seeded from whatever is active right now.
    var editing: CustomPalette?
    var seed: AppTheme
    var onSaved: (String) -> Void

    @Environment(SessionStore.self) private var session
    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    private enum Mode: String, CaseIterable { case simple = "Simple", yaml = "YAML" }

    @State private var mode: Mode = .simple
    @State private var draft: ThemeDraft
    @State private var yamlText: String
    @State private var cursor = 0
    @State private var notes: [ThemeLoader.LintNote] = []
    @State private var lintTask: Task<Void, Never>?
    @State private var saving = false
    @State private var errorMessage: String?

    init(editing: CustomPalette?, seed: AppTheme, author: String,
         onSaved: @escaping (String) -> Void) {
        self.editing = editing
        self.seed = seed
        self.onSaved = onSaved
        let initial = editing.map { ThemeDraft(editing: $0, author: author) }
            ?? ThemeDraft(seededFrom: seed, author: author)
        _draft = State(initialValue: initial)
        _yamlText = State(initialValue: initial.render())
    }

    var body: some View {
        VStack(spacing: 0) {
            SheetHeader(title: editing == nil ? "Make a theme" : "Edit theme")

            Picker("Mode", selection: $mode) {
                ForEach(Mode.allCases, id: \.self) { Text($0.rawValue).tag($0) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .padding(.horizontal)
            .padding(.bottom, 10)
            .onChange(of: mode) { _, newMode in
                // Leaving YAML: adopt whatever the text parsed to. Entering it:
                // re-render from the draft so the two never drift.
                if newMode == .yaml {
                    yamlText = draft.render()
                } else if let parsed = ThemeDraft(yaml: yamlText) {
                    var next = parsed
                    next.author = draft.author
                    draft = next
                }
            }

            #if os(macOS)
            // macOS: split view — the app preview lives beside the editor and
            // updates as you type (§3.6 live preview).
            HStack(alignment: .top, spacing: 0) {
                ScrollView { editorBody.padding() }
                Divider()
                ScrollView {
                    ThemePreviewPane(palette: draft.palette)
                        .padding()
                }
                .frame(width: 260)
            }
            #else
            ScrollView { editorBody.padding(.horizontal).padding(.bottom, 12) }
            // iPhone: the preview strip is pinned above the keyboard.
            ThemePreviewStrip(palette: draft.palette)
                .padding(.horizontal)
                .padding(.vertical, 8)
            #endif

            lintBar

            SheetActionBar(saveTitle: "Save", saving: saving, canSave: canSave,
                           onCancel: { dismiss() },
                           onSave: { Task { await save() } })
                .padding()
        }
        .themeBackground()
        .onChange(of: yamlText) { _, text in
            // Lint as you type, debounced — not only on save (§3.6).
            lintTask?.cancel()
            lintTask = Task {
                try? await Task.sleep(for: .milliseconds(400))
                guard !Task.isCancelled else { return }
                notes = ThemeLoader.lint(text)
                if let parsed = ThemeDraft(yaml: text) {
                    var next = parsed
                    next.author = draft.author
                    draft = next
                }
            }
        }
        #if os(macOS)
        .frame(minWidth: 720, idealWidth: 820, minHeight: 560, idealHeight: 640)
        #else
        .presentationDetents([.large])
        #endif
    }

    @ViewBuilder
    private var editorBody: some View {
        switch mode {
        case .simple: simpleMode
        case .yaml: yamlMode
        }
    }

    // MARK: - Simple mode (the front door)

    private var simpleMode: some View {
        VStack(spacing: 14) {
            SheetCard(title: "Name it") {
                TextField("My Theme", text: $draft.name)
                    .sheetFieldBackground()
                TextField("cool and quiet", text: $draft.mood)
                    .sheetFieldBackground()
                Text("The id is made from the name — that's what other devices match on.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            SheetCard(title: "Colours") {
                Toggle("Dark", isOn: $draft.isDark)
                well("Background", $draft.backgroundHex, "behind everything")
                well("Card", $draft.cardHex, "the cards that float on top")
                well("Accent", $draft.accentHex, "buttons, your name, the important stuff")
                well("Secondary", $draft.secondaryHex, "the little done-checks")
            }

            SheetCard(title: "Gold and red") {
                Text("Gold means money and red means Help in every theme — that's how they stay findable. You can take them, but say so on purpose.")
                    .font(.caption).foregroundStyle(.secondary)
                Toggle("Let me change them", isOn: $draft.unlockInvariants)
                if draft.unlockInvariants {
                    well("Money", $draft.goldHex, "balances and earnings")
                    well("Help", $draft.helpHex, "the Help alert")
                }
            }

            #if os(iOS)
            ThemePreviewPane(palette: draft.palette)
            #endif
        }
    }

    private func well(_ label: String, _ hex: Binding<UInt32>, _ note: String) -> some View {
        HStack(spacing: 12) {
            ColorPicker("", selection: Binding(
                get: { Color(hex: hex.wrappedValue) },
                set: { hex.wrappedValue = $0.hexValue ?? hex.wrappedValue }))
                .labelsHidden()
            VStack(alignment: .leading, spacing: 1) {
                Text(label).font(.subheadline.weight(.medium))
                Text(note).font(.caption2).foregroundStyle(.secondary)
            }
            Spacer()
            Text(ThemeHex.format(hex.wrappedValue))
                .font(.caption.monospaced())
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - YAML mode

    private var yamlMode: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .top, spacing: 8) {
                colourGutter
                YAMLTextView(text: $yamlText, cursor: $cursor,
                             accent: draft.palette.accent)
            }
            .padding(.horizontal, 10)
            .background(DB.fillSubtle(scheme),
                        in: RoundedRectangle(cornerRadius: 12, style: .continuous))

            Text("Tap a swatch to pick a colour — you almost never have to type a hex.")
                .font(.caption2).foregroundStyle(.secondary)
        }
        #if os(iOS)
        // §3.6 note 2 — YAML is indentation-sensitive and the stock iPhone
        // keyboard buries every one of these behind a modifier tap.
        .toolbar {
            ToolbarItemGroup(placement: .keyboard) {
                ForEach([":", "-", "#", "\""], id: \.self) { key in
                    Button(key) { insert(key) }
                        .font(.body.monospaced())
                }
                Button { insert("  ") } label: { Image(systemName: "arrow.right.to.line") }
                Spacer()
                Button("Done") { hideKeyboard() }
            }
        }
        #endif
    }

    /// §3.6 — a narrow column beside the text with a swatch for every line that
    /// contains a hex. Tapping opens the system picker and the hex is rewritten
    /// in place. Alignment is free: fixed advance, one row per line.
    private var colourGutter: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(lines.enumerated()), id: \.offset) { index, line in
                Group {
                    if let hex = firstHex(in: line) {
                        ColorPicker("", selection: Binding(
                            get: { Color(hex: hex) },
                            set: { rewriteHex(line: index, with: $0) }))
                            .labelsHidden()
                            .scaleEffect(0.7)
                            .frame(width: 22)
                    } else if notes.contains(where: { $0.line == index + 1 && $0.isFatal }) {
                        Circle().fill(DB.help(scheme)).frame(width: 6, height: 6)
                            .frame(width: 22)
                    } else if notes.contains(where: { $0.line == index + 1 }) {
                        Circle().fill(DB.gold(scheme)).frame(width: 6, height: 6)
                            .frame(width: 22)
                    } else {
                        Color.clear.frame(width: 22)
                    }
                }
                .frame(height: YAMLFont.lineHeight)
            }
        }
        .padding(.top, YAMLFont.insets)
    }

    private var lines: [String] { yamlText.components(separatedBy: "\n") }

    private func firstHex(in line: String) -> UInt32? {
        guard let range = line.range(of: "#[0-9A-Fa-f]{6}", options: .regularExpression)
        else { return nil }
        return ThemeHex.parse(String(line[range]))
    }

    private func rewriteHex(line index: Int, with color: Color) {
        guard let hex = color.hexValue else { return }
        var all = lines
        guard all.indices.contains(index),
              let range = all[index].range(of: "#[0-9A-Fa-f]{6}", options: .regularExpression)
        else { return }
        all[index].replaceSubrange(range, with: ThemeHex.format(hex))
        yamlText = all.joined(separator: "\n")
    }

    #if os(iOS)
    private func insert(_ text: String) {
        let index = yamlText.index(yamlText.startIndex,
                                   offsetBy: min(cursor, yamlText.count))
        yamlText.insert(contentsOf: text, at: index)
        cursor += text.count
    }

    private func hideKeyboard() {
        UIApplication.shared.sendAction(#selector(UIResponder.resignFirstResponder),
                                        to: nil, from: nil, for: nil)
    }
    #endif

    // MARK: - Lint bar + save

    private var lintBar: some View {
        VStack(alignment: .leading, spacing: 4) {
            if let errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.circle")
                    .font(.footnote).foregroundStyle(DB.help(scheme))
            }
            ForEach(notes.prefix(3)) { note in
                HStack(spacing: 6) {
                    Image(systemName: note.isFatal ? "xmark.circle.fill" : "lightbulb")
                        .foregroundStyle(note.isFatal ? DB.help(scheme) : DB.gold(scheme))
                    Text(note.line.map { "Line \($0): \(note.message)" } ?? note.message)
                        .font(.caption)
                }
            }
            if !ThemeContrast.passesAA(draft.palette) {
                // §3.3a — advisory, never blocking. It's their app.
                HStack(spacing: 6) {
                    Image(systemName: "eye.trianglebadge.exclamationmark")
                        .foregroundStyle(DB.gold(scheme))
                    Text("Text on cards is a bit hard to read at this contrast.")
                        .font(.caption)
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal)
    }

    private var canSave: Bool {
        !draft.name.trimmingCharacters(in: .whitespaces).isEmpty
            && !notes.contains(where: \.isFatal)
    }

    private func save() async {
        saving = true
        defer { saving = false }
        errorMessage = nil

        var final = draft
        if final.id.isEmpty { final.id = Self.slug(from: final.name) }
        final.author = session.currentUser?.userName ?? final.author

        // The saved document is the YAML the user was actually looking at when
        // they were in YAML mode — comments and all.
        let text = mode == .yaml ? yamlText : final.render()

        // Never write a file the picker would then refuse to select (§3.3 r4).
        guard case .success(let palette) = ThemeLoader.parse(text) else {
            errorMessage = "This isn't valid yet — fix the line above and try again."
            return
        }

        guard ThemeLoader.save(id: palette.id, yaml: text) != nil else {
            errorMessage = "Couldn't write the theme file."
            return
        }

        // §3.1 — push it to the family's server so every device gets it. A
        // failure here is not fatal: the theme already exists locally.
        _ = try? await session.client.putTheme(id: palette.id, yaml: text)

        Haptics.success()
        onSaved(palette.id)
        dismiss()
    }

    /// meta.id from the name: lowercase, dashes, safe for a filename.
    static func slug(from name: String) -> String {
        let lowered = name.lowercased()
        let mapped = lowered.map { ch -> Character in
            ch.isLetter || ch.isNumber ? ch : "-"
        }
        let collapsed = String(mapped)
            .split(separator: "-", omittingEmptySubsequences: true)
            .joined(separator: "-")
        return collapsed.isEmpty ? "my-theme" : String(collapsed.prefix(64))
    }
}

// MARK: - Live preview

/// A card, a button and a row in the theme being written (§3.6). Everything is
/// drawn from the draft palette explicitly — it must NOT inherit the app's
/// active theme, or the preview would lie.
struct ThemePreviewPane: View {
    let palette: CustomPalette

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("PREVIEW")
                .font(.caption.weight(.bold)).kerning(0.8)
                .foregroundStyle(.secondary)

            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 10) {
                    Circle().fill(palette.accent).frame(width: 30, height: 30)
                        .overlay(Image(systemName: "checkmark")
                            .font(.caption.weight(.black))
                            .foregroundStyle(palette.onAccent))
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Make your bed")
                            .font(.subheadline.weight(.medium))
                            .foregroundStyle(labelColor)
                        Text("$1.50 · due tonight")
                            .font(.caption)
                            .foregroundStyle(goldColor)
                    }
                    Spacer(minLength: 0)
                }
                .padding(12)
                .background(palette.cardColor,
                            in: RoundedRectangle(cornerRadius: 14, style: .continuous))

                Text("Approve")
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(palette.onAccent)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 9)
                    .background(palette.accent, in: Capsule())

                HStack(spacing: 6) {
                    Capsule().fill(palette.secondary).frame(height: 6)
                    Capsule().fill(palette.secondary.opacity(0.25)).frame(height: 6)
                }
            }
            .padding(12)
            .background(
                LinearGradient(colors: [palette.backgroundTop, palette.backgroundBottom],
                               startPoint: .top, endPoint: .bottom),
                in: RoundedRectangle(cornerRadius: 18, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous)
                .strokeBorder(Color.primary.opacity(0.08), lineWidth: 0.5))
        }
    }

    private var labelColor: Color {
        Color(hex: ThemeContrast.labelHex(isDark: palette.isDark))
    }

    private var goldColor: Color {
        palette.goldHex.map { Color(hex: $0) } ?? DB.gold(palette.isDark ? .dark : .light)
    }
}

/// The iPhone strip: same idea, one line tall, so it survives above a keyboard.
struct ThemePreviewStrip: View {
    let palette: CustomPalette

    var body: some View {
        HStack(spacing: 10) {
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .fill(LinearGradient(colors: [palette.backgroundTop, palette.backgroundBottom],
                                     startPoint: .top, endPoint: .bottom))
                .frame(width: 34, height: 34)
                .overlay(RoundedRectangle(cornerRadius: 4, style: .continuous)
                    .fill(palette.cardColor).frame(width: 20, height: 12))
            Circle().fill(palette.accent).frame(width: 18, height: 18)
            Circle().fill(palette.secondary).frame(width: 18, height: 18)
            Capsule().fill(palette.accent).frame(width: 54, height: 16)
            Spacer(minLength: 0)
            Text(palette.isDark ? "dark" : "light")
                .font(.caption2).foregroundStyle(.secondary)
        }
        .padding(8)
        .background(DB.fillSubtle(.light), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
    }
}

// MARK: - Colour → hex

extension Color {
    /// The editor's colour wells speak hex; SwiftUI speaks Color.
    var hexValue: UInt32? {
        #if os(iOS)
        var r: CGFloat = 0, g: CGFloat = 0, b: CGFloat = 0, a: CGFloat = 0
        guard UIColor(self).getRed(&r, green: &g, blue: &b, alpha: &a) else { return nil }
        #else
        guard let converted = NSColor(self).usingColorSpace(.sRGB) else { return nil }
        let r = converted.redComponent, g = converted.greenComponent, b = converted.blueComponent
        #endif
        return (UInt32(max(0, min(1, r)) * 255 + 0.5) << 16)
            | (UInt32(max(0, min(1, g)) * 255 + 0.5) << 8)
            | UInt32(max(0, min(1, b)) * 255 + 0.5)
    }
}

/// Sheet identity for the picker: nil palette = a brand-new theme.
struct ThemeEditorTarget: Identifiable {
    var palette: CustomPalette?
    var id: String { palette?.id ?? "new" }
}
