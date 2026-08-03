import SwiftUI
import DailyBreadKit

/// Driving on the kid's Home — because for a teen working toward a licence this
/// is the biggest number in the app, and it was three taps deep in Settings.
///
/// The card answers the only question that matters ("how many hours have I
/// got?") and puts logging a drive one tap away instead of four. It appears
/// only for a child whose parent has switched driving on, so it never shows up
/// on a nine-year-old's Home.
struct DrivingCard: View {
    let progress: DrivingLogProgress
    /// Parent drill-in passes the child; nil is the kid looking at their own.
    var userId: String?
    /// Opening the full log. Kept as a closure rather than wrapping the card in
    /// a NavigationLink: a Button inside a link's label fights it for the tap,
    /// and "Log a drive" must stay its own target.
    var onOpen: () -> Void
    var onLogged: () async -> Void

    @Environment(\.colorScheme) private var scheme
    @State private var logging = false

    /// A parent's explicit choice, not a guess from the data. The old rule
    /// ("hours banked or a goal set") hid the card from the one child who most
    /// needed it: a new permit holder on day one, with neither.
    static func shouldShow(_ progress: DrivingLogProgress?) -> Bool {
        progress?.isEnabled ?? false
    }

    private var ratio: Double {
        guard let goal = progress.totalGoalHours, goal > 0 else { return 0 }
        return min(1, progress.totalHours / goal)
    }

    private var remaining: Double? {
        guard let goal = progress.totalGoalHours, goal > progress.totalHours else { return nil }
        return goal - progress.totalHours
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            // Everything above the action opens the full log.
            Button(action: onOpen) {
                VStack(alignment: .leading, spacing: 12) {
                    header
                    if progress.totalGoalHours != nil {
                        ProgressView(value: ratio)
                            .tint(Color.dbAccent)
                        subtitle
                    }
                    nightLine
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityHint("Opens the driving log")

            logButton
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .glassCard(padding: 16)
        .sheet(isPresented: $logging) {
            DriveEditorSheet(asParent: userId != nil, childUserId: userId) {
                await onLogged()
            }
        }
    }

    private var header: some View {
        HStack(alignment: .center, spacing: 12) {
            // The prominent mark: driving gets a real icon, not a list glyph.
            Image(systemName: "car.fill")
                .font(.title2.weight(.semibold))
                .foregroundStyle(Color.dbAccent)
                .frame(width: 44, height: 44)
                .background(Color.dbAccent.opacity(0.14),
                            in: RoundedRectangle(cornerRadius: 12, style: .continuous))

            VStack(alignment: .leading, spacing: 2) {
                Text("DRIVING")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.secondary)
                    .kerning(0.8)
                HStack(alignment: .firstTextBaseline, spacing: 4) {
                    Text(hours(progress.totalHours))
                        .font(.system(size: 28, weight: .heavy, design: .rounded))
                        .foregroundStyle(Color.dbAccent)
                        .contentTransition(.numericText())
                    if let goal = progress.totalGoalHours {
                        Text("of \(hours(goal)) hours")
                            .font(.subheadline.weight(.medium))
                            .foregroundStyle(.secondary)
                    } else {
                        Text("hours")
                            .font(.subheadline.weight(.medium))
                            .foregroundStyle(.secondary)
                    }
                }
            }
            Spacer(minLength: 0)
        }
    }

    private var subtitle: some View {
        Group {
            if let left = remaining {
                Text("**\(hours(left))** more to go")
            } else {
                Text("Goal reached ✨")
            }
        }
        .font(.caption)
        .foregroundStyle(.secondary)
    }

    @ViewBuilder
    private var nightLine: some View {
        if let nightGoal = progress.nightGoalHours, nightGoal > 0 {
            HStack(spacing: 6) {
                Image(systemName: "moon.stars.fill")
                    .font(.caption)
                    .foregroundStyle(DB.night(scheme))
                Text("\(hours(progress.nightHours)) of \(hours(nightGoal)) night hours")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        } else if progress.nightHours > 0 {
            HStack(spacing: 6) {
                Image(systemName: "moon.stars.fill")
                    .font(.caption)
                    .foregroundStyle(DB.night(scheme))
                Text("\(hours(progress.nightHours)) at night")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var logButton: some View {
        Button {
            logging = true
        } label: {
            Label("Log a drive", systemImage: "plus.circle.fill")
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(.white)
                .frame(maxWidth: .infinity, minHeight: 40)
                .background(Color.dbAccent,
                            in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Log a drive")
    }

    private func hours(_ v: Double) -> String {
        v == v.rounded() ? String(Int(v)) : String(format: "%.1f", v)
    }
}
