import SwiftUI
import DailyBreadKit

struct SettingsView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @AppStorage(ThemeStore.key) private var themeRaw = DBTheme.sunroom.rawValue
    @AppStorage(ThemeStore.customKey) private var customRaw = ""
    @State private var themeExpanded = false
    /// §3: user themes from the Themes folder — valid ones selectable, broken
    /// ones listed with their error, never hidden and never selectable.
    @State private var userThemes: [LoadedUserTheme] = []
    /// §3.3 rule 7 — selecting PREVIEWS live; it isn't persisted until
    /// confirmed, and backing out restores what was there before.
    @State private var pending: (builtin: String, custom: String)?
    @State private var editorTarget: ThemeEditorTarget?
    /// Children, for the per-child driving switches. Parent-only fetch.
    @State private var children: [FamilyMember] = []
    @State private var securityError: String?
    /// Optimistic switch positions, held only until the prompt behind them
    /// resolves. Non-nil beats the stored answer.
    @State private var pendingGateEnabled: Bool?
    @State private var pendingSessionProtected: Bool?

    var body: some View {
        List {
            if let user = session.currentUser {
                Section {
                    HStack(spacing: 12) {
                        Circle()
                            .fill(Color.dbAccent.gradient)
                            .frame(width: 44, height: 44)
                            .overlay {
                                Text(String(user.name.prefix(1)).uppercased())
                                    .font(.headline)
                                    .foregroundStyle(.white)
                            }
                        VStack(alignment: .leading, spacing: 2) {
                            Text(user.name)
                                .font(.body.weight(.semibold))
                            Text(user.roles.joined(separator: " · "))
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                }
            }

            Section {
                if let fallbackNote {
                    // §3.3 rule 5 — the last-known-good banner. Derived, not
                    // stored: it clears itself if the missing file turns up
                    // (sync), and OK just stops pointing at the broken theme.
                    HStack(alignment: .top, spacing: 10) {
                        Image(systemName: "exclamationmark.triangle")
                            .foregroundStyle(DB.gold(scheme))
                        Text(fallbackNote)
                            .font(.footnote)
                        Spacer()
                        Button("OK") {
                            customRaw = ""
                        }
                        .font(.footnote.weight(.semibold))
                    }
                }

                if pending != nil {
                    // §3.3 rule 7 — previewing, not yet persisted.
                    HStack(spacing: 10) {
                        Text("Trying it on…")
                            .font(.footnote).foregroundStyle(.secondary)
                        Spacer()
                        Button("Keep") { keepPending() }
                            .font(.footnote.weight(.semibold))
                        Button("Undo") { revertPending() }
                            .font(.footnote)
                            .foregroundStyle(.secondary)
                    }
                }

                DisclosureGroup(isExpanded: $themeExpanded) {
                    // Yours first — they're the personal ones, and burying
                    // them under six built-ins is how nobody found Delete.
                    ForEach(visibleThemes) { user in
                        if let palette = user.palette {
                            HStack(spacing: 8) {
                                Button {
                                    preview { customRaw = palette.id }
                                } label: {
                                    themeRow(.custom(palette), selected: customRaw == palette.id)
                                }
                                .contentShape(Rectangle())
                                .buttonStyle(.plain)

                                // Edit/Delete live behind a VISIBLE control.
                                // Swipe stays as an iOS shortcut, but hidden
                                // gestures can't be the only door — the app's
                                // own author couldn't find it.
                                Menu {
                                    Button("Edit") {
                                        editorTarget = ThemeEditorTarget(palette: palette)
                                    }
                                    Button("Delete", role: .destructive) {
                                        Task { await deleteTheme(palette.id) }
                                    }
                                } label: {
                                    Image(systemName: "ellipsis.circle")
                                        .foregroundStyle(.secondary)
                                        .frame(width: 32, height: 32)
                                        .contentShape(Rectangle())
                                }
                                .menuIndicator(.hidden)
                                .buttonStyle(.plain)
                                .accessibilityLabel("Theme options")
                            }
                            .swipeActions(edge: .trailing) {
                                Button("Delete", role: .destructive) {
                                    Task { await deleteTheme(palette.id) }
                                }
                                Button("Edit") {
                                    editorTarget = ThemeEditorTarget(palette: palette)
                                }
                                .tint(Color.dbAccent)
                            }
                        } else {
                            // Listed, explained, not selectable (§3.3 rule 3).
                            HStack(spacing: 10) {
                                Image(systemName: "exclamationmark.triangle")
                                    .foregroundStyle(DB.help(scheme))
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(user.fileName)
                                        .font(.body.weight(.medium))
                                    Text(user.error ?? "Invalid theme file.")
                                        .font(.caption)
                                        .foregroundStyle(.secondary)
                                }
                            }
                            .padding(.vertical, 4)
                        }
                    }

                    ForEach(DBTheme.allCases) { theme in
                        Button {
                            preview { themeRaw = theme.rawValue; customRaw = "" }
                        } label: {
                            themeRow(.builtin(theme), selected: customRaw.isEmpty && themeRaw == theme.rawValue)
                        }
                        .contentShape(Rectangle())
                        .buttonStyle(.plain)
                    }

                    // §3.6 — the front door: an editor that produces valid YAML
                    // by construction. Raw files are the advanced route.
                    Button {
                        editorTarget = ThemeEditorTarget(palette: nil)
                    } label: {
                        Label("Make a theme", systemImage: "paintbrush")
                            .font(.subheadline.weight(.semibold))
                            .padding(.vertical, 4)
                    }
                    .buttonStyle(.plain)
                    .foregroundStyle(Color.dbAccent)

                    if customRaw.isEmpty == false || themeRaw != DBTheme.sunroom.rawValue {
                        // §3.3 rule 6 — the escape hatch is ALWAYS drawn in
                        // built-in Sunroom colours, never the active theme. The
                        // one intentional exception to "nothing hardcoded".
                        Button {
                            // Commits immediately — the escape hatch never asks
                            // you to confirm your way out of an unusable theme.
                            withAnimation(.easeInOut(duration: 0.2)) {
                                themeRaw = DBTheme.sunroom.rawValue
                                customRaw = ""
                                themeExpanded = false
                            }
                            pending = nil
                            WidgetBridge.themeChanged()
                        } label: {
                            Text("Reset to Sunroom")
                                .font(.subheadline.weight(.semibold))
                                .foregroundStyle(Color(hex: 0xC7284F))
                                .frame(maxWidth: .infinity)
                                .padding(.vertical, 10)
                                .background(Color(hex: 0xFFFDF9),
                                            in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                                .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous)
                                    .strokeBorder(Color.black.opacity(0.08), lineWidth: 0.5))
                        }
                        .buttonStyle(.plain)
                    }
                } label: {
                    HStack(spacing: 14) {
                        ThemeSwatch(theme: resolvedTheme)
                            .frame(width: 64, height: 44)
                        VStack(alignment: .leading, spacing: 2) {
                            Text("Theme")
                                .font(.caption).foregroundStyle(.secondary)
                            Text(resolvedTheme.displayName)
                                .font(.body.weight(.semibold))
                        }
                        Spacer(minLength: 0)
                    }
                    // macOS only toggles a DisclosureGroup from its chevron, so
                    // the label — swatch, "Theme", the name — was dead. Same
                    // complaint as the dead row gaps: if it looks like the
                    // control, it should be the control.
                    .contentShape(Rectangle())
                    .onTapGesture {
                        withAnimation(.easeInOut(duration: 0.2)) { themeExpanded.toggle() }
                    }
                }
                .tint(.primary)
            } footer: {
                Text("Pick the look you like — or drop a .yaml file in the Themes folder and make your own. Your themes are yours: they follow you to every device you sign in on.")
            }

            if let user = session.currentUser, user.isParent {
                securitySection(user)
            }

            if session.currentUser?.isParent == true {
                Section {
                    featureToggle("Savings goals", goalsSubtitle, \.enableGoals)
                    featureToggle("Confetti", "Celebrate completed days", \.enableConfetti)
                    featureToggle("Streaks", "Show streak counters", \.enableStreaks)
                } header: {
                    Text("Family features")
                } footer: {
                    Text("These apply on every device.")
                }
            }

            if session.currentUser?.isParent == true, !children.isEmpty {
                drivingSection
            }

            if session.currentUser?.isParent == true {
                Section("Manage") {
                    NavigationLink {
                        AchievementDefinitionsView()
                    } label: {
                        Label("Achievements", systemImage: "trophy")
                    }
                    NavigationLink {
                        FamilyMembersView()
                    } label: {
                        Label("Family", systemImage: "person.2")
                    }
                }
            }

            if session.currentUser?.isChild == true {
                Section("Mine") {
                    NavigationLink {
                        DrivingLogView(mode: .kid)
                    } label: {
                        Label("Driving log", systemImage: "car")
                    }
                }
            }

            Section("Server") {
                if let url = session.serverURL {
                    LabeledContent("Connected to", value: url.host() ?? url.absoluteString)
                }
                Button("Change server", role: .destructive) {
                    Task { await session.forgetServer() }
                }
            }

            Section {
                Button("Sign Out", role: .destructive) {
                    Task { await session.signOut() }
                }
            }
        }
        .navigationTitle("Settings")
        .themeBackground()
        .task {
            // available() reads the folder fresh every call — no need to drop
            // the resolve memo (and force fallback re-derivation) just to list.
            userThemes = ThemeLoader.available()
            await session.refreshFeatures()
            await loadChildren()
            // §3.1 — my server themes appear here; local edits go up.
            await ThemeSync.sync(session.client, userId: session.currentUser?.userId)
            userThemes = ThemeLoader.available()
        }
        .refreshable {
            await ThemeSync.sync(session.client, userId: session.currentUser?.userId)
            userThemes = ThemeLoader.available()
        }
        .sheet(item: $editorTarget) { target in
            ThemeEditorSheet(editing: target.palette,
                             seed: resolvedTheme,
                             author: session.currentUser?.name ?? "") { savedId in
                userThemes = ThemeLoader.available()
                customRaw = savedId
                pending = nil
                WidgetBridge.themeChanged()
            }
            .environment(session)
        }
        #if os(macOS)
        // §3.3a — hot reload on macOS: edit the YAML in any editor and the
        // picker catches up when the window comes back to the front.
        .onReceive(NotificationCenter.default.publisher(
            for: NSApplication.didBecomeActiveNotification)) { _ in
            ThemeLoader.invalidate()
            userThemes = ThemeLoader.available()
        }
        #endif
    }

    // MARK: - Theme picker

    /// §3.3 rule 7 — apply live so she can SEE it, but remember what to go back
    /// to. Nothing is committed to the widgets or the server until "Keep".
    private func preview(_ apply: () -> Void) {
        if pending == nil { pending = (themeRaw, customRaw) }
        withAnimation(.easeInOut(duration: 0.2)) { apply() }
    }

    private func keepPending() {
        pending = nil
        withAnimation(.easeInOut(duration: 0.2)) { themeExpanded = false }
        WidgetBridge.themeChanged()
    }

    private func revertPending() {
        guard let previous = pending else { return }
        withAnimation(.easeInOut(duration: 0.2)) {
            themeRaw = previous.builtin
            customRaw = previous.custom
        }
        pending = nil
    }

    private func deleteTheme(_ id: String) async {
        // Local file first — the picker must never keep offering a theme the
        // user just deleted, even if the server is unreachable. The server
        // delete is what carries it to the user's other devices: their next
        // sync sees the id they once synced is gone and removes their copy.
        ThemeLoader.delete(id: id)
        if customRaw == id { customRaw = "" }
        userThemes = ThemeLoader.available()
        try? await session.client.deleteTheme(id: id)
        WidgetBridge.themeChanged()
    }

    /// User-bound themes: mine and unowned files; a sibling's synced copies
    /// on a shared device stay out of my picker (broken files always listed —
    /// whoever sees the folder should see the error).
    private var visibleThemes: [LoadedUserTheme] {
        userThemes.filter { theme in
            guard let palette = theme.palette else { return true }
            return ThemeOwnership.isVisible(palette.id, to: session.currentUser?.userId)
        }
    }

    /// One selectable theme: a real preview of the look beside its name.
    private func themeRow(_ theme: AppTheme, selected: Bool) -> some View {
        HStack(spacing: 14) {
            ThemeSwatch(theme: theme)
                .frame(width: 76, height: 52)

            VStack(alignment: .leading, spacing: 2) {
                Text(theme.displayName)
                    .font(.body.weight(.semibold))
                    .foregroundStyle(.primary)
                HStack(spacing: 5) {
                    Text(theme.mood)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    // §3.3a — advisory contrast badge. Never blocks: it's their
                    // app, and rule 6 keeps even an unreadable theme escapable.
                    if case .custom(let palette) = theme, !ThemeContrast.passesAA(palette) {
                        Image(systemName: "eye.trianglebadge.exclamationmark")
                            .font(.caption2)
                            .foregroundStyle(DB.gold(scheme))
                            .help("Text on cards is hard to read at this contrast.")
                    }
                }
            }

            Spacer()

            // Selected reads as one filled mark, not a fat donut with a
            // checkmark crammed inside it.
            ZStack {
                if selected {
                    Circle()
                        .fill(theme.accent(scheme))
                        .frame(width: 22, height: 22)
                    Image(systemName: "checkmark")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundStyle(.white)
                } else {
                    Circle()
                        .strokeBorder(DB.fillStrong(scheme), lineWidth: 1.5)
                        .frame(width: 22, height: 22)
                }
            }
        }
        .contentShape(Rectangle())
        .padding(.vertical, 4)
    }

    // MARK: - Security

    /// Both switches live inside the parent region, and therefore behind the
    /// wall — a child cannot turn the gate off. Settings itself stays
    /// ungated for children, so no biometric prompt ever appears in front of a
    /// kid's theme picker.
    private func securitySection(_ user: ApiUser) -> some View {
        let gate = session.parentGate
        let capable = gate.capability.canAuthenticate
        let on = gate.isEnabled(for: user)
        let enrolled: Bool
        if case .biometry = gate.capability { enrolled = true } else { enrolled = false }
        return Section {
            // Operable whenever the gate is ON, even on a device that has since
            // lost its passcode. Off-and-disabled there would be a wall with no
            // switch to answer it — the parent's only remaining way out of
            // their own app would be signing out on every launch.
            Toggle(isOn: Binding(
                get: { pendingGateEnabled ?? on },
                set: { newValue in
                    // Optimistic, like every other switch on this screen: the
                    // rehearsal below is a Face ID sheet, and a toggle that
                    // snaps back before the sheet even appears reads as broken.
                    pendingGateEnabled = newValue
                    Task { await setGateEnabled(newValue, for: user) }
                })
            ) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Require \(gate.capability.displayName)")
                    Text("Approvals, money and family settings")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            .disabled(!capable && !on)

            Button("Lock now") { gate.lockNow() }
                .disabled(!on)

            #if os(iOS)
            // A .biometryCurrentSet item cannot be created, let alone opened,
            // without enrolled biometry — so the row is shown and explained
            // rather than hidden, but it is only ever operable with it.
            Toggle(isOn: Binding(
                get: { pendingSessionProtected ?? session.isSessionProtected },
                set: { newValue in
                    pendingSessionProtected = newValue
                    Task { await setSessionProtection(newValue) }
                })
            ) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Protect my session")
                    Text("Keep the saved sign-in behind \(gate.capability.displayName)")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
            .disabled(!enrolled)
            #endif

            if let note = session.securityNote {
                Label(note, systemImage: "exclamationmark.circle")
                    .font(.footnote).foregroundStyle(DB.help(scheme))
            }

            if let securityError {
                Label(securityError, systemImage: "exclamationmark.circle")
                    .font(.footnote).foregroundStyle(DB.help(scheme))
            }
        } header: {
            Text("Security")
        } footer: {
            Text(securityFooter)
        }
    }

    private func setGateEnabled(_ on: Bool, for user: ApiUser) async {
        securityError = nil
        session.clearSecurityNote()
        let outcome = await session.parentGate.setEnabled(on, for: user)
        pendingGateEnabled = nil
        // nil means the device cannot host the gate at all and the flag was
        // force-written off; the footer already explains that. Cancelling is a
        // choice, not a failure, and says nothing.
        guard let outcome, outcome != .success, outcome != .cancelled else { return }
        securityError = "Couldn't confirm it's you, so this stayed off."
    }

    private var securityFooter: String {
        switch session.parentGate.capability {
        case .none:
            #if os(macOS)
            return "Set a login password on this Mac to use this."
            #else
            return "Set a passcode on this iPhone to use this."
            #endif
        case .ownerPasscodeOnly:
            #if os(macOS)
            return "This Mac has no Touch ID, so Daily Bread will ask for your login password."
            #else
            return "This iPhone has no enrolled biometry, so Daily Bread will ask for your passcode."
            #endif
        case .biometry, .biometryLockedOut:
            let name = session.parentGate.capability.displayName
            var text = "Parent screens stay locked until it's you. If you ever can't unlock, sign out and back in with your password."
            #if os(iOS)
            // Said BEFORE the switch is flipped, so the one re-login an
            // invalidated enrolment costs was announced in advance.
            text += "\n\nYour saved sign-in can be kept behind \(name) too, so it works only when you're here. If you add a new face or fingerprint, you'll sign in again."
            #endif
            return text
        }
    }

    #if os(iOS)
    private func setSessionProtection(_ on: Bool) async {
        securityError = nil
        session.clearSecurityNote()
        let result = on ? await session.enableProtectedSession()
                        : await session.disableProtectedSession()
        pendingSessionProtected = nil
        if case .failure(let outcome) = result {
            // Cancelling is a choice, not a failure: the switch snaps back and
            // says nothing. `.invalidated` has already cleared the session and
            // put its own explanation on the sign-in screen.
            if outcome != .cancelled && outcome != .invalidated {
                securityError = on ? "Couldn't protect the session on this device."
                                   : "Couldn't turn session protection off."
            }
        }
    }
    #endif

    // MARK: - Family features

    private var resolvedTheme: AppTheme {
        ThemeStore.resolve(builtinRaw: themeRaw, customId: customRaw)
    }

    /// Non-nil while the selected custom theme has no loadable file behind it.
    private var fallbackNote: String? {
        ThemeStore.fallbackDescription(builtinRaw: themeRaw, customId: customRaw)
    }

    /// Single-child mode: name the one child instead of saying "the kids".
    private var goalsSubtitle: String {
        if let name = session.onlyChild?.name {
            return "Show goals to \(name.capitalized)"
        }
        return "Show savings goals"
    }

    /// Driving is per-child, not family-wide: a house with a teen and a
    /// nine-year-old wants it on for exactly one of them. With a single child
    /// this renders as a single switch, which is the whole of the feature.
    private var drivingSection: some View {
        Section {
            ForEach(children) { child in
                Toggle(isOn: drivingBinding(for: child)) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(child.name)
                        Text(child.drives ? "Log, hours and approvals are on"
                                          : "Hidden everywhere for this child")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
            }
        } header: {
            Text(children.count == 1 ? "Driving" : "Driving by child")
        } footer: {
            Text("Turn this off when a child no longer needs the log. Nothing is deleted — their hours are still there if you switch it back on.")
        }
    }

    /// Flips locally, saves, and reverts on failure — same contract as the
    /// family-feature switches.
    private func drivingBinding(for child: FamilyMember) -> Binding<Bool> {
        Binding(
            get: { child.drives },
            set: { newValue in
                guard let index = children.firstIndex(where: { $0.id == child.id }) else { return }
                let previous = children[index].drivingEnabled
                children[index].drivingEnabled = newValue
                Task {
                    do {
                        try await session.client.setMemberDriving(userId: child.id, enabled: newValue)
                    } catch {
                        if let i = children.firstIndex(where: { $0.id == child.id }) {
                            children[i].drivingEnabled = previous
                        }
                    }
                }
            })
    }

    private func loadChildren() async {
        guard session.currentUser?.isParent == true else { return }
        guard let members = try? await session.client.familyMembers() else { return }
        children = members.filter { $0.canHavePerChildSettings }
    }

    /// A family-feature switch: flips locally, saves to the server, reverts
    /// on failure.
    private func featureToggle(
        _ title: String,
        _ subtitle: String,
        _ keyPath: WritableKeyPath<FamilyFeatures, Bool>
    ) -> some View {
        Toggle(isOn: Binding(
            get: { session.features[keyPath: keyPath] },
            set: { newValue in
                var updated = session.features
                updated[keyPath: keyPath] = newValue
                let previous = session.features
                session.features = updated
                Task {
                    do {
                        session.features = try await session.client.updateFamilyFeatures(updated)
                    } catch {
                        session.features = previous
                    }
                }
            })
        ) {
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                Text(subtitle)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }
}

/// A little live preview of a theme — its real background, a floating card with the accent,
/// a gold coin, and the progress glow. What she taps is what the app becomes.
private struct ThemeSwatch: View {
    let theme: AppTheme

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .fill(theme.backgroundGradient)

            VStack(spacing: 4) {
                // Mini card with accent + gold dots.
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .fill(theme.cardColor)
                    .frame(height: 20)
                    .overlay(
                        HStack(spacing: 4) {
                            Circle().fill(theme.accent()).frame(width: 8, height: 8)
                            Circle().fill(Color(hex: 0xC98A1E)).frame(width: 8, height: 8)
                            Spacer()
                        }
                        .padding(.horizontal, 5))
                    .overlay(
                        RoundedRectangle(cornerRadius: 6, style: .continuous)
                            .strokeBorder(theme.cardStroke, lineWidth: 0.5))

                // Mini progress glow.
                Capsule()
                    .fill(theme.progressGradient)
                    .frame(height: 5)
            }
            .padding(7)
        }
        .overlay(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .strokeBorder(Color.black.opacity(0.08), lineWidth: 0.5))
        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
    }
}
