import SwiftUI

// Daily Bread's look. Color is warmth, and warmth is the point: every theme owns the WHOLE
// surface — its background, its cards, its accent — not just a tint over grey. Themes are hers
// to pick; the app follows instantly. Invariants that never change across themes: gold = money,
// red = the Help alert. Those two carry meaning and must stay legible in every palette.

public extension Color {
    init(hex: UInt32) {
        self.init(
            red: Double((hex >> 16) & 0xFF) / 255,
            green: Double((hex >> 8) & 0xFF) / 255,
            blue: Double(hex & 0xFF) / 255)
    }
}

/// The palettes she can choose from. Each is a full look — light or dark — with its own warm
/// background, card surface, and accent. Sunroom is the default.
public enum DBTheme: String, CaseIterable, Identifiable, Sendable {
    case sunroom      // raspberry + teal, warm white  (default)
    case sky          // soft blue, warm white
    case rosewater    // rose/pink, warm white
    case meadow       // garden green, warm white
    case mulberry     // raspberry + teal, soft plum   (dark)
    case harbor       // calm blue, deep evening        (dark)

    public var id: String { rawValue }

    public var displayName: String {
        switch self {
        case .sunroom: return "Sunroom"
        case .sky: return "Sky"
        case .rosewater: return "Rosewater"
        case .meadow: return "Meadow"
        case .mulberry: return "Mulberry"
        case .harbor: return "Harbor"
        }
    }

    /// A one-word feel, shown under the name in the picker.
    public var mood: String {
        switch self {
        case .sunroom: return "warm · light"
        case .sky: return "calm · light"
        case .rosewater: return "soft · light"
        case .meadow: return "fresh · light"
        case .mulberry: return "cozy · dark"
        case .harbor: return "quiet · dark"
        }
    }

    public var isDark: Bool {
        switch self {
        case .mulberry, .harbor: return true
        default: return false
        }
    }

    /// The interactive accent (buttons, links, selection). Scheme is accepted for API
    /// compatibility; each theme forces its own appearance, so the accent is fixed per theme.
    public func accent(_ scheme: ColorScheme = .light) -> Color {
        switch self {
        case .sunroom: return Color(hex: 0xC7284F)
        case .sky: return Color(hex: 0x3D7BE0)
        case .rosewater: return Color(hex: 0xD24E86)
        case .meadow: return Color(hex: 0x3E9E6B)
        case .mulberry: return Color(hex: 0xEA6E92)
        case .harbor: return Color(hex: 0x5B9BE0)
        }
    }

    /// A gentle second colour used for soft "done/positive" touches (kept distinct from the
    /// money-gold and the Help-red).
    public func secondary(_ scheme: ColorScheme = .light) -> Color {
        switch self {
        case .sunroom, .mulberry: return Color(hex: 0x2E8C86) // teal
        case .sky, .harbor: return Color(hex: 0x4BA39C)       // sea-teal
        case .rosewater: return Color(hex: 0x6BA3C6)          // soft blue
        case .meadow: return Color(hex: 0x8AA83E)             // leaf
        }
    }

    /// The whole-screen background — a soft warm gradient, never flat.
    public var backgroundGradient: LinearGradient {
        let (top, bottom): (UInt32, UInt32) = {
            switch self {
            case .sunroom: return (0xFFFDF9, 0xFBF1E2)
            case .sky: return (0xFBFCFF, 0xEAF1FE)
            case .rosewater: return (0xFFF9FB, 0xFBEAF1)
            case .meadow: return (0xF8FBF6, 0xE9F4E7)
            case .mulberry: return (0x3E1B30, 0x2A1220)
            case .harbor: return (0x223049, 0x161E2C)
            }
        }()
        return LinearGradient(
            colors: [Color(hex: top), Color(hex: bottom)],
            startPoint: .top,
            endPoint: isDark ? UnitPoint(x: 0.5, y: 0.6) : .bottom)
    }

    /// The card surface that floats on the background.
    public var cardColor: Color {
        switch self {
        case .mulberry: return Color(hex: 0x4A2237)
        case .harbor: return Color(hex: 0x2A3852)
        default: return .white
        }
    }

    public var cardStroke: Color {
        isDark ? Color.white.opacity(0.07) : Color.black.opacity(0.05)
    }

    public var cardShadow: Color {
        isDark ? Color.black.opacity(0.25) : Color.black.opacity(0.07)
    }

    /// Progress-glow gradient — kept within ONE warm family so it never muddies (the fix to the
    /// two-hue bar): the accent deepening into gold-warm.
    public var progressGradient: LinearGradient {
        LinearGradient(
            colors: [accent(), Color(hex: 0xE7A83C)],
            startPoint: .leading,
            endPoint: .trailing)
    }
}

/// Semantic invariants — the same meaning in every palette. Gold = money, red = Help alert.
public enum DB {
    public static func gold(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(hex: 0xE7B44A) : Color(hex: 0xC98A1E)
    }

    /// The Approve/Blessing glow.
    public static func glow(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(hex: 0xF0C868) : Color(hex: 0xE0A21E)
    }

    /// Help / errors — reserved. Kept clearly distinct from any theme accent so the alert never
    /// hides in a pink or berry palette.
    public static func help(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(hex: 0xF06B6B) : Color(hex: 0xD1363B)
    }

    public static func success(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(hex: 0x86C08F) : Color(hex: 0x2E9E63)
    }

    // Fill levels — the only opacities that may mean "off"/inactive. Three levels, no in-betweens.
    // Scheme is accepted for API consistency; Color.secondary already resolves per scheme.

    /// 0.06 — large surfaces (unselected day cells, big quiet areas).
    public static func fillSubtle(_ scheme: ColorScheme) -> Color {
        Color.secondary.opacity(0.06)
    }

    /// 0.12 — control backgrounds, unselected chips and toggles.
    public static func fillOff(_ scheme: ColorScheme) -> Color {
        Color.secondary.opacity(0.12)
    }

    /// 0.32 — pressed / prominent fills and disabled-but-visible chrome.
    public static func fillStrong(_ scheme: ColorScheme) -> Color {
        Color.secondary.opacity(0.32)
    }

    /// Night driving — a fact about a drive, not a reward, so it is the quietest hue in the
    /// app: deliberately duller than every theme accent and every rarity ("dusk slate").
    public static func night(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(hex: 0x8D97D8) : Color(hex: 0x5560A8)
    }
}

/// Achievement rarity — invariant across themes so rarity means the same thing in every
/// palette. Uncommon borrows the success invariant, legendary the money gold; the rest are
/// fixed hexes. Not themeable, not YAML-overridable.
public enum DBRarity: String, Codable, CaseIterable, Sendable {
    case common, uncommon, rare, epic, legendary

    /// Lenient: accepts any casing ("Rare", "RARE"); unknown strings read as common.
    public init(_ raw: String) {
        self = DBRarity(rawValue: raw.lowercased()) ?? .common
    }

    public func color(_ scheme: ColorScheme) -> Color {
        switch self {
        case .common: return Color(hex: 0x8A8F98)
        case .uncommon: return DB.success(scheme)
        case .rare: return Color(hex: 0x3B82D6)
        case .epic: return Color(hex: 0x7A5AF8)
        case .legendary: return DB.gold(scheme)
        }
    }
}

/// Reads the currently chosen theme from storage (used by the theme-aware
/// modifiers). §3: selection is two keys — the built-in raw value, and an
/// optional custom-theme id that wins when set AND loads. A custom id that
/// fails to resolve falls back to the built-in (last-known-good, §3.3 rule 5)
/// and records why, so the picker can show a dismissible banner.
public enum ThemeStore {
    public static let key = "db.theme"
    public static let customKey = "db.theme.custom"

    public static var current: DBTheme {
        DBTheme(rawValue: UserDefaults.standard.string(forKey: key) ?? "") ?? .sunroom
    }

    // Resolving is on the hottest path in the app: `Color.dbAccent` goes
    // through it, and that is read dozens of times per render across every
    // view — so it must be a pure cached read. It has now been the app's
    // freeze twice. First it hit the disk (ThemeLoader re-scanned the Themes
    // folder on every cache miss). Then, with a selected theme id that had no
    // file behind it, it WROTE to UserDefaults from inside `body` — recording
    // the fallback message for the picker's banner. Mutating state during a
    // view update is undefined behaviour, and the observed behaviour was the
    // AttributeGraph re-dirtying itself forever: SettingsView (which showed
    // that message) re-rendered in a tight loop, the main thread never idled
    // again, and every touch on the screen was dropped. Caught by sample(1)
    // with the walker holding Settings open. Resolve never writes anything;
    // the banner derives its message with fallbackDescription at read time.
    private nonisolated(unsafe) static var resolvedCache: [String: AppTheme] = [:]
    private static let resolveLock = NSLock()

    public static func resolve(builtinRaw: String, customId: String) -> AppTheme {
        // Both selection keys in one memo key; a dictionary rather than a
        // single slot so call sites with different pairs can't thrash it.
        let cacheKey = builtinRaw + "\u{1}" + customId
        resolveLock.lock()
        if let cached = resolvedCache[cacheKey] {
            resolveLock.unlock()
            return cached
        }
        resolveLock.unlock()

        let builtin = AppTheme.builtin(DBTheme(rawValue: builtinRaw) ?? .sunroom)
        let resolved: AppTheme
        if customId.isEmpty {
            resolved = builtin
        } else if let palette = ThemeLoader.palette(id: customId) {
            resolved = .custom(palette)
        } else {
            resolved = builtin
        }

        resolveLock.lock()
        resolvedCache[cacheKey] = resolved
        resolveLock.unlock()
        return resolved
    }

    /// §3.3 rule 5's banner text, derived on demand: non-nil exactly while a
    /// selected custom theme can't be loaded. Nothing is stored, so the banner
    /// clears itself the moment the theme file appears (say, via sync) — and
    /// there's no render-path write to wedge the view graph.
    public static func fallbackDescription(builtinRaw: String, customId: String) -> String? {
        guard !customId.isEmpty, ThemeLoader.palette(id: customId) == nil else { return nil }
        let builtin = AppTheme.builtin(DBTheme(rawValue: builtinRaw) ?? .sunroom)
        return "The theme \u{201C}\(customId)\u{201D} couldn't be loaded, so you're back on \(builtin.displayName)."
    }

    /// Drop the memo — the theme files changed underneath us.
    public static func invalidateResolved() {
        resolveLock.lock()
        resolvedCache = [:]
        resolveLock.unlock()
    }

    public static var resolvedCurrent: AppTheme {
        resolve(builtinRaw: UserDefaults.standard.string(forKey: key) ?? "",
                customId: UserDefaults.standard.string(forKey: customKey) ?? "")
    }
}

/// Card treatment — a soft, elevated surface in the theme's card colour.
public struct GlassCard: ViewModifier {
    @AppStorage(ThemeStore.key) private var themeRaw = DBTheme.sunroom.rawValue
    @AppStorage(ThemeStore.customKey) private var customRaw = ""
    var padding: CGFloat

    private var theme: AppTheme { ThemeStore.resolve(builtinRaw: themeRaw, customId: customRaw) }

    public func body(content: Content) -> some View {
        content
            .padding(padding)
            .background(theme.cardColor, in: RoundedRectangle(cornerRadius: 20, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: 20, style: .continuous)
                    .strokeBorder(theme.cardStroke, lineWidth: 0.5))
            .shadow(color: theme.cardShadow, radius: 10, y: 3)
    }
}

public extension View {
    func glassCard(padding: CGFloat = 14) -> some View {
        modifier(GlassCard(padding: padding))
    }
}

/// Screen background — the chosen theme's warm gradient.
public struct ThemeBackground: ViewModifier {
    @AppStorage(ThemeStore.key) private var themeRaw = DBTheme.sunroom.rawValue
    @AppStorage(ThemeStore.customKey) private var customRaw = ""

    private var theme: AppTheme { ThemeStore.resolve(builtinRaw: themeRaw, customId: customRaw) }

    public func body(content: Content) -> some View {
        content
            .scrollContentBackground(.hidden)
            .background(theme.backgroundGradient.ignoresSafeArea())
    }
}

public extension View {
    func themeBackground() -> some View {
        modifier(ThemeBackground())
    }
}

/// Re-runs an action whenever the app returns to the foreground.
public struct RefreshOnForeground: ViewModifier {
    @Environment(\.scenePhase) private var scenePhase
    let action: @Sendable () async -> Void

    public func body(content: Content) -> some View {
        content.onChange(of: scenePhase) { _, newPhase in
            if newPhase == .active {
                Task { await action() }
            }
        }
    }
}

public extension View {
    func refreshOnForeground(_ action: @escaping @Sendable () async -> Void) -> some View {
        modifier(RefreshOnForeground(action: action))
    }
}

/// Haptics: no-ops on macOS so call sites stay clean.
public enum Haptics {
    public static func success() {
        #if os(iOS)
        UINotificationFeedbackGenerator().notificationOccurred(.success)
        #endif
    }

    public static func warning() {
        #if os(iOS)
        UINotificationFeedbackGenerator().notificationOccurred(.warning)
        #endif
    }

    public static func tick() {
        #if os(iOS)
        UIImpactFeedbackGenerator(style: .light).impactOccurred()
        #endif
    }

    /// The check moment. Checking earns a haptic; undo is deliberately silent.
    public static func rigid() {
        #if os(iOS)
        UIImpactFeedbackGenerator(style: .rigid).impactOccurred()
        #endif
    }
}

/// Re-runs an action on an interval while the app is frontmost and the view is
/// on screen — the app's only live-refresh mechanism.
///
/// Daily Bread has no push and no sockets: a screen learns about changes made
/// elsewhere (the other parent's phone, a kid on the Mac) only when it asks
/// again. Without this it asks on appear and on foreground, which is why a
/// chore ticked on the Mac wouldn't show on the phone until you navigated away
/// and back.
///
/// Deliberately cheap and well-behaved:
/// - Keyed on `scenePhase`, so backgrounding CANCELS the loop rather than
///   polling a server from a phone in a pocket. Returning restarts it.
/// - Sleeps first, so it never doubles up with the view's own initial load.
/// - Cancelled automatically when the view goes away (it's a `.task`).
/// - `isPaused` lets a screen hold the poll off while a mutation is in flight,
///   so a refresh can't stomp an optimistic row mid-tap.
public struct Poll: ViewModifier {
    @Environment(\.scenePhase) private var scenePhase

    let interval: Duration
    let isPaused: () -> Bool
    let action: @Sendable () async -> Void

    public func body(content: Content) -> some View {
        content.task(id: scenePhase) {
            guard scenePhase == .active else { return }
            while !Task.isCancelled {
                try? await Task.sleep(for: interval)
                guard !Task.isCancelled, scenePhase == .active else { return }
                if isPaused() { continue }
                await action()
            }
        }
    }
}

public extension View {
    /// 30s is the compromise: fast enough that handing the iPad over feels live,
    /// slow enough to be invisible on a home server and a battery.
    func poll(every interval: Duration = .seconds(30),
              isPaused: @escaping () -> Bool = { false },
              _ action: @escaping @Sendable () async -> Void) -> some View {
        modifier(Poll(interval: interval, isPaused: isPaused, action: action))
    }
}

public extension Color {
    /// The ACTIVE THEME's accent. Use this — never `Color.accentColor`.
    ///
    /// On macOS `Color.accentColor` resolves to `NSColor.controlAccentColor`,
    /// i.e. whatever the user picked in System Settings, and it ignores the
    /// `.tint()` applied at the app root. iOS honours the tint, so this went
    /// unnoticed: the Mac app rendered a blue avatar, blue sidebar and blue
    /// buttons inside a Mulberry theme, while the handful of places that asked
    /// the theme directly stayed correctly pink.
    ///
    /// Cache-first, so this is cheap enough to read during layout: a custom
    /// theme resolves from ThemeLoader's in-memory palette cache, which the app
    /// warms at bootstrap.
    static var dbAccent: Color { ThemeStore.resolvedCurrent.accent() }

    /// The theme's secondary — same reasoning as `dbAccent`.
    static var dbSecondary: Color { ThemeStore.resolvedCurrent.secondary() }
}
