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
        case signedIn(ApiUser)
    }

    public private(set) var state: State = .loading
    public let client = APIClient()

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
        let userJSON = Keychain.get(Keys.user)
        apiLog.notice("bootstrap: access=\(access != nil, privacy: .public) refresh=\(refresh != nil, privacy: .public) user=\(userJSON != nil, privacy: .public)")

        await client.configure(baseURL: serverURL, accessToken: access, refreshToken: refresh)
        await installCallbacks()

        guard refresh != nil,
              let userJSON,
              let user = try? JSONDecoder().decode(ApiUser.self, from: Data(userJSON.utf8))
        else {
            apiLog.notice("bootstrap: → needsLogin")
            state = .needsLogin
            return
        }

        // Optimistic: show the app immediately. A failed call refreshes the
        // token or signs out through the callbacks.
        apiLog.notice("bootstrap: → signedIn as \(user.userName, privacy: .public)")
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
        state = .needsLogin
    }

    public func login(userName: String, password: String) async throws {
        let tokens = try await client.login(userName: userName, password: password)
        persist(tokens)
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
        await client.logout()
        Keychain.delete(Keys.access)
        Keychain.delete(Keys.refresh)
        Keychain.delete(Keys.user)
        state = .needsLogin
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
            onTokensRotated: { tokens in
                Task { @MainActor in
                    SessionStore.persistStatic(tokens)
                }
            },
            onSessionExpired: { [weak self] in
                Task { @MainActor in
                    guard let self else { return }
                    Keychain.delete(Keys.access)
                    Keychain.delete(Keys.refresh)
                    self.state = .needsLogin
                }
            })
    }

    private func persist(_ tokens: TokenResponse) {
        Self.persistStatic(tokens)
        persistUser(tokens.user)
    }

    private static func persistStatic(_ tokens: TokenResponse) {
        Keychain.set(tokens.accessToken, forKey: Keys.access)
        Keychain.set(tokens.refreshToken, forKey: Keys.refresh)
        if let data = try? JSONEncoder().encode(tokens.user),
           let json = String(data: data, encoding: .utf8) {
            Keychain.set(json, forKey: Keys.user)
        }
    }

    private func persistUser(_ user: ApiUser) {
        if let data = try? JSONEncoder().encode(user),
           let json = String(data: data, encoding: .utf8) {
            Keychain.set(json, forKey: Keys.user)
        }
    }
}
