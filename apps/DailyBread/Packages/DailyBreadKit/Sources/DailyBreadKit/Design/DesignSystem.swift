import SwiftUI

// Graphite & Glass: surfaces belong to the system, color belongs to
// Daily Bread. Themes are ACCENT TINTS only — surfaces never change per
// theme. Invariants (never themed away): gold = money, gold-glow = the
// Approve moment, red = Help.
//
// Neutrals here are starting values approximating system materials — do the
// real-device pass (plan §4) before treating any neutral as final; they all
// live in this one file on purpose.

public extension Color {
    init(hex: UInt32) {
        self.init(
            red: Double((hex >> 16) & 0xFF) / 255,
            green: Double((hex >> 8) & 0xFF) / 255,
            blue: Double(hex & 0xFF) / 255)
    }
}

/// The five themes — same names as the web app, accents only.
public enum DBTheme: String, CaseIterable, Identifiable, Sendable {
    case guadalupe, sea, garden, violet, rosa

    public var id: String { rawValue }

    public var displayName: String {
        switch self {
        case .guadalupe: return "Guadalupe"
        case .sea: return "Sea"
        case .garden: return "Garden"
        case .violet: return "Violet"
        case .rosa: return "Rose"
        }
    }

    /// Accent for the current appearance (light needs deeper steps on white).
    public func accent(_ scheme: ColorScheme) -> Color {
        switch (self, scheme) {
        case (.guadalupe, .dark): return Color(hex: 0x4DA8C6)
        case (.guadalupe, _): return Color(hex: 0x0E8FC4)
        case (.sea, .dark): return Color(hex: 0x6A82E6)
        case (.sea, _): return Color(hex: 0x3A5BD0)
        case (.garden, .dark): return Color(hex: 0x5AAE7B)
        case (.garden, _): return Color(hex: 0x2E9E63)
        case (.violet, .dark): return Color(hex: 0x9B7BE0)
        case (.violet, _): return Color(hex: 0x7A4FD0)
        case (.rosa, .dark): return Color(hex: 0xE08AA6)
        case (.rosa, _): return Color(hex: 0xD14E7E)
        }
    }
}

/// Semantic colors — the invariants. Per-mode values match the web app's
/// shipped constants so both clients agree on what gold and red mean.
public enum DB {
    public static func gold(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(hex: 0xD7A23F) : Color(hex: 0xD99514)
    }

    /// The Blessing glow — the Approve moment only.
    public static func glow(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(hex: 0xEAC468) : Color(hex: 0xF2B705)
    }

    /// Help / errors. Reserved: nothing else in the app is ever red.
    public static func help(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(hex: 0xE0655C) : Color(hex: 0xD33B4E)
    }

    public static func success(_ scheme: ColorScheme) -> Color {
        scheme == .dark ? Color(hex: 0x84B98F) : Color(hex: 0x3E9E63)
    }
}

/// Card treatment: rely on system materials so Liquid Glass does the work.
public struct GlassCard: ViewModifier {
    var padding: CGFloat

    public func body(content: Content) -> some View {
        content
            .padding(padding)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 20, style: .continuous))
    }
}

public extension View {
    func glassCard(padding: CGFloat = 14) -> some View {
        modifier(GlassCard(padding: padding))
    }
}

/// Screen background: graphite, not OLED black. A soft glow at the top,
/// settling into deep graphite — matches the approved concept. Hides the
/// system list background so every screen sits on the same surface.
public struct GraphiteBackground: ViewModifier {
    @Environment(\.colorScheme) private var scheme

    public func body(content: Content) -> some View {
        content
            .scrollContentBackground(.hidden)
            .background(background.ignoresSafeArea())
    }

    @ViewBuilder
    private var background: some View {
        if scheme == .dark {
            LinearGradient(
                colors: [Color(hex: 0x1A1A1E), Color(hex: 0x0E0E10)],
                startPoint: .top,
                endPoint: UnitPoint(x: 0.5, y: 0.55))
        } else {
            Color(hex: 0xF2F2F6)
        }
    }
}

public extension View {
    func graphiteBackground() -> some View {
        modifier(GraphiteBackground())
    }
}

/// Re-runs an action whenever the app returns to the foreground —
/// stale data reads as "broken", so every screen refreshes on wake.
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
