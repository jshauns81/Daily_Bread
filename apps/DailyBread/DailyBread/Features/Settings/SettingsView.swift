import SwiftUI
import DailyBreadKit

/// The parent's list of kids for screen-time tuning, from the dashboard.
/// Only children with a userId can be tuned (the PUT needs an explicit id).
@MainActor
@Observable
final class ScreenTimeChildrenStore {
    var children: [ChildProgress] = []
    var loadingChildId: String?

    func load(_ session: SessionStore) async {
        let dashboard = try? await session.client.parentDashboard()
        // Server order — never re-sort.
        children = (dashboard?.childrenProgress ?? []).filter { $0.userId != nil }
    }

    /// Fetch the child's current summary before opening the sheet.
    /// Fails quietly (the row just settles back) — no alerts.
    func fetchSummary(_ session: SessionStore, childId: String) async -> ScreenTimeSummary? {
        loadingChildId = childId
        defer { loadingChildId = nil }
        return try? await session.client.screenTime(userId: childId)
    }
}

/// Sheet payload: the child plus their freshly fetched summary.
private struct ScreenTimeSheetTarget: Identifiable {
    let userId: String
    let name: String
    let summary: ScreenTimeSummary

    var id: String { userId }
}

struct SettingsView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @AppStorage("db.theme") private var themeRaw = DBTheme.guadalupe.rawValue
    @State private var screenTimeStore = ScreenTimeChildrenStore()
    @State private var screenTimeTarget: ScreenTimeSheetTarget?

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

                if !screenTimeStore.children.isEmpty {
                    Section {
                        ForEach(screenTimeStore.children) { child in
                            screenTimeRow(child)
                        }
                    } header: {
                        Text("Screen time")
                    } footer: {
                        Text("Tune each kid's weekly allowance and how much is at stake.")
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
        .task {
            await session.refreshFeatures()
            if session.currentUser?.isParent == true {
                await screenTimeStore.load(session)
            }
        }
        .sheet(item: $screenTimeTarget) { target in
            ScreenTimeSettingsSheet(
                childUserId: target.userId,
                childName: target.name,
                summary: target.summary
            ) { _ in
                // Settings has nothing on screen to refresh; the Today and
                // screen-time cards pick up the change on their next load.
            }
        }
    }

    /// One child row: name + chevron; tap fetches their current summary
    /// (inline spinner) then presents the tuning sheet.
    private func screenTimeRow(_ child: ChildProgress) -> some View {
        Button {
            guard let childId = child.userId,
                  screenTimeStore.loadingChildId == nil else { return }
            Haptics.tick()
            Task {
                if let summary = await screenTimeStore.fetchSummary(session, childId: childId) {
                    screenTimeTarget = ScreenTimeSheetTarget(
                        userId: childId,
                        name: child.displayName,
                        summary: summary)
                }
            }
        } label: {
            HStack {
                Text(child.displayName)
                    .foregroundStyle(.primary)
                Spacer()
                if screenTimeStore.loadingChildId == child.userId {
                    ProgressView()
                } else {
                    Image(systemName: "chevron.right")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(.tertiary)
                }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
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
