import SwiftUI
import DailyBreadKit

/// The wall. An opaque screen between `RootView` and `MainView`: a parent is
/// either past it or sees nothing but it, "Sign in with your password", and
/// "Change server".
///
/// It sits above `MainView` rather than inside it on purpose. `MainView` is
/// never constructed while locked, so its `.task`, `.poll` and
/// `.refreshOnForeground` never run, pushed destinations cannot outlive a
/// re-lock, presented sheets go with them, and ⌘1…⌘N reach nothing. It is also
/// where any future deep link, App Intent or `onOpenURL` would land, so those
/// arrive gated by construction rather than by somebody remembering.
struct GateWallView: View {
    enum Mode: Equatable {
        /// The session itself is sealed (job 2) — unlocking loads the tokens.
        case session(ApiUser)
        /// The session is live; the parent shell is what's behind the wall.
        case shell(ApiUser)
    }

    let mode: Mode

    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.scenePhase) private var scenePhase

    @State private var didAutoPrompt = false
    @State private var message: String?

    private var gate: ParentGate { session.parentGate }

    /// An evaluation is up. `.session` mode authenticates against the Keychain
    /// ACL rather than through `ParentGate`, so that type's own flag never moves
    /// there — reading only it left the Unlock button live and undimmed under
    /// the auto-prompt, which is one tap away from two stacked Face ID sheets.
    private var busy: Bool { gate.authenticating || session.isUnlockingSession }

    private var user: ApiUser {
        switch mode {
        case .session(let user), .shell(let user): return user
        }
    }

    /// The explanatory panel, shown until a parent has passed the gate once.
    /// It has no "Not now": between a TestFlight update installing and the
    /// parent first opening the app there is a window in which a child could
    /// tap it, and a one-tap permanent disable sitting on an unlocked phone is
    /// exactly the hole this feature closes. The way off is inside, past a
    /// match, in Settings → Security.
    private var showsIntro: Bool {
        if case .session = mode { return false }
        return !gate.hasBeenIntroduced(to: user)
    }

    var body: some View {
        VStack(spacing: 24) {
            Spacer()

            VStack(spacing: 8) {
                Text("🍞").font(.system(size: 56))
                Text("Daily Bread")
                    .font(.largeTitle.weight(.bold))
            }

            card
                .glassCard(padding: 20)
                .frame(maxWidth: 420)
                #if os(macOS)
                .frame(minWidth: 420, idealWidth: 460)
                #endif

            escapes

            Spacer()
            Spacer()
        }
        .padding()
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .themeBackground()
        .task { await autoPromptIfNeeded() }
        #if os(iOS)
        .onChange(of: scenePhase) { _, phase in
            switch phase {
            // The wall is built DURING the `.background` transition — that is
            // the only thing that raises it on iOS — so `.task` runs while the
            // app is on its way out. Re-arming here is what makes tapping back
            // into the app produce Face ID rather than "tap Unlock, then Face
            // ID" for the rest of the process.
            case .background: didAutoPrompt = false
            case .active: Task { await autoPromptIfNeeded() }
            default: break
            }
        }
        #endif
    }

    // MARK: - The card

    @ViewBuilder
    private var card: some View {
        VStack(spacing: 14) {
            Image(systemName: showsIntro ? "lock.shield" : gate.capability.symbolName)
                .font(.system(size: 40))
                .foregroundStyle(Color.dbAccent)
                .accessibilityHidden(true)

            Text(title)
                .font(.title3.weight(.semibold))
                .multilineTextAlignment(.center)

            Text(explanation)
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)

            Button {
                Task { await attempt() }
            } label: {
                Group {
                    if busy {
                        ProgressView()
                    } else {
                        Text(buttonTitle)
                    }
                }
                .frame(maxWidth: .infinity, minHeight: 44)
                .contentShape(Rectangle())
            }
            .buttonStyle(.borderedProminent)
            .tint(Color.dbAccent)
            .disabled(busy)

            if showsIntro {
                Text("You can change this in Settings → Security.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
            }

            // Inline, never an alert: the app has exactly one alert and this
            // must not become its second.
            if let message {
                Label(message, systemImage: "exclamationmark.circle")
                    .font(.footnote)
                    .foregroundStyle(DB.help(scheme))
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
    }

    /// Always drawn, always ungated. A parent whose sensor has failed needs a
    /// road back into their own app; the cost of a child using it is one
    /// password entry, and it grants nothing.
    private var escapes: some View {
        HStack(spacing: 16) {
            Button("Sign in with your password") {
                Task { await session.signOut() }
            }
            Text("·").foregroundStyle(.secondary)
            Button("Change server") {
                Task { await session.forgetServer() }
            }
        }
        .font(.footnote)
        .foregroundStyle(.secondary)
    }

    // MARK: - Copy

    private var title: String {
        switch mode {
        case .session: return "Welcome back, \(user.name)"
        case .shell: return showsIntro ? "Parent screens are protected" : "Daily Bread is locked"
        }
    }

    private var explanation: String {
        let name = gate.capability.displayName
        switch mode {
        case .session:
            return "Your sign-in is kept behind \(name)."
        case .shell where showsIntro:
            return "Approvals, the money and family settings now ask for \(name) first, so an unlocked \(deviceNoun) isn't an open till."
        case .shell:
            return gate.capability.kind == nil
                ? "Parent screens need your password."
                : "Parent screens need \(name)."
        }
    }

    private var buttonTitle: String {
        guard showsIntro else { return "Unlock" }
        return gate.capability.kind == nil ? "Unlock with your password"
                                           : "Unlock with \(gate.capability.displayName)"
    }

    private var deviceNoun: String {
        #if os(macOS)
        "Mac"
        #else
        "phone"
        #endif
    }

    // MARK: - Behaviour

    /// One prompt per appearance, and never one that cannot succeed.
    ///
    /// `evaluatePolicy` from an app that is not frontmost resolves straight to
    /// `.appCancel` / `.notInteractive`, so firing it on the way to the
    /// background spends the appearance's single auto-prompt on an evaluation
    /// nobody could have answered — and `didAutoPrompt` latches, so every later
    /// return shows the wall with no sheet at all.
    ///
    /// A cancel deliberately does NOT re-arm it: auto-prompting on cancel is a
    /// loop the user cannot escape.
    private func autoPromptIfNeeded() async {
        #if os(iOS)
        guard scenePhase == .active else { return }
        #endif
        guard !didAutoPrompt, !showsIntro, !busy else { return }
        didAutoPrompt = true
        await attempt()
    }

    private func attempt() async {
        let outcome: BiometricOutcome
        switch mode {
        case .session:
            outcome = await session.unlockSession()
        case .shell:
            outcome = await gate.unlock(for: user, reason: "unlock parent screens")
        }
        let note = Self.message(for: outcome, capability: gate.capability)
        if reduceMotion {
            message = note
        } else {
            withAnimation(.easeInOut(duration: 0.2)) { message = note }
        }
    }

    /// Cancelling is a choice, not a failure — an error row that appears every
    /// time somebody dismisses a sheet trains people to ignore error rows.
    private static func message(for outcome: BiometricOutcome,
                                capability: BiometricCapability) -> String? {
        switch outcome {
        case .success, .cancelled, .invalidated:
            return nil
        case .failed:
            return "That didn't match. Try again."
        case .lockedOut:
            // Recovery is the OS's own: unlocking the device resets Apple's
            // counter. There is deliberately no in-app passcode button —
            // offering one would let a child fail five times on purpose and
            // walk straight in.
            return "\(capability.displayName) is locked. Unlock your \(deviceName) with its passcode, then try again."
        case .unavailable:
            return "This device can't check who you are. Sign in with your password."
        case .error:
            return "Couldn't check who you are. Try again, or sign in with your password."
        }
    }

    private static var deviceName: String {
        #if os(macOS)
        "Mac"
        #else
        "iPhone"
        #endif
    }
}

/// The app-switcher cover. Not a lock — it hides content without asking for
/// anything, so the snapshot iOS takes on `.inactive` is this rather than every
/// child's balance.
struct PrivacyCoverView: View {
    var body: some View {
        VStack(spacing: 8) {
            Text("🍞").font(.system(size: 56))
            Text("Daily Bread")
                .font(.largeTitle.weight(.bold))
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .themeBackground()
    }
}

/// Holds the idle timer off while a sheet with typed free text is open.
/// Suppresses only the idle path — backgrounding and macOS hard locks still
/// tear the wall down immediately.
struct ParentGateHold: ViewModifier {
    @Environment(SessionStore.self) private var session

    func body(content: Content) -> some View {
        content
            .onAppear { session.parentGate.beginHold() }
            .onDisappear { session.parentGate.endHold() }
    }
}

extension View {
    func parentGateHold() -> some View {
        modifier(ParentGateHold())
    }
}
