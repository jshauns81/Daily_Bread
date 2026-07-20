import SwiftUI
import DailyBreadKit

/// The screen-time meter, ported from the web dashboard: this week's two
/// pools (what's left, the guaranteed floor, what's on the line) and the
/// live minute price of every chore. Quietly renders nothing for users
/// without a child profile (parents viewing their own Today).
@MainActor
@Observable
final class ScreenTimeStore {
    var summary: ScreenTimeSummary?
    var loaded = false

    func load(_ session: SessionStore, userId: String?) async {
        summary = try? await session.client.screenTime(userId: userId)
        loaded = true
    }
}

struct ScreenTimeCard: View {
    var userId: String?

    @Environment(SessionStore.self) private var session
    @Environment(\.colorScheme) private var scheme
    @State private var store = ScreenTimeStore()
    @State private var showHistory = false

    var body: some View {
        if let summary = store.summary {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    Text("SCREEN TIME THIS WEEK")
                        .font(.caption.weight(.bold))
                        .foregroundStyle(.secondary)
                        .kerning(0.8)
                    Spacer()
                    if !summary.recentEntries.isEmpty {
                        Button {
                            Haptics.tick()
                            showHistory = true
                        } label: {
                            HStack(spacing: 3) {
                                Text("History")
                                Image(systemName: "chevron.right")
                            }
                            .font(.caption.weight(.semibold))
                            .foregroundStyle(Color.accentColor)
                        }
                        .buttonStyle(.plain)
                    }
                }

                HStack(alignment: .top, spacing: 10) {
                    pool(summary.weekdayPool, name: "Weekdays", tag: "Mon–Fri")
                    pool(summary.weekendPool, name: "Weekend", tag: "Sat–Sun")
                }

                if summary.chorePrices.isEmpty {
                    Text("No chores are priced this week — nothing at risk. 😎")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                } else {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("What each chore is worth")
                            .font(.caption.weight(.semibold))
                            .foregroundStyle(.secondary)
                        ForEach(summary.chorePrices) { price in
                            HStack {
                                Text(price.name)
                                    .font(.subheadline)
                                Spacer()
                                Text("Miss once: −\(price.perInstanceMinutes) min")
                                    .font(.caption.weight(.semibold))
                                    .foregroundStyle(DB.help(scheme))
                            }
                        }
                    }
                }
            }
            .glassCard()
            .sheet(isPresented: $showHistory) {
                ScreenTimeHistorySheet(entries: summary.recentEntries)
            }
            .task(id: userId) { await store.load(session, userId: userId) }
        } else {
            // Load invisibly; the card only appears once there's a meter to show.
            Color.clear
                .frame(height: 0)
                .task(id: userId) { await store.load(session, userId: userId) }
        }
    }

    private func pool(_ p: ScreenTimePool, name: String, tag: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text(name)
                    .font(.caption.weight(.bold))
                Spacer()
                Text(tag)
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }

            Text(ScreenTimeFormat.minutes(p.effectiveMinutes))
                .font(.title3.weight(.bold))
                .foregroundStyle(Color.accentColor)
            Text("in your pool")
                .font(.caption2)
                .foregroundStyle(.secondary)

            Label {
                Text("Always keeps **\(ScreenTimeFormat.minutes(p.floorMinutes))**")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            } icon: {
                Image(systemName: "lock.fill")
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }
            .labelStyle(.titleAndIcon)
            .padding(.top, 2)

            if p.atRiskMinutes > 0 {
                Text("Up to **\(p.atRiskMinutes) min** on the line")
                    .font(.caption2)
                    .foregroundStyle(DB.help(scheme))
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(10)
        .background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
    }
}

/// The screen-time ledger: every budget change as a labeled line — deductions,
/// earn-backs, adjustments, Time Machine corrections. Mercy on the record.
struct ScreenTimeHistorySheet: View {
    let entries: [ScreenTimeEntry]

    @Environment(\.dismiss) private var dismiss
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        NavigationStack {
            List {
                ForEach(entries) { entry in
                    HStack(spacing: 12) {
                        Image(systemName: icon(entry.kind))
                            .font(.subheadline)
                            .foregroundStyle(entry.minutes < 0 ? DB.help(scheme) : DB.success(scheme))
                            .frame(width: 28)

                        VStack(alignment: .leading, spacing: 2) {
                            Text(entry.choreName ?? label(entry.kind))
                                .font(.subheadline.weight(.medium))
                            Text(subtitle(entry))
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }

                        Spacer()

                        Text(entry.minutes < 0 ? "−\(-entry.minutes) min" : "+\(entry.minutes) min")
                            .font(.subheadline.weight(.bold))
                            .foregroundStyle(entry.minutes < 0 ? DB.help(scheme) : DB.success(scheme))
                    }
                    .padding(.vertical, 2)
                }
            }
            .navigationTitle("Screen-Time History")
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
                }
            }
        }
        .graphiteBackground()
    }

    private func icon(_ kind: String) -> String {
        switch kind {
        case "EarnBack": return "arrow.uturn.up"
        case "Adjustment": return "slider.horizontal.3"
        case "TimeMachine": return "clock.arrow.circlepath"
        default: return "minus.circle"
        }
    }

    private func label(_ kind: String) -> String {
        switch kind {
        case "EarnBack": return "Earned back"
        case "Adjustment": return "Parent adjustment"
        case "TimeMachine": return "Time Machine"
        default: return "Missed chore"
        }
    }

    private func subtitle(_ entry: ScreenTimeEntry) -> String {
        let pool = entry.pool == "Weekend" ? "Weekend pool" : "Weekday pool"
        if let note = entry.note, !note.isEmpty {
            return "\(pool) · \(note)"
        }
        return "\(pool) · week of \(entry.weekStart.shortDisplay)"
    }
}
