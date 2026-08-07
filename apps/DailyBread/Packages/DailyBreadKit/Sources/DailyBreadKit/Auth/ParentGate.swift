import Foundation
import Observation

/// The grant that stands between a parent session and the parent shell.
///
/// This is a local presence lock on an already-authenticated session, not an
/// authorization boundary — the server's JWT roles remain the real one. What it
/// buys is the case it was built for: a parent's already-unlocked phone in a
/// child's hands.
///
/// Owned by `SessionStore`, not injected separately, so the lifecycle wiring
/// (bind on sign-in, lock on sign-out, clear on user change) lives where the
/// user changes and views read through `session.parentGate`.
@MainActor
@Observable
public final class ParentGate {

    public struct Policy: Sendable {
        /// How long a grant survives with nobody touching the app.
        public var idleGrace: TimeInterval
        /// How recent a match has to be to satisfy a step-up without re-prompting.
        public var stepUpFreshness: TimeInterval = 30

        public init(idleGrace: TimeInterval, stepUpFreshness: TimeInterval = 30) {
            self.idleGrace = idleGrace
            self.stepUpFreshness = stepUpFreshness
        }

        /// Honest guesses, not measurements, and deliberately different: on a
        /// Mac without Touch ID the prompt is a password you type, and a
        /// desktop in a locked house is a weaker threat than a phone in a
        /// pocket. Both live here so the first week of real use can move them.
        public static var platformDefault: Policy {
            #if os(macOS)
            Policy(idleGrace: 3600)
            #else
            Policy(idleGrace: 300)
            #endif
        }
    }

    public var policy: Policy = .platformDefault

    /// Stored, never computed from a body: reading `BiometricAuthenticator`
    /// during layout would build an `LAContext` on every render pass.
    public private(set) var capability: BiometricCapability

    public private(set) var isUnlocked = false
    /// The privacy screen. Not a lock — it hides content without demanding a match.
    public private(set) var isCovered = false
    public private(set) var authenticating = false
    public private(set) var lastOutcome: BiometricOutcome?

    private let capabilityProvider: @MainActor () -> BiometricCapability
    private let authenticator: @MainActor (String) async -> BiometricOutcome
    private let defaults: UserDefaults
    private let now: () -> Date

    private var boundUser: ApiUser?
    private var expiryTask: Task<Void, Never>?
    private var holdDepth = 0
    private var lastSuccess: Date?

    /// Written on every touch, which is every gesture anywhere in the shell, so
    /// it is deliberately outside the observation graph: no view reads it, and
    /// notifying the graph at gesture frequency buys nothing.
    @ObservationIgnored private var lastActivity: ContinuousClock.Instant?

    /// The closures are injected so the decision table and the grace arithmetic
    /// are testable with no hardware in the room.
    public init(capability: @escaping @MainActor () -> BiometricCapability
                    = { BiometricAuthenticator.capability() },
                authenticate: @escaping @MainActor (String) async -> BiometricOutcome
                    = { await BiometricAuthenticator.authenticate(reason: $0) },
                defaults: UserDefaults = .standard,
                now: @escaping () -> Date = Date.init) {
        capabilityProvider = capability
        authenticator = authenticate
        self.defaults = defaults
        self.now = now
        self.capability = capability()
    }

    public func refreshCapability() {
        capability = capabilityProvider()
    }

    // MARK: - Preferences (per user, per device — nothing goes to the server)

    private enum Keys {
        static func enabled(_ userId: String) -> String { "db.parentGate.\(userId)" }
        static func introduced(_ userId: String) -> String { "db.parentGate.introduced.\(userId)" }
    }

    /// Bumped on every preference write, and read by everything that consults
    /// one. UserDefaults notifies nobody, so without this the Settings switch
    /// would flip and the wall would stay exactly where it was.
    private var preferenceGeneration = 0

    private func writePreference(_ value: Bool, forKey key: String) {
        defaults.set(value, forKey: key)
        preferenceGeneration += 1
    }

    /// Absent means default-by-capability; an explicit value always wins.
    ///
    /// Enrolled biometry defaults on, including while locked out — the
    /// enrollment is still there and a device unlock clears the counter.
    /// A login password defaults off: typing one on every return is friction
    /// you ask for, not friction imposed on you.
    public func isEnabled(for user: ApiUser) -> Bool {
        _ = preferenceGeneration
        if let stored = defaults.object(forKey: Keys.enabled(user.userId)) as? Bool {
            return stored
        }
        return Self.defaultEnabled(for: capability)
    }

    private static func defaultEnabled(for capability: BiometricCapability) -> Bool {
        switch capability {
        case .biometry, .biometryLockedOut: return true
        case .ownerPasscodeOnly, .none: return false
        }
    }

    /// Writes the default-on answer down the moment a parent session lands on a
    /// device that can host the gate.
    ///
    /// Left implicit, the whole feature evaporates the first time `capability`
    /// drops: the child who knows the passcode opens Settings → Face ID &
    /// Passcode → Reset Face ID, `isEnabled` falls back through the table to
    /// false, and the wall never renders again — no prompt, no explanation, no
    /// app credential ever asked for. An explicit `true` survives that, and the
    /// degraded device then authenticates with what it still has.
    ///
    /// Only the `true` answer is materialised. Writing an explicit `false` for a
    /// device with no enrolled biometry would freeze it off, so enrolling Face
    /// ID later would no longer arm the gate the way a fresh install does.
    private func materialiseDefault(for user: ApiUser) {
        guard user.isParent,
              defaults.object(forKey: Keys.enabled(user.userId)) == nil,
              Self.defaultEnabled(for: capability) else { return }
        writePreference(true, forKey: Keys.enabled(user.userId))
    }

    /// Whether an explicit preference says the gate is on. Distinct from
    /// `isEnabled` on exactly one axis — it never consults the capability
    /// table — which is what lets `isLocked` tell "this device was never set up
    /// for the gate" apart from "the gate was on and the device just lost the
    /// means to satisfy it".
    private func isExplicitlyEnabled(for user: ApiUser) -> Bool {
        _ = preferenceGeneration
        return defaults.object(forKey: Keys.enabled(user.userId)) as? Bool == true
    }

    public func hasBeenIntroduced(to user: ApiUser) -> Bool {
        _ = preferenceGeneration
        return defaults.bool(forKey: Keys.introduced(user.userId))
    }

    /// Turning it on runs one immediate rehearsal, so the first prompt a parent
    /// ever sees from this switch happens at the moment they asked for it,
    /// where failing costs nothing and the switch simply stays off.
    @discardableResult
    public func setEnabled(_ on: Bool, for user: ApiUser) async -> BiometricOutcome? {
        guard capability.canAuthenticate else {
            // The stored flag must never claim more than the device can do.
            writePreference(false, forKey: Keys.enabled(user.userId))
            return nil
        }
        guard on else {
            writePreference(false, forKey: Keys.enabled(user.userId))
            return .success
        }
        let outcome = await evaluate(reason: "unlock parent screens")
        guard outcome == .success else { return outcome }
        writePreference(true, forKey: Keys.enabled(user.userId))
        writePreference(true, forKey: Keys.introduced(user.userId))
        isUnlocked = true
        scheduleExpiry()
        return .success
    }

    // MARK: - The one question RootView asks

    public func isLocked(for user: ApiUser) -> Bool {
        // A child's phone never executes another line of this file.
        guard user.isParent else { return false }
        #if DEBUG
        // Debug-only, and an environment variable rather than a launch argument
        // or a defaults key precisely so nothing outside the test harness can
        // set it. UI tests build Debug; TestFlight builds Release.
        if ProcessInfo.processInfo.environment["DB_DISABLE_PARENT_GATE"] == "1" { return false }
        #endif
        guard isEngaged(for: user) else { return false }
        return !isUnlocked
    }

    /// Whether the wall stands for this user on this device, grant aside.
    ///
    /// A device that never hosted the gate has no security to strand a parent
    /// for, so it stays open. A device where the gate was explicitly ON is a
    /// different question: removing the passcode is four taps with a passcode
    /// the adversary is assumed to know, and it must not be the cheapest way
    /// through the wall. That case keeps the wall — `authenticate` answers
    /// `.unavailable`, the wall says so in words, and the password sign-in
    /// underneath it is the way back in.
    private func isEngaged(for user: ApiUser) -> Bool {
        guard user.isParent else { return false }
        guard capability.canAuthenticate || isExplicitlyEnabled(for: user) else { return false }
        return isEnabled(for: user)
    }

    // MARK: - The grant

    public func bind(to user: ApiUser?) {
        guard user?.userId != boundUser?.userId else { return }
        boundUser = user
        lockNow()
        if let user { materialiseDefault(for: user) }
    }

    @discardableResult
    public func unlock(for user: ApiUser, reason: String) async -> BiometricOutcome {
        let outcome = await evaluate(reason: reason)
        guard outcome == .success else { return outcome }
        // Both, together: passing the wall is the parent accepting it, and an
        // explicit preference is what keeps the wall standing if the device
        // later loses the biometry that armed it.
        writePreference(true, forKey: Keys.enabled(user.userId))
        writePreference(true, forKey: Keys.introduced(user.userId))
        isUnlocked = true
        scheduleExpiry()
        return .success
    }

    /// The step-up, asked only where the gate is live.
    ///
    /// A device the gate never engages on has no prompt worth answering, and a
    /// parent who switched it off never agreed to one — an unconditional
    /// step-up there does not make the action safer, it makes it impossible.
    @discardableResult
    public func requireFresh(reason: String, ifEngagedFor user: ApiUser) async -> BiometricOutcome {
        guard capability.canAuthenticate, isEnabled(for: user) else { return .success }
        return await requireFresh(reason: reason)
    }

    /// A step-up for one dangerous action. Deliberately does not consult
    /// `isUnlocked`: the point is that the person is here *now*, not that
    /// somebody was here when the wall came down.
    @discardableResult
    public func requireFresh(reason: String) async -> BiometricOutcome {
        if let lastSuccess, now().timeIntervalSince(lastSuccess) < policy.stepUpFreshness {
            return .success
        }
        return await evaluate(reason: reason)
    }

    /// Presence already proved by something stronger — a password sign-in, or a
    /// job-2 session unlock. Without this, launch would prompt twice in a row.
    /// Opens the wall but does not stamp the step-up clock: a step-up asks for
    /// a match on the spot, and a password typed a moment ago is not one.
    public func grantPresenceEstablished() {
        isUnlocked = true
        scheduleExpiry()
    }

    /// Reports activity. Called from the shell on every gesture, so it is one
    /// stored write and nothing else — the running expiry task re-reads the
    /// deadline rather than being torn down and rebuilt sixty times a second.
    public func touch() {
        guard isUnlocked else { return }
        lastActivity = .now
    }

    public func lockNow() {
        expiryTask?.cancel()
        expiryTask = nil
        // A hold is a claim by a view that is about to be torn down with the
        // shell. Leaving the depth standing would strand it for the rest of the
        // process, and a stranded hold disarms the idle timer permanently.
        holdDepth = 0
        isUnlocked = false
        lastOutcome = nil
        // A step-up asks whether the person is here *now*. After a deliberate
        // lock, a sign-out, or a different user signing in, a match from
        // thirty seconds ago answers a question nobody asked.
        lastSuccess = nil
        lastActivity = nil
    }

    /// Locks past any hold. The screen went to sleep or the machine switched
    /// users — a half-typed reason is not worth staying open for.
    public func hardLock() {
        lockNow()
    }

    /// Suppresses only the idle timer, so it cannot tear down a half-typed
    /// balance adjustment. Backgrounding and hard locks ignore it entirely.
    public func beginHold() {
        holdDepth += 1
        expiryTask?.cancel()
        expiryTask = nil
    }

    public func endHold() {
        guard holdDepth > 0 else { return }
        holdDepth -= 1
        if holdDepth == 0 { scheduleExpiry() }
    }

    // MARK: - Scene lifecycle

    /// Clearing the cover is unconditional. Guarding it on `authenticating`
    /// meant a `.active` that arrived before the evaluation's continuation had
    /// resumed — which is exactly the order a dismissed Face ID sheet produces
    /// — left the splash sitting over a live app with nothing left to remove it.
    public func sceneBecameActive() {
        isCovered = false
        refreshCapability()
    }

    /// `.inactive` fires for the Face ID sheet itself, Control Centre and a call
    /// banner — locking here is a prompt loop. Covering is also what the
    /// app-switcher snapshot catches, so the thumbnail is the cover rather than
    /// every child's balance.
    public func sceneWentInactive() {
        guard !authenticating, coversWhenInactive else { return }
        isCovered = true
    }

    /// Zero grace: backgrounding a phone is the hand-over moment, and this is
    /// the strongest re-lock trigger in the design.
    ///
    /// Deliberately not guarded on `authenticating`. The guard cost more than
    /// it bought: a parent who swipes home while the Settings rehearsal or a
    /// step-up sheet is up would background the app with the grant still open,
    /// LocalAuthentication would cancel, and nothing would re-examine the scene
    /// phase afterwards — the child reopens straight into the parent shell.
    /// Locking under a live prompt costs nothing, because the wall is what the
    /// user comes back to either way.
    public func sceneWentToBackground() {
        let cover = coversWhenInactive
        lockNow()
        isCovered = cover
    }

    /// Whether there is anything here this feature is meant to hide: a parent
    /// session, on a device the gate engages on, currently past the wall.
    ///
    /// Without the check the cover fires for every session, and iPadOS reports
    /// `foregroundInactive` for a scene that is merely visible-but-unfocused —
    /// so a child's chore list in Split View would be replaced by the splash
    /// the moment they touched the app beside it.
    private var coversWhenInactive: Bool {
        guard let boundUser, isUnlocked else { return false }
        return isEngaged(for: boundUser)
    }

    // MARK: - Private

    private func evaluate(reason: String) async -> BiometricOutcome {
        authenticating = true
        defer { authenticating = false }
        let outcome = await authenticator(reason)
        lastOutcome = outcome
        if outcome == .success { lastSuccess = now() }
        return outcome
    }

    /// `isUnlocked` is a stored Bool flipped by this task rather than a
    /// computed `deadline > now`: a date comparison never notifies the
    /// Observation graph, so the wall would not reappear while a parent sat
    /// looking at the screen.
    ///
    /// The task loops rather than sleeping once, so `touch()` only has to move
    /// `lastActivity` and the sleeper re-reads it on waking. Slept once, this
    /// was not an idle timer at all but an absolute grant lifetime: five
    /// minutes into a continuous run through Approvals the shell was torn down
    /// mid-scroll, which also resets the section a parent was standing in.
    ///
    /// The clock is `ContinuousClock`, not the injected `now` — that one exists
    /// to test the step-up arithmetic, and it is deliberately frozen there,
    /// which is the one thing a sleep deadline cannot survive.
    private func scheduleExpiry() {
        expiryTask?.cancel()
        expiryTask = nil
        guard holdDepth == 0 else { return }
        lastActivity = .now
        expiryTask = Task { [weak self] in
            while !Task.isCancelled {
                guard let self, let last = self.lastActivity else { return }
                let remaining = ContinuousClock.now
                    .duration(to: last.advanced(by: .seconds(self.policy.idleGrace)))
                guard remaining > .zero else {
                    self.expiryTask = nil
                    self.isUnlocked = false
                    return
                }
                try? await Task.sleep(for: remaining)
            }
        }
    }
}
