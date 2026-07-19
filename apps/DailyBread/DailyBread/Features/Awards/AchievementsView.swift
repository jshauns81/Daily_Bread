import SwiftUI
import DailyBreadKit

/// The trophy case: earned badges glow, locked ones show their path,
/// hidden ones stay mysterious.
@MainActor
@Observable
final class AchievementsStore {
    var list: AchievementsList?
    var loading = false
    var errorMessage: String?

    func load(_ session: SessionStore) async {
        loading = list == nil
        defer { loading = false }
        do {
            list = try await session.client.achievements()
            errorMessage = nil
            if list?.achievements.contains(where: \.isNew) == true {
                await session.client.markAchievementsSeen()
            }
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    var earned: [Achievement] {
        (list?.achievements ?? []).filter(\.isEarned)
            .sorted { ($0.earnedAtUtc?.date ?? .distantPast) > ($1.earnedAtUtc?.date ?? .distantPast) }
    }

    var locked: [Achievement] {
        (list?.achievements ?? []).filter { !$0.isEarned }
    }
}

struct AchievementsView: View {
    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = AchievementsStore()

    private let columns = [GridItem(.flexible(), spacing: 10),
                           GridItem(.flexible(), spacing: 10)]

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                if let list = store.list {
                    header(list)

                    if !store.earned.isEmpty {
                        sectionHeader("Earned")
                        LazyVGrid(columns: columns, spacing: 10) {
                            ForEach(store.earned) { achievement in
                                card(achievement)
                            }
                        }
                    }

                    if !store.locked.isEmpty {
                        sectionHeader("Still out there")
                        LazyVGrid(columns: columns, spacing: 10) {
                            ForEach(store.locked) { achievement in
                                card(achievement)
                            }
                        }
                    }
                } else if store.loading {
                    ProgressView()
                        .frame(maxWidth: .infinity)
                        .padding(.top, 60)
                }

                if let error = store.errorMessage {
                    Label(error, systemImage: "wifi.exclamationmark")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
            .padding()
        }
        .navigationTitle("Awards")
        .graphiteBackground()
        .refreshable { await store.load(session) }
        .refreshOnForeground { await store.load(session) }
        .task { await store.load(session) }
    }

    private func header(_ list: AchievementsList) -> some View {
        HStack(spacing: 12) {
            statTile(value: "\(list.totalPoints)", label: "points", color: DB.gold(scheme))
            statTile(value: "\(list.earnedCount)/\(list.totalCount)", label: "earned", color: .accentColor)
        }
    }

    private func statTile(value: String, label: String, color: Color) -> some View {
        VStack(spacing: 3) {
            Text(value)
                .font(.title3.weight(.bold))
                .foregroundStyle(color)
            Text(label)
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
        .glassCard(padding: 12)
    }

    private func card(_ achievement: Achievement) -> some View {
        VStack(spacing: 6) {
            Text(achievement.icon)
                .font(.system(size: 34))
                .grayscale(achievement.isEarned ? 0 : 1)
                .opacity(achievement.isEarned ? 1 : 0.55)

            Text(achievement.name)
                .font(.subheadline.weight(.semibold))
                .multilineTextAlignment(.center)
                .lineLimit(1)
                .minimumScaleFactor(0.7)

            Text(achievement.description)
                .font(.caption2)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .lineLimit(2)
                .frame(maxWidth: .infinity)

            if achievement.isEarned {
                HStack(spacing: 4) {
                    Text(rarityLabel(achievement.rarity))
                        .font(.caption2.weight(.bold))
                        .foregroundStyle(rarityColor(achievement.rarity))
                    Text("· \(achievement.points) pts")
                        .font(.caption2)
                        .foregroundStyle(DB.gold(scheme))
                }
            } else if achievement.showProgress, achievement.targetProgress > 0 {
                VStack(spacing: 3) {
                    ProgressView(value: Double(achievement.currentProgress),
                                 total: Double(achievement.targetProgress))
                        .tint(Color.accentColor)
                    Text("\(achievement.currentProgress)/\(achievement.targetProgress)")
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                }
            } else {
                Text("\(achievement.points) pts")
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }
        }
        .frame(maxWidth: .infinity, minHeight: 128, alignment: .top)
        .glassCard(padding: 12)
        .overlay {
            if achievement.isEarned {
                RoundedRectangle(cornerRadius: 20, style: .continuous)
                    .strokeBorder(DB.gold(scheme).opacity(0.35), lineWidth: 1)
            }
        }
    }

    private func rarityLabel(_ rarity: String) -> String {
        rarity.capitalized
    }

    private func rarityColor(_ rarity: String) -> Color {
        switch rarity.lowercased() {
        case "legendary": return DB.gold(scheme)
        case "epic": return Color(hex: 0x9B7BE0)
        case "rare": return .accentColor
        case "uncommon": return DB.success(scheme)
        default: return .secondary
        }
    }

    private func sectionHeader(_ title: String) -> some View {
        Text(title.uppercased())
            .font(.caption.weight(.bold))
            .foregroundStyle(.secondary)
            .kerning(0.8)
            .padding(.top, 4)
    }
}
