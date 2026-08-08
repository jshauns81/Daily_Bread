import Foundation
import Observation

/// App-wide auth state. Owns the APIClient, persists tokens to the Keychain,
/// and drives the root view: needsServer → needsLogin → signedIn.
@MainActor
@Observable
public final class SessionStore {
    public enum State: Equatable {
        case loading
        case needsServer
        case needsLogin
        /// A session exists, but its tokens are sealed behind biometry (job 2).
        case locked(ApiUser)
        case signedIn(ApiUser)
    }

    public private(set) var state: State = .loading
    public let client = APIClient()

    /// The presence lock on parent surfaces. Owned here rather than injected
    /// separately so the lifecycle wiring lives where the user changes.
    public let parentGate = ParentGate()

    /// Rendered by LoginView above its fields. It explains a sign-out the user
    /// did not ask for, so an invalidated Keychain ACL never reads as a
    /// mystery logout.
    public private(set) var signInNote: String?

    public func clearSignInNote() { signInNote = nil }

    /// Rendered by Settings → Security. It explains a protection that turned
    /// itself off — the one thing the toggle alone cannot say, because a switch
    /// sitting off looks identical whether the parent set it there or the
    /// Keychain did.
    public private(set) var securityNote: String?

    public func clearSecurityNote() { securityNote = nil }

    /// True while the sealed session is being opened.
    ///
    /// The wall reads this exactly the way it reads `parentGate.authenticating`:
    /// job 2's unlock authenticates against the Keychain ACL rather than through
    /// `BiometricAuthenticator`, so the gate's own flag never moves on that path
    /// and the Unlock button would otherwise stay live under the auto-prompt.
    public private(set) var isUnlockingSession = false

    /// The one session unlock allowed to be in flight. Outside the observation
    /// graph: `isUnlockingSession` is what views watch.
    @ObservationIgnored private var unlockInFlight: Task<BiometricOutcome, Never>?

    /// Family feature switches — refreshed on sign-in and foreground.
    /// Defaults apply until loaded (goals off, delight on).
    public var features = FamilyFeatures()

    public func refreshFeatures() async {
        if let fetched = try? await client.familyFeatures() {
            features = fetched
        }
    }

    /// The parent shell's Approvals badge: chores waiting + Helps raised.
    ///
    /// It lives here, not in the tab shell, because the number changes from
    /// screens the shell can't see. Clearing a Help inside Approvals used to
    /// leave the badge stale until the app was relaunched — the shell only
    /// recounted on first appearance and on foreground, and an in-app action is
    /// neither. Anything that learns the true count should write it here.
    public var approvalsWaiting = 0

    /// Ask the server. This is the fallback for a parent who never opens the
    /// Approvals tab; while that screen is alive it publishes the count itself
    /// from its queue, which is the only number guaranteed to match the list.
    public func refreshApprovalsBadge() async {
        guard currentUser?.isParent == true else { return }
        if let queue = try? await client.approvalsQueue() {
            setApprovalsWaiting(from: queue)
        }
    }

    public func setApprovalsWaiting(from queue: ApprovalsQueue) {
        approvalsWaiting = queue.pendingApprovals.count + queue.helpRequests.count
    }

    /// The household's children — the single source of truth for single-child mode.
    /// Fetched for parents on sign-in/foreground. When there is exactly one child the
    /// whole app presents in the SINGULAR, by name: no child pickers, filters, switchers,
    /// or "which child" affordances anywhere. (See the single-child-mode invariant.)
    public var children: [AssignableChild] = []

    /// True when the family has exactly one child — drives singular presentation everywhere.
    public var isSingleChild: Bool { children.count == 1 }

    /// The one child, when this is a single-child family (nil otherwise).
    public var onlyChild: AssignableChild? { children.count == 1 ? children.first : nil }

    /// Refreshes the children roster. Parents only; a child's own session keeps it empty.
    public func refreshChildren() async {
        guard currentUser?.isParent == true else {
            children = []
            return
        }
        if let roster = try? await client.assignableChildren() {
            children = roster.children
        }
    }

    /// Whether driving is worth a place in the shell's navigation: for a parent,
    /// any child drives; for a child, they do. Off until proven on, so a family
    /// that never turns driving on never sees it.
    public var drivingVisible = false

    /// Refreshed on sign-in and foreground rather than on the badge poll — the
    /// answer only changes when a parent flips a switch in Settings, and a
    /// navigation item appearing mid-session is worse than one arriving a
    /// moment late.
    public func refreshDrivingVisibility() async {
        guard let user = currentUser else {
            drivingVisible = false
            return
        }
        if user.isParent {
            let members = (try? await client.familyMembers()) ?? []
            drivingVisible = members.contains { $0.drives }
        } else {
            drivingVisible = (try? await client.drivingProgress())?.isEnabled ?? false
        }
    }

    private enum Keys {
        static let serverURL = "db.serverURL"     // UserDefaults (not secret)
        static let access = "accessToken"          // Keychain
        static let refresh = "refreshToken"        // Keychain
        static let user = "userJSON"               // Keychain (contains name/roles)
        /// UserDefaults hint only — ProtectedKeychain.exists() is the authority.
        static let sessionLockHintPrefix = "db.sessionLock."
    }

    /// Whether this device's saved sign-in is sealed behind biometry (job 2).
    /// The Settings switch reads this; nothing *routes* on it — every code path
    /// that matters asks `ProtectedKeychain.exists()`, so a prefs edit can
    /// never talk the app into writing a plaintext token beside the sealed one.
    public private(set) var isSessionProtected = false

    private func setSessionLockHint(_ on: Bool, for userId: String) {
        UserDefaults.standard.set(on, forKey: Keys.sessionLockHintPrefix + userId)
        isSessionProtected = on
    }

    private func clearSessionLockHint() {
        isSessionProtected = false
        guard let userId = stateUser?.userId else { return }
        UserDefaults.standard.removeObject(forKey: Keys.sessionLockHintPrefix + userId)
    }

    /// The user behind either of the two states that carry one.
    private var stateUser: ApiUser? {
        switch state {
        case .signedIn(let user), .locked(let user): return user
        default: return nil
        }
    }

    public var serverURL: URL? {
        get { UserDefaults.standard.url(forKey: Keys.serverURL) }
        set { UserDefaults.standard.set(newValue, forKey: Keys.serverURL) }
    }

    public var currentUser: ApiUser? {
        if case .signedIn(let user) = state { return user }
        return nil
    }

    /// Age-appropriate voice for the signed-in child (younger by default).
    public var voice: KidVoice { KidVoice(AgeTier(wire: currentUser?.ageTier)) }

    /// Pull the authoritative user (roles, age tier) from the server and update
    /// the signed-in state. Safe to call after an optimistic sign-in.
    public func refreshCurrentUser() async {
        if let fresh = try? await client.me() {
            persistUser(fresh)
            if case .signedIn = state { state = .signedIn(fresh) }
        }
    }

    public init() {}

    /// Call once at launch: restores server + tokens and lands on the right screen.
    ///
    /// Which screen you land on is decided from **local state only** — server
    /// URL, Keychain tokens, cached user. Nothing here awaits the network.
    /// It used to: with no cached user it fell through to `client.me()` and
    /// waited, so a call that stalled before it ever hit the wire left the app
    /// on a bare spinner with no error, no timeout and no way out. A missing
    /// cached user now just means signing in again — a worse outcome than a
    /// silent refresh, and a far better one than a dead launch screen.
    public func bootstrap() async {
        guard let serverURL else {
            state = .needsServer
            return
        }
        let access = Keychain.get(Keys.access)
        let refresh = Keychain.get(Keys.refresh)
        // The cached user deliberately stays in the plain item: it holds a
        // display name and roles, not a credential, and keeping it readable is
        // what lets the lock screen greet by name and lets this method pick a
        // screen without ever raising a prompt.
        let userJSON = Keychain.get(Keys.user)
        let cached = userJSON.flatMap {
            try? JSONDecoder().decode(ApiUser.self, from: Data($0.utf8))
        }
        apiLog.notice("bootstrap: access=\(access != nil, privacy: .public) refresh=\(refresh != nil, privacy: .public) user=\(userJSON != nil, privacy: .public)")

        #if os(iOS)
        // Item presence, not the stored hint — the item is the authority.
        isSessionProtected = ProtectedKeychain.exists()
        if isSessionProtected {
            guard let cached else {
                // A lock screen for nobody is a dead end with no way out.
                Self.clearTokens()
                isSessionProtected = false
                state = .needsLogin
                return
            }
            await client.configure(baseURL: serverURL, accessToken: nil, refreshToken: nil)
            await installCallbacks()
            parentGate.bind(to: cached)
            apiLog.notice("bootstrap: → locked as \(cached.userName, privacy: .public)")
            state = .locked(cached)
            return
        }
        #endif

        await client.configure(baseURL: serverURL, accessToken: access, refreshToken: refresh)
        await installCallbacks()

        guard refresh != nil, let user = cached else {
            apiLog.notice("bootstrap: → needsLogin")
            state = .needsLogin
            return
        }

        // Optimistic: show the app immediately. A failed call refreshes the
        // token or signs out through the callbacks.
        apiLog.notice("bootstrap: → signedIn as \(user.userName, privacy: .public)")
        parentGate.bind(to: user)
        state = .signedIn(user)
        Task { [weak self] in
            await self?.refreshFeatures()
            await self?.refreshChildren()
            await self?.refreshCurrentUser()
        }
    }

    public func setServer(_ url: URL) async {
        serverURL = url
        await client.configure(baseURL: url, accessToken: nil, refreshToken: nil)
        await installCallbacks()
        parentGate.bind(to: nil)
        state = .needsLogin
    }

    public func login(userName: String, password: String) async throws {
        let tokens = try await client.login(userName: userName, password: password)
        persist(tokens)
        signInNote = nil
        parentGate.bind(to: tokens.user)
        #if os(iOS)
        // Mount the shell the way a relaunch does: from a settled scene, never
        // straight out of the login form. iOS 26's floating tab bar assembles
        // its glass layers from scene and keyboard state sampled at mount
        // time; built while the login keyboard is still tearing down, it can
        // come up with dead hit testing — tab bar or all tab content, varies —
        // and nothing short of a relaunch recomputes it (the wedge that hit
        // the family on 2026-08-07; Apple's release notes acknowledge the
        // sibling bugs 151126350 / 156174227 with "quit the app then
        // re-launch it"). LoginView resigns the keyboard as Sign In is
        // tapped; this interstitial beat holds the swap until that teardown
        // is over, which is exactly what the launch path does by showing
        // `.loading` first.
        state = .loading
        try? await Task.sleep(for: .milliseconds(500))
        #endif
        state = .signedIn(tokens.user)

        // Everything past this point is enrichment, and none of it gates being
        // signed in — the token response already carries the user. Awaiting it
        // here meant one stalled call left a successful login spinning forever
        // with nothing on screen to say why. Kicked off detached so the sign-in
        // completes the moment the server says yes.
        Task { [weak self] in
            await self?.refreshFeatures()
            await self?.refreshChildren()
            await self?.refreshCurrentUser()
        }
    }

    public func signOut() async {
        clearSessionLockHint()
        securityNote = nil
        await client.logout()
        Self.clearTokens()
        parentGate.lockNow()
        parentGate.bind(to: nil)
        // db.parentGate.<userId> deliberately survives: the gate defaults on
        // for parents anyway, and the escape hatch is this password sign-in,
        // not a preference reset.
        state = .needsLogin
    }

    // MARK: - Protected session (job 2 — iOS only, opt-in per user per device)

    /// The lock screen's one action. A cancelled or failed read stays `.locked`
    /// and deletes nothing — one fat-fingered cancel costing a full
    /// username/password re-login is the specific failure this avoids.
    ///
    /// Concurrent callers share the one evaluation in flight rather than
    /// stacking two sheets. `BiometricAuthenticator` has its own memo for job 1;
    /// this path never reaches it, because the ACL is what does the
    /// authenticating, so the memo has to live here. The store is `@MainActor`,
    /// so the check-and-set before the first `await` is atomic.
    public func unlockSession() async -> BiometricOutcome {
        #if os(iOS)
        if let existing = unlockInFlight { return await existing.value }
        guard case .locked = state, serverURL != nil else { return .success }
        let task = Task { [weak self] in
            await self?.performUnlockSession() ?? .cancelled
        }
        unlockInFlight = task
        isUnlockingSession = true
        let outcome = await task.value
        unlockInFlight = nil
        isUnlockingSession = false
        return outcome
        #else
        return .success
        #endif
    }

    #if os(iOS)
    private func performUnlockSession() async -> BiometricOutcome {
        guard case .locked(let user) = state, let serverURL else { return .success }
        switch await ProtectedKeychain.readAuthenticating(reason: "unlock your saved sign-in") {
        case .success(let tokens):
            await client.configure(baseURL: serverURL,
                                   accessToken: tokens.access,
                                   refreshToken: tokens.refresh)
            await installCallbacks()
            parentGate.bind(to: user)
            state = .signedIn(user)
            // The person just proved presence by a stronger means than job 1
            // would ask for; without this, launch prompts twice in a row.
            parentGate.grantPresenceEstablished()
            Task { [weak self] in
                await self?.refreshFeatures()
                await self?.refreshChildren()
                await self?.refreshCurrentUser()
            }
            return .success
        case .failure(.invalidated):
            setSessionLockHint(false, for: user.userId)
            Self.clearTokens()
            signInNote = "Your \(parentGate.capability.displayName) setup changed on this device, so the saved sign-in was cleared. Sign in again."
            parentGate.bind(to: nil)
            state = .needsLogin
            return .invalidated
        case .failure(let outcome):
            return outcome
        }
    }
    #endif

    /// Moves the tokens into one biometry-sealed item. Exactly one prompt,
    /// inside which the item is written and read back on the same context.
    public func enableProtectedSession() async -> Result<Void, BiometricOutcome> {
        #if os(iOS)
        guard case .signedIn(let user) = state else { return .failure(.unavailable) }
        // A .biometryCurrentSet ACL cannot be satisfied without enrolled
        // biometry, so there is no point creating an item nobody can open.
        guard case .biometry = parentGate.capability else { return .failure(.unavailable) }
        guard let access = Keychain.get(Keys.access),
              let refresh = Keychain.get(Keys.refresh) else { return .failure(.unavailable) }

        lastRotated = nil
        let outcome = await ProtectedKeychain.enroll(
            ProtectedTokens(access: access, refresh: refresh),
            reason: "protect your saved sign-in")
        guard outcome == .success else {
            // enroll() has already removed any partial item; the plain items
            // are untouched and the session keeps working normally.
            return .failure(outcome)
        }
        // The pair above was read before the sheet went up, and the sheet is
        // seconds of wall clock during which a fifteen-minute rotation can land.
        // Sealing the superseded refresh token is not a stale-data nuisance:
        // the server treats a revoked token as theft and answers by signing out
        // every device the household owns, hours later, with nothing tying
        // cause to effect. Everything from here to the deletes runs without an
        // await, so no rotation can interleave.
        if let latest = lastRotated {
            let status = ProtectedKeychain.write(ProtectedTokens(access: latest.access,
                                                                refresh: latest.refresh))
            guard status == errSecSuccess else {
                ProtectedKeychain.remove()
                return .failure(.error(code: Int(status)))
            }
        }
        Keychain.delete(Keys.access)
        Keychain.delete(Keys.refresh)
        setSessionLockHint(true, for: user.userId)
        lastRotated = nil
        return .success(())
        #else
        return .failure(.unavailable)
        #endif
    }

    public func disableProtectedSession() async -> Result<Void, BiometricOutcome> {
        #if os(iOS)
        guard case .signedIn(let user) = state else { return .failure(.unavailable) }
        lastRotated = nil
        switch await ProtectedKeychain.readAuthenticating(reason: "turn off session protection") {
        case .success(let tokens):
            // The read happened under the sheet, so a rotation that landed
            // while it was up already sealed a newer pair. Writing the read-back
            // pair over it would hand the server a revoked refresh token, which
            // it answers by revoking the whole household.
            let pair = lastRotated ?? (access: tokens.access, refresh: tokens.refresh)
            // The delete is mandatory: Keychain.set tries SecItemUpdate first
            // and only applies kSecAttrAccessibleAfterFirstUnlock on the
            // SecItemAdd fallback.
            Keychain.delete(Keys.access)
            Keychain.set(pair.access, forKey: Keys.access)
            Keychain.delete(Keys.refresh)
            Keychain.set(pair.refresh, forKey: Keys.refresh)
            ProtectedKeychain.remove()
            setSessionLockHint(false, for: user.userId)
            lastRotated = nil
            return .success(())
        case .failure(.invalidated):
            // The match passed and the Keychain still refused: the enrolment
            // changed under us and the sealed refresh token is unrecoverable.
            // Left as an ordinary failure this is a switch that can never be
            // turned off, followed by a forced re-login the parent only meets
            // at the next cold launch.
            setSessionLockHint(false, for: user.userId)
            Self.clearTokens()
            signInNote = "Your \(parentGate.capability.displayName) setup changed on this device, so the saved sign-in was cleared. Sign in again."
            parentGate.bind(to: nil)
            state = .needsLogin
            return .failure(.invalidated)
        case .failure(let outcome):
            return .failure(outcome)
        }
        #else
        return .failure(.unavailable)
        #endif
    }

    /// Full reset: forget the server too (Settings → "Change server").
    public func forgetServer() async {
        await signOut()
        UserDefaults.standard.removeObject(forKey: Keys.serverURL)
        state = .needsServer
    }

    // MARK: - Private

    private func installCallbacks() async {
        await client.setCallbacks(
            onTokensRotated: { [weak self] tokens in
                Task { @MainActor in
                    self?.persistRotated(tokens)
                }
            },
            onSessionExpired: { [weak self] in
                Task { @MainActor in
                    guard let self else { return }
                    // Everything, including userJSON. It used to leave the
                    // cached user behind, which with a protected item would
                    // show a lock screen for a session the server has already
                    // rejected.
                    SessionStore.clearTokens()
                    self.parentGate.lockNow()
                    self.parentGate.bind(to: nil)
                    self.state = .needsLogin
                }
            })
    }

    private static func clearTokens() {
        Keychain.delete(Keys.access)
        Keychain.delete(Keys.refresh)
        Keychain.delete(Keys.user)
        #if os(iOS)
        ProtectedKeychain.remove()
        #endif
    }

    private func persist(_ tokens: TokenResponse) {
        persistRotated(tokens)
        persistUser(tokens.user)
    }

    /// The pair the client most recently rotated to.
    ///
    /// Read only by `enableProtectedSession` / `disableProtectedSession`, which
    /// both have a biometric sheet standing between their read of the tokens and
    /// their write of them. Nothing else may route on it.
    private var lastRotated: (access: String, refresh: String)?

    /// Routes on item presence, never on the UserDefaults hint — a prefs edit
    /// must not be able to drop a plaintext token beside the sealed one.
    ///
    /// Every fifteen-minute rotation for the life of a session lands here.
    private func persistRotated(_ tokens: TokenResponse) {
        lastRotated = (access: tokens.accessToken, refresh: tokens.refreshToken)
        #if os(iOS)
        if ProtectedKeychain.exists() {
            let status = ProtectedKeychain.write(ProtectedTokens(access: tokens.accessToken,
                                                                refresh: tokens.refreshToken))
            if status == errSecSuccess {
                // Never leave a plaintext copy beside the sealed one.
                Keychain.delete(Keys.access)
                Keychain.delete(Keys.refresh)
                Self.persistUserJSON(tokens.user)
                return
            }
            // The sealed item is `…WhenPasscodeSetThisDeviceOnly`, so it is
            // unwritable while the device is locked — a rotation completing in
            // the runway between the screen locking and the app suspending
            // comes back errSecInteractionNotAllowed. `write` left the old pair
            // in place rather than destroying it, but the server has already
            // revoked that pair on rotation, so keeping it would hand the next
            // launch a token the server reads as theft.
            //
            // So the session degrades instead of dying: the live pair goes into
            // the AfterFirstUnlock items, which ARE writable while locked, and
            // the protection turns itself off in a way the parent can see. A
            // switch left claiming "on" over a cleartext bearer token would be
            // the worse lie.
            apiLog.error("rotate: sealed write failed (OSStatus \(status, privacy: .public)) — session protection degraded to plain items")
            ProtectedKeychain.remove()
            if let userId = stateUser?.userId {
                UserDefaults.standard.removeObject(forKey: Keys.sessionLockHintPrefix + userId)
            }
            isSessionProtected = false
            securityNote = "Your saved sign-in couldn't be re-sealed while the device was locked, so session protection turned itself off. You can turn it back on."
        }
        #endif
        Keychain.set(tokens.accessToken, forKey: Keys.access)
        Keychain.set(tokens.refreshToken, forKey: Keys.refresh)
        Self.persistUserJSON(tokens.user)
    }

    private func persistUser(_ user: ApiUser) {
        Self.persistUserJSON(user)
    }

    private static func persistUserJSON(_ user: ApiUser) {
        if let data = try? JSONEncoder().encode(user),
           let json = String(data: data, encoding: .utf8) {
            Keychain.set(json, forKey: Keys.user)
        }
    }
}
