import XCTest
@testable import DailyBreadKit

/// The gate's decision table and grace arithmetic, driven entirely through the
/// injected capability / authenticate / now closures — no hardware, no prompts.
@MainActor
final class ParentGateTests: XCTestCase {

    private var defaults: UserDefaults!
    private var suiteName: String!

    override func setUp() {
        super.setUp()
        suiteName = "db.tests.parentGate.\(UUID().uuidString)"
        defaults = UserDefaults(suiteName: suiteName)
    }

    override func tearDown() {
        UserDefaults.standard.removePersistentDomain(forName: suiteName)
        defaults = nil
        super.tearDown()
    }

    // MARK: - Fixtures

    private func user(_ id: String = "p1", parent: Bool = true) -> ApiUser {
        ApiUser(userId: id,
                userName: id,
                roles: [parent ? "Parent" : "Child"],
                householdId: "h1",
                ageTier: nil,
                displayName: nil)
    }

    private final class Clock {
        var now = Date(timeIntervalSince1970: 1_000_000)
    }

    private func makeGate(_ capability: BiometricCapability = .biometry(.faceID),
                          outcome: @escaping () -> BiometricOutcome = { .success },
                          clock: Clock = Clock(),
                          calls: Counter = Counter()) -> ParentGate {
        ParentGate(capability: { capability },
                   authenticate: { _ in calls.count += 1; return outcome() },
                   defaults: defaults,
                   now: { clock.now })
    }

    private final class Counter { var count = 0 }

    // MARK: - isLocked

    func testChildIsNeverLocked() {
        let gate = makeGate()
        XCTAssertFalse(gate.isLocked(for: user("kid", parent: false)))
    }

    func testEnabledParentWithNoGrantIsLocked() {
        let gate = makeGate()
        XCTAssertTrue(gate.isLocked(for: user()))
    }

    func testDisabledGateIsNotLocked() {
        let gate = makeGate()
        defaults.set(false, forKey: "db.parentGate.p1")
        XCTAssertFalse(gate.isLocked(for: user()))
    }

    func testNoPasscodeDeviceIsNotLocked() {
        let gate = makeGate(.none)
        XCTAssertFalse(gate.isLocked(for: user()))
    }

    func testGrantUnlocks() async {
        let gate = makeGate()
        await gate.unlock(for: user(), reason: "unlock parent screens")
        XCTAssertFalse(gate.isLocked(for: user()))
    }

    // MARK: - Default by capability

    func testDefaultsOnForEnrolledBiometry() {
        XCTAssertTrue(makeGate(.biometry(.faceID)).isEnabled(for: user()))
    }

    func testDefaultsOnWhileLockedOut() {
        XCTAssertTrue(makeGate(.biometryLockedOut(.touchID)).isEnabled(for: user()))
    }

    func testDefaultsOffForPasswordOnlyDevice() {
        XCTAssertFalse(makeGate(.ownerPasscodeOnly).isEnabled(for: user()))
    }

    func testDefaultsOffWithNoPasscode() {
        XCTAssertFalse(makeGate(.none).isEnabled(for: user()))
    }

    func testStoredValueBeatsTheDefault() {
        let gate = makeGate(.biometry(.faceID))
        defaults.set(false, forKey: "db.parentGate.p1")
        XCTAssertFalse(gate.isEnabled(for: user()))

        let passwordGate = makeGate(.ownerPasscodeOnly)
        defaults.set(true, forKey: "db.parentGate.p1")
        XCTAssertTrue(passwordGate.isEnabled(for: user()))
    }

    func testPreferencesAreScopedPerUser() async {
        let gate = makeGate()
        defaults.set(false, forKey: "db.parentGate.p1")
        XCTAssertFalse(gate.isEnabled(for: user("p1")))
        XCTAssertTrue(gate.isEnabled(for: user("p2")))
    }

    func testSetEnabledForcesFalseWithoutACapableDevice() async {
        let gate = makeGate(.none)
        let outcome = await gate.setEnabled(true, for: user())
        XCTAssertNil(outcome)
        XCTAssertEqual(defaults.object(forKey: "db.parentGate.p1") as? Bool, false)
    }

    func testSetEnabledStoresNothingWhenTheRehearsalFails() async {
        let gate = makeGate(outcome: { .failed })
        let outcome = await gate.setEnabled(true, for: user())
        XCTAssertEqual(outcome, .failed)
        XCTAssertNil(defaults.object(forKey: "db.parentGate.p1"))
        XCTAssertTrue(gate.isLocked(for: user()))
    }

    // MARK: - The default-on answer, written down

    /// Left implicit, deleting the Face ID enrolment silently removes the wall
    /// for good: capability drops, the default table answers false, and no
    /// credential is ever asked for again.
    func testBindWritesTheDefaultOnAnswerDown() {
        let gate = makeGate()
        gate.bind(to: user())
        XCTAssertEqual(defaults.object(forKey: "db.parentGate.p1") as? Bool, true)
    }

    /// A device with nothing enrolled must stay unwritten, or enrolling Face ID
    /// later would find an explicit `false` and never arm the gate.
    func testBindWritesNothingWhenTheDefaultIsOff() {
        let gate = makeGate(.ownerPasscodeOnly)
        gate.bind(to: user())
        XCTAssertNil(defaults.object(forKey: "db.parentGate.p1"))
    }

    func testBindWritesNothingForAChild() {
        let gate = makeGate()
        gate.bind(to: user("kid", parent: false))
        XCTAssertNil(defaults.object(forKey: "db.parentGate.kid"))
    }

    func testUnlockWritesTheEnabledFlag() async {
        let gate = makeGate()
        await gate.unlock(for: user(), reason: "unlock parent screens")
        XCTAssertEqual(defaults.object(forKey: "db.parentGate.p1") as? Bool, true)
    }

    /// Deleting the enrolment drops capability to a passcode. The wall has to
    /// stand, and unlock falls back to what the device still has.
    func testAnExplicitlyEnabledGateSurvivesLosingBiometry() {
        let gate = makeGate(.ownerPasscodeOnly)
        defaults.set(true, forKey: "db.parentGate.p1")
        XCTAssertTrue(gate.isLocked(for: user()))
    }

    /// Turning the passcode off is four taps with a passcode the adversary is
    /// assumed to know. It must not be the cheapest way through the wall.
    func testRemovingThePasscodeDoesNotOpenAnExplicitlyEnabledGate() {
        let gate = makeGate(.none)
        defaults.set(true, forKey: "db.parentGate.p1")
        XCTAssertTrue(gate.isLocked(for: user()))
    }

    /// A device that never hosted the gate still fails open — failing closed
    /// there strands a parent for security the device does not have.
    func testNoPasscodeAndNoStoredPreferenceStillFailsOpen() {
        XCTAssertFalse(makeGate(.none).isLocked(for: user()))
    }

    // MARK: - Step-up engagement

    func testStepUpIsSkippedWhereTheGateNeverEngages() async {
        let calls = Counter()
        let gate = makeGate(.none, calls: calls)
        let outcome = await gate.requireFresh(reason: "reset a family member's password",
                                              ifEngagedFor: user())
        XCTAssertEqual(outcome, .success, "a device with no prompt to answer must not block the action")
        XCTAssertEqual(calls.count, 0)
    }

    func testStepUpIsSkippedWhenTheParentTurnedTheGateOff() async {
        let calls = Counter()
        let gate = makeGate(calls: calls)
        defaults.set(false, forKey: "db.parentGate.p1")
        _ = await gate.requireFresh(reason: "reset a family member's password",
                                    ifEngagedFor: user())
        XCTAssertEqual(calls.count, 0)
    }

    func testStepUpStillRunsWhereTheGateIsLive() async {
        let calls = Counter()
        let gate = makeGate(calls: calls)
        _ = await gate.requireFresh(reason: "reset a family member's password",
                                    ifEngagedFor: user())
        XCTAssertEqual(calls.count, 1)
    }

    func testUnlockMarksTheUserIntroduced() async {
        let gate = makeGate()
        XCTAssertFalse(gate.hasBeenIntroduced(to: user()))
        await gate.unlock(for: user(), reason: "unlock parent screens")
        XCTAssertTrue(gate.hasBeenIntroduced(to: user()))
    }

    // MARK: - Grace

    func testGrantExpiresAtTheDeadline() async throws {
        let gate = makeGate()
        gate.policy = ParentGate.Policy(idleGrace: 0.05)
        await gate.unlock(for: user(), reason: "unlock parent screens")
        XCTAssertTrue(gate.isUnlocked)
        try await Task.sleep(for: .milliseconds(200))
        XCTAssertFalse(gate.isUnlocked)
    }

    func testTouchExtendsTheGrant() async throws {
        let gate = makeGate()
        gate.policy = ParentGate.Policy(idleGrace: 0.2)
        await gate.unlock(for: user(), reason: "unlock parent screens")
        try await Task.sleep(for: .milliseconds(120))
        gate.touch()
        try await Task.sleep(for: .milliseconds(120))
        XCTAssertTrue(gate.isUnlocked)
    }

    func testHoldSuppressesTheIdleTimerAndEndHoldRestartsAFullWindow() async throws {
        let gate = makeGate()
        gate.policy = ParentGate.Policy(idleGrace: 0.15)
        await gate.unlock(for: user(), reason: "unlock parent screens")
        gate.beginHold()
        try await Task.sleep(for: .milliseconds(300))
        XCTAssertTrue(gate.isUnlocked, "a hold must outlive the idle window")
        gate.endHold()
        try await Task.sleep(for: .milliseconds(60))
        XCTAssertTrue(gate.isUnlocked, "endHold restarts a full window")
        try await Task.sleep(for: .milliseconds(200))
        XCTAssertFalse(gate.isUnlocked)
    }

    /// The grace is an IDLE window, not a grant lifetime: a parent working
    /// continuously must never be thrown out mid-scroll.
    func testContinuousActivityKeepsTheGrantAlive() async throws {
        let gate = makeGate()
        gate.policy = ParentGate.Policy(idleGrace: 0.1)
        await gate.unlock(for: user(), reason: "unlock parent screens")
        for _ in 0..<8 {
            try await Task.sleep(for: .milliseconds(40))
            gate.touch()
        }
        XCTAssertTrue(gate.isUnlocked, "320ms of activity through a 100ms idle window")
        try await Task.sleep(for: .milliseconds(250))
        XCTAssertFalse(gate.isUnlocked, "and it still expires once activity stops")
    }

    /// A hold belongs to a sheet that is torn down with the shell. Left
    /// standing, it disarms the idle timer for the rest of the process.
    func testLockNowClearsAStrandedHold() async throws {
        let gate = makeGate()
        gate.policy = ParentGate.Policy(idleGrace: 0.05)
        await gate.unlock(for: user(), reason: "unlock parent screens")
        gate.beginHold()
        gate.lockNow()
        await gate.unlock(for: user(), reason: "unlock parent screens")
        try await Task.sleep(for: .milliseconds(200))
        XCTAssertFalse(gate.isUnlocked)
    }

    func testHardLockIgnoresHolds() async {
        let gate = makeGate()
        await gate.unlock(for: user(), reason: "unlock parent screens")
        gate.beginHold()
        gate.hardLock()
        XCTAssertFalse(gate.isUnlocked)
    }

    // MARK: - Scene triggers

    func testBackgroundLocksImmediately() async {
        let gate = makeGate()
        gate.bind(to: user())
        await gate.unlock(for: user(), reason: "unlock parent screens")
        gate.sceneWentToBackground()
        XCTAssertFalse(gate.isUnlocked)
        XCTAssertTrue(gate.isCovered)
    }

    func testInactiveOnlyCovers() async {
        let gate = makeGate()
        gate.bind(to: user())
        await gate.unlock(for: user(), reason: "unlock parent screens")
        gate.sceneWentInactive()
        XCTAssertTrue(gate.isUnlocked)
        XCTAssertTrue(gate.isCovered)
    }

    /// `.inactive` fires for the Face ID sheet itself, so locking there is a
    /// prompt loop. `.background` is a different event and must still lock —
    /// see the test below.
    func testInactiveIsANoOpWhileAuthenticating() async {
        let gate = slowGate()
        gate.bind(to: user())
        gate.grantPresenceEstablished()
        async let unlocked: BiometricOutcome = gate.unlock(for: user(), reason: "unlock parent screens")
        try? await Task.sleep(for: .milliseconds(40))
        gate.sceneWentInactive()
        XCTAssertTrue(gate.isUnlocked, "the prompt's own .inactive must not lock")
        XCTAssertFalse(gate.isCovered)
        _ = await unlocked
    }

    /// The hand-over moment. Guarded on `authenticating`, a parent who swiped
    /// home while a step-up sheet was up left the app backgrounded with the
    /// grant still open and no cover — and nothing re-examined the phase later.
    func testBackgroundLocksEvenWhileAuthenticating() async {
        let gate = slowGate()
        gate.bind(to: user())
        gate.grantPresenceEstablished()
        async let unlocked: BiometricOutcome = gate.unlock(for: user(), reason: "unlock parent screens")
        try? await Task.sleep(for: .milliseconds(40))
        gate.sceneWentToBackground()
        XCTAssertFalse(gate.isUnlocked)
        XCTAssertTrue(gate.isCovered)
        _ = await unlocked
    }

    /// Holds the evaluation open long enough to fire the scene triggers the
    /// Face ID sheet itself would raise.
    private func slowGate() -> ParentGate {
        ParentGate(capability: { .biometry(.faceID) },
                   authenticate: { _ in
                       try? await Task.sleep(for: .milliseconds(150))
                       return .success
                   },
                   defaults: defaults,
                   now: Date.init)
    }

    func testActiveClearsTheCoverEvenWhileAuthenticating() async {
        let gate = slowGate()
        gate.bind(to: user())
        gate.grantPresenceEstablished()
        gate.sceneWentInactive()
        XCTAssertTrue(gate.isCovered)
        async let unlocked: BiometricOutcome = gate.unlock(for: user(), reason: "unlock parent screens")
        try? await Task.sleep(for: .milliseconds(40))
        gate.sceneBecameActive()
        XCTAssertFalse(gate.isCovered, "a cover nothing clears sits over a live app forever")
        _ = await unlocked
    }

    func testAChildSessionIsNeverCovered() async {
        let gate = makeGate()
        gate.bind(to: user("kid", parent: false))
        gate.grantPresenceEstablished()
        gate.sceneWentInactive()
        XCTAssertFalse(gate.isCovered,
                       "iPadOS reports .inactive for a visible Split View scene")
    }

    func testAParentPastTheWallIsCovered() async {
        let gate = makeGate()
        gate.bind(to: user())
        await gate.unlock(for: user(), reason: "unlock parent screens")
        gate.sceneWentInactive()
        XCTAssertTrue(gate.isCovered)
    }

    // MARK: - Step-up

    func testRequireFreshHonoursThenExhaustsItsWindow() async {
        let clock = Clock()
        let calls = Counter()
        let gate = makeGate(clock: clock, calls: calls)
        await gate.unlock(for: user(), reason: "unlock parent screens")
        XCTAssertEqual(calls.count, 1)

        clock.now.addTimeInterval(10)
        let inWindow = await gate.requireFresh(reason: "reset a family member's password")
        XCTAssertEqual(inWindow, .success)
        XCTAssertEqual(calls.count, 1, "inside the freshness window, no second prompt")

        clock.now.addTimeInterval(100)
        let pastWindow = await gate.requireFresh(reason: "reset a family member's password")
        XCTAssertEqual(pastWindow, .success)
        XCTAssertEqual(calls.count, 2, "past the window it evaluates again")
    }

    func testRequireFreshDoesNotConsultTheGrant() async {
        let calls = Counter()
        let gate = makeGate(calls: calls)
        gate.grantPresenceEstablished()
        gate.lockNow()
        _ = await gate.requireFresh(reason: "reset a family member's password")
        XCTAssertEqual(calls.count, 1)
    }

    // MARK: - Presence established elsewhere

    func testGrantPresenceEstablishedNeverPrompts() {
        let calls = Counter()
        let gate = makeGate(calls: calls)
        gate.grantPresenceEstablished()
        XCTAssertTrue(gate.isUnlocked)
        XCTAssertEqual(calls.count, 0)
    }

    // MARK: - Binding

    func testBindClearsTheGrantOnAUserChange() async {
        let gate = makeGate()
        gate.bind(to: user("p1"))
        await gate.unlock(for: user("p1"), reason: "unlock parent screens")
        gate.bind(to: user("p2"))
        XCTAssertFalse(gate.isUnlocked)
        XCTAssertNil(gate.lastOutcome)
    }

    func testBindToNilLocks() async {
        let gate = makeGate()
        gate.bind(to: user("p1"))
        await gate.unlock(for: user("p1"), reason: "unlock parent screens")
        gate.bind(to: nil)
        XCTAssertFalse(gate.isUnlocked)
    }
}
