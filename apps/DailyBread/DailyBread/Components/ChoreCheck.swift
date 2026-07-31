import SwiftUI
import DailyBreadKit

/// The four states a chore's check control can show. Grey never means "empty and
/// waiting" here — unchecked is a quiet outline, and the two waiting states carry
/// their invariant colours (money for approval, help-red for a raised hand).
enum ChoreCheckState: Equatable {
    case unchecked
    case done
    /// Terminal from the kid's side — the chore is done and a parent owns the next move.
    case awaitingApproval
    /// Terminal from the kid's side — protected until a parent helps or forgives.
    case helpRaised
}

extension ChoreItem {
    var checkState: ChoreCheckState {
        if isHelp { return .helpRaised }
        if isApproved { return .done }
        if status == "Completed" { return .awaitingApproval }
        return .unchecked
    }
}

/// The per-chore check — 29pt visual in a 44pt hit target.
///
/// - `unchecked`: 2.5px outline ring, no fill.
/// - `done`: accent ring + fill, white checkmark, halo bloom + symbol bounce.
/// - `awaitingApproval`: dashed money ring over a 12% money wash.
/// - `helpRaised`: help ring over a 12% help wash, questionmark glyph.
///
/// Taps on the two terminal states wiggle instead of toggling unless the caller
/// supplies `onTerminalTap` (a parent approving from a drill-in). Earning chores
/// flash the ring money-gold for ~180ms before it settles to accent — the one
/// moment where money and action are the same event.
struct ChoreCheck: View {
    let state: ChoreCheckState
    var isEarning = false
    var onToggle: () -> Void
    var onTerminalTap: (() -> Void)? = nil

    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    @State private var haloScale: CGFloat = 1
    @State private var haloOpacity: Double = 0
    @State private var goldFlash = false
    @State private var wiggle = 0

    // Glyphs are pinned to the 29pt control, not the type ramp — DBIcon's Dynamic
    // Type coupling would overflow the fixed ring at accessibility sizes.
    private let visual: CGFloat = 29

    var body: some View {
        Button(action: tapped) {
            ZStack {
                Circle()
                    .fill(Color.accentColor)
                    .frame(width: visual, height: visual)
                    .scaleEffect(haloScale)
                    .opacity(haloOpacity)

                Circle()
                    .fill(fillColor)
                    .frame(width: visual, height: visual)

                ring
                    .frame(width: visual, height: visual)

                glyph
            }
            .animation(.spring(response: 0.28, dampingFraction: 0.55), value: state)
            .rotationEffect(.degrees(wiggle == 0 ? 0 : (wiggle % 2 == 0 ? 6 : -6)))
            .frame(width: 44, height: 44)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onChange(of: state) { _, new in
            if new == .done { bloom() }
        }
        .accessibilityLabel(accessibilityText)
    }

    private var ringColor: Color {
        switch state {
        case .unchecked: return .primary.opacity(0.22)
        case .done: return goldFlash ? DB.gold(scheme) : .accentColor
        case .awaitingApproval: return DB.gold(scheme)
        case .helpRaised: return DB.help(scheme)
        }
    }

    @ViewBuilder private var ring: some View {
        if state == .awaitingApproval {
            Circle().stroke(ringColor, style: StrokeStyle(lineWidth: 2.5, dash: [4, 3.5]))
        } else {
            Circle().stroke(ringColor, lineWidth: 2.5)
        }
    }

    private var fillColor: Color {
        switch state {
        case .unchecked: return .clear
        case .done: return goldFlash ? DB.gold(scheme) : .accentColor
        case .awaitingApproval: return DB.gold(scheme).opacity(0.12)
        case .helpRaised: return DB.help(scheme).opacity(0.12)
        }
    }

    @ViewBuilder private var glyph: some View {
        switch state {
        case .unchecked:
            EmptyView()
        case .done:
            Image(systemName: "checkmark")
                .font(.system(size: 13, weight: .bold))
                .foregroundStyle(.white)
                .symbolEffect(.bounce, value: state)
        case .awaitingApproval:
            Image(systemName: "checkmark")
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(DB.gold(scheme))
        case .helpRaised:
            Image(systemName: "questionmark")
                .font(.system(size: 13, weight: .bold))
                .foregroundStyle(DB.help(scheme))
        }
    }

    private var accessibilityText: Text {
        switch state {
        case .unchecked: return Text("Not done")
        case .done: return Text("Done")
        case .awaitingApproval: return Text("Waiting for approval")
        case .helpRaised: return Text("Help raised")
        }
    }

    private func tapped() {
        switch state {
        case .unchecked, .done:
            if state == .unchecked, isEarning { flashGold() }
            onToggle()
        case .awaitingApproval, .helpRaised:
            if let onTerminalTap {
                onTerminalTap()
            } else {
                nudge()
            }
        }
    }

    /// Halo blooms to 1.35× and fades over ~320ms.
    private func bloom() {
        guard !reduceMotion else { return }
        haloScale = 1
        haloOpacity = 0.5
        withAnimation(.easeOut(duration: 0.32)) {
            haloScale = 1.35
            haloOpacity = 0
        }
    }

    private func flashGold() {
        goldFlash = true
        Task {
            try? await Task.sleep(for: .milliseconds(180))
            withAnimation(.easeOut(duration: 0.15)) { goldFlash = false }
        }
    }

    /// Terminal states answer a tap with a shake, not a toggle — the row's caption
    /// carries the reason.
    private func nudge() {
        guard !reduceMotion else { return }
        Task {
            for i in 1...4 {
                withAnimation(.easeInOut(duration: 0.06)) { wiggle = i }
                try? await Task.sleep(for: .milliseconds(60))
            }
            withAnimation(.easeInOut(duration: 0.06)) { wiggle = 0 }
        }
    }
}

#Preview("All four states") {
    struct Demo: View {
        @State private var first: ChoreCheckState = .unchecked
        var body: some View {
            VStack(alignment: .leading, spacing: 20) {
                HStack {
                    ChoreCheck(state: first, isEarning: true) {
                        first = first == .done ? .unchecked : .done
                    }
                    Text("Tap me — earning, so the ring flashes gold")
                }
                HStack {
                    ChoreCheck(state: .done) {}
                    Text("Done")
                }
                HStack {
                    ChoreCheck(state: .awaitingApproval) {}
                    Text("Waiting for approval")
                }
                HStack {
                    ChoreCheck(state: .helpRaised) {}
                    Text("Help raised")
                }
            }
            .padding()
        }
    }
    return Demo()
}
