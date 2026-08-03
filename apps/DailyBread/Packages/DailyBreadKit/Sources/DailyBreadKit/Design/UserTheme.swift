import SwiftUI
import Yams

// §3 YAML theming — a theme file is a first-class citizen, not a skin. The
// schema can express everything a built-in expresses (parity of schema), but
// built-ins stay compiled in (NOT parity of load path): delete every theme file
// on the system and Daily Bread still launches in Sunroom. No theme file can
// prevent the app from launching or being used — every rule in this file bends
// toward that guarantee.

// MARK: - Manifest (the schema, decoded leniently)

/// Mirrors §3.2. Every key is optional except `meta.id` and `meta.name`;
/// unknown or misspelled keys are ignored by decoding, never fatal — a typo
/// degrades to the default instead of failing (§3.3 rule 2).
public struct ThemeManifest: Codable, Sendable {
    public struct Meta: Codable, Sendable {
        public var id: String?
        public var name: String?
        public var mood: String?
        public var author: String?
        public var scheme: String?

        public init(id: String? = nil, name: String? = nil, mood: String? = nil,
                    author: String? = nil, scheme: String? = nil) {
            self.id = id; self.name = name; self.mood = mood
            self.author = author; self.scheme = scheme
        }
    }

    /// `background:` accepts either one colour ("#1B2340" — the two-stop
    /// gradient is derived, so a kid never has to think about gradients) or an
    /// explicit `{ top:, bottom: }`.
    public struct BackgroundSpec: Codable, Sendable {
        public var top: String?
        public var bottom: String?

        public init(top: String? = nil, bottom: String? = nil) {
            self.top = top; self.bottom = bottom
        }

        public init(from decoder: Decoder) throws {
            if let single = try? decoder.singleValueContainer().decode(String.self) {
                top = single
                bottom = nil
                return
            }
            let keyed = try decoder.container(keyedBy: CodingKeys.self)
            top = try keyed.decodeIfPresent(String.self, forKey: .top)
            bottom = try keyed.decodeIfPresent(String.self, forKey: .bottom)
        }
    }

    public struct Colors: Codable, Sendable {
        public var background: BackgroundSpec?
        public var card: String?
        public var accent: String?
        public var secondary: String?
        public var onAccent: String?
        public var label: String?

        public init(background: BackgroundSpec? = nil, card: String? = nil,
                    accent: String? = nil, secondary: String? = nil,
                    onAccent: String? = nil, label: String? = nil) {
            self.background = background; self.card = card; self.accent = accent
            self.secondary = secondary; self.onAccent = onAccent; self.label = label
        }
    }

    /// §3.4 — gold/help carry meaning and are NOT themeable by default. The
    /// block is silently ignored without `unlock: true`.
    public struct Invariants: Codable, Sendable {
        public var unlock: Bool?
        public var gold: String?
        public var help: String?

        public init(unlock: Bool? = nil, gold: String? = nil, help: String? = nil) {
            self.unlock = unlock; self.gold = gold; self.help = help
        }
    }

    public var meta: Meta?
    public var colors: Colors?
    public var invariants: Invariants?

    public init(meta: Meta? = nil, colors: Colors? = nil, invariants: Invariants? = nil) {
        self.meta = meta; self.colors = colors; self.invariants = invariants
    }
}

// MARK: - Resolved palette

/// What a valid user theme resolves to — full surfaces, ready to render.
/// Missing keys inherited from the base theme for the manifest's scheme
/// (Sunroom for light, Harbor for dark) before this is ever constructed, so
/// there is no partial state at render time (§3.3 rule 4).
public struct CustomPalette: Hashable, Sendable {
    public var id: String
    public var name: String
    public var mood: String
    public var isDark: Bool
    public var accentHex: UInt32
    public var secondaryHex: UInt32
    public var cardHex: UInt32
    public var backgroundTopHex: UInt32
    public var backgroundBottomHex: UInt32
    public var onAccentHex: UInt32
    /// §3.4 — only set when the invariants block was explicitly unlocked.
    public var goldHex: UInt32?
    public var helpHex: UInt32?

    public var accent: Color { Color(hex: accentHex) }
    public var secondary: Color { Color(hex: secondaryHex) }
    public var cardColor: Color { Color(hex: cardHex) }
    public var backgroundTop: Color { Color(hex: backgroundTopHex) }
    public var backgroundBottom: Color { Color(hex: backgroundBottomHex) }
    public var onAccent: Color { Color(hex: onAccentHex) }
}

// MARK: - Hex utilities

public enum ThemeHex {
    /// "#RRGGBB" or "RRGGBB", any case. Nil for anything else — a bad hex is a
    /// missing key, and missing keys inherit (§3.3 rule 2).
    public static func parse(_ raw: String?) -> UInt32? {
        guard var s = raw?.trimmingCharacters(in: .whitespaces) else { return nil }
        if s.hasPrefix("#") { s.removeFirst() }
        guard s.count == 6, let v = UInt32(s, radix: 16) else { return nil }
        return v
    }

    public static func format(_ v: UInt32) -> String {
        String(format: "#%06X", v)
    }

    /// Lightness shift in HSL space — how one `background:` colour becomes the
    /// soft two-stop gradient every built-in has.
    public static func shiftLightness(_ hex: UInt32, by delta: Double) -> UInt32 {
        let r = Double((hex >> 16) & 0xFF) / 255
        let g = Double((hex >> 8) & 0xFF) / 255
        let b = Double(hex & 0xFF) / 255
        let maxC = max(r, g, b), minC = min(r, g, b)
        var h = 0.0, s = 0.0
        let l = (maxC + minC) / 2
        if maxC != minC {
            let d = maxC - minC
            s = l > 0.5 ? d / (2 - maxC - minC) : d / (maxC + minC)
            switch maxC {
            case r: h = (g - b) / d + (g < b ? 6 : 0)
            case g: h = (b - r) / d + 2
            default: h = (r - g) / d + 4
            }
            h /= 6
        }
        let l2 = min(1, max(0, l + delta))
        func hue2rgb(_ p: Double, _ q: Double, _ t: Double) -> Double {
            var t = t
            if t < 0 { t += 1 }
            if t > 1 { t -= 1 }
            if t < 1 / 6 { return p + (q - p) * 6 * t }
            if t < 1 / 2 { return q }
            if t < 2 / 3 { return p + (q - p) * (2 / 3 - t) * 6 }
            return p
        }
        let rgb: (Double, Double, Double)
        if s == 0 {
            rgb = (l2, l2, l2)
        } else {
            let q = l2 < 0.5 ? l2 * (1 + s) : l2 + s - l2 * s
            let p = 2 * l2 - q
            rgb = (hue2rgb(p, q, h + 1 / 3), hue2rgb(p, q, h), hue2rgb(p, q, h - 1 / 3))
        }
        return (UInt32(rgb.0 * 255 + 0.5) << 16) | (UInt32(rgb.1 * 255 + 0.5) << 8) | UInt32(rgb.2 * 255 + 0.5)
    }
}

// MARK: - Resolution

public extension ThemeManifest {
    /// Base-theme values for each scheme — Sunroom carries light, Harbor dark.
    /// Kept as raw hexes so resolution is pure data, no Color round-trips.
    private static let lightBase: (accent: UInt32, secondary: UInt32, card: UInt32,
                                   top: UInt32, bottom: UInt32, onAccent: UInt32) =
        (0xC7284F, 0x2E8C86, 0xFFFFFF, 0xFFFDF9, 0xFBF1E2, 0xFFFFFF)
    private static let darkBase: (accent: UInt32, secondary: UInt32, card: UInt32,
                                  top: UInt32, bottom: UInt32, onAccent: UInt32) =
        (0x5B9BE0, 0x4BA39C, 0x2A3852, 0x223049, 0x161E2C, 0xFFFFFF)

    /// nil when `meta.id` or `meta.name` is missing — the only validation
    /// failures besides malformed YAML itself (§3.3 rules 2–3).
    func resolved() -> CustomPalette? {
        guard let id = meta?.id?.trimmingCharacters(in: .whitespaces), !id.isEmpty,
              let name = meta?.name?.trimmingCharacters(in: .whitespaces), !name.isEmpty
        else { return nil }

        let isDark = (meta?.scheme?.lowercased() ?? "light") == "dark"
        let base = isDark ? Self.darkBase : Self.lightBase

        let top: UInt32
        let bottom: UInt32
        if let t = ThemeHex.parse(colors?.background?.top) {
            // One colour given → derive the second stop; both given → honor both.
            if let b = ThemeHex.parse(colors?.background?.bottom) {
                top = t; bottom = b
            } else {
                top = ThemeHex.shiftLightness(t, by: isDark ? 0.035 : 0.02)
                bottom = ThemeHex.shiftLightness(t, by: isDark ? -0.035 : -0.045)
            }
        } else {
            top = base.top; bottom = base.bottom
        }

        // §3.4 — without unlock: true the whole block is silently ignored.
        let unlocked = invariants?.unlock == true

        return CustomPalette(
            id: id,
            name: name,
            mood: meta?.mood ?? (isDark ? "custom · dark" : "custom · light"),
            isDark: isDark,
            accentHex: ThemeHex.parse(colors?.accent) ?? base.accent,
            secondaryHex: ThemeHex.parse(colors?.secondary) ?? base.secondary,
            cardHex: ThemeHex.parse(colors?.card) ?? base.card,
            backgroundTopHex: top,
            backgroundBottomHex: bottom,
            onAccentHex: ThemeHex.parse(colors?.onAccent) ?? base.onAccent,
            goldHex: unlocked ? ThemeHex.parse(invariants?.gold) : nil,
            helpHex: unlocked ? ThemeHex.parse(invariants?.help) : nil)
    }
}

// MARK: - Loader

/// The one shape a theme problem takes: a human sentence, never a crash.
public struct ThemeParseError: Error, Hashable, Sendable, CustomStringConvertible {
    public let message: String
    public var description: String { message }
}

/// One theme file as the picker sees it: valid (palette present) or listed as
/// invalid with the error — never hidden, never selectable, never a crash.
public struct LoadedUserTheme: Identifiable, Sendable {
    public var fileName: String
    public var palette: CustomPalette?
    public var error: String?

    public var id: String { palette?.id ?? fileName }
    public var displayName: String { palette?.name ?? fileName }
}

public enum ThemeLoader {
    private static let lock = NSLock()
    nonisolated(unsafe) private static var paletteCache: [String: CustomPalette]?

    /// §3.1 — the local authoring folder. iOS: Documents/Themes (visible in
    /// Files). macOS: ~/Library/Application Support/DailyBread/Themes.
    public static func themesDirectory() -> URL {
        #if os(macOS)
        let base = FileManager.default.urls(for: .applicationSupportDirectory,
                                            in: .userDomainMask)[0]
            .appendingPathComponent("DailyBread", isDirectory: true)
        #else
        let base = FileManager.default.urls(for: .documentDirectory,
                                            in: .userDomainMask)[0]
        #endif
        let dir = base.appendingPathComponent("Themes", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    /// Never throws; bad files come back as listed-but-invalid (§3.3 rule 3).
    public static func available() -> [LoadedUserTheme] {
        let dir = themesDirectory()
        let files = (try? FileManager.default.contentsOfDirectory(
            at: dir, includingPropertiesForKeys: nil)) ?? []
        var out: [LoadedUserTheme] = []
        for url in files.sorted(by: { $0.lastPathComponent < $1.lastPathComponent })
        where ["yaml", "yml"].contains(url.pathExtension.lowercased()) {
            // Reference exports of the built-ins aren't user themes; skip them
            // so the picker doesn't show every built-in twice.
            if url.lastPathComponent.hasPrefix("builtin-") { continue }
            out.append(loadFile(url))
        }
        refreshCache(from: out)
        return out
    }

    /// §3.3 rule 4: validation happens at select, not render — a broken file
    /// never becomes the active theme.
    public static func palette(id: String) -> CustomPalette? {
        lock.lock()
        let cached = paletteCache?[id]
        lock.unlock()
        if let cached { return cached }
        return available().first { $0.palette?.id == id }?.palette
    }

    public static func invalidate() {
        lock.lock()
        paletteCache = nil
        lock.unlock()
    }

    private static func refreshCache(from themes: [LoadedUserTheme]) {
        var map: [String: CustomPalette] = [:]
        for t in themes { if let p = t.palette { map[p.id] = p } }
        lock.lock()
        paletteCache = map
        lock.unlock()
    }

    private static func loadFile(_ url: URL) -> LoadedUserTheme {
        let name = url.lastPathComponent
        guard let text = try? String(contentsOf: url, encoding: .utf8) else {
            return LoadedUserTheme(fileName: name, palette: nil, error: "Couldn't read the file.")
        }
        return LoadedUserTheme(fileName: name, palette: nil, error: nil)
            .parsed(from: text)
    }

    /// Shared by the file loader and (later) the editor's live lint.
    public static func parse(_ text: String) -> Result<CustomPalette, ThemeParseError> {
        let manifest: ThemeManifest
        do {
            manifest = try YAMLDecoder().decode(ThemeManifest.self, from: text)
        } catch let error as YamlError {
            // The only true parse failure is malformed YAML — surface the line.
            return .failure(ThemeParseError(message: describe(error)))
        } catch let DecodingError.dataCorrupted(context)
            where context.underlyingError is YamlError {
            // YAMLDecoder wraps composer/scanner failures — unwrap so the line
            // number survives to the picker.
            return .failure(ThemeParseError(message: describe(context.underlyingError as! YamlError)))
        } catch {
            return .failure(ThemeParseError(message: "Couldn't read this as a theme file."))
        }
        guard let palette = manifest.resolved() else {
            return .failure(ThemeParseError(message: "meta.id and meta.name are required — everything else is optional."))
        }
        return .success(palette)
    }

    private static func describe(_ error: YamlError) -> String {
        switch error {
        case let .parser(context: _, problem: problem, mark, yaml: _):
            return "Line \(mark.line): \(problem)"
        case let .scanner(context: _, problem: problem, mark, yaml: _):
            return "Line \(mark.line): \(problem)"
        default:
            return "This file isn't valid YAML."
        }
    }

    /// example.yaml is the breadcrumb (§3.6) — deliberately readable, comments
    /// on every key. Built-ins export as `builtin-*.yaml` for reference and
    /// copying, but the app NEVER reads them back (§3.3 rule 1).
    public static func exportReferenceThemesIfNeeded() {
        let dir = themesDirectory()
        let example = dir.appendingPathComponent("example.yaml")
        guard !FileManager.default.fileExists(atPath: example.path) else { return }
        try? Self.exampleYAML.write(to: example, atomically: true, encoding: .utf8)
        for theme in DBTheme.allCases {
            let url = dir.appendingPathComponent("builtin-\(theme.rawValue).yaml")
            try? referenceYAML(for: theme).write(to: url, atomically: true, encoding: .utf8)
        }
    }

    static func referenceYAML(for theme: DBTheme) -> String {
        let palette = theme.referenceHexes
        return """
        # \(theme.displayName) — built-in reference. The app does NOT read this file;
        # it ships compiled in. Copy it, change meta.id and meta.name, make it yours.
        meta:
          id: \(theme.rawValue)-copy
          name: \(theme.displayName) copy
          mood: \(theme.mood)
          scheme: \(theme.isDark ? "dark" : "light")

        colors:
          background: { top: "\(ThemeHex.format(palette.top))", bottom: "\(ThemeHex.format(palette.bottom))" }
          card:       "\(ThemeHex.format(palette.card))"
          accent:     "\(ThemeHex.format(palette.accent))"
          secondary:  "\(ThemeHex.format(palette.secondary))"
        """
    }

    static let exampleYAML = """
    # Daily Bread theme — copy this file, change the values, pick it in
    # Settings ▸ Theme. A typo can't break anything: unknown keys are ignored
    # and missing ones fall back to the defaults for your scheme.

    meta:
      id: my-theme          # required — short, unique, no spaces
      name: My Theme        # required — what the picker shows
      mood: cool and quiet  # shows up under the name
      scheme: dark          # dark or light — the whole app follows

    colors:
      background: "#1B2340" # one colour is enough — the soft gradient is derived
      card:       "#26304F" # the cards that float on top
      accent:     "#4C8DFF" # buttons, your name, the important stuff
      secondary:  "#3FC9B0" # the little done-checks and progress fills

    # Gold means money and red means Help in every theme — that's on purpose.
    # If you really want them different, unlock them explicitly:
    #
    # invariants:
    #   unlock: true
    #   gold: "#E7B44A"
    #   help: "#F06B6B"
    """
}

private extension LoadedUserTheme {
    func parsed(from text: String) -> LoadedUserTheme {
        var copy = self
        switch ThemeLoader.parse(text) {
        case .success(let palette):
            copy.palette = palette
            copy.error = nil
        case .failure(let error):
            copy.palette = nil
            copy.error = error.message
        }
        return copy
    }
}

private extension DBTheme {
    /// The compiled values, exposed once for the reference export.
    var referenceHexes: (accent: UInt32, secondary: UInt32, card: UInt32, top: UInt32, bottom: UInt32) {
        switch self {
        case .sunroom: return (0xC7284F, 0x2E8C86, 0xFFFFFF, 0xFFFDF9, 0xFBF1E2)
        case .sky: return (0x3D7BE0, 0x4BA39C, 0xFFFFFF, 0xFBFCFF, 0xEAF1FE)
        case .rosewater: return (0xD24E86, 0x6BA3C6, 0xFFFFFF, 0xFFF9FB, 0xFBEAF1)
        case .meadow: return (0x3E9E6B, 0x8AA83E, 0xFFFFFF, 0xF8FBF6, 0xE9F4E7)
        case .mulberry: return (0xEA6E92, 0x2E8C86, 0x4A2237, 0x3E1B30, 0x2A1220)
        case .harbor: return (0x5B9BE0, 0x4BA39C, 0x2A3852, 0x223049, 0x161E2C)
        }
    }
}

// MARK: - AppTheme (what the app actually renders)

/// The resolved look: a compiled built-in or a validated user palette, with one
/// surface API. Everything that used to take `DBTheme` for surfaces takes this.
public enum AppTheme {
    case builtin(DBTheme)
    case custom(CustomPalette)

    public var isDark: Bool {
        switch self {
        case .builtin(let t): return t.isDark
        case .custom(let p): return p.isDark
        }
    }

    public var displayName: String {
        switch self {
        case .builtin(let t): return t.displayName
        case .custom(let p): return p.name
        }
    }

    public var mood: String {
        switch self {
        case .builtin(let t): return t.mood
        case .custom(let p): return p.mood
        }
    }

    public func accent(_ scheme: ColorScheme = .light) -> Color {
        switch self {
        case .builtin(let t): return t.accent(scheme)
        case .custom(let p): return p.accent
        }
    }

    public func secondary(_ scheme: ColorScheme = .light) -> Color {
        switch self {
        case .builtin(let t): return t.secondary(scheme)
        case .custom(let p): return p.secondary
        }
    }

    public var cardColor: Color {
        switch self {
        case .builtin(let t): return t.cardColor
        case .custom(let p): return p.cardColor
        }
    }

    public var cardStroke: Color {
        isDark ? Color.white.opacity(0.07) : Color.black.opacity(0.05)
    }

    public var cardShadow: Color {
        isDark ? Color.black.opacity(0.25) : Color.black.opacity(0.07)
    }

    public var backgroundGradient: LinearGradient {
        switch self {
        case .builtin(let t):
            return t.backgroundGradient
        case .custom(let p):
            return LinearGradient(
                colors: [p.backgroundTop, p.backgroundBottom],
                startPoint: .top,
                endPoint: p.isDark ? UnitPoint(x: 0.5, y: 0.6) : .bottom)
        }
    }

    public var progressGradient: LinearGradient {
        LinearGradient(colors: [accent(), Color(hex: 0xE7A83C)],
                       startPoint: .leading, endPoint: .trailing)
    }

    /// §3.4 — unlocked invariant overrides, nil in every built-in.
    public var goldOverride: Color? {
        if case .custom(let p) = self, let g = p.goldHex { return Color(hex: g) }
        return nil
    }

    public var helpOverride: Color? {
        if case .custom(let p) = self, let h = p.helpHex { return Color(hex: h) }
        return nil
    }
}
