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

    public init() {}

    /// Call once at launch: restores server + tokens and lands on the right screen.
    public func bootstrap() async {
        guard let serverURL else {
            state = .needsServer
            return
        }
        let access = Keychain.get(Keys.access)
        let refresh = Keychain.get(Keys.refresh)
        await client.configure(baseURL: serverURL, accessToken: access, refreshToken: refresh)
        await installCallbacks()

        guard refresh != nil else {
            state = .needsLogin
            return
        }
        if let userJSON = Keychain.get(Keys.user),
           let user = try? JSONDecoder().decode(ApiUser.self, from: Data(userJSON.utf8)) {
            // Optimistic: show the app immediately; a failed call will refresh
            // or sign out via the callbacks.
            state = .signedIn(user)
            await refreshFeatures()
        } else if let user = try? await client.me() {
            persistUser(user)
            state = .signedIn(user)
            await refreshFeatures()
        } else {
            state = .needsLogin
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
        await refreshFeatures()
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
