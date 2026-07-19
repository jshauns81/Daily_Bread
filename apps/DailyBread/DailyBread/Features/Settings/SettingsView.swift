import SwiftUI
import DailyBreadKit

struct SettingsView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @AppStorage("db.theme") private var themeRaw = DBTheme.guadalupe.rawValue

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

            Section("Theme") {
                ForEach(DBTheme.allCases) { theme in
                    Button {
                        themeRaw = theme.rawValue
                    } label: {
                        HStack {
                            Circle()
                                .fill(theme.accent(scheme))
                                .frame(width: 18, height: 18)
                            Text(theme.displayName)
                                .foregroundStyle(.primary)
                            Spacer()
                            if themeRaw == theme.rawValue {
                                Image(systemName: "checkmark")
                                    .foregroundStyle(Color.accentColor)
                            }
                        }
                        .contentShape(Rectangle())
                    }
                    // .plain stops macOS from tinting the whole row label
                    // with the accent color (the "weird lines" look).
                    .buttonStyle(.plain)
                }
            }

            if session.currentUser?.isParent == true {
                Section {
                    featureToggle("Savings goals", "Show goals to the kids", \.enableGoals)
                    featureToggle("Confetti", "Celebrate completed days", \.enableConfetti)
                    featureToggle("Streaks", "Show streak counters", \.enableStreaks)
                } header: {
                    Text("Family features")
                } footer: {
                    Text("These apply to the whole family, on every device.")
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
