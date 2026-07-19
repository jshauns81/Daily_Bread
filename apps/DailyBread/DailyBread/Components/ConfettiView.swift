import SwiftUI
import DailyBreadKit

/// The day-complete celebration — the web app's confetti, reborn native.
/// Deterministic particles (seeded per index), drawn in a Canvas over a
/// TimelineView; runs ~2.5s from `start` then the overlay removes itself.
struct ConfettiView: View {
    let start: Date

    private struct Particle {
        let x: Double        // 0...1 horizontal start
        let hue: Int         // palette index
        let size: Double
        let fallSpeed: Double
        let sway: Double
        let spin: Double
        let delay: Double
    }

    private static let particles: [Particle] = (0..<70).map { i in
        // Simple deterministic pseudo-random from the index.
        func rnd(_ salt: Int) -> Double {
            let v = Double((i * 73 + salt * 179) % 997) / 997.0
            return v
        }
        return Particle(
            x: rnd(1),
            hue: Int(rnd(2) * 5),
            size: 6 + rnd(3) * 7,
            fallSpeed: 0.55 + rnd(4) * 0.5,
            sway: 20 + rnd(5) * 60,
            spin: 2 + rnd(6) * 6,
            delay: rnd(7) * 0.4)
    }

    @Environment(\.colorScheme) private var scheme

    private var palette: [Color] {
        [DB.gold(scheme), DB.glow(scheme), DB.success(scheme),
         Color.accentColor, Color.accentColor.opacity(0.6)]
    }

    var body: some View {
        TimelineView(.animation) { timeline in
            Canvas { context, size in
                let t = timeline.date.timeIntervalSince(start)
                guard t >= 0, t < 2.6 else { return }
                for p in Self.particles {
                    let pt = t - p.delay
                    guard pt > 0 else { continue }
                    let progress = pt * p.fallSpeed
                    let y = progress * (size.height + 60) - 30
                    guard y < size.height + 20 else { continue }
                    let x = p.x * size.width + sin(pt * 3 + p.x * 10) * p.sway
                    let fade = max(0, min(1, 2.2 - pt))
                    var rect = context
                    rect.opacity = fade
                    rect.translateBy(x: x, y: y)
                    rect.rotate(by: .radians(pt * p.spin))
                    rect.fill(
                        Path(roundedRect: CGRect(x: -p.size / 2, y: -p.size / 3,
                                                 width: p.size, height: p.size * 0.66),
                             cornerRadius: 1.5),
                        with: .color(palette[p.hue]))
                }
            }
        }
        .allowsHitTesting(false)
        .ignoresSafeArea()
    }
}
