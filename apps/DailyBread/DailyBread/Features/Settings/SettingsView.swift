import SwiftUI
import DailyBreadKit

struct SettingsView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @AppStorage(ThemeStore.key) private var themeRaw = DBTheme.sunroom.rawValue
    @AppStorage(ThemeStore.customKey) private var customRaw = ""
    @AppStorage(ThemeStore.fallbackKey) private var fallbackMessage = ""
    @State private var themeExpanded = false
    /// §3: user themes from the Themes folder — valid ones selectable, broken
    /// ones listed with their error, never hidden and never selectable.
    @State private var userThemes: [LoadedUserTheme] = []

    var body: some View {
        List {
            if let user = session.currentUser {
                Section {
                    HStack(spacing: 12) {
                        Circle()
                            .fill(Color.accentColor.gradient)
                            .frame(width: 44, height: 44)
                            .overlay {
                                Text(String(user.userName.prefix(1)).uppercased())
                                    .font(.headline)
                                    .foregroundStyle(.white)
                            }
                        VStack(alignment: .leading, spacing: 2) {
                            Text(user.userName)
                                .font(.body.weight(.semibold))
                            Text(user.roles.joined(separator: " · "))
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                }
            }

            Section {
                if !fallbackMessage.isEmpty {
                    // §3.3 rule 5 — the dismissible last-known-good banner.
                    HStack(alignment: .top, spacing: 10) {
                        Image(systemName: "exclamationmark.triangle")
                            .foregroundStyle(DB.gold(scheme))
                        Text(fallbackMessage)
                            .font(.footnote)
                        Spacer()
                        Button("OK") {
                            fallbackMessage = ""
                            customRaw = ""
                        }
                        .font(.footnote.weight(.semibold))
                    }
                }

                DisclosureGroup(isExpanded: $themeExpanded) {
                    ForEach(DBTheme.allCases) { theme in
                        Button {
                            pick { themeRaw = theme.rawValue; customRaw = "" }
                        } label: {
                            themeRow(.builtin(theme), selected: customRaw.isEmpty && themeRaw == theme.rawValue)
                        }
                        .buttonStyle(.plain)
                    }

                    ForEach(userThemes) { user in
                        if let palette = user.palette {
                            Button {
                                pick { customRaw = palette.id }
                            } label: {
                                themeRow(.custom(palette), selected: customRaw == palette.id)
                            }
                            .buttonStyle(.plain)
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

                    if customRaw.isEmpty == false || themeRaw != DBTheme.sunroom.rawValue {
                        // §3.3 rule 6 — the escape hatch is ALWAYS drawn in
                        // built-in Sunroom colours, never the active theme. The
                        // one intentional exception to "nothing hardcoded".
                        Button {
                            pick { themeRaw = DBTheme.sunroom.rawValue; customRaw = "" }
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
                    }
                }
                .tint(.primary)
            } footer: {
                Text("Pick the look you like — or drop a .yaml file in the Themes folder and make your own. It changes everywhere, on every device.")
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

            if session.currentUser?.isParent == true {
                Section("Manage") {
                    NavigationLink {
                        AchievementDefinitionsView()
                    } label: {
                        Label("Achievements", systemImage: "trophy")
                    }
                    NavigationLink {
                        DrivingLogView(mode: .parent)
                    } label: {
                        Label("Driving approvals", systemImage: "car")
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
            ThemeLoader.invalidate()
            userThemes = ThemeLoader.available()
            await session.refreshFeatures()
            // §3.1 — server themes appear here; local edits go up.
            await ThemeSync.sync(session.client)
            userThemes = ThemeLoader.available()
        }
        .refreshable {
            await ThemeSync.sync(session.client)
            userThemes = ThemeLoader.available()
        }
    }

    // MARK: - Theme picker

    private func pick(_ apply: () -> Void) {
        withAnimation(.easeInOut(duration: 0.2)) {
            apply()
            themeExpanded = false
        }
        fallbackMessage = ""
        WidgetBridge.themeChanged()
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
                Text(theme.mood)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Spacer()

            ZStack {
                Circle()
                    .strokeBorder(selected ? theme.accent(scheme) : DB.fillStrong(scheme),
                                  lineWidth: selected ? 6 : 1.5)
                    .frame(width: 22, height: 22)
                if selected {
                    Image(systemName: "checkmark")
                        .font(.system(size: 10, weight: .black))
                        .foregroundStyle(.white)
                }
            }
        }
        .contentShape(Rectangle())
        .padding(.vertical, 4)
    }

    // MARK: - Family features

    private var resolvedTheme: AppTheme {
        ThemeStore.resolve(builtinRaw: themeRaw, customId: customRaw)
    }

    /// Single-child mode: name the one child instead of saying "the kids".
    private var goalsSubtitle: String {
        if let name = session.onlyChild?.userName {
            return "Show goals to \(name.capitalized)"
        }
        return "Show savings goals"
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
