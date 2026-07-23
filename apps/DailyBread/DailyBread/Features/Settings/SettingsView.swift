import SwiftUI
import DailyBreadKit

struct SettingsView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @AppStorage("db.theme") private var themeRaw = DBTheme.sunroom.rawValue

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
                ForEach(DBTheme.allCases) { theme in
                    Button {
                        withAnimation(.easeInOut(duration: 0.25)) {
                            themeRaw = theme.rawValue
                        }
                    } label: {
                        themeRow(theme)
                    }
                    .buttonStyle(.plain)
                }
            } header: {
                Text("Theme")
            } footer: {
                Text("Pick the look you like. It changes everywhere, on every device — switch whenever you feel like a change.")
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
        .graphiteBackground()
        .task { await session.refreshFeatures() }
    }

    // MARK: - Theme picker

    /// One selectable theme: a real preview of the look beside its name.
    private func themeRow(_ theme: DBTheme) -> some View {
        let selected = themeRaw == theme.rawValue
        return HStack(spacing: 14) {
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
                    .strokeBorder(selected ? theme.accent(scheme) : Color.secondary.opacity(0.35),
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
    let theme: DBTheme

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
