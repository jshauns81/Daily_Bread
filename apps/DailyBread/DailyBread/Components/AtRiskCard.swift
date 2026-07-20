import SwiftUI
import DailyBreadKit

/// "At Risk Today" (MECHANICS §E): what's actually on the line right now —
/// and nothing else. The server owns all the urgency logic and ordering
/// (DueTonight → MustDoDaily → GettingTight); this card only renders true
/// states, never re-sorts, and never nags. Quietly renders nothing for
/// users without a child profile (parents viewing their own Today).
@MainActor
@Observable
final class AtRiskStore {
    var atRisk: AtRiskToday?
    var loaded = false

    func load(_ session: SessionStore, userId: String?) async {
        atRisk = try? await session.client.atRiskToday(userId: userId)
        loaded = true
    }
}

struct AtRiskCard: View {
    var userId: String?

    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = AtRiskStore()

    var body: some View {
        if let data = store.atRisk {
            content(data)
                .glassCard()
                .task(id: userId) { await store.load(session, userId: userId) }
        } else {
            // Load invisibly; the card only appears once there's something
            // (or a calm nothing) to say. A 404 (no child profile) stays hidden.
            Color.clear
                .frame(height: 0)
                .task(id: userId) { await store.load(session, userId: userId) }
        }
    }

    @ViewBuilder
    private func content(_ data: AtRiskToday) -> some View {
        if data.items.isEmpty {
            // Calm state: one quiet line, at most one preview. Never nag.
            VStack(alignment: .leading, spacing: 4) {
                Text("Nothing at risk today ✌️")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                if let preview = data.previewLine {
                    Text(preview)
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        } else {
            VStack(alignment: .leading, spacing: 12) {
                Text("AT RISK TODAY")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.secondary)
                    .kerning(0.8)

                VStack(alignment: .leading, spacing: 10) {
                    // Server order is the truth: DueTonight → MustDoDaily →
                    // GettingTight. Never re-sort.
                    ForEach(data.items) { item in
                        row(item)
                    }
                }

                if data.items.count >= 2, let footer = footerText(data) {
                    Text(footer)
                        .font(.footnote.weight(.bold))
                        .padding(.top, 2)
                }
            }
        }
    }

    private func row(_ item: AtRiskItem) -> some View {
        HStack(alignment: .center, spacing: 10) {
            Image(systemName: symbol(item.urgency))
                .font(.subheadline)
                .foregroundStyle(tint(item.urgency))
                .frame(width: 24)

            VStack(alignment: .leading, spacing: 2) {
                Text(item.name)
                    .font(.subheadline.weight(.medium))
                Text(item.detail)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Spacer()

            VStack(alignment: .trailing, spacing: 2) {
                if !item.moneyAtRisk.isZero {
                    Text(item.moneyAtRisk.display)
                        .font(.caption.weight(.bold))
                        .foregroundStyle(DB.gold(scheme))
                }
                if item.minutesAtRisk > 0 {
                    Text("−\(item.minutesAtRisk) min")
                        .font(.caption.weight(.bold))
                        .foregroundStyle(DB.help(scheme))
                }
            }
        }
    }

    /// "On the line today: $15.00 + 27 min" — totals are server-summed.
    private func footerText(_ data: AtRiskToday) -> String? {
        var parts: [String] = []
        if !data.totalMoneyAtRisk.isZero {
            parts.append(data.totalMoneyAtRisk.display)
        }
        if data.totalMinutesAtRisk > 0 {
            parts.append("\(data.totalMinutesAtRisk) min")
        }
        guard !parts.isEmpty else { return nil }
        return "On the line today: \(parts.joined(separator: " + "))"
    }

    private func symbol(_ urgency: String) -> String {
        switch urgency {
        case "DueTonight": return "exclamationmark.circle.fill"
        case "MustDoDaily": return "flame.fill"
        default: return "clock.badge.exclamationmark"   // GettingTight
        }
    }

    private func tint(_ urgency: String) -> Color {
        switch urgency {
        case "DueTonight", "MustDoDaily": return DB.help(scheme)
        default: return DB.gold(scheme)                 // GettingTight
        }
    }
}
