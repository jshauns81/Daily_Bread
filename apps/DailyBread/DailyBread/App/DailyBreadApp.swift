import SwiftUI
import DailyBreadKit

@main
struct DailyBreadApp: App {
    @State private var session = SessionStore()
    @AppStorage(ThemeStore.key) private var themeRaw = DBTheme.sunroom.rawValue
    @AppStorage(ThemeStore.customKey) private var customRaw = ""
    @Environment(\.scenePhase) private var scenePhase

    /// §3.3 rule 5: a custom id that fails to load resolves to the built-in —
    /// there is no code path where a broken file reaches the render layer.
    private var theme: AppTheme { ThemeStore.resolve(builtinRaw: themeRaw, customId: customRaw) }

    var body: some Scene {
        WindowGroup {
            RootView()
                // A restored-narrow window squeezed the sidebar until every
                // label was an ellipsis. defaultSize only applies to a FIRST
                // launch; a floor applies to every one, including a window
                // macOS restores from a previous session.
                #if os(macOS)
                .frame(minWidth: 860, minHeight: 560)
                #endif
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
                }
                // §3.1 — pull MY themes, push local authoring (user-bound).
                //
                // Driven off the user rather than called once after bootstrap:
                // a device with the session sealed behind biometry comes back
                // from bootstrap `.locked`, where `currentUser` is nil, and the
                // launch `.task` does not run again for the life of the process.
                // Called there alone, turning on "Protect my session" silently
                // cost this device theme sync forever.
                .onChange(of: session.currentUser?.userId, initial: true) { _, userId in
                    guard let userId else { return }
                    Task { await ThemeSync.sync(session.client, userId: userId) }
                }
                // The parent gate's re-lock triggers. RootView is the only door
                // onto SettingsView today; a future macOS `Settings` scene or a
                // `.commands` entry would be a SECOND door onto it, and would
                // need its own `parentGate.isLocked` check.
                #if os(iOS)
                .onChange(of: scenePhase) { _, phase in
                    switch phase {
                    case .background: session.parentGate.sceneWentToBackground()
                    case .inactive: session.parentGate.sceneWentInactive()
                    case .active: session.parentGate.sceneBecameActive()
                    @unknown default: break
                    }
                }
                #endif
                #if os(macOS)
                // Locking your Mac locks parent mode — that is the correct Mac
                // semantic. A window merely losing focus is NOT "walked away":
                // `didResignActive` is deliberately not observed, so alt-tabbing
                // to Xcode for twenty seconds cannot re-lock.
                //
                // Screen lock needs all three of these. `screensDidSleep` is
                // display sleep, `sessionDidResignActive` is fast user
                // switching, and NEITHER fires for ⌃⌘Q or a hot corner with the
                // display still lit — the one gesture a parent actually makes
                // when walking away from a desk. Only the distributed
                // `com.apple.screenIsLocked` covers that, and it is not on
                // NSWorkspace's own center.
                .onReceive(DistributedNotificationCenter.default().publisher(
                    for: Notification.Name("com.apple.screenIsLocked"))) { _ in
                    session.parentGate.hardLock()
                }
                .onReceive(NSWorkspace.shared.notificationCenter.publisher(
                    for: NSWorkspace.screensDidSleepNotification)) { _ in
                    session.parentGate.hardLock()
                }
                .onReceive(NSWorkspace.shared.notificationCenter.publisher(
                    for: NSWorkspace.sessionDidResignActiveNotification)) { _ in
                    session.parentGate.hardLock()
                }
                // ⌘W is "I'm done here" on a Mac, and closing the last window
                // does not end the process: `session` is @State on the App, so
                // reopening from the Dock rebuilds RootView against a gate that
                // is still unlocked. Sheets and panels are NSWindows too and
                // close constantly in normal use, so only real windows count.
                .onReceive(NotificationCenter.default.publisher(
                    for: NSWindow.willCloseNotification)) { note in
                    guard let window = note.object as? NSWindow,
                          !window.isSheet, !(window is NSPanel) else { return }
                    session.parentGate.hardLock()
                }
                .onReceive(NotificationCenter.default.publisher(
                    for: NSApplication.didBecomeActiveNotification)) { _ in
                    // A parent can enrol Touch ID without relaunching.
                    session.parentGate.refreshCapability()
                }
                #endif
        }
        #if os(macOS)
        .defaultSize(width: 1000, height: 700)
        #endif
    }
}

/// Applies the theme accent for the current appearance.
///
/// This tint is load-bearing: it is why every stock control is coloured right in every
/// theme. Use bare `Color.dbAccent` for accent. Reach for `DB.*` only for invariants:
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
