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
}

/// Reads the currently chosen theme from storage (used by the theme-aware modifiers).
enum ThemeStore {
    static let key = "db.theme"
    static var current: DBTheme {
        DBTheme(rawValue: UserDefaults.standard.string(forKey: key) ?? "") ?? .sunroom
    }
}

/// Card treatment — a soft, elevated surface in the theme's card colour.
public struct GlassCard: ViewModifier {
    @AppStorage(ThemeStore.key) private var themeRaw = DBTheme.sunroom.rawValue
    var padding: CGFloat

    private var theme: DBTheme { DBTheme(rawValue: themeRaw) ?? .sunroom }

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

/// Screen background — the chosen theme's warm gradient. (Name kept for call-site stability.)
public struct GraphiteBackground: ViewModifier {
    @AppStorage(ThemeStore.key) private var themeRaw = DBTheme.sunroom.rawValue

    private var theme: DBTheme { DBTheme(rawValue: themeRaw) ?? .sunroom }

    public func body(content: Content) -> some View {
        content
            .scrollContentBackground(.hidden)
            .background(theme.backgroundGradient.ignoresSafeArea())
    }
}

public extension View {
    func graphiteBackground() -> some View {
        modifier(GraphiteBackground())
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
}
