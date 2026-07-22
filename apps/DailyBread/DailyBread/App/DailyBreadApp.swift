import SwiftUI
import DailyBreadKit

@main
struct DailyBreadApp: App {
    @State private var session = SessionStore()
    @AppStorage("db.theme") private var themeRaw = DBTheme.sunroom.rawValue

    private var theme: DBTheme { DBTheme(rawValue: themeRaw) ?? .sunroom }

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(session)
                .themedTint(theme)
                // The chosen theme owns the whole appearance: a dark theme forces
                // dark, a light theme forces light — so her pick always looks the
                // way it looked in the picker, regardless of the system setting.
                .preferredColorScheme(theme.isDark ? .dark : .light)
                .task { await session.bootstrap() }
        }
        #if os(macOS)
        .defaultSize(width: 1000, height: 700)
        #endif
    }
}

/// Applies the theme accent for the current appearance.
private struct ThemedTint: ViewModifier {
    let theme: DBTheme
    @Environment(\.colorScheme) private var scheme

    func body(content: Content) -> some View {
        content.tint(theme.accent(scheme))
    }
}

extension View {
    func themedTint(_ theme: DBTheme) -> some View {
        modifier(ThemedTint(theme: theme))
    }
}
