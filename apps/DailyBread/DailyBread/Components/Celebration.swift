import SwiftUI
import AVFoundation
import DailyBreadKit

// The celebration ladder — IMPLEMENTATION.md §1.3. Three tiers so feedback scales
// with the achievement instead of firing the same confetti for one chore or six:
//   1. per-chore: the ChoreCheck halo bloom + rigid haptic (lives in ChoreCheck).
//   2. threshold: the last EARNING chore lands — a money-gold coin arcs from the
//      check into the header balance, which pulses. ~600ms, no takeover.
//   3. perfect day: a real particle system. Fires less often than the old
//      confetti — which is what makes it worth something.
// Tiers 2 and 3 respect `enableConfetti` (gated at the call sites) and Reduce
// Motion (tier 3 collapses to the radial bloom alone, never to nothing).

/// Tier-3 tuning, ported verbatim from the design system's tweaks panel
/// ("full" preset). These values are the decisions, not suggestions.
struct CelebrationConfig {
    var particleCount: Int      = 1200
    var duration: TimeInterval  = 2.50
    var gravity: CGFloat        = 1150     // points/s²
    var drag: CGFloat           = 0.55     // velocity damping per second
    var burstVelocity: CGFloat  = 900      // radial impulse from the bloom origin
    var spin: CGFloat           = 7        // radians/s, randomised ±40%
    var depthBlur: CGFloat      = 3.50     // max blur on the farthest layer
    var bloomStrength: CGFloat  = 0.90
    var wobble: CGFloat         = 34       // lateral drift amplitude
    var playsChime: Bool        = true

    static let full = CelebrationConfig()
}

// MARK: - Deterministic particles

private struct SeededRandom {
    private var state: UInt64
    init(_ seed: UInt64) { state = seed &+ 0x9E3779B97F4A7C15 }

    mutating func next() -> Double {
        state = state &+ 0x9E3779B97F4A7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58476D1CE4E5B9
        z = (z ^ (z >> 27)) &* 0x94D049BB133111EB
        z ^= z >> 31
        return Double(z >> 11) * (1.0 / 9007199254740992.0)
    }

    mutating func range(_ a: Double, _ b: Double) -> Double { a + next() * (b - a) }
}

private enum ParticleShape: CaseIterable {
    case coin, ribbon, star, spark
}

/// One particle, fully determined at spawn — motion is closed-form in `t`, so the
/// renderer carries no simulation state and a dropped frame costs nothing.
private struct Particle {
    let fromBurst: Bool
    let x0: CGFloat          // burst: offset from origin. rain: fraction of width.
    let y0: CGFloat          // burst: offset from origin. rain: fraction of height.
    let vx: CGFloat
    let vy: CGFloat
    let depth: CGFloat       // 0 far … 1 near
    let shape: ParticleShape
    let size: CGFloat
    let r0: CGFloat
    let vr: CGFloat
    let colorIndex: Int
    let wobblePhase: CGFloat
    let wobbleFreq: CGFloat
    let life: TimeInterval

    // balanced mix — coin 3 : ribbon 3 : star 2 : spark 2
    private static let mix: [ParticleShape] = [.coin, .coin, .coin, .ribbon, .ribbon, .ribbon,
                                               .star, .star, .spark, .spark]

    init(index: Int, config: CelebrationConfig, paletteCount: Int) {
        var rnd = SeededRandom(UInt64(index))
        shape = Self.mix[Int(rnd.next() * Double(Self.mix.count)) % Self.mix.count]
        fromBurst = rnd.next() < 0.55   // emitter 'both'
        if fromBurst {
            let angle = rnd.range(0, .pi * 2)
            let speed = config.burstVelocity * rnd.range(0.35, 1)
            x0 = rnd.range(-8, 8)
            y0 = rnd.range(-8, 8)
            vx = cos(angle) * speed
            vy = sin(angle) * speed - config.burstVelocity * 0.35
        } else {
            x0 = rnd.range(-0.05, 1.05)
            y0 = rnd.range(-0.35, -0.02)
            vx = rnd.range(-70, 70)
            vy = rnd.range(40, 220)
        }
        depth = rnd.next()
        size = (shape == .ribbon ? rnd.range(5, 9) : rnd.range(4.5, 9)) * (0.55 + depth * 0.95)
        r0 = rnd.range(0, .pi * 2)
        vr = rnd.range(-1, 1) * config.spin * rnd.range(0.6, 1.4)
        colorIndex = Int(rnd.next() * Double(paletteCount)) % paletteCount
        wobblePhase = rnd.range(0, .pi * 2)
        wobbleFreq = rnd.range(0.8, 2.4)
        life = config.duration * rnd.range(0.7, 1.15)
    }

    /// Linear-drag ballistics, closed form: v' = g − k·v.
    func position(at t: TimeInterval, in size: CGSize, origin: CGPoint,
                  config: CelebrationConfig) -> CGPoint {
        let k = config.drag
        let parallax = 0.55 + depth * 0.75
        let g = config.gravity * parallax
        let decay = 1 - exp(-k * t)
        let vTerm = g / k
        let baseX: CGFloat = fromBurst ? origin.x + x0 : x0 * size.width
        let baseY: CGFloat = fromBurst ? origin.y + y0 : y0 * size.height
        let wobbleOffset = config.wobble * 0.35
            * sin(wobblePhase + wobbleFreq * t) * min(1, t)
        return CGPoint(
            x: baseX + vx * decay / k + wobbleOffset,
            y: baseY + vTerm * t + (vy - vTerm) * decay / k)
    }

    func alpha(at t: TimeInterval) -> Double {
        let fade = pow(max(0, 1 - t / life), 0.6)
        return min(1, 0.45 + depth * 0.55) * fade
    }
}

// MARK: - Shape drawing

private func drawShape(_ ctx: GraphicsContext, _ shape: ParticleShape, size s: CGFloat,
                       rotation r: CGFloat, color: Color) {
    var c = ctx
    switch shape {
    case .coin:
        let sx = max(0.12, abs(cos(r)))
        c.scaleBy(x: sx, y: 1)
        c.fill(Path(ellipseIn: CGRect(x: -s, y: -s, width: s * 2, height: s * 2)),
               with: .color(color))
        c.opacity = 0.45
        c.fill(Path(ellipseIn: CGRect(x: -s * 0.22 - s * 0.42, y: -s * 0.24 - s * 0.42,
                                      width: s * 0.84, height: s * 0.84)),
               with: .color(Color(hex: 0xFFF6DC)))
    case .ribbon:
        let sx = max(0.1, abs(cos(r * 0.7)))
        c.rotate(by: .radians(r))
        c.scaleBy(x: sx, y: 1)
        let w = s * 0.62, h = s * 2.6
        c.fill(Path(roundedRect: CGRect(x: -w / 2, y: -h / 2, width: w, height: h),
                    cornerRadius: w * 0.5),
               with: .color(color))
    case .star:
        c.rotate(by: .radians(r))
        var path = Path()
        for i in 0..<10 {
            let radius = i % 2 == 0 ? s * 1.05 : s * 0.46
            let angle = Double(i) / 10 * .pi * 2 - .pi / 2
            let point = CGPoint(x: cos(angle) * radius, y: sin(angle) * radius)
            if i == 0 { path.move(to: point) } else { path.addLine(to: point) }
        }
        path.closeSubpath()
        c.fill(path, with: .color(color))
    case .spark:
        c.fill(Path(ellipseIn: CGRect(x: -s * 1.1, y: -s * 1.1,
                                      width: s * 2.2, height: s * 2.2)),
               with: .color(color))
    }
}

// MARK: - Tier 3: perfect day

/// The perfect-day particle system: gravity and drag, varied geometry, depth via
/// scale + per-band blur, theme tint + money-gold, radial bloom, rising chime.
/// Under Reduce Motion it collapses to the bloom alone — never to nothing.
struct PerfectDayCelebration: View {
    let start: Date
    var config: CelebrationConfig = .full

    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private static let particles: [Particle] = (0..<CelebrationConfig.full.particleCount)
        .map { Particle(index: $0, config: .full, paletteCount: 6) }

    /// Money-gold always participates, whatever the theme.
    private var palette: [Color] {
        [DB.gold(scheme), Color(hex: 0xC98A1E), Color(hex: 0xF0C868),
         Color.accentColor, DB.success(scheme), DBRarity.epic.color(scheme)]
    }

    // Depth bands: far [0, 0.34), mid [0.34, 0.68), near [0.68, 1]. Blur is
    // applied once per BAND, never per particle — the per-particle version cost
    // ~1 fps in the reference implementation.
    private static let bands: [(Range<CGFloat>, Int)] = [(0..<0.34, 0), (0.34..<0.68, 1), (0.68..<1.01, 2)]

    var body: some View {
        TimelineView(.animation) { timeline in
            Canvas { context, size in
                let t = timeline.date.timeIntervalSince(start)
                guard t >= 0 else { return }
                let origin = CGPoint(x: size.width / 2, y: size.height * 0.42)

                drawBloom(context, t: t, size: size, origin: origin)
                guard !reduceMotion, t < config.duration * 1.15 else { return }

                let colors = palette
                for (range, bandIndex) in Self.bands {
                    let blur = config.depthBlur * (1 - CGFloat(bandIndex) / 2)
                    var band = context
                    if blur > 0.05 { band.addFilter(.blur(radius: blur)) }
                    band.drawLayer { layer in
                        for p in Self.particles where range.contains(p.depth) {
                            guard t < p.life else { continue }
                            let pos = p.position(at: t, in: size, origin: origin, config: config)
                            guard pos.y < size.height + 80 else { continue }
                            var ctx = layer
                            ctx.opacity = p.alpha(at: t)
                            ctx.translateBy(x: pos.x, y: pos.y)
                            drawShape(ctx, p.shape, size: p.size,
                                      rotation: p.r0 + p.vr * t,
                                      color: colors[p.colorIndex])
                        }
                    }
                }
            }
        }
        .allowsHitTesting(false)
        .ignoresSafeArea()
        .onAppear {
            if config.playsChime && !reduceMotion { Chime.play(rising: true) }
        }
    }

    /// The brief radial bloom behind the burst.
    private func drawBloom(_ context: GraphicsContext, t: TimeInterval,
                           size: CGSize, origin: CGPoint) {
        let life = reduceMotion ? 0.7 : config.duration * 0.45
        let k = t / life
        guard k < 1 else { return }
        let radius = (0.15 + k * 0.85) * max(size.width, size.height) * 0.85
        let alpha = (1 - k) * 0.5 * config.bloomStrength
        let gradient = Gradient(stops: [
            .init(color: Color(red: 1, green: 232 / 255, blue: 168 / 255).opacity(alpha), location: 0),
            .init(color: Color(red: 1, green: 206 / 255, blue: 102 / 255).opacity(alpha * 0.35), location: 0.55),
            .init(color: .clear, location: 1)])
        context.fill(
            Path(CGRect(origin: .zero, size: size)),
            with: .radialGradient(gradient, center: origin, startRadius: 0, endRadius: radius))
    }
}

// MARK: - Tier 2: the coin arc

/// Anchors the coin arc flies between: each row's check centre, and the header's
/// money line. Collected in one preference so a single overlay reads both.
struct CelebrationAnchors {
    var checks: [Int: Anchor<CGPoint>] = [:]
    var balance: Anchor<CGPoint>?
}

struct CelebrationAnchorsKey: PreferenceKey {
    static let defaultValue = CelebrationAnchors()
    static func reduce(value: inout CelebrationAnchors, nextValue: () -> CelebrationAnchors) {
        let next = nextValue()
        value.checks.merge(next.checks) { _, new in new }
        value.balance = next.balance ?? value.balance
    }
}

/// A money-gold coin arcs from the completed check into the balance over ~600ms,
/// then bursts into a few gold sparks. The balance pulse is driven by the store
/// (on a timer matching the flight), so this layer is purely visual.
struct CoinArcLayer: View {
    let start: Date
    let from: CGPoint
    let to: CGPoint

    @Environment(\.colorScheme) private var scheme

    private static let flight: TimeInterval = 0.62
    private static let sparkLife: TimeInterval = 0.52
    static let total = flight + sparkLife

    private static let sparks: [(angle: Double, speed: Double, size: Double)] = {
        var rnd = SeededRandom(77)
        return (0..<10).map { _ in
            (rnd.range(0, .pi * 2), rnd.range(80, 220), rnd.range(2.5, 4.5))
        }
    }()

    var body: some View {
        TimelineView(.animation) { timeline in
            Canvas { context, _ in
                let t = timeline.date.timeIntervalSince(start)
                guard t >= 0 else { return }
                let gold = DB.gold(scheme)

                if t < Self.flight {
                    let k = t / Self.flight
                    let eased = 1 - pow(1 - k, 2.2)
                    let mid = CGPoint(x: (from.x + to.x) / 2, y: min(from.y, to.y) - 130)
                    let u = 1 - eased
                    let x = u * u * from.x + 2 * u * eased * mid.x + eased * eased * to.x
                    let y = u * u * from.y + 2 * u * eased * mid.y + eased * eased * to.y
                    var ctx = context
                    ctx.opacity = k > 0.88 ? (1 - k) / 0.12 : 1
                    ctx.translateBy(x: x, y: y)
                    drawShape(ctx, .coin, size: 11, rotation: t * 14, color: gold)
                } else if t < Self.total {
                    let st = t - Self.flight
                    for spark in Self.sparks {
                        let pos = CGPoint(
                            x: to.x + cos(spark.angle) * spark.speed * st,
                            y: to.y + sin(spark.angle) * spark.speed * st
                                - 60 * st + 500 * st * st)
                        var ctx = context
                        ctx.opacity = max(0, 1 - st / Self.sparkLife)
                        ctx.translateBy(x: pos.x, y: pos.y)
                        drawShape(ctx, .spark, size: spark.size, rotation: 0, color: gold)
                    }
                }
            }
        }
        .allowsHitTesting(false)
        .ignoresSafeArea()
        .onAppear { Chime.play(rising: false) }
    }
}

#if DEBUG
/// Launch with environment `DB_TEST_CELEBRATION=1` (e.g. `SIMCTL_CHILD_DB_TEST_CELEBRATION=1
/// xcrun simctl launch …`) to fire the perfect-day celebration on a 4s loop from any
/// screen — for eyeballing tier-3 tuning without doing six chores. DEBUG builds only.
struct CelebrationTestLoop: ViewModifier {
    @State private var start: Date?

    func body(content: Content) -> some View {
        content
            .overlay {
                if let start {
                    PerfectDayCelebration(start: start)
                }
            }
            .task {
                guard ProcessInfo.processInfo.environment["DB_TEST_CELEBRATION"] == "1" else { return }
                while !Task.isCancelled {
                    start = Date()
                    try? await Task.sleep(for: .seconds(4))
                }
            }
    }
}

extension View {
    func celebrationTestLoop() -> some View { modifier(CelebrationTestLoop()) }
}
#endif

// MARK: - Chime

/// Two-note (threshold) or four-note rising (perfect day) chime, synthesized —
/// triangle waves, 85ms apart, fast attack and a ~0.55s decay. Ambient audio
/// category on iOS so the ringer switch is respected. Audio is a bonus, never a
/// failure: every call is best-effort.
@MainActor
enum Chime {
    private static var engine: AVAudioEngine?

    private final class SampleClock: @unchecked Sendable {
        var time: Double = 0
    }

    static func play(rising: Bool) {
        #if os(iOS)
        try? AVAudioSession.sharedInstance().setCategory(.ambient)
        try? AVAudioSession.sharedInstance().setActive(true)
        #endif

        let notes: [Double] = rising
            ? [523.25, 659.25, 783.99, 1046.5]
            : [783.99, 1046.5]

        let engine = AVAudioEngine()
        let sampleRate = engine.outputNode.outputFormat(forBus: 0).sampleRate
        guard sampleRate > 0 else { return }
        let clock = SampleClock()

        let source = AVAudioSourceNode { _, _, frameCount, audioBufferList in
            let buffers = UnsafeMutableAudioBufferListPointer(audioBufferList)
            for frame in 0..<Int(frameCount) {
                let t = clock.time + Double(frame) / sampleRate
                var sample: Double = 0
                for (i, freq) in notes.enumerated() {
                    let dt = t - Double(i) * 0.085
                    guard dt > 0, dt < 0.6 else { continue }
                    let envelope = dt < 0.012
                        ? 0.16 * dt / 0.012
                        : 0.16 * exp(-(dt - 0.012) / 0.075)
                    sample += envelope * (2 / Double.pi) * asin(sin(2 * .pi * freq * dt))
                }
                for buffer in buffers {
                    buffer.mData?.assumingMemoryBound(to: Float.self)[frame] = Float(sample)
                }
            }
            clock.time += Double(frameCount) / sampleRate
            return noErr
        }

        engine.attach(source)
        engine.connect(source, to: engine.mainMixerNode, format: nil)
        guard (try? engine.start()) != nil else { return }
        Self.engine?.stop()
        Self.engine = engine

        Task {
            try? await Task.sleep(for: .seconds(1.4))
            engine.stop()
            if Self.engine === engine { Self.engine = nil }
        }
    }
}
