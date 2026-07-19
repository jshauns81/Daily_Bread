import SwiftUI
import DailyBreadKit

@main
struct DailyBreadApp: App {
    @State private var session = SessionStore()
    @AppStorage("db.theme") private var themeRaw = DBTheme.guadalupe.rawValue

    private var theme: DBTheme { DBTheme(rawValue: themeRaw) ?? .guadalupe }

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(session)
                .themedTint(theme)
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
