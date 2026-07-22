import Foundation

public enum APIError: Error, LocalizedError, Sendable {
    case noServerConfigured
    case notAuthenticated
    case server(code: String, message: String, status: Int)
    case http(status: Int)
    case network(String)
    case decoding(String)

    public var errorDescription: String? {
        switch self {
        case .noServerConfigured: return "No server configured yet."
        case .notAuthenticated: return "Please sign in again."
        case .server(_, let message, _): return message
        case .http(let status): return "Server error (\(status))."
        case .network(let message): return "Can't reach the server. \(message)"
        case .decoding: return "The server sent something unexpected."
        }
    }
}

/// All /api/v1 calls. An actor: one token state, serialized refreshes.
/// On 401 it silently refreshes once and retries; if the refresh fails,
/// `onSessionExpired` fires so the UI can drop to the login screen.
public actor APIClient {
    private let session: URLSession
    private let decoder: JSONDecoder
    private let encoder: JSONEncoder

    private var baseURL: URL?
    private var accessToken: String?
    private var refreshToken: String?

    /// Persist rotated tokens (Keychain) — set by SessionStore.
    public var onTokensRotated: (@Sendable (TokenResponse) -> Void)?
    /// Refresh failed for good — sign the user out.
    public var onSessionExpired: (@Sendable () -> Void)?

    public init(session: URLSession = .shared) {
        self.session = session
        decoder = JSONDecoder()
        encoder = JSONEncoder()
    }

    public func configure(baseURL: URL?, accessToken: String?, refreshToken: String?) {
        self.baseURL = baseURL
        self.accessToken = accessToken
        self.refreshToken = refreshToken
    }

    public func setCallbacks(
        onTokensRotated: (@Sendable (TokenResponse) -> Void)?,
        onSessionExpired: (@Sendable () -> Void)?
    ) {
        self.onTokensRotated = onTokensRotated
        self.onSessionExpired = onSessionExpired
    }

    // MARK: - Core request machinery

    private struct Empty: Codable {}

    private func makeRequest(path: String, method: String, body: Data?, authorized: Bool) throws -> URLRequest {
        guard let baseURL else { throw APIError.noServerConfigured }
        guard let url = URL(string: path, relativeTo: baseURL) else {
            throw APIError.network("Bad URL: \(path)")
        }
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.timeoutInterval = 20
        if let body {
            request.httpBody = body
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        if authorized {
            guard let accessToken else { throw APIError.notAuthenticated }
            request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        }
        return request
    }

    private func perform(_ request: URLRequest) async throws -> (Data, Int) {
        do {
            let (data, response) = try await session.data(for: request)
            let status = (response as? HTTPURLResponse)?.statusCode ?? 0
            return (data, status)
        } catch {
            throw APIError.network(error.localizedDescription)
        }
    }

    private func send<T: Decodable>(
        _ type: T.Type,
        path: String,
        method: String = "GET",
        body: Data? = nil,
        authorized: Bool = true,
        retryOn401: Bool = true
    ) async throws -> T {
        let request = try makeRequest(path: path, method: method, body: body, authorized: authorized)
        let (data, status) = try await perform(request)

        if status == 401, authorized, retryOn401 {
            try await refreshTokens()
            return try await send(type, path: path, method: method, body: body,
                                  authorized: authorized, retryOn401: false)
        }

        guard (200...299).contains(status) else {
            if let payload = try? decoder.decode(ApiErrorPayload.self, from: data) {
                throw APIError.server(code: payload.code, message: payload.message, status: status)
            }
            throw APIError.http(status: status)
        }

        if T.self == Empty.self || data.isEmpty, let empty = Empty() as? T {
            return empty
        }
        do {
            return try decoder.decode(T.self, from: data)
        } catch {
            throw APIError.decoding(String(describing: error))
        }
    }

    private func sendVoid(path: String, method: String, body: Data? = nil) async throws {
        _ = try await send(Empty.self, path: path, method: method, body: body)
    }

    private func encodeBody<B: Encodable>(_ value: B) throws -> Data {
        try encoder.encode(value)
    }

    private func refreshTokens() async throws {
        guard let refreshToken else {
            onSessionExpired?()
            throw APIError.notAuthenticated
        }
        struct RefreshBody: Codable { let refreshToken: String }
        let body = try encodeBody(RefreshBody(refreshToken: refreshToken))
        do {
            let tokens: TokenResponse = try await send(
                TokenResponse.self,
                path: "api/v1/auth/refresh",
                method: "POST",
                body: body,
                authorized: false,
                retryOn401: false)
            accessToken = tokens.accessToken
            self.refreshToken = tokens.refreshToken
            onTokensRotated?(tokens)
        } catch {
            onSessionExpired?()
            throw APIError.notAuthenticated
        }
    }

    // MARK: - Health / auth

    /// True if the URL points at a live Daily Bread server.
    public func checkHealth(at url: URL) async -> Bool {
        var probe = url
        if !probe.path.hasSuffix("/") { probe = probe.appendingPathComponent("") }
        guard let healthURL = URL(string: "api/v1/health", relativeTo: probe) else { return false }
        var request = URLRequest(url: healthURL)
        request.timeoutInterval = 8
        guard let (data, status) = try? await perform(request) else { return false }
        return status == 200 && String(data: data, encoding: .utf8)?.contains("Healthy") == true
    }

    public func login(userName: String, password: String) async throws -> TokenResponse {
        struct LoginBody: Codable { let userName: String; let password: String }
        let body = try encodeBody(LoginBody(userName: userName, password: password))
        let tokens: TokenResponse = try await send(
            TokenResponse.self, path: "api/v1/auth/login", method: "POST",
            body: body, authorized: false, retryOn401: false)
        accessToken = tokens.accessToken
        refreshToken = tokens.refreshToken
        return tokens
    }

    public func logout() async {
        struct LogoutBody: Codable { let refreshToken: String? }
        let body = try? encodeBody(LogoutBody(refreshToken: refreshToken))
        try? await sendVoid(path: "api/v1/auth/logout", method: "POST", body: body)
        accessToken = nil
        refreshToken = nil
    }

    public func me() async throws -> ApiUser {
        try await send(ApiUser.self, path: "api/v1/auth/me")
    }

    // MARK: - Chores

    public func todayChores(date: DayDate? = nil, userId: String? = nil) async throws -> TodayChores {
        try await send(TodayChores.self, path: path("api/v1/chores/today", [
            ("date", date?.wireString), ("userId", userId)]))
    }

    public func toggleChore(choreDefinitionId: Int, date: DayDate?, userId: String? = nil) async throws -> ChoreToggleResult {
        struct ToggleBody: Codable { let date: DayDate?; let userId: String? }
        let body = try encodeBody(ToggleBody(date: date, userId: userId))
        return try await send(ChoreToggleResult.self,
                              path: "api/v1/chores/\(choreDefinitionId)/toggle",
                              method: "POST", body: body)
    }

    public func raiseHelp(choreDefinitionId: Int, date: DayDate?, reason: String) async throws {
        struct HelpBody: Codable { let date: DayDate?; let reason: String }
        let body = try encodeBody(HelpBody(date: date, reason: reason))
        try await sendVoid(path: "api/v1/chores/\(choreDefinitionId)/help", method: "POST", body: body)
    }

    public func weekProgress(asOf: DayDate? = nil, userId: String? = nil) async throws -> WeekProgress {
        try await send(WeekProgress.self, path: path("api/v1/chores/week", [
            ("asOf", asOf?.wireString), ("userId", userId)]))
    }

    // MARK: - Planner (parents)

    /// The full chore list in SortOrder (the kid's list order — never
    /// re-sort). `includeInactive` is only sent when true; the server
    /// default is active-only.
    public func plannerChores(includeInactive: Bool = false, userId: String? = nil) async throws -> PlannerChoreList {
        try await send(PlannerChoreList.self, path: path("api/v1/planner/chores", [
            ("includeInactive", includeInactive ? "true" : nil),
            ("userId", userId)]))
    }

    public func createChore(_ chore: ChoreWrite) async throws -> PlannerChore {
        let body = try encodeBody(chore)
        return try await send(PlannerChore.self, path: "api/v1/planner/chores",
                              method: "POST", body: body)
    }

    public func updateChore(id: Int, _ chore: ChoreWrite) async throws -> PlannerChore {
        let body = try encodeBody(chore)
        return try await send(PlannerChore.self, path: "api/v1/planner/chores/\(id)",
                              method: "PUT", body: body)
    }

    /// Deletes a chore. The server soft-deletes to inactive when history
    /// exists — either way the chore leaves the active list.
    public func deleteChore(id: Int) async throws {
        try await sendVoid(path: "api/v1/planner/chores/\(id)", method: "DELETE")
    }

    public func toggleChoreActive(id: Int) async throws -> PlannerChore {
        try await send(PlannerChore.self, path: "api/v1/planner/chores/\(id)/toggle-active",
                       method: "POST")
    }

    /// PUT the full new order — contiguous sortOrders for every chore in
    /// the list. Body: { "items": [ { choreDefinitionId, sortOrder }, … ] }.
    public func reorderChores(_ items: [ChoreOrderItem]) async throws {
        struct OrderBody: Codable { let items: [ChoreOrderItem] }
        let body = try encodeBody(OrderBody(items: items))
        try await sendVoid(path: "api/v1/planner/chores/order", method: "PUT", body: body)
    }

    public func assignableChildren() async throws -> AssignableChildren {
        try await send(AssignableChildren.self, path: "api/v1/planner/assignable")
    }

    // MARK: - Ledger / goals / calendar

    public func balance(userId: String? = nil) async throws -> Balance {
        try await send(Balance.self, path: path("api/v1/ledger/balance", [("userId", userId)]))
    }

    public func history(userId: String? = nil, limit: Int = 50) async throws -> LedgerHistory {
        try await send(LedgerHistory.self, path: path("api/v1/ledger/history", [
            ("userId", userId), ("limit", String(limit))]))
    }

    public func goals(userId: String? = nil) async throws -> [Goal] {
        try await send([Goal].self, path: path("api/v1/goals", [("userId", userId)]))
    }

    public func createGoal(_ goal: GoalWrite) async throws -> Goal? {
        let body = try encodeBody(goal)
        return try? await send(Goal.self, path: "api/v1/goals", method: "POST", body: body)
    }

    /// Edit a goal in place. The target user rides in the GoalWrite body.
    public func updateGoal(id: Int, _ goal: GoalWrite) async throws {
        let body = try encodeBody(goal)
        try await sendVoid(path: "api/v1/goals/\(id)", method: "PUT", body: body)
    }

    /// Make one goal the primary (the one shown front-and-centre on Earnings/Home).
    public func setPrimaryGoal(id: Int, userId: String? = nil) async throws {
        try await sendVoid(path: path("api/v1/goals/\(id)/primary", [("userId", userId)]), method: "POST")
    }

    public func deleteGoal(id: Int, userId: String? = nil) async throws {
        try await sendVoid(path: path("api/v1/goals/\(id)", [("userId", userId)]), method: "DELETE")
    }

    public func calendarRange(from: DayDate, to: DayDate, userId: String? = nil) async throws -> CalendarRange {
        try await send(CalendarRange.self, path: path("api/v1/calendar/range", [
            ("from", from.wireString), ("to", to.wireString), ("userId", userId)]))
    }

    // MARK: - Family features / achievements

    public func familyFeatures() async throws -> FamilyFeatures {
        try await send(FamilyFeatures.self, path: "api/v1/family/features")
    }

    public func updateFamilyFeatures(_ features: FamilyFeatures) async throws -> FamilyFeatures {
        let body = try encodeBody(features)
        return try await send(FamilyFeatures.self, path: "api/v1/family/features", method: "PUT", body: body)
    }

    public func achievements(userId: String? = nil) async throws -> AchievementsList {
        try await send(AchievementsList.self, path: path("api/v1/achievements", [("userId", userId)]))
    }

    public func markAchievementsSeen() async {
        try? await sendVoid(path: "api/v1/achievements/seen", method: "POST")
    }

    // MARK: - Screen time

    public func screenTime(userId: String? = nil) async throws -> ScreenTimeSummary {
        try await send(ScreenTimeSummary.self, path: path("api/v1/screentime", [("userId", userId)]))
    }

    /// The kid's "At Risk Today" list (MECHANICS §E). Children query self;
    /// parents may pass a child userId. 404 ChildProfileNotFound when the
    /// target has no child profile (e.g. a parent's own view).
    public func atRiskToday(userId: String? = nil) async throws -> AtRiskToday {
        try await send(AtRiskToday.self, path: path("api/v1/screentime/atrisk", [("userId", userId)]))
    }

    /// Parent-only: tune one child's screen-time settings. Returns the fresh
    /// summary so the client can refresh in one round trip.
    public func updateScreenTimeSettings(_ update: ScreenTimeSettingsUpdate) async throws -> ScreenTimeSummary {
        let body = try encodeBody(update)
        return try await send(ScreenTimeSummary.self, path: "api/v1/screentime/settings",
                              method: "PUT", body: body)
    }

    // MARK: - Approvals / dashboard (parents)

    public func approvalsQueue() async throws -> ApprovalsQueue {
        try await send(ApprovalsQueue.self, path: "api/v1/approvals")
    }

    public func approve(choreLogId: Int) async throws {
        try await sendVoid(path: "api/v1/approvals/\(choreLogId)/approve", method: "POST")
    }

    public func respondToHelp(choreLogId: Int, response: HelpResponseKind, note: String? = nil) async throws {
        struct RespondBody: Codable { let response: String; let note: String? }
        let body = try encodeBody(RespondBody(response: response.rawValue, note: note))
        try await sendVoid(path: "api/v1/approvals/\(choreLogId)/help/respond", method: "POST", body: body)
    }

    public func parentDashboard() async throws -> ParentDashboard {
        try await send(ParentDashboard.self, path: "api/v1/dashboard/parent")
    }

    // MARK: - Helpers

    private func path(_ base: String, _ query: [(String, String?)]) -> String {
        let items = query.compactMap { key, value -> String? in
            guard let value, !value.isEmpty,
                  let escaped = value.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed)
            else { return nil }
            return "\(key)=\(escaped)"
        }
        return items.isEmpty ? base : "\(base)?\(items.joined(separator: "&"))"
    }
}
