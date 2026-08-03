import SwiftUI
import DailyBreadKit

@main
struct DailyBreadApp: App {
    @State private var session = SessionStore()
    @AppStorage(ThemeStore.key) private var themeRaw = DBTheme.sunroom.rawValue
    @AppStorage(ThemeStore.customKey) private var customRaw = ""

    /// §3.3 rule 5: a custom id that fails to load resolves to the built-in —
    /// there is no code path where a broken file reaches the render layer.
    private var theme: AppTheme { ThemeStore.resolve(builtinRaw: themeRaw, customId: customRaw) }

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(session)
                .themedTint(theme)
                // The chosen theme owns the whole appearance: a dark theme forces
                // dark, a light theme forces light — so her pick always looks the
                // way it looked in the picker, regardless of the system setting.
                .preferredColorScheme(theme.isDark ? .dark : .light)
                .task {
                    // example.yaml + builtin references — the breadcrumb (§3.6).
                    ThemeLoader.exportReferenceThemesIfNeeded()
                    await session.bootstrap()
                    // §3.1 — pull the family's themes, push local authoring.
                    await ThemeSync.sync(session.client)
                }
        }
        #if os(macOS)
        .defaultSize(width: 1000, height: 700)
        #endif
    }
}

/// Applies the theme accent for the current appearance.
///
/// This tint is load-bearing: it is why every stock control is coloured right in every
/// theme. Use bare `Color.accentColor` for accent. Reach for `DB.*` only for invariants:
/// money, blessing, help, done, rarity, night.
private struct ThemedTint: ViewModifier {
    let theme: AppTheme
    @Environment(\.colorScheme) private var scheme

    func body(content: Content) -> some View {
        content.tint(theme.accent(scheme))
    }
}

extension View {
    func themedTint(_ theme: AppTheme) -> some View {
        modifier(ThemedTint(theme: theme))
    }
}
